using HistoricalTimeline.Api.Models;
using HistoricalTimeline.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HistoricalTimeline.Api.Functions;

public sealed class TimelinesFunctions(HistoricalEventStore eventStore)
{
    [Function(nameof(GetTimelines))]
    public async Task<IActionResult> GetTimelines(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timelines")] HttpRequest request) =>
        new OkObjectResult(await eventStore.GetTimelinesAsync());

    [Function(nameof(GetTimeline))]
    public async Task<IActionResult> GetTimeline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timelines/{timelineId:guid}")] HttpRequest request,
        Guid timelineId)
    {
        var timeline = await eventStore.GetTimelineAsync(timelineId);
        return timeline is null ? new NotFoundResult() : new OkObjectResult(timeline);
    }

    [Function(nameof(CreateTimeline))]
    public async Task<IActionResult> CreateTimeline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "timelines")] HttpRequest request)
    {
        var (timelineRequest, error) = await ReadTimelineRequestAsync(request);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var imageBlobName = timelineRequest!.Image is null
                ? null
                : await eventStore.UploadImageAsync(timelineRequest.Image);
            var timeline = await eventStore.AddTimelineAsync(
                timelineRequest.Title,
                timelineRequest.Description,
                imageBlobName);
            return new CreatedResult($"/api/timelines/{timeline.Id:N}", timeline);
        }
        catch (HistoricalEventStore.ImageValidationException exception)
        {
            return ValidationError("image", exception.Message);
        }
    }

    [Function(nameof(UpdateTimeline))]
    public async Task<IActionResult> UpdateTimeline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "timelines/{timelineId:guid}")] HttpRequest request,
        Guid timelineId)
    {
        var existing = await eventStore.GetTimelineAsync(timelineId);
        if (existing is null)
        {
            return new NotFoundResult();
        }

        var (timelineRequest, error) = await ReadTimelineRequestAsync(request);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var imageUrl = timelineRequest!.Image is null
                ? existing.ImageUrl
                : await eventStore.UploadImageAsync(timelineRequest.Image);
            var timeline = new TimelineTopic
            {
                Id = timelineId,
                Title = timelineRequest.Title,
                Description = timelineRequest.Description,
                ImageUrl = imageUrl
            };
            if (!await eventStore.UpdateTimelineAsync(timeline))
            {
                return new NotFoundResult();
            }

            return new OkObjectResult(await eventStore.GetTimelineAsync(timelineId));
        }
        catch (HistoricalEventStore.ImageValidationException exception)
        {
            return ValidationError("image", exception.Message);
        }
    }

    [Function(nameof(DeleteTimeline))]
    public async Task<IActionResult> DeleteTimeline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "timelines/{timelineId:guid}")] HttpRequest request,
        Guid timelineId) =>
        await eventStore.DeleteTimelineAsync(timelineId)
            ? new NoContentResult()
            : new NotFoundResult();

    [Function(nameof(GetTimelineImage))]
    public async Task<IActionResult> GetTimelineImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timelines/{timelineId:guid}/images/{blobName}")] HttpRequest request,
        Guid timelineId,
        string blobName)
    {
        var image = await eventStore.DownloadTimelineImageAsync(timelineId, blobName);
        return image is null
            ? new NotFoundResult()
            : new FileStreamResult(image.Content, image.Details.ContentType);
    }

    private static async Task<(TimelineRequest? Request, IActionResult? Error)> ReadTimelineRequestAsync(
        HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return (null, new BadRequestObjectResult(new { error = "A multipart form request is required." }));
        }

        var form = await request.ReadFormAsync();
        var errors = new Dictionary<string, string[]>();
        var title = form["title"].ToString().Trim();
        var description = form["description"].ToString().Trim();

        AddRequiredStringError(errors, "title", title, 200);
        AddRequiredStringError(errors, "description", description, 500);

        return errors.Count > 0
            ? (null, new BadRequestObjectResult(new { errors }))
            : (new TimelineRequest(title, description, form.Files.GetFile("image")), null);
    }

    private static IActionResult ValidationError(string field, string message) =>
        new BadRequestObjectResult(new { errors = new Dictionary<string, string[]> { [field] = [message] } });

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

    private sealed record TimelineRequest(string Title, string Description, IFormFile? Image);
}
