using Microsoft.EntityFrameworkCore;

namespace InquiryService.Api.Infrastructure;

public static class DbInitializer
{
    /// <summary>
    /// Applies pending EF migrations at startup. Retries briefly because the API container may
    /// come up before SQL Server is ready to accept connections.
    /// </summary>
    public static async Task InitializeAsync(InquiryDbContext db, ILogger logger, CancellationToken ct)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Migrations failed (attempt {Attempt}/{Max}); is SQL Server ready?", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}