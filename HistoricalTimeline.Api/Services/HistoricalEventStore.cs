using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HistoricalTimeline.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HistoricalTimeline.Api.Services;

public sealed class HistoricalEventStore
{
    private const string TimelinePartitionKey = "timelines";
    private readonly TableClient eventTable;
    private readonly TableClient timelineTable;
    private readonly BlobContainerClient imageContainer;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public HistoricalEventStore(IConfiguration configuration)
    {
        var connectionString = configuration["StorageConnectionString"]
            ?? throw new InvalidOperationException("StorageConnectionString must be configured.");

        eventTable = new TableClient(connectionString, "HistoricalEvents");
        timelineTable = new TableClient(connectionString, "TimelineTopics");
        imageContainer = new BlobContainerClient(connectionString, "event-images");
    }

    public async Task<IReadOnlyCollection<TimelineTopic>> GetTimelinesAsync()
    {
        await EnsureInitializedAsync();
        var timelines = new List<TimelineTopic>();

        await foreach (var entity in timelineTable.QueryAsync<TimelineTopicEntity>(
            entity => entity.PartitionKey == TimelinePartitionKey))
        {
            timelines.Add(ToModel(entity));
        }

        return timelines.OrderBy(timeline => timeline.Title).ToArray();
    }

    public async Task<TimelineTopic?> GetTimelineAsync(Guid timelineId)
    {
        await EnsureInitializedAsync();

        try
        {
            var response = await timelineTable.GetEntityAsync<TimelineTopicEntity>(
                TimelinePartitionKey,
                timelineId.ToString("N"));
            return ToModel(response.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<TimelineTopic> AddTimelineAsync(
        string title,
        string description,
        string? imageBlobName)
    {
        await EnsureInitializedAsync();

        var entity = new TimelineTopicEntity
        {
            PartitionKey = TimelinePartitionKey,
            RowKey = Guid.NewGuid().ToString("N"),
            Title = title,
            Description = description,
            ImageBlobName = imageBlobName
        };
        await timelineTable.AddEntityAsync(entity);
        return ToModel(entity);
    }

    public async Task<bool> UpdateTimelineAsync(TimelineTopic timeline)
    {
        await EnsureInitializedAsync();

        try
        {
            await timelineTable.UpdateEntityAsync(
                ToEntity(timeline),
                ETag.All,
                TableUpdateMode.Replace);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return false;
        }
    }

    public async Task<bool> TimelineExistsAsync(Guid timelineId) =>
        await GetTimelineAsync(timelineId) is not null;

    public async Task<IReadOnlyCollection<HistoricalEvent>> GetAllAsync(Guid timelineId)
    {
        await EnsureInitializedAsync();
        var historicalEvents = new List<HistoricalEvent>();

        await foreach (var entity in eventTable.QueryAsync<HistoricalEventEntity>(
            entity => entity.PartitionKey == GetTimelinePartitionKey(timelineId)))
        {
            historicalEvents.Add(ToModel(timelineId, entity));
        }

        return historicalEvents.OrderBy(historicalEvent => historicalEvent.StartDate).ToArray();
    }

    public async Task<HistoricalEvent?> GetAsync(Guid timelineId, Guid id)
    {
        await EnsureInitializedAsync();

        try
        {
            var response = await eventTable.GetEntityAsync<HistoricalEventEntity>(
                GetTimelinePartitionKey(timelineId),
                id.ToString("N"));
            return ToModel(timelineId, response.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<HistoricalEvent> AddAsync(Guid timelineId, HistoricalEvent historicalEvent)
    {
        await EnsureInitializedAsync();
        var entity = ToEntity(timelineId, historicalEvent);
        await eventTable.AddEntityAsync(entity);
        return ToModel(timelineId, entity);
    }

    public async Task<bool> UpdateAsync(Guid timelineId, HistoricalEvent historicalEvent)
    {
        await EnsureInitializedAsync();

        try
        {
            await eventTable.UpdateEntityAsync(
                ToEntity(timelineId, historicalEvent),
                ETag.All,
                TableUpdateMode.Replace);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return false;
        }
    }

    public async Task<string> UploadImageAsync(IFormFile image)
    {
        var extension = ValidateImage(image);
        await EnsureInitializedAsync();

        var blobName = $"{Guid.NewGuid():N}{extension}";
        var blobClient = imageContainer.GetBlobClient(blobName);
        await using var imageStream = image.OpenReadStream();
        await blobClient.UploadAsync(imageStream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = image.ContentType }
        });

        return blobName;
    }

    public async Task<BlobDownloadStreamingResult?> DownloadImageAsync(Guid timelineId, string blobName)
    {
        await EnsureInitializedAsync();

        var imageBelongsToTimeline = false;
        await foreach (var entity in eventTable.QueryAsync<HistoricalEventEntity>(
            entity => entity.PartitionKey == GetTimelinePartitionKey(timelineId)))
        {
            if (string.Equals(entity.ImageBlobName, blobName, StringComparison.Ordinal))
            {
                imageBelongsToTimeline = true;
                break;
            }
        }

        if (!imageBelongsToTimeline)
        {
            return null;
        }

        try
        {
            return (await imageContainer.GetBlobClient(blobName).DownloadStreamingAsync()).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<BlobDownloadStreamingResult?> DownloadTimelineImageAsync(
        Guid timelineId,
        string blobName)
    {
        await EnsureInitializedAsync();

        try
        {
            var timeline = await timelineTable.GetEntityAsync<TimelineTopicEntity>(
                TimelinePartitionKey,
                timelineId.ToString("N"));
            if (!string.Equals(timeline.Value.ImageBlobName, blobName, StringComparison.Ordinal))
            {
                return null;
            }

            return (await imageContainer.GetBlobClient(blobName).DownloadStreamingAsync()).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync();
        try
        {
            if (initialized)
            {
                return;
            }

            await eventTable.CreateIfNotExistsAsync();
            await timelineTable.CreateIfNotExistsAsync();
            await imageContainer.CreateIfNotExistsAsync(PublicAccessType.None);

            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static HistoricalEventEntity ToEntity(Guid timelineId, HistoricalEvent historicalEvent) =>
        new()
        {
            PartitionKey = GetTimelinePartitionKey(timelineId),
            RowKey = historicalEvent.Id.ToString("N"),
            Title = historicalEvent.Title,
            Summary = historicalEvent.Summary,
            Description = historicalEvent.Description,
            ImageBlobName = historicalEvent.ImageUrl is null
                ? null
                : Path.GetFileName(historicalEvent.ImageUrl),
            StartDate = historicalEvent.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndDate = historicalEvent.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

    private static HistoricalEvent ToModel(Guid timelineId, HistoricalEventEntity entity) =>
        new()
        {
            Id = Guid.ParseExact(entity.RowKey, "N"),
            Title = entity.Title,
            Summary = entity.Summary,
            Description = entity.Description,
            ImageUrl = entity.ImageBlobName is null
                ? null
                : $"/api/timelines/{timelineId:N}/historical-events/images/{entity.ImageBlobName}",
            StartDate = DateOnly.FromDateTime(entity.StartDate),
            EndDate = DateOnly.FromDateTime(entity.EndDate)
        };

    private static TimelineTopic ToModel(TimelineTopicEntity entity) =>
        new()
        {
            Id = Guid.ParseExact(entity.RowKey, "N"),
            Title = entity.Title,
            Description = entity.Description,
            ImageUrl = entity.ImageBlobName is null
                ? null
                : $"/api/timelines/{entity.RowKey}/images/{entity.ImageBlobName}"
        };

    private static TimelineTopicEntity ToEntity(TimelineTopic timeline) =>
        new()
        {
            PartitionKey = TimelinePartitionKey,
            RowKey = timeline.Id.ToString("N"),
            Title = timeline.Title,
            Description = timeline.Description,
            ImageBlobName = timeline.ImageUrl is null
                ? null
                : Path.GetFileName(timeline.ImageUrl)
        };

    private static string GetTimelinePartitionKey(Guid timelineId) => timelineId.ToString("N");

    private static string ValidateImage(IFormFile image)
    {
        const long maximumImageSize = 5 * 1024 * 1024;
        var allowedImageTypes = new HashSet<string>
        {
            "image/jpeg", "image/png", "image/gif", "image/webp"
        };
        var allowedExtensions = new HashSet<string>
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp"
        };

        if (image.Length is 0 or > maximumImageSize)
        {
            throw new ImageValidationException("Images must be between 1 byte and 5 MB.");
        }

        if (!allowedImageTypes.Contains(image.ContentType))
        {
            throw new ImageValidationException("Supported formats are JPEG, PNG, GIF, and WebP.");
        }

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            throw new ImageValidationException("The image filename must have a supported extension.");
        }

        return extension;
    }

    public sealed class ImageValidationException(string message) : Exception(message);
}
