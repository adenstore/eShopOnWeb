using Polly;

namespace Microsoft.eShopWeb.BasketService.IntegrationTests;

/// <summary>
/// Polled waits for assertions that cannot be made immediately.
/// <para>
/// Messaging is not request/response: publishing a command returns as soon as the broker has the
/// message, not when the service has acted on it. Asserting straight after a publish measures the
/// speed of the machine rather than the behaviour of the service — it passes on a fast laptop and
/// fails in CI. So the assertion is re-checked until it holds or the budget runs out.
/// </para>
/// <para>
/// Each method returns whether it succeeded rather than throwing, so the caller does the asserting
/// and the failure message says what was actually being waited for.
/// </para>
/// </summary>
public static class PolledWait
{
    private const int Attempts = 10;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>Re-checks <paramref name="condition"/> until it is true. False means it never was.</summary>
    public static Task<bool> UntilAsync(Func<Task<bool>> condition) =>
        Policy.HandleResult<bool>(success => !success)
            .WaitAndRetryAsync(Attempts, _ => Interval)
            .ExecuteAsync(async () =>
            {
                try
                {
                    return await condition();
                }
                catch
                {
                    // A queue the service has not declared yet throws rather than returning false,
                    // and that is still just "not true yet".
                    return false;
                }
            });

    /// <summary>
    /// Re-reads until <paramref name="read"/> produces a value, and returns it — or null if it
    /// never did.
    /// </summary>
    public static async Task<T?> ForValueAsync<T>(Func<Task<T?>> read) where T : class
    {
        T? result = null;

        await UntilAsync(async () =>
        {
            result = await read();
            return result is not null;
        });

        return result;
    }
}
