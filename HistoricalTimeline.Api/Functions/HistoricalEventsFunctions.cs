using Azure.Storage.Blobs.Models;
using HistoricalTimeline.Api.Models;
using HistoricalTimeline.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HistoricalTimeline.Api.Functions;

public sealed class HistoricalEventsFunctions(HistoricalEventStore eventStore)
{
    [Function(nameof(GetHistoricalEvents))]
    public async Task<IActionResult> GetHistoricalEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "historical-events")] HttpRequest request) =>
        new OkObjectResult(await eventStore.GetAllAsync());

    [Function(nameof(GetHistoricalEvent))]
    public async Task<IActionResult> GetHistoricalEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "historical-events/{id:guid}")] HttpRequest request,
        Guid id)
    {
        var historicalEvent = await eventStore.GetAsync(id);
        return historicalEvent is null ? new NotFoundResult() : new OkObjectResult(historicalEvent);
    }

    [Function(nameof(CreateHistoricalEvent))]
    public async Task<IActionResult> CreateHistoricalEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "historical-events")] HttpRequest request)
    {
        var (eventRequest, error) = await ReadEventRequestAsync(request);
        if (error is not null)
        {
            return error;
        }

        if (eventRequest!.Image is null)
        {
            return ValidationError("image", "An image is required.");
        }

        try
        {
            var imageBlobName = await eventStore.UploadImageAsync(eventRequest.Image);
            var historicalEvent = await eventStore.AddAsync(new HistoricalEvent
            {
                Id = Guid.NewGuid(),
                Title = eventRequest.Title,
                Summary = eventRequest.Summary,
                Description = eventRequest.Description,
                ImageUrl = imageBlobName,
                StartDate = eventRequest.StartDate,
                EndDate = eventRequest.EndDate
            });

            return new CreatedResult($"/api/historical-events/{historicalEvent.Id}", historicalEvent);
        }
        catch (HistoricalEventStore.ImageValidationException exception)
        {
            return ValidationError("image", exception.Message);
        }
    }

    [Function(nameof(UpdateHistoricalEvent))]
    public async Task<IActionResult> UpdateHistoricalEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "historical-events/{id:guid}")] HttpRequest request,
        Guid id)
    {
        var existing = await eventStore.GetAsync(id);
        if (existing is null)
        {
            return new NotFoundResult();
        }

        var (eventRequest, error) = await ReadEventRequestAsync(request);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var imageUrl = eventRequest!.Image is null
                ? existing.ImageUrl
                : await eventStore.UploadImageAsync(eventRequest.Image);
            var historicalEvent = new HistoricalEvent
            {
                Id = id,
                Title = eventRequest.Title,
                Summary = eventRequest.Summary,
                Description = eventRequest.Description,
                ImageUrl = imageUrl,
                StartDate = eventRequest.StartDate,
                EndDate = eventRequest.EndDate
            };

            if (!await eventStore.UpdateAsync(historicalEvent))
            {
                return new NotFoundResult();
            }

            return new OkObjectResult(await eventStore.GetAsync(id));
        }
        catch (HistoricalEventStore.ImageValidationException exception)
        {
            return ValidationError("image", exception.Message);
        }
    }

    [Function(nameof(GetHistoricalEventImage))]
    public async Task<IActionResult> GetHistoricalEventImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "historical-events/images/{blobName}")] HttpRequest request,
        string blobName)
    {
        var image = await eventStore.DownloadImageAsync(blobName);
        return image is null
            ? new NotFoundResult()
            : new FileStreamResult(image.Content, image.Details.ContentType);
    }

    private static async Task<(EventRequest? Request, IActionResult? Error)> ReadEventRequestAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return (null, new BadRequestObjectResult(new { error = "A multipart form request is required." }));
        }

        var form = await request.ReadFormAsync();
        var errors = new Dictionary<string, string[]>();
        var title = form["title"].ToString().Trim();
        var summary = form["summary"].ToString().Trim();
        var description = form["description"].ToString().Trim();

        AddRequiredStringError(errors, "title", title, 200);
        AddRequiredStringError(errors, "summary", summary, 500);
        AddRequiredStringError(errors, "description", description, 5_000);

        var validStartDate = DateOnly.TryParse(form["startDate"], out var startDate);
        var validEndDate = DateOnly.TryParse(form["endDate"], out var endDate);
        if (!validStartDate)
        {
            errors["startDate"] = ["A valid start date is required."];
        }

        if (!validEndDate)
        {
            errors["endDate"] = ["A valid end date is required."];
        }
        else if (validStartDate && endDate < startDate)
        {
            errors["endDate"] = ["The end date cannot be before the start date."];
        }

        return errors.Count > 0
            ? (null, new BadRequestObjectResult(new { errors }))
            : (new EventRequest(title, summary, description, startDate, endDate, form.Files.GetFile("image")), null);
    }

    private static void AddRequiredStringError(
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

    private static IActionResult ValidationError(string field, string message) =>
        new BadRequestObjectResult(new { errors = new Dictionary<string, string[]> { [field] = [message] } });

    private sealed record EventRequest(
        string Title,
        string Summary,
        string Description,
        DateOnly StartDate,
        DateOnly EndDate,
        IFormFile? Image);
}
