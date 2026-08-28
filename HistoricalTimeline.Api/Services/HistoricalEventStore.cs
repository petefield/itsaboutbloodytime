using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HistoricalTimeline.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace HistoricalTimeline.Api.Services;

public sealed class HistoricalEventStore
{
    private const string PartitionKey = "events";
    private readonly TableClient eventTable;
    private readonly BlobContainerClient imageContainer;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public HistoricalEventStore(IConfiguration configuration)
    {
        var connectionString = configuration["StorageConnectionString"]
            ?? throw new InvalidOperationException("StorageConnectionString must be configured.");

        eventTable = new TableClient(connectionString, "HistoricalEvents");
        imageContainer = new BlobContainerClient(connectionString, "event-images");
    }

    public async Task<IReadOnlyCollection<HistoricalEvent>> GetAllAsync()
    {
        await EnsureInitializedAsync();
        var historicalEvents = new List<HistoricalEvent>();

        await foreach (var entity in eventTable.QueryAsync<HistoricalEventEntity>(
            entity => entity.PartitionKey == PartitionKey))
        {
            historicalEvents.Add(ToModel(entity));
        }

        return historicalEvents.OrderBy(historicalEvent => historicalEvent.StartDate).ToArray();
    }

    public async Task<HistoricalEvent?> GetAsync(Guid id)
    {
        await EnsureInitializedAsync();

        try
        {
            var response = await eventTable.GetEntityAsync<HistoricalEventEntity>(PartitionKey, id.ToString("N"));
            return ToModel(response.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<HistoricalEvent> AddAsync(HistoricalEvent historicalEvent)
    {
        await EnsureInitializedAsync();
        var entity = ToEntity(historicalEvent);
        await eventTable.AddEntityAsync(entity);
        return ToModel(entity);
    }

    public async Task<bool> UpdateAsync(HistoricalEvent historicalEvent)
    {
        await EnsureInitializedAsync();

        try
        {
            await eventTable.UpdateEntityAsync(ToEntity(historicalEvent), ETag.All, TableUpdateMode.Replace);
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

    public async Task<BlobDownloadStreamingResult?> DownloadImageAsync(string blobName)
    {
        await EnsureInitializedAsync();

        try
        {
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
            await imageContainer.CreateIfNotExistsAsync(PublicAccessType.None);

            var hasEvents = false;
            await foreach (var _ in eventTable.QueryAsync<HistoricalEventEntity>(
                entity => entity.PartitionKey == PartitionKey,
                maxPerPage: 1))
            {
                hasEvents = true;
                break;
            }

            if (!hasEvents)
            {
                foreach (var historicalEvent in SeedEvents)
                {
                    try
                    {
                        await eventTable.AddEntityAsync(ToEntity(historicalEvent));
                    }
                    catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status409Conflict)
                    {
                    }
                }
            }

            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static HistoricalEventEntity ToEntity(HistoricalEvent historicalEvent) =>
        new()
        {
            PartitionKey = PartitionKey,
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

    private static HistoricalEvent ToModel(HistoricalEventEntity entity) =>
        new()
        {
            Id = Guid.ParseExact(entity.RowKey, "N"),
            Title = entity.Title,
            Summary = entity.Summary,
            Description = entity.Description,
            ImageUrl = entity.ImageBlobName is null
                ? null
                : $"/api/historical-events/images/{entity.ImageBlobName}",
            StartDate = DateOnly.FromDateTime(entity.StartDate),
            EndDate = DateOnly.FromDateTime(entity.EndDate)
        };

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

    private static IReadOnlyCollection<HistoricalEvent> SeedEvents =>
    [
        CreateEvent(
            "Invasion of Poland",
            "Germany's invasion of Poland began the Second World War in Europe.",
            "German forces invaded Poland on 1 September 1939. The United Kingdom and France declared war on Germany two days later, and Poland was defeated after fighting on two fronts.",
            new DateOnly(1939, 9, 1),
            new DateOnly(1939, 10, 6)),
        CreateEvent(
            "Battle of Britain",
            "The Royal Air Force defended the United Kingdom against German air attacks.",
            "From the summer into the autumn of 1940, the Luftwaffe attempted to gain air superiority over southern England. The RAF's successful defence prevented a planned German invasion of Britain.",
            new DateOnly(1940, 7, 10),
            new DateOnly(1940, 10, 31)),
        CreateEvent(
            "Operation Barbarossa",
            "Germany invaded the Soviet Union, opening the Eastern Front.",
            "On 22 June 1941, Germany and its allies launched the largest land invasion in history against the Soviet Union. The campaign failed to secure a decisive victory before the winter.",
            new DateOnly(1941, 6, 22),
            new DateOnly(1941, 12, 5)),
        CreateEvent(
            "Battle of Moscow",
            "Soviet forces halted Germany's advance on Moscow.",
            "German forces began their offensive towards Moscow in October 1941. A Soviet counteroffensive in December pushed German troops back from the capital during the winter.",
            new DateOnly(1941, 10, 2),
            new DateOnly(1942, 1, 7)),
        CreateEvent(
            "Attack on Pearl Harbor",
            "Japan's attack on the United States Pacific Fleet brought the United States into the war.",
            "Japanese aircraft attacked the US naval base at Pearl Harbor, Hawaii, on 7 December 1941. The following day, the United States declared war on Japan.",
            new DateOnly(1941, 12, 7),
            new DateOnly(1941, 12, 7)),
        CreateEvent(
            "Battle of Midway",
            "A decisive naval battle shifted the balance of power in the Pacific.",
            "US naval forces defeated a Japanese fleet near Midway Atoll in June 1942, sinking four Japanese aircraft carriers while losing one of their own.",
            new DateOnly(1942, 6, 4),
            new DateOnly(1942, 6, 7)),
        CreateEvent(
            "Second Battle of El Alamein",
            "Allied forces defeated Axis armies in Egypt.",
            "British-led Allied forces under General Bernard Montgomery defeated the German and Italian Panzer Army Africa. The victory marked a turning point in the North African campaign.",
            new DateOnly(1942, 10, 23),
            new DateOnly(1942, 11, 11)),
        CreateEvent(
            "Operation Torch",
            "Allied forces landed in French North Africa.",
            "American and British forces landed in Morocco and Algeria in November 1942. The campaign opened a second front in North Africa while the Battle of El Alamein was still under way.",
            new DateOnly(1942, 11, 8),
            new DateOnly(1942, 11, 16)),
        CreateEvent(
            "D-Day Landings",
            "Allied forces established a beachhead in Normandy.",
            "On 6 June 1944, Allied forces landed on five beaches in Normandy as part of Operation Overlord. The invasion began the liberation of western Europe from Nazi occupation.",
            new DateOnly(1944, 6, 6),
            new DateOnly(1944, 6, 6)),
        CreateEvent(
            "Battle of Normandy",
            "Allied armies fought to break out from the Normandy beachhead.",
            "Following the D-Day landings, Allied and German forces fought across Normandy for almost three months. The campaign ended with the collapse of German forces in the Falaise pocket.",
            new DateOnly(1944, 6, 6),
            new DateOnly(1944, 8, 30)),
        CreateEvent(
            "Liberation of Paris",
            "French and Allied forces liberated Paris from German occupation.",
            "Following an uprising by the French Resistance, the French 2nd Armoured Division and US forces entered Paris. The German garrison surrendered on 25 August 1944.",
            new DateOnly(1944, 8, 19),
            new DateOnly(1944, 8, 25)),
        CreateEvent(
            "Battle of the Bulge",
            "Germany's final major offensive on the Western Front was defeated.",
            "German forces launched a surprise counteroffensive through the Ardennes in December 1944. Allied resistance and reinforcements halted the offensive by late January 1945.",
            new DateOnly(1944, 12, 16),
            new DateOnly(1945, 1, 25)),
        CreateEvent(
            "Victory in Europe Day",
            "The Allies celebrated Germany's unconditional surrender.",
            "Nazi Germany's armed forces formally surrendered to the Allies, ending the war in Europe. Celebrations took place across the United Kingdom, the United States, and other Allied nations.",
            new DateOnly(1945, 5, 8),
            new DateOnly(1945, 5, 8))
    ];

    private static HistoricalEvent CreateEvent(
        string title,
        string summary,
        string description,
        DateOnly startDate,
        DateOnly endDate) =>
        new()
        {
            Id = CreateSeedId(title),
            Title = title,
            Summary = summary,
            Description = description,
            StartDate = startDate,
            EndDate = endDate
        };

    private static Guid CreateSeedId(string title) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(title)).AsSpan(0, 16));

    public sealed class ImageValidationException(string message) : Exception(message);
}
