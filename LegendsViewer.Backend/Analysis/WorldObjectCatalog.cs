using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Interfaces;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Maps a world object type name to the corresponding list on <see cref="IWorld"/>.
/// Lookup ignores case and separators, so "HistoricalFigure", "historical_figure" and
/// "historicalfigure" all resolve to the same list.
/// </summary>
public sealed class WorldObjectCatalog
{
    private readonly Dictionary<string, (string Name, IReadOnlyList<WorldObject> Items)> _byKey = [];
    private readonly List<(string Name, IReadOnlyList<WorldObject> Items)> _ordered = [];

    public WorldObjectCatalog(IWorld world)
    {
        Add("HistoricalFigure", world.HistoricalFigures);
        Add("Entity", world.Entities);
        Add("Site", world.Sites);
        Add("Artifact", world.Artifacts);
        Add("Structure", world.Structures);
        Add("Region", world.Regions);
        Add("UndergroundRegion", world.UndergroundRegions);
        Add("Landmass", world.Landmasses);
        Add("MountainPeak", world.MountainPeaks);
        Add("River", world.Rivers);
        Add("WorldConstruction", world.WorldConstructions);
        Add("WrittenContent", world.WrittenContents);
        Add("DanceForm", world.DanceForms);
        Add("MusicalForm", world.MusicalForms);
        Add("PoeticForm", world.PoeticForms);
        Add("Era", world.Eras);

        Add("War", world.Wars);
        Add("Battle", world.Battles);
        Add("Duel", world.Duels);
        Add("BeastAttack", world.BeastAttacks);
        Add("Raid", world.Raids);
        Add("SiteConquered", world.SiteConquerings);
        Add("Insurrection", world.Insurrections);
        Add("Persecution", world.Persecutions);
        Add("Purge", world.Purges);
        Add("Coup", world.Coups);
        Add("Abduction", world.Abductions);
        Add("Theft", world.Thefts);
        Add("Procession", world.Processions);
        Add("Performance", world.Performances);
        Add("Journey", world.Journeys);
        Add("Competition", world.Competitions);
        Add("Ceremony", world.Ceremonies);
        Add("Occasion", world.Occasions);
    }

    public IReadOnlyList<(string Name, IReadOnlyList<WorldObject> Items)> All => _ordered;

    private void Add(string name, IReadOnlyList<WorldObject> items)
    {
        _byKey[Normalize(name)] = (name, items);
        _ordered.Add((name, items));
    }

    public static string Normalize(string value)
    {
        return new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
    }

    public bool TryResolve(string type, out string name, out IReadOnlyList<WorldObject> items)
    {
        if (!string.IsNullOrWhiteSpace(type) && _byKey.TryGetValue(Normalize(type), out var entry))
        {
            (name, items) = entry;
            return true;
        }
        name = string.Empty;
        items = [];
        return false;
    }

    public WorldObject? Find(IReadOnlyList<WorldObject> items, int id)
    {
        // Ids are usually dense and zero based, so the direct hit covers almost every lookup.
        if (id >= 0 && id < items.Count && items[id].Id == id)
        {
            return items[id];
        }
        return items.FirstOrDefault(item => item.Id == id);
    }
}
