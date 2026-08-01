using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.EventCollections;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.Legends.WorldObjects;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// A single searchable property of a world object. <see cref="Field"/> is the query key
/// (lower case, no spaces), <see cref="Label"/> is how the dossier prints it.
/// </summary>
public readonly record struct Facet(string Field, string Label, string Value);

/// <summary>
/// The structured properties of a world object: goals, races, positions, affiliations — everything
/// that lives on the object rather than in its events, and that <c>events/search</c> therefore
/// cannot see.
///
/// This is the single source for both the dossier's "Facts" block and the property search, so what
/// a reader sees is exactly what they can query back.
/// </summary>
public static class ObjectFacets
{
    public static List<Facet> For(WorldObject worldObject)
    {
        var facets = new List<Facet>();

        switch (worldObject)
        {
            case HistoricalFigure hf:
                Add(facets, "race", "Race", hf.Race.NameSingular);
                Add(facets, "caste", "Caste", hf.Caste);
                Add(facets, "born", "Born", hf.BirthYear != -1 ? hf.BirthYear.ToString() : null);
                Add(facets, "died", "Died", hf.DeathYear != -1 ? hf.DeathYear.ToString() : "still alive or unrecorded");
                Add(facets, "deathcause", "Cause of death", hf.DeathYear != -1 ? hf.DeathCause.ToString() : null);
                Add(facets, "goal", "Goal", hf.Goal);
                foreach (string sphere in hf.Spheres)
                {
                    Add(facets, "sphere", "Spheres", sphere);
                }
                Add(facets, "isdeity", "Deity", hf.IsDeity ? "yes" : null);
                // The worship relation is stored on the deity, so these counts only exist here.
                // Entity.Worshipped is the other direction and answers a different question:
                // how many religions are devoted to this figure, not how many people worship it.
                Add(facets, "worshippers", "Worshipped by (figures)", hf.WorshippingFigures?.Count > 0 ? hf.WorshippingFigures.Count.ToString() : null);
                Add(facets, "worshippingentities", "Worshipped by (entities)", hf.WorshippingEntities?.Count > 0 ? hf.WorshippingEntities.Count.ToString() : null);
                Add(facets, "kills", "Notable kills", hf.NotableKills.Count > 0 ? hf.NotableKills.Count.ToString() : null);
                foreach (var artifact in hf.HoldingArtifacts)
                {
                    Add(facets, "artifact", "Held artifacts", artifact.Name);
                }
                if (hf.Positions != null)
                {
                    foreach (var position in hf.Positions.OrderBy(p => p.StartYear ?? -1))
                    {
                        Add(facets, "position", "Positions",
                            $"{position.Title} of {position.Entity?.Name ?? "unknown"} ({position.StartYear?.ToString() ?? "?"}-{position.EndYear?.ToString() ?? "?"})");
                    }
                }
                break;

            case Entity entity:
                Add(facets, "entitytype", "Entity type", entity.EntityType.ToString());
                Add(facets, "race", "Race", entity.Race.NameSingular);
                Add(facets, "civilization", "Is civilization", entity.IsCiv ? "yes" : null);
                Add(facets, "sites", "Sites held over time", entity.SiteHistory.Count > 0 ? entity.SiteHistory.Count.ToString() : null);
                Add(facets, "subgroups", "Subgroups", entity.Groups.Count > 0 ? entity.Groups.Count.ToString() : null);
                Add(facets, "wars", "Wars", entity.Wars.Count > 0 ? entity.Wars.Count.ToString() : null);
                foreach (var deity in entity.Worshipped)
                {
                    // One entry per religion, not per believer: see "worshippers" on HistoricalFigure.
                    Add(facets, "deity", "Deity this entity is devoted to", deity.Name);
                }
                break;

            case Site site:
                Add(facets, "sitetype", "Site type", site.SiteType.ToString());
                // Who holds the site now. CurrentOwner is usually a site level group, so the civ is
                // resolved by walking up its parents; the civ's race is what turns "the dwarven
                // sites" into one property query instead of a guess from the site type.
                Add(facets, "owner", "Owner", site.CurrentOwner?.Name);
                Add(facets, "civ", "Civilization", site.CurrentCiv?.Name);
                var race = site.CurrentCiv?.Race;
                Add(facets, "race", "Race", race != null && race != CreatureInfo.Unknown ? race.NameSingular : null);
                // Only worth printing when the site changed hands: equal to the owner it says nothing.
                var founder = site.OwnerHistory.FirstOrDefault()?.Owner;
                Add(facets, "founder", "Founded by", founder != null && founder != site.CurrentOwner ? founder.Name : null);
                Add(facets, "coordinates", "Coordinates", site.Coordinates.Count > 0
                    ? string.Join(" ", site.Coordinates.Select(location => $"{location.X},{location.Y}"))
                    : null);
                Add(facets, "structures", "Structures", site.Structures.Count > 0 ? site.Structures.Count.ToString() : null);
                foreach (var connection in site.Connections)
                {
                    Add(facets, "connection", "Connections", connection.Name);
                }
                break;

            case EventCollection collection:
                Add(facets, "started", "Started", collection.StartYear >= 0 ? collection.StartDate : null);
                Add(facets, "ended", "Ended", collection.EndYear >= 0 ? collection.EndDate : null);
                if (collection is War war)
                {
                    Add(facets, "attacker", "Attacker", war.Attacker?.Name);
                    Add(facets, "defender", "Defender", war.Defender?.Name);
                    Add(facets, "deaths", "Deaths", war.DeathCount > 0
                        ? $"{war.DeathCount} ({war.AttackerDeathCount} attacker / {war.DefenderDeathCount} defender)"
                        : null);
                }
                break;
        }

        return facets;
    }

    private static void Add(List<Facet> facets, string field, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            facets.Add(new Facet(field, label, PlainText.Strip(value)));
        }
    }
}
