using Polly;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Opgave 3B – håndtering af eventual consistency med polling.
///
/// RabbitMQ-kommunikation er asynkron: når HTTP-kaldet returnerer, er beskeden måske ikke
/// nået frem eller behandlet endnu. En test kan derfor ikke tjekke resultatet med det samme.
/// I stedet spørger den igen med en kort pause, indtil tjekket lykkes eller forsøgene er brugt op.
/// </summary>
public static class Eventually
{
    /// <summary>Antal NYE forsøg efter det første. 1 + 5 = højst 6 tjek i alt.</summary>
    public const int Retries = 5;

    /// <summary>Pause mellem hvert forsøg. Med 5 forsøg venter testen højst ca. 5 sekunder.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Kører <paramref name="check"/> indtil den returnerer true, eller forsøgene er brugt op.
    /// Returnerer resultatet af det sidste forsøg, så testen kan asserte på det.
    /// </summary>
    public static Task<bool> TrueAsync(Func<Task<bool>> check) =>
        Policy
            // Polly ser normalt på exceptions; her reagerer den i stedet på resultatet:
            // "false" betyder "ikke klar endnu, prøv igen".
            .HandleResult<bool>(success => !success)
            // Prøv igen op til 5 gange og vent 1 sekund før hvert nyt forsøg.
            .WaitAndRetryAsync(Retries, _ => Delay)
            // Kør selve tjekket gennem politikken.
            .ExecuteAsync(check);
}
