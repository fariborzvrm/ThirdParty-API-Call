# Prompt for Claude Code — Inquiry Service API

Copy everything below into Claude Code as the task brief.

---

## Context

I'm building a take-home assignment for a .NET backend engineering position. Build a complete, working ASP.NET Core Web API project called **InquiryService**. This is a graded assignment, so code quality, sane architecture, and correct error/failover handling matter more than feature count. Write it the way an experienced backend engineer would for a real internal service — not a tutorial, not over-abstracted, no unnecessary layers or design patterns just to show them off.

## Business problem

The API receives an "inquiry" request from a client and needs to fetch a result from one of several external providers. Providers are unreliable (slow, down, or returning business-level errors), so the service must call them in priority order and fail over between them — but only for *technical* failures, never for *business* failures.

## Functional requirements

1. `POST /api/inquiries` — accepts an inquiry request, returns the result (design the request/response shape yourself, keep it small and sensible).
2. At least **2 providers**, each with a configurable **priority**.
3. Each provider call has a **timeout**; a timeout counts as a technical failure.
4. **Failover behavior**:
   - If a provider fails technically (timeout, connection error, unhandled exception) → try the next provider in priority order.
   - If a provider returns a **valid business response** — even if that response represents a business-level error (e.g. "inquiry not found", "invalid input") — **do not fail over**. That result is final and gets returned/stored as-is.
5. **Caching**: cache successful results for a configurable duration. Support a way to bypass the cache per-request (e.g. a query/header flag) to force a fresh call.
6. **Duplicate/concurrent request protection**: if the same inquiry is requested again while it's already in flight (or was already completed recently), don't re-run the whole provider pipeline — reuse the in-progress or cached result. Handle this safely under concurrency (no duplicate DB rows, no double provider calls for the same key).
7. **Persistence (SQL Server)** — store per inquiry:
   - the request payload
   - status (e.g. pending/completed/failed)
   - final result
   - which provider ultimately served it
   - created/completed timestamps
   - error info if it failed
   - an **attempt history** per provider (which providers were tried, in what order, outcome, timing) — this needs to be queryable later for evaluating provider reliability.
8. **Logging**: structured logging around provider calls and request handling, enough to trace what happened for any given inquiry (which providers were tried, why failover triggered, timings).
9. **Error handling**: consistent, centralized handling of technical vs. business errors — don't let exceptions leak as raw 500s with stack traces.
10. **Unit tests** for the core logic — the failover/orchestration service and provider selection are the most important things to cover. Don't aim for 100% coverage of everything; focus on the interesting logic.

## Fake providers

Implement **2 fake/mock providers** (no real external calls). Each should be configurable (via request input, config, or a simple in-memory toggle) to simulate:
- a normal successful response
- a business error response (valid response, error semantics)
- a technical failure (thrown exception / connection failure)
- a timeout (artificial delay past the configured timeout)

The point is that failover behavior must be demonstrable and testable end-to-end using these fakes — don't make me swap in real HTTP calls to prove it works.

## Tech stack

- C#, .NET 8+
- ASP.NET Core Web API
- SQL Server (EF Core or Dapper — your call, pick one and be consistent, don't mix them without reason)
- Git repo with sensible commit history (not one giant commit)

## Docker

- Provide a `Dockerfile` for the API.
- Provide a `docker-compose.yml` that runs the API **and** a SQL Server container together, so the whole thing comes up with `docker compose up` with no manual setup.
- Include DB migrations/schema creation running automatically on startup (or a clearly documented one-liner), so a fresh clone + `docker compose up` is enough to try it.

## Architecture guidance

- Keep it simple: a clean separation between API layer, the orchestration/failover logic, provider abstraction, caching, and persistence — but don't invent a mini-framework. No CQRS/MediatR/generic-repository-on-top-of-EF ceremony unless it genuinely earns its place for a project this size. A senior engineer reviewing this should see clear, boring, correct code, not a showcase of patterns.
- One interface for "a provider" that both fakes implement; the orchestrator iterates providers by priority and applies the failover rule above.
- Cache can be in-memory (e.g. `IMemoryDistributedCache`/`IMemoryCache`) — no need for Redis unless you think it meaningfully improves the answer.
- Concurrency protection can be a simple key-based lock/semaphore per inquiry key, or a DB-level uniqueness/idempotency check — pick whichever is simplest to reason about and explain.

## Code style — this is important

Write it so it reads like a human engineer wrote it under normal time pressure, not like an AI generated it:
- No excessive XML doc comments on every method, no comments restating what the code obviously does.
- No defensive over-engineering (extra interfaces with a single implementation "for testability" unless actually used in tests; no speculative extensibility for requirements that don't exist).
- Consistent, ordinary naming — no overly clever or overly generic names.
- Prefer a handful of well-organized files/folders over dozens of tiny near-empty ones.
- It's fine to leave a couple of pragmatic TODO/assumption notes in the README rather than gold-plating edge cases nobody asked about.

## Deliverables

- Full project source, buildable and runnable via Docker.
- SQL script(s) / migrations for the schema.
- Unit tests (runnable via `dotnet test`).
- `README.md` covering:
  - how to run the project (Docker instructions primarily)
  - a short architecture overview
  - key technical decisions and why
  - assumptions made where the spec was ambiguous

Ask me if anything in these requirements is ambiguous before making a big structural decision — otherwise proceed and just note the assumption in the README.
