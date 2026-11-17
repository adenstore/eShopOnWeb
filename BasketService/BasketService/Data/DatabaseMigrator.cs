using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.BasketService.Data;

/// <summary>
/// Creates <c>Microsoft.eShopOnWeb.BasketDb</c> and brings it up to the latest migration, before
/// the queue consumers start.
/// <para>
/// The ordering is the point. The consumers are hosted services, so they do not start until
/// <c>app.Run()</c>; migrating before that means this service never consumes a command it cannot
/// commit. Commands are one-way, so a command that fails on a missing table is dead-lettered to the
/// "invalid" queue with no caller left to tell — the shopper just sees nothing happen.
/// </para>
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>
    /// Covers a cold SQL Server comfortably. Measured on this machine: the compose
    /// <c>azure-sql-edge</c> container logged "ready for client connections" <b>42 seconds</b> after
    /// it started, and finished recovery at 48. A loaded machine or an image that has to be pulled
    /// is slower still, so the budget is minutes rather than seconds.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Retries until <see cref="Budget"/> runs out, then rethrows.
    /// <para>
    /// Retrying is not optional here, and <c>EnableRetryOnFailure()</c> on the DbContext does not
    /// cover it: a SQL Server that is still booting fails the TCP pre-login handshake, which the
    /// SqlClient transient-error detector does not classify as transient, so the execution strategy
    /// passes it straight through and the exception comes out of <c>MigrateAsync</c> unhandled —
    /// which stops the host. Verified by starting this service against a freshly started compose
    /// <c>sqlserver</c>: it died after ~20s with "error occurred during the pre-login handshake",
    /// 22 seconds before the database was ready. <c>depends_on</c> waits for the container to start,
    /// not for SQL Server to finish booting, so that race is the normal case rather than an exotic
    /// one — the same reason the consumers in <c>Messaging/RabbitMqConsumerService</c> retry their
    /// first attach.
    /// </para>
    /// <para>
    /// On exhaustion it throws rather than starting anyway. A container that dies is visible; a
    /// service that is up and quietly dead-lettering every command it receives is not. Note that
    /// <c>docker-compose.yml</c> sets no restart policy, so a death here is final until someone
    /// looks — which is why the budget is generous.
    /// </para>
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<BasketDbContext>().Database;

        // The integration tests replace the DbContext with the in-memory provider, which has no
        // migrations and throws on MigrateAsync.
        if (!database.IsRelational())
        {
            logger.LogInformation("Skipping migrations: the basket database is not relational");
            return;
        }

        var deadline = DateTimeOffset.UtcNow + Budget;
        var delay = FirstRetryDelay;

        while (true)
        {
            try
            {
                await database.MigrateAsync(cancellationToken);
                logger.LogInformation("Basket database is up to date");
                return;
            }
            catch (Exception ex)
            {
                // Deliberately broad. At startup there is nothing else to be doing, and telling a
                // database that is not up yet from one that will never work is guesswork — a
                // migration that fails deterministically just takes the whole budget before it
                // reports the same error. Every attempt logs it, so the log says which it was.
                if (DateTimeOffset.UtcNow + delay >= deadline)
                {
                    logger.LogCritical(ex, "Could not migrate the basket database within {Budget}s; giving up",
                        Budget.TotalSeconds);
                    throw;
                }

                logger.LogWarning(ex, "Could not migrate the basket database; retrying in {Delay}s",
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }
}
