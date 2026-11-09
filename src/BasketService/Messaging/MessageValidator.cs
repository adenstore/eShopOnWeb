using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// Runs the DataAnnotations on a message by hand. [ApiController] used to do this before an action
/// ran; nothing does it for a queue consumer, and the domain services have no guard clauses for
/// these values because they never needed them.
/// </summary>
public static class MessageValidator
{
    /// <returns>
    /// true if <paramref name="message"/> is valid; otherwise false, with <paramref name="error"/>
    /// set to a single human-readable line for the log and the dead-letter reason.
    /// </returns>
    public static bool TryValidate(object message, out string error)
    {
        var results = new List<ValidationResult>();

        // validateAllProperties: true — without it only [Required] runs, and every [Range] on the
        // commands would be silently skipped.
        if (Validator.TryValidateObject(message, new ValidationContext(message), results, validateAllProperties: true))
        {
            error = string.Empty;
            return true;
        }

        error = string.Join("; ", results.Select(r => r.ErrorMessage));
        return false;
    }
}
