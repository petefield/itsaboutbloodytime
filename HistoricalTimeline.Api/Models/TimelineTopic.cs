namespace HistoricalTimeline.Api.Models;

public sealed class TimelineTopic
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
}
