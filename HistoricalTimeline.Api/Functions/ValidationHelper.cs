using Microsoft.AspNetCore.Mvc;

namespace HistoricalTimeline.Api.Functions;

internal static class ValidationHelper
{
    internal static IActionResult ValidationError(string field, string message) =>
        new BadRequestObjectResult(new { errors = new Dictionary<string, string[]> { [field] = [message] } });

    internal static void AddRequiredStringError(
        IDictionary<string, string[]> errors,
        string name,
        string value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = [$"{name} is required."];
        }
        else if (value.Length > maximumLength)
        {
            errors[name] = [$"{name} cannot exceed {maximumLength} characters."];
        }
    }
}
