using System.Collections.Concurrent;
using HistoricalTimeline.Api.Models;

namespace HistoricalTimeline.Api.Services;

public sealed class HistoricalEventStore
{
    private readonly ConcurrentDictionary<Guid, HistoricalEvent> events = new();

    public HistoricalEventStore()
    {
        foreach (var historicalEvent in SeedEvents)
        {
            events[historicalEvent.Id] = historicalEvent;
        }
    }

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
            "D-Day Landings",
            "Allied forces established a beachhead in Normandy.",
            "On 6 June 1944, Allied forces landed on five beaches in Normandy as part of Operation Overlord. The invasion began the liberation of western Europe from Nazi occupation.",
            new DateOnly(1944, 6, 6),
            new DateOnly(1944, 6, 6)),
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
            Id = Guid.NewGuid(),
            Title = title,
            Summary = summary,
            Description = description,
            StartDate = startDate,
            EndDate = endDate
        };
}
