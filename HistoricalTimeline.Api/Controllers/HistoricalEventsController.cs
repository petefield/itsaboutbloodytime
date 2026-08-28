using HistoricalTimeline.Api.Models;
using HistoricalTimeline.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistoricalTimeline.Api.Controllers;

[ApiController]
[Route("api/historical-events")]
public sealed class HistoricalEventsController(
    HistoricalEventStore eventStore,
    IWebHostEnvironment environment) : ControllerBase
{
    private const long MaximumImageSize = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageTypes =
        ["image/jpeg", "image/png", "image/gif", "image/webp"];

    [HttpGet]
    public ActionResult<IReadOnlyCollection<HistoricalEvent>> GetAll() =>
        Ok(eventStore.GetAll());

    [HttpGet("{id:guid}")]
    public ActionResult<HistoricalEvent> Get(Guid id)
    {
        var historicalEvent = eventStore.Get(id);
        return historicalEvent is null ? NotFound() : Ok(historicalEvent);
    }

    [HttpPost]
    public async Task<ActionResult<HistoricalEvent>> Create([FromForm] HistoricalEventRequest request)
    {
        if (request.Image is null)
        {
            ModelState.AddModelError(nameof(request.Image), "An image is required.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var imageUrl = await SaveImageAsync(request.Image!);
        if (imageUrl is null)
        {
            return ValidationProblem(ModelState);
        }

        var historicalEvent = eventStore.Add(new HistoricalEvent
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Description = request.Description.Trim(),
            ImageUrl = imageUrl,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        });

        return CreatedAtAction(nameof(Get), new { historicalEvent.Id }, historicalEvent);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HistoricalEvent>> Update(Guid id, [FromForm] HistoricalEventRequest request)
    {
        var existing = eventStore.Get(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var imageUrl = request.Image is null ? existing.ImageUrl : await SaveImageAsync(request.Image);
        if (request.Image is not null && imageUrl is null)
        {
            return ValidationProblem(ModelState);
        }

        var updated = new HistoricalEvent
        {
            Id = id,
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Description = request.Description.Trim(),
            ImageUrl = imageUrl,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };

        return eventStore.Update(updated) ? Ok(updated) : NotFound();
    }

    private async Task<string?> SaveImageAsync(IFormFile image)
    {
        if (image.Length is 0 or > MaximumImageSize)
        {
            ModelState.AddModelError(nameof(image), "Images must be between 1 byte and 5 MB.");
            return null;
        }

        if (!AllowedImageTypes.Contains(image.ContentType))
        {
            ModelState.AddModelError(nameof(image), "Supported formats are JPEG, PNG, GIF, and WebP.");
            return null;
        }

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        if (!allowedExtensions.Contains(extension))
        {
            ModelState.AddModelError(nameof(image), "The image filename must have a supported extension.");
            return null;
        }

        var uploadDirectory = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(uploadDirectory);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var output = System.IO.File.Create(Path.Combine(uploadDirectory, fileName));
        await image.CopyToAsync(output);
        return $"/uploads/{fileName}";
    }
}
