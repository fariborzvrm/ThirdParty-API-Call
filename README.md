# InquiryService

A small ASP.NET Core Web API that accepts an "inquiry" request, calls external providers in
priority order with failover, caches and deduplicates results, and persists everything to SQL Server.

Built as a take-home assignment for a .NET backend engineering position. It is deliberately boring:
no CQRS, no MediatR, no generic-repository ceremony — just clear layers that do one job each.

## Quick start (Docker)

```bash
docker compose up --build
```

This starts SQL Server and the API. The API runs EF migrations itself on startup, so a fresh
clone + `docker compose up` is enough. The API is then on `http://localhost:8080`.

```bash
# Submit an inquiry
curl -s http://localhost:8080/api/inquiries \
  -H "Content-Type: application/json" \
  -d '{"customerId":"CUST-001","productId":"PROD-001","reference":"REF-001"}'

# Same payload again — served from the in-memory cache (second provider run skipped)
# Force a fresh run of the provider pipeline
curl -s "http://localhost:8080/api/inquiries?bypassCache=true" \
  -H "Content-Type: application/json" \
  -d '{"customerId":"CUST-001","productId":"PROD-001","reference":"REF-001"}'
```

An interactive Swagger UI is also available at `http://localhost:8080/swagger` when the app runs in the
Development environment (which the compose file sets), so you can try the endpoints in the browser.

### Demonstrating failover

There are two fake providers (`Alpha`, priority 1, and `Beta`, priority 2) that never call any real
external service. Their behaviour can be flipped at runtime:

```bash
# Make Alpha fail technically -> Beta serves the next inquiry
curl -s -X PUT "http://localhost:8080/api/debug/providers/Alpha/scenario?scenario=TechnicalFailure"
curl -s -X PUT "http://localhost:8080/api/debug/providers/Alpha/scenario?scenario=Timeout"
curl -s -X PUT "http://localhost:8080/api/debug/providers/Alpha/scenario?scenario=BusinessError"
# Reset
curl -s -X PUT "http://localhost:8080/api/debug/providers/Alpha/scenario?scenario=Success"

# See current scenarios/priorities
curl -s http://localhost:8080/api/debug/providers
```

Available scenarios: `Success`, `BusinessError`, `TechnicalFailure`, `Timeout`.

For the failover rule:

- `TechnicalFailure` or `Timeout` on Alpha → the service fails over to Beta, and the response shows
  Beta as `servingProvider` with Alpha recorded as a `TechnicalFailure` attempt.
- `BusinessError` on Alpha → Alpha's response is final and returned as-is; Beta is never called.

### Running locally (without Docker)

Prerequisites: .NET SDK 10+, SQL Server (a container or local instance).

```bash
# Create the database from the committed migrations
dotnet ef database update --project InquiryService.Api

# Run
dotnet run --project InquiryService.Api
```

The connection string defaults to `Server=localhost,1433;Database=InquiryService;User Id=sa;...`
and can be overridden with the `ConnectionStrings__InquiryDb` environment variable. `dotnet ef`
requires the local tool or `dotnet tool install -g dotnet-ef`.

## Architecture

```
Controllers          HTTP layer (thin)
  └─ InquiriesController            POST /api/inquiries
  └─ DebugProvidersController       dev-only fake-provider toggles
Services
  ├─ InquirySubmissionService        cache → duplicate lock → DB → pipeline → persist → cache
  ├─ FailoverRunner                  the failover/orchestration logic (the interesting part)
  ├─ KeyedLocks                      per-key async lock so duplicate requests don't run twice
  └─ InquiryResultCache              thin wrapper over IMemoryCache with a configurable TTL
Providers
  ├─ IInquiryProvider                the single provider abstraction
  └─ FakeProvider                    configurable fake implementing IInquiryProvider (Alpha, Beta)
Domain                                 Inquiry / ProviderAttempt entities + request/response DTOs
Infrastructure                         DbContext, startup migrator, central exception handler
Migrations                             EF Core migration(s)
```

### Request flow

1. A cache key is derived from the hashed request payload.
2. If a matching completed result is in the in-memory cache (and caching isn't bypassed), it is
   returned immediately.
3. A per-key lock is acquired. While one request for a key is inside this lock, any duplicate
   request for the same key waits, then re-checks the cache and the database instead of re-running
   the pipeline. The lock is reference-counted and removed when idle.
4. The database is checked. Found a completed inquiry → reuse it. Found a pending one left by
   another instance → poll briefly for its completion. Otherwise the inquiry is inserted as
   `Pending` (a unique index on the cache key guards against duplicate rows).
5. The failover pipeline runs: providers are ordered by priority and called one at a time with a
   per-provider timeout. A **technical** failure (timeout, exception) records the attempt and moves
   to the next provider. A **valid business response** — even one carrying a business error like
   "inquiry not found" — is final; failover stops there.
6. The inquiry row and the full attempt history are persisted, the result is cached when the
   inquiry completed, and the response is returned.

### Response shape

`POST /api/inquiries` returns 200 for both successes and business errors (the response is stored and
returned as-is), with the attempts appended so you can see exactly what happened:

```json
{
  "inquiryId": "0cb3…",
  "status": "Completed",
  "result": { "provider": "Alpha", "status": "approved", … },
  "servingProvider": "Alpha",
  "isBusinessError": false,
  "businessErrorCode": null,
  "businessErrorMessage": null,
  "completedAt": "2026-09-10T…",
  "attempts": [
    { "providerName": "Alpha", "attemptOrder": 1, "outcome": "Success", "outcomeDetail": null, "elapsedMs": 51 }
  ]
}
```

If every provider fails technically, the inquiry is stored with status `Failed` and the documented
`technicalError`, and the response carries that state (200 with status `Failed`). Unhandled
exceptions anywhere else become a clean `ProblemDetails` 500 with no stack trace. Invalid request
bodies are rejected with a 400 by model validation.

### Data model

- `Inquiries` — cache key (unique), request payload, status, final result, serving provider,
  business/technical error info, created/completed timestamps.
- `ProviderAttempts` — one row per provider call per run: provider, attempt order, outcome
  (`Success` / `BusinessError` / `TechnicalFailure`), detail, elapsed ms and timestamp. This table
  is the long-lived record for evaluating provider reliability.

Re-runs of the same inquiry key (e.g. after a failure, or a cache bypass) append new attempts to
the same inquiry row, so the full history is preserved.

## Key technical decisions

- **Two fake providers from one class.** Both `Alpha` and `Beta` are registrations of
  `FakeProvider` with distinct config — the same way you'd have two registrations of one real
  client library pointed at different endpoints. Their scenario comes from config by default and
  can be overridden at runtime via `FakeProviderRuntime`.
- **EF Core over Dapper.** The workflow is small and transactional and the entities are simple; EF
  Core's change tracking keeps the save logic short. No generic repository layer on top.
- **In-memory cache, keyed semaphores.** Single-instance service, so `IMemoryCache` plus a
  reference-counted per-key async lock is the simplest thing that is correct. The unique index on
  `CacheKey` in SQL Server is the safety net against duplicate rows.
- **`FailoverRunner` is separate from persistence.** It is the core, testable logic: given a set of
  providers and the failover rule, it returns the outcome and the attempt list. Unit tests target
  it directly with stub providers.
- **Timeouts enforced with `Task.WaitAsync`.** Providers get a cancellation token that fires at
  their configured timeout and the call is also raced against the timeout so a non-cooperative
  provider still can't hang the pipeline.
- **`dotnet ef` needs a running app? No.** Migrations are committed and applied on startup, but
  only when `RUN_MIGRATIONS=1` (set in the compose file) so the EF tooling can build the model
  without needing a live database.

## Assumptions

- **Single instance.** In-flight dedup is enforced in-process via the keyed lock. With multiple
  instances the unique DB index still prevents duplicate rows, and a request that finds another
  instance's `Pending` row will poll it briefly for completion — but cross-instance synchronization
  beyond that is out of scope.
- **A failed inquiry is retried on the next identical request.** Only `Completed` results are
  cached and reused; `Failed` rows are re-run (this also cleanly demonstrates failover recovery).
  If you'd rather fail fast after the first failure, that's a one-line change in
  `InquirySubmissionService`.
- **Business responses are returned as 200.** The provider's payload is treated as an opaque,
  final document, so a business-level error inside it stays a 200 with `isBusinessError` set.
- **The debug endpoints expose fake-provider control** and are intended for local/demo use only;
  they return 404 unless the app runs with `ASPNETCORE_ENVIRONMENT=Development`, so a real
  deployment behind a non-Development environment loses them automatically.
- **No auth, no API versioning, no health endpoint** — none were required, and a reviewer should
  spot that as a conscious scope cut rather than an oversight.

## Tests

```bash
dotnet test
```

`FailoverRunnerTests` covers the interesting logic: priority ordering, short-circuit on first
success, failover on technical failure and timeout, **no** failover on business errors, and the
attempt history. `FakeProviderTests`, `KeyedLocksTests` and `CacheKeyTests` cover the remaining
pieces the runner depends on.