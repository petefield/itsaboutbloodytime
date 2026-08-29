using System.Text.Json;
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
        TimelineRequest? timelineRequest;
        try
        {
            timelineRequest = await request.ReadFromJsonAsync<TimelineRequest>();
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "A valid JSON request is required." });
        }

        var title = timelineRequest?.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return ValidationError("title", "title is required.");
        }

        if (title.Length > 200)
        {
            return ValidationError("title", "title cannot exceed 200 characters.");
        }

        var timeline = await eventStore.AddTimelineAsync(title);
        return new CreatedResult($"/api/timelines/{timeline.Id:N}", timeline);
    }

    private static IActionResult ValidationError(string field, string message) =>
        new BadRequestObjectResult(new { errors = new Dictionary<string, string[]> { [field] = [message] } });

    private sealed record TimelineRequest(string? Title);
}
