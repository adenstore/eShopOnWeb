using Polly;

namespace Microsoft.eShopWeb.LagerService.Tests.Messaging;

/// <summary>
/// Opgave 3B: messages are handled in the background, so a test cannot assert right after the request.
/// Instead it asks again until the check succeeds or the attempts run out (eventual consistency).
/// </summary>
public static class Eventually
{
    public const int Retries = 5;
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    /// <summary>Runs <paramref name="check"/> up to 1 + 5 times, 1 second apart. Returns the last result.</summary>
    public static Task<bool> TrueAsync(Func<Task<bool>> check) =>
        Policy
            .HandleResult<bool>(success => !success)
            .WaitAndRetryAsync(Retries, _ => Delay)
            .ExecuteAsync(check);
}
