using LegendsViewer.Backend.Legends;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Counts facet values across a set of world objects.
///
/// The distinction that matters: a field such as <c>race</c> holds one value per object, so counting
/// occurrences and counting objects give the same number. A field such as <c>deity</c> or
/// <c>position</c> holds several, so the two diverge and only the object count can be turned into a
/// rate. Both are reported, together with the honest denominator — the objects that carry the field
/// at all, not every object in scope.
/// </summary>
public static class FacetStatistics
{
    private sealed class Tally
    {
        public string Label = string.Empty;
        public int Objects;
        public int Occurrences;
        public readonly HashSet<string> DistinctValues = new(StringComparer.OrdinalIgnoreCase);
    }

    public static FacetFieldsDto Fields(
        IEnumerable<(string Name, IReadOnlyList<WorldObject> Items)> scope,
        string? type,
        int limit)
    {
        var tallies = new Dictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        int objectsInScope = 0;

        foreach (var (_, items) in scope)
        {
            foreach (WorldObject item in items)
            {
                objectsInScope++;
                var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Facet facet in ObjectFacets.For(item))
                {
                    if (!tallies.TryGetValue(facet.Field, out Tally? tally))
                    {
                        tally = new Tally { Label = facet.Label };
                        tallies[facet.Field] = tally;
                    }

                    tally.Occurrences++;
                    tally.DistinctValues.Add(facet.Value);
                    if (seenFields.Add(facet.Field))
                    {
                        tally.Objects++;
                    }
                }
            }
        }

        var fields = tallies
            .Select(entry => new FacetFieldDto(
                entry.Key,
                entry.Value.Label,
                entry.Value.Objects,
                entry.Value.Occurrences,
                entry.Value.DistinctValues.Count,
                Round(entry.Value.Objects == 0 ? 0 : entry.Value.Occurrences / (double)entry.Value.Objects)))
            .OrderByDescending(entry => entry.Objects)
            .ThenBy(entry => entry.Field, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return new FacetFieldsDto(type, objectsInScope, fields);
    }

    /// <summary>Returns null when the field is unknown or holds no value in the given scope.</summary>
    public static FacetDistributionDto? Distribution(
        IEnumerable<(string Name, IReadOnlyList<WorldObject> Items)> scope,
        string? type,
        string field,
        int limit)
    {
        var tallies = new Dictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        int objectsInScope = 0;
        int objectsWithField = 0;
        int occurrences = 0;
        string label = field;

        foreach (var (_, items) in scope)
        {
            foreach (WorldObject item in items)
            {
                objectsInScope++;
                var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Facet facet in ObjectFacets.For(item))
                {
                    if (!facet.Field.Equals(field, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    label = facet.Label;
                    occurrences++;

                    if (!tallies.TryGetValue(facet.Value, out Tally? tally))
                    {
                        tally = new Tally();
                        tallies[facet.Value] = tally;
                    }
                    tally.Occurrences++;

                    // One object counts once per distinct value, however often it repeats it.
                    if (seenValues.Add(facet.Value))
                    {
                        tally.Objects++;
                    }
                }

                if (seenValues.Count > 0)
                {
                    objectsWithField++;
                }
            }
        }

        if (tallies.Count == 0)
        {
            return null;
        }

        var values = tallies
            .OrderByDescending(entry => entry.Value.Objects)
            .ThenByDescending(entry => entry.Value.Occurrences)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select((entry, index) => new FacetValueDto(
                index + 1,
                entry.Key,
                entry.Value.Objects,
                entry.Value.Occurrences,
                Round(entry.Value.Objects / (double)objectsWithField)))
            .ToList();

        return new FacetDistributionDto(
            type,
            field,
            label,
            objectsInScope,
            objectsWithField,
            values.Count,
            occurrences,
            Math.Min(limit, values.Count),
            values.Take(limit).ToList());
    }

    private static double Round(double value)
    {
        return Math.Round(value, 4);
    }
}

/// <summary>One queryable field, with how many objects carry it and how many values they hold.</summary>
public sealed record FacetFieldDto(string Field, string Label, int Objects, int Occurrences, int DistinctValues, double ValuesPerObject);

/// <summary>
/// One value of a field. <see cref="Share"/> is <see cref="Objects"/> over the objects that carry
/// the field, so it is a rate even when the field holds several values per object.
/// </summary>
public sealed record FacetValueDto(int Rank, string Value, int Objects, int Occurrences, double Share);

public sealed record FacetDistributionDto(
    string? Type,
    string Field,
    string Label,
    int ObjectsInScope,
    int ObjectsWithField,
    int DistinctValues,
    int Occurrences,
    int Returned,
    List<FacetValueDto> Values);

public sealed record FacetFieldsDto(string? Type, int ObjectsInScope, List<FacetFieldDto> Fields);
