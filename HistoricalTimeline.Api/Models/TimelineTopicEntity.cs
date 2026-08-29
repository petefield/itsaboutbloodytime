using Azure;
using Azure.Data.Tables;

namespace HistoricalTimeline.Api.Models;

public sealed class TimelineTopicEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Title { get; set; } = string.Empty;
}
