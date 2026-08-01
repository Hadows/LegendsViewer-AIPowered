using System.Text;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.EventCollections;
using LegendsViewer.Backend.Legends.Events;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Renders the history of a single world object as plain prose, in one response.
/// This is the non-paginated, markup-free counterpart of the frontend detail page.
/// Each event line carries its raw type, so a reader can narrow the next request with
/// <c>eventTypes=</c> using a value copied straight out of the previous answer.
/// </summary>
public sealed class DossierBuilder
{
    public const int DefaultMaxEvents = 1000;

    public string Build(string typeName, WorldObject worldObject, int maxEvents, EventFilter? filter = null)
    {
        filter ??= EventFilter.None;

        var sb = new StringBuilder();
        ObjectHeader.Append(sb, typeName, worldObject);

        if (!filter.IsEmpty)
        {
            sb.AppendLine($"Filter: {filter.Describe()}");
        }

        AppendEventCollections(sb, worldObject, filter);
        AppendEvents(sb, worldObject, maxEvents, filter);

        return sb.ToString();
    }

    private static void AppendEventCollections(StringBuilder sb, WorldObject worldObject, EventFilter filter)
    {
        // Collections have their own span, so only the year range applies; an event type filter
        // is about events and must not silently drop the collection overview.
        var collections = worldObject.EventCollections
            .Where(c => WithinYears(c, filter))
            .OrderBy(c => c.StartYear)
            .ToList();

        if (collections.Count == 0)
        {
            return;
        }

        int total = worldObject.EventCollections.Count;
        string header = collections.Count == total
            ? $"-- Event collections ({total}) --"
            : $"-- Event collections ({collections.Count} of {total} in range) --";

        sb.AppendLine();
        sb.AppendLine(header);
        foreach (EventCollection collection in collections)
        {
            string collectionName = string.IsNullOrWhiteSpace(collection.Name) ? "(unnamed)" : collection.Name;
            sb.AppendLine($"[{collection.StartDate} .. {collection.EndDate}] {PlainText.Strip(collection.Type)}: {collectionName} ({collection.Events.Count} events)");
        }
    }

    private static bool WithinYears(EventCollection collection, EventFilter filter)
    {
        if (filter.FromYear is int from && collection.EndYear != -1 && collection.EndYear < from)
        {
            return false;
        }
        if (filter.ToYear is int to && collection.StartYear != -1 && collection.StartYear > to)
        {
            return false;
        }
        return true;
    }

    private static void AppendEvents(StringBuilder sb, WorldObject worldObject, int maxEvents, EventFilter filter)
    {
        int total = worldObject.Events.Count;
        sb.AppendLine();

        var matching = worldObject.Events
            .Where(filter.Matches)
            .OrderBy(e => e.Year)
            .ThenBy(e => e.Seconds72)
            .ThenBy(e => e.Id)
            .ToList();

        if (matching.Count == 0)
        {
            sb.AppendLine("-- Events (0) --");
            sb.AppendLine(total == 0 ? "No recorded events." : $"No events match the filter (the object has {total}).");
            return;
        }

        bool truncated = maxEvents > 0 && matching.Count > maxEvents;
        int shownCount = truncated ? maxEvents : matching.Count;

        string scope = matching.Count == total ? $"{matching.Count}" : $"{matching.Count} of {total} matching";
        sb.AppendLine(truncated
            ? $"-- Events ({shownCount} of {scope}, truncated: raise maxEvents to see the rest) --"
            : $"-- Events ({scope}) --");

        foreach (WorldEvent worldEvent in matching.Take(shownCount))
        {
            sb.AppendLine(FormatEvent(worldEvent, worldObject));
        }
    }

    public static string FormatEvent(WorldEvent worldEvent, WorldObject? pov)
    {
        string prose = PlainText.Strip(worldEvent.Print(link: false, pov: pov));
        return $"{worldEvent.Date}  [{worldEvent.Type}]  {prose}";
    }
}
