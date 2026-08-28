using System.Collections.Concurrent;
using HistoricalTimeline.Api.Models;

namespace HistoricalTimeline.Api.Services;

public sealed class HistoricalEventStore
{
    private readonly ConcurrentDictionary<Guid, HistoricalEvent> events = new();

    public IReadOnlyCollection<HistoricalEvent> GetAll() =>
        events.Values.OrderBy(item => item.StartDate).ToArray();

    public HistoricalEvent? Get(Guid id) =>
        events.TryGetValue(id, out var historicalEvent) ? historicalEvent : null;

    public HistoricalEvent Add(HistoricalEvent historicalEvent)
    {
        events[historicalEvent.Id] = historicalEvent;
        return historicalEvent;
    }

    public bool Update(HistoricalEvent historicalEvent) =>
        events.TryUpdate(historicalEvent.Id, historicalEvent, Get(historicalEvent.Id)!);
}
