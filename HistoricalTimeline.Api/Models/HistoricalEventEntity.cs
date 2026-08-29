using Azure;
using Azure.Data.Tables;

namespace HistoricalTimeline.Api.Models;

public sealed class HistoricalEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageBlobName { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? StartDateText { get; set; }
    public string? EndDateText { get; set; }
}
