using System.Text;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Events;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Condensed view for objects whose full dossier is too large to read: a civilization with
/// thousands of events produces hundreds of kilobytes of prose, most of it recurring festivals.
/// The digest keeps the shape of the history (what kinds of things happened, and when) and quotes
/// only the events that stand out.
/// </summary>
public sealed class DigestBuilder
{
    public const int DefaultMaxNotableEvents = 80;
    private const int TimelineBuckets = 20;
    private const int BarWidth = 40;

    public string Build(string typeName, WorldObject worldObject, int maxNotableEvents, EventFilter? filter = null)
    {
        filter ??= EventFilter.None;

        var sb = new StringBuilder();
        ObjectHeader.Append(sb, typeName, worldObject);

        if (!filter.IsEmpty)
        {
            sb.AppendLine($"Filter: {filter.Describe()}");
        }

        var events = worldObject.Events
            .Where(filter.Matches)
            .OrderBy(e => e.Year)
            .ThenBy(e => e.Seconds72)
            .ThenBy(e => e.Id)
            .ToList();

        sb.AppendLine();
        if (events.Count == 0)
        {
            sb.AppendLine(worldObject.Events.Count == 0
                ? "No recorded events."
                : $"No events match the filter (the object has {worldObject.Events.Count}).");
            return sb.ToString();
        }

        sb.AppendLine($"Events considered: {events.Count} of {worldObject.Events.Count}, years {events[0].Year} to {events[^1].Year}");

        var byType = events
            .GroupBy(e => e.Type)
            .Select(group => (Type: group.Key, Count: group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ToList();

        AppendTypeBreakdown(sb, byType);
        AppendCollections(sb, worldObject);
        AppendTimeline(sb, events);
        AppendNotableEvents(sb, worldObject, events, byType, maxNotableEvents);

        return sb.ToString();
    }

    private static void AppendTypeBreakdown(StringBuilder sb, List<(string Type, int Count)> byType)
    {
        sb.AppendLine();
        sb.AppendLine($"-- Event types ({byType.Count} distinct) --");
        foreach (var (type, count) in byType)
        {
            sb.AppendLine($"{count,8}  {type}");
        }
    }

    private static void AppendCollections(StringBuilder sb, WorldObject worldObject)
    {
        if (worldObject.EventCollections.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"-- Event collections by kind ({worldObject.EventCollections.Count} total) --");

        var byKind = worldObject.EventCollections
            .GroupBy(c => PlainText.Strip(c.Type))
            .Select(group => new
            {
                Kind = group.Key,
                Count = group.Count(),
                First = group.Min(c => c.StartYear),
                Last = group.Max(c => c.EndYear)
            })
            .OrderByDescending(entry => entry.Count);

        foreach (var kind in byKind)
        {
            sb.AppendLine($"{kind.Count,8}  {kind.Kind} (years {kind.First} to {kind.Last})");
        }
    }

    private static void AppendTimeline(StringBuilder sb, List<WorldEvent> events)
    {
        int firstYear = events[0].Year;
        int lastYear = events[^1].Year;
        int span = lastYear - firstYear + 1;
        if (span <= 1)
        {
            return;
        }

        int bucketSize = Math.Max(1, (int)Math.Ceiling(span / (double)TimelineBuckets));
        var buckets = new SortedDictionary<int, int>();
        foreach (WorldEvent worldEvent in events)
        {
            int bucket = (worldEvent.Year - firstYear) / bucketSize;
            buckets[bucket] = buckets.GetValueOrDefault(bucket) + 1;
        }

        int peak = buckets.Values.Max();
        sb.AppendLine();
        sb.AppendLine($"-- Activity ({bucketSize}-year buckets) --");
        foreach (var (bucket, count) in buckets)
        {
            int start = firstYear + (bucket * bucketSize);
            int end = start + bucketSize - 1;
            int bar = Math.Max(1, count * BarWidth / peak);
            sb.AppendLine($"{start,5}-{end,-5} {new string('#', bar),-40} {count}");
        }
    }

    private static void AppendNotableEvents(
        StringBuilder sb,
        WorldObject worldObject,
        List<WorldEvent> events,
        List<(string Type, int Count)> byType,
        int maxNotableEvents)
    {
        // An event type that fires hundreds of times for one object is that object's routine:
        // festivals, job changes, membership churn. What tells a story is what happened rarely.
        int threshold = Math.Max(3, events.Count / 100);
        var rareTypes = byType.Where(entry => entry.Count <= threshold).Select(entry => entry.Type).ToHashSet();

        if (rareTypes.Count == 0)
        {
            return;
        }

        var notable = events.Where(e => rareTypes.Contains(e.Type)).ToList();
        bool truncated = maxNotableEvents > 0 && notable.Count > maxNotableEvents;

        sb.AppendLine();
        sb.AppendLine(truncated
            ? $"-- Notable events ({maxNotableEvents} of {notable.Count}: types occurring at most {threshold} times) --"
            : $"-- Notable events ({notable.Count}: types occurring at most {threshold} times) --");

        foreach (WorldEvent worldEvent in truncated ? notable.Take(maxNotableEvents) : notable)
        {
            sb.AppendLine(DossierBuilder.FormatEvent(worldEvent, worldObject));
        }
    }
}
