using System.Globalization;
using LegendsViewer.Backend.Legends;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Ranks world objects by a numeric measure: either an intrinsic count (events, event collections)
/// or any facet whose value parses as a number (worshippers, kills, deaths, sites, ...).
///
/// <c>/facets</c> orders by how many objects share a value, which answers "how common is this" but
/// never "who holds the maximum". This is the other half: it names the objects, and reports the
/// distribution alongside so a top position can be read against the spread instead of in isolation.
/// </summary>
public static class RankingService
{
    public const string EventsMeasure = "events";
    public const string EventCollectionsMeasure = "eventcollections";

    public static RankingDto? Rank(
        IEnumerable<(string Name, IReadOnlyList<WorldObject> Items)> scope,
        string? type,
        string by,
        bool ascending,
        int limit)
    {
        var measured = new List<(WorldObject Object, string Type, double Value)>();
        string label = by;

        // Counted per type so the denominator can drop the types the measure never applies to.
        // "30 of 89,732 objects" mixes deities with rivers and answers nothing; the population a
        // reader needs is the one the measure could have been recorded on.
        var totalsByType = new Dictionary<string, int>();
        var typesWithMeasure = new HashSet<string>();

        foreach (var (typeName, items) in scope)
        {
            foreach (WorldObject item in items)
            {
                totalsByType[typeName] = totalsByType.GetValueOrDefault(typeName) + 1;

                if (TryMeasure(item, by, ref label, out double value))
                {
                    measured.Add((item, typeName, value));
                    typesWithMeasure.Add(typeName);
                }
            }
        }

        int objectsInScope = totalsByType
            .Where(entry => typesWithMeasure.Contains(entry.Key))
            .Sum(entry => entry.Value);

        if (measured.Count == 0)
        {
            return null;
        }

        var values = measured.Select(entry => entry.Value).OrderBy(value => value).ToList();
        var ordered = ascending
            ? measured.OrderBy(entry => entry.Value).ThenBy(entry => entry.Object.Name, StringComparer.Ordinal)
            : measured.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Object.Name, StringComparer.Ordinal);

        var results = ordered
            .Take(limit)
            .Select((entry, index) => new RankedObjectDto(
                index + 1,
                entry.Type,
                entry.Object.Id,
                string.IsNullOrWhiteSpace(entry.Object.Name) ? "(unnamed)" : entry.Object.Name,
                entry.Value,
                entry.Object.EventCount,
                $"/api/Analysis/dossier/{entry.Type}/{entry.Object.Id}"))
            .ToList();

        return new RankingDto(
            type,
            by,
            label,
            ascending ? "asc" : "desc",
            objectsInScope,
            measured.Count,
            values.Sum(),
            values[0],
            values[^1],
            Median(values),
            results.Count,
            results);
    }

    /// <summary>Numeric measures available in the given scope, for discovery.</summary>
    public static List<MeasureDto> Measures(
        IEnumerable<(string Name, IReadOnlyList<WorldObject> Items)> scope,
        string? type)
    {
        var found = new Dictionary<string, (string Label, int Objects)>(StringComparer.OrdinalIgnoreCase);
        int objectsInScope = 0;
        int withCollections = 0;

        foreach (var (_, items) in scope)
        {
            foreach (WorldObject item in items)
            {
                objectsInScope++;
                if (item.EventCollectionCount > 0)
                {
                    withCollections++;
                }

                foreach (Facet facet in ObjectFacets.For(item))
                {
                    if (!IsNumeric(facet.Value))
                    {
                        continue;
                    }

                    found.TryGetValue(facet.Field, out var entry);
                    found[facet.Field] = (facet.Label, entry.Objects + 1);
                }
            }
        }

        var measures = new List<MeasureDto>
        {
            new(EventsMeasure, "Events", objectsInScope),
            new(EventCollectionsMeasure, "Event collections", withCollections)
        };

        measures.AddRange(found
            .Select(entry => new MeasureDto(entry.Key, entry.Value.Label, entry.Value.Objects))
            .OrderByDescending(entry => entry.Objects)
            .ThenBy(entry => entry.Measure, StringComparer.Ordinal));

        _ = type;
        return measures;
    }

    private static bool TryMeasure(WorldObject worldObject, string by, ref string label, out double value)
    {
        if (by.Equals(EventsMeasure, StringComparison.OrdinalIgnoreCase))
        {
            label = "Events";
            value = worldObject.EventCount;
            return true;
        }

        if (by.Equals(EventCollectionsMeasure, StringComparison.OrdinalIgnoreCase))
        {
            label = "Event collections";
            value = worldObject.EventCollectionCount;
            return true;
        }

        // A facet may repeat; the largest value is the meaningful one for a ranking.
        bool found = false;
        value = 0;

        foreach (Facet facet in ObjectFacets.For(worldObject))
        {
            if (!facet.Field.Equals(by, StringComparison.OrdinalIgnoreCase) || !TryParse(facet.Value, out double parsed))
            {
                continue;
            }

            label = facet.Label;
            value = found ? Math.Max(value, parsed) : parsed;
            found = true;
        }

        return found;
    }

    private static bool IsNumeric(string value)
    {
        return TryParse(value, out _);
    }

    private static bool TryParse(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static double Median(List<double> sorted)
    {
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

public sealed record RankedObjectDto(int Rank, string Type, int Id, string Name, double Value, int EventCount, string Dossier);

public sealed record MeasureDto(string Measure, string Label, int Objects);

public sealed record RankingDto(
    string? Type,
    string By,
    string Label,
    string Order,
    int ObjectsInScope,
    int ObjectsWithMeasure,
    double Total,
    double Min,
    double Max,
    double Median,
    int Returned,
    List<RankedObjectDto> Results);
