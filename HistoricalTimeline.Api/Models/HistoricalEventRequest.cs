using System.ComponentModel.DataAnnotations;

namespace HistoricalTimeline.Api.Models;

public sealed class HistoricalEventRequest : IValidatableObject
{
    [Required, StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string Summary { get; init; } = string.Empty;

    [Required, StringLength(5_000)]
    public string Description { get; init; } = string.Empty;

    public IFormFile? Image { get; init; }

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
        {
            yield return new ValidationResult(
                "The end date cannot be before the start date.",
                [nameof(EndDate)]);
        }
    }
}
