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
    /// <summary>
    /// How long an assertion is given to come true.
    /// <para>
    /// Deliberately generous, because polling stops the moment the condition holds: on a healthy
    /// run this budget costs nothing, and only a genuine failure waits it out. The cost of it being
    /// too small is the opposite shape — an intermittent red build that says nothing about the code.
    /// </para>
    /// <para>
    /// 5 seconds was not enough. This suite runs a RabbitMQ container alongside the service, and on
    /// a machine that is busy with something else — a parallel build locally, two shared cores on a
    /// CI runner — the round trip from publishing a command to seeing the event on
    /// "basket-events" has been measured at over 4 of those 5 seconds. That is slow, but it is not
    /// wrong: nothing in the design promises a deadline, so a test that fails on it is measuring the
    /// machine rather than the service.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    // Short enough that a fast reply is still observed quickly: the budget sets the ceiling, this
    // sets how soon the common case gets out.
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private static readonly int Attempts = (int)(Budget / Interval);

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
