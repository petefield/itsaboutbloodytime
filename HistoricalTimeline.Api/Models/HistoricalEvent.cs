namespace HistoricalTimeline.Api.Models;

public sealed class HistoricalEvent
{
    public required Guid Id { get; init; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public required string Description { get; set; }
    public string? ImageUrl { get; set; }
    public required string StartDate { get; set; }
    public required string EndDate { get; set; }
}
