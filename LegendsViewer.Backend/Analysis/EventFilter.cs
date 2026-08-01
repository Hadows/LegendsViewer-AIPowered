using LegendsViewer.Backend.Legends.Events;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Year range and event type restrictions shared by the dossier, digest and event search routes.
/// Types are the raw XML names ("hf died", "created site"), which is what the dossier prints and
/// therefore what a reader can copy straight back into a query.
/// </summary>
public sealed class EventFilter
{
    public static readonly EventFilter None = new(null, null, null);

    private readonly HashSet<string>? _types;

    private EventFilter(int? fromYear, int? toYear, HashSet<string>? types)
    {
        FromYear = fromYear;
        ToYear = toYear;
        _types = types;
    }

    public int? FromYear { get; }
    public int? ToYear { get; }
    public IReadOnlyCollection<string> Types => _types is null ? [] : _types;

    public bool IsEmpty => FromYear is null && ToYear is null && _types is null;

    public static EventFilter Parse(int? fromYear, int? toYear, string? eventTypes)
    {
        HashSet<string>? types = null;
        if (!string.IsNullOrWhiteSpace(eventTypes))
        {
            types = eventTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (types.Count == 0)
            {
                types = null;
            }
        }

        return new EventFilter(fromYear, toYear, types);
    }

    public bool Matches(WorldEvent worldEvent)
    {
        if (FromYear is int from && worldEvent.Year < from)
        {
            return false;
        }
        if (ToYear is int to && worldEvent.Year > to)
        {
            return false;
        }
        return _types is null || _types.Contains(worldEvent.Type);
    }

    /// <summary>Human readable echo of the filter, so a truncated answer never looks like the whole truth.</summary>
    public string Describe()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (FromYear is not null || ToYear is not null)
        {
            parts.Add($"years {FromYear?.ToString() ?? "start"}-{ToYear?.ToString() ?? "end"}");
        }
        if (_types is not null)
        {
            parts.Add($"types [{string.Join(", ", _types.Order())}]");
        }
        return string.Join(", ", parts);
    }
}
