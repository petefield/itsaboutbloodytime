using System.ComponentModel.DataAnnotations;

namespace HistoricalTimeline.Client.Models;

public sealed class HistoricalEvent
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string StartDate { get; set; } = HistoricalDate.Today;
    public string EndDate { get; set; } = HistoricalDate.Today;
}

public sealed class TimelineTopic
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public sealed class TimelineTopicForm
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;
}

public sealed class HistoricalEventForm : IValidatableObject
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Summary { get; set; } = string.Empty;

    [Required, StringLength(5_000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public string StartDate { get; set; } = HistoricalDate.Today;

    [Required]
    public string EndDate { get; set; } = HistoricalDate.Today;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HistoricalDate.TryParse(StartDate, out var startDate))
        {
            yield return new ValidationResult(
                "Use YYYY-MM-DD or a signed BCE date such as -1599-01-01.",
                [nameof(StartDate)]);
        }

        if (!HistoricalDate.TryParse(EndDate, out var endDate))
        {
            yield return new ValidationResult(
                "Use YYYY-MM-DD or a signed BCE date such as -1599-01-01.",
                [nameof(EndDate)]);
        }
        else if (HistoricalDate.TryParse(StartDate, out startDate) && endDate.Ordinal < startDate.Ordinal)
        {
            yield return new ValidationResult(
                "The end date cannot be before the start date.",
                [nameof(EndDate)]);
        }
    }
}
