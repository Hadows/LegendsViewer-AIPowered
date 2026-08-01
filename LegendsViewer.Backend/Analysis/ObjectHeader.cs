using System.Text;
using LegendsViewer.Backend.Legends;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Identity block and type specific facts shared by the dossier and the digest.
/// </summary>
public static class ObjectHeader
{
    public static void Append(StringBuilder sb, string typeName, WorldObject worldObject)
    {
        string name = string.IsNullOrWhiteSpace(worldObject.Name) ? "(unnamed)" : worldObject.Name;
        sb.AppendLine($"=== {name} ===");
        sb.AppendLine($"Type: {typeName} (id {worldObject.Id})");

        string detail = PlainText.Strip(worldObject.Type);
        if (!string.IsNullOrWhiteSpace(detail) && !string.Equals(detail, typeName, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"Classification: {detail}");
        }
        string subtype = PlainText.Strip(worldObject.Subtype);
        if (!string.IsNullOrWhiteSpace(subtype))
        {
            sb.AppendLine($"Subtype: {subtype}");
        }

        AppendFacts(sb, worldObject);
    }

    private static void AppendFacts(StringBuilder sb, WorldObject worldObject)
    {
        var facets = ObjectFacets.For(worldObject);
        if (facets.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("-- Facts --");

        // GroupBy keeps first-occurrence order, so repeated fields (positions, spheres) collapse
        // into one line where ObjectFacets emitted them.
        foreach (var group in facets.GroupBy(facet => facet.Field))
        {
            sb.AppendLine($"{group.First().Label} [{group.Key}]: {string.Join("; ", group.Select(facet => facet.Value))}");
        }
    }
}
