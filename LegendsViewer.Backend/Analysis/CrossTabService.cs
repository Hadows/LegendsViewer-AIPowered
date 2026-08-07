using LegendsViewer.Backend.Legends;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Breaks one facet down by another: groups objects by a categorical facet and, optionally,
/// aggregates a numeric measure within each group.
///
/// <c>/facets</c> counts how many objects carry a value and <c>/top</c> names the single leader,
/// but neither can answer a question phrased over two properties at once — "age at death by caste",
/// "war deaths by attacker race". Without this the only route was reading the object list and
/// re-deriving the join outside the API, which for the classic DTOs means parsing ids out of HTML
/// anchors. Every value here comes from <see cref="ObjectFacets"/>, so anything a dossier prints
/// can be grouped or aggregated.
/// </summary>
public static class CrossTabService
{
    public static CrossTabDto? Build(
        IEnumerable<(string Name, IReadOnlyList<WorldObject> Items)> scope,
        string? type,
        string field,
        string? measure,
        int limit)
    {
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        string fieldLabel = field;
        string? measureLabel = measure;

        int objectsInScope = 0;
        int objectsWithField = 0;
        int objectsWithMeasure = 0;

        foreach (var (_, items) in scope)
        {
            foreach (WorldObject item in items)
            {
                objectsInScope++;

                var values = new List<string>();
                foreach (Facet facet in ObjectFacets.For(item))
                {
                    if (!facet.Field.Equals(field, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    fieldLabel = facet.Label;
                    // A multi-valued facet must not count its object twice in the same group.
                    if (!values.Contains(facet.Value, StringComparer.OrdinalIgnoreCase))
                    {
                        values.Add(facet.Value);
                    }
                }

                if (values.Count == 0)
                {
                    continue;
                }

                objectsWithField++;

                double? value = null;
                if (!string.IsNullOrWhiteSpace(measure)
                    && RankingService.TryMeasureValue(item, measure, out string label, out double measured))
                {
                    measureLabel = label;
                    value = measured;
                    objectsWithMeasure++;
                }

                // An object carrying several values belongs to every one of their groups, so the
                // group counts can add up to more than the objects: ObjectsWithField is the honest
                // denominator, exactly as in /facets.
                foreach (string groupValue in values)
                {
                    if (!groups.TryGetValue(groupValue, out var group))
                    {
                        group = new Group(groupValue);
                        groups[groupValue] = group;
                    }

                    group.Objects++;
                    if (value.HasValue)
                    {
                        group.Values.Add(value.Value);
                    }
                }
            }
        }

        if (groups.Count == 0)
        {
            return null;
        }

        bool aggregating = !string.IsNullOrWhiteSpace(measure);
        var ordered = aggregating
            ? groups.Values.OrderByDescending(group => group.Values.Sum()).ThenByDescending(group => group.Objects)
            : groups.Values.OrderByDescending(group => group.Objects).ThenBy(group => group.Value, StringComparer.Ordinal);

        var results = ordered
            .Take(limit)
            .Select((group, index) => group.ToDto(index + 1, aggregating))
            .ToList();

        return new CrossTabDto(
            type,
            field,
            fieldLabel,
            aggregating ? measure : null,
            aggregating ? measureLabel : null,
            objectsInScope,
            objectsWithField,
            aggregating ? objectsWithMeasure : null,
            groups.Count,
            results.Count,
            results);
    }

    private sealed class Group(string value)
    {
        public string Value { get; } = value;
        public int Objects { get; set; }
        public List<double> Values { get; } = [];

        public CrossTabGroupDto ToDto(int rank, bool aggregating)
        {
            if (!aggregating || Values.Count == 0)
            {
                return new CrossTabGroupDto(rank, Value, Objects, 0, null, null, null, null, null);
            }

            var sorted = Values.OrderBy(value => value).ToList();
            int middle = sorted.Count / 2;

            return new CrossTabGroupDto(
                rank,
                Value,
                Objects,
                sorted.Count,
                sorted.Sum(),
                sorted[0],
                sorted[^1],
                sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2,
                Math.Round(sorted.Average(), 2));
        }
    }
}

public sealed record CrossTabGroupDto(
    int Rank,
    string Value,
    int Objects,
    int ObjectsWithMeasure,
    double? Total,
    double? Min,
    double? Max,
    double? Median,
    double? Mean);

public sealed record CrossTabDto(
    string? Type,
    string Field,
    string FieldLabel,
    string? Measure,
    string? MeasureLabel,
    int ObjectsInScope,
    int ObjectsWithField,
    int? ObjectsWithMeasure,
    int Groups,
    int Returned,
    List<CrossTabGroupDto> Results);
