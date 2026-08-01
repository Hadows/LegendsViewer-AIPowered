using System.Text;
using LegendsViewer.Backend.Legends.Enums;
using LegendsViewer.Backend.Legends.Interfaces;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Compact overview of the loaded world: the anchor a reader starts from before drilling into
/// individual dossiers.
/// </summary>
public sealed class WorldSummaryBuilder
{
    public string Build(IWorld world, WorldObjectCatalog catalog)
    {
        var sb = new StringBuilder();

        string alternativeName = string.IsNullOrWhiteSpace(world.AlternativeName) ? "" : $" — {world.AlternativeName}";
        sb.AppendLine($"=== {world.Name}{alternativeName} ===");
        sb.AppendLine($"Map: {world.Width} x {world.Height}");

        int lastEventYear = world.Events.Count > 0 ? world.Events.Max(e => e.Year) : -1;
        sb.AppendLine($"Last recorded event year: {lastEventYear}");
        if (world.CurrentYear != lastEventYear)
        {
            sb.AppendLine($"(IWorld.CurrentYear reports {world.CurrentYear}, which disagrees with the events; prefer the event year.)");
        }
        sb.AppendLine($"Total events: {world.Events.Count}");

        AppendTotals(sb, catalog);
        AppendCivilizations(sb, world);
        AppendEras(sb, world);
        AppendWars(sb, world);
        AppendTopFigures(sb, world);
        AppendEventTypes(sb, world);

        return sb.ToString();
    }

    /// <summary>
    /// -1 is the model's "not recorded" default. Other negative years are real: figures such as
    /// deities and megabeasts legitimately predate year 0.
    /// </summary>
    private static string Year(int value)
    {
        return value == -1 ? "?" : value.ToString();
    }

    private static void AppendTotals(StringBuilder sb, WorldObjectCatalog catalog)
    {
        sb.AppendLine();
        sb.AppendLine("-- Objects --");
        foreach (var (name, items) in catalog.All.Where(entry => entry.Items.Count > 0).OrderByDescending(entry => entry.Items.Count))
        {
            sb.AppendLine($"{items.Count,8}  {name}");
        }
    }

    private static void AppendCivilizations(StringBuilder sb, IWorld world)
    {
        var civilizations = world.Entities
            .Where(e => e.IsCiv || (e.EntityType == EntityType.Civilization && e.SiteHistory.Count > 0))
            .OrderByDescending(e => e.EventCount)
            .Take(20)
            .ToList();

        if (civilizations.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("-- Main civilizations --");
        foreach (var civilization in civilizations)
        {
            string race = string.IsNullOrWhiteSpace(civilization.Race.NamePlural) ? "unknown race" : civilization.Race.NamePlural;
            sb.AppendLine($"[{civilization.Id,6}] {civilization.Name} ({race}) — {civilization.SiteHistory.Count} sites, {civilization.EventCount} events");
        }
    }

    private static void AppendEras(StringBuilder sb, IWorld world)
    {
        if (world.Eras.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("-- Eras --");
        foreach (var era in world.Eras.OrderBy(e => e.StartYear))
        {
            sb.AppendLine($"{Year(era.StartYear),5} - {Year(era.EndYear),-5} {era.Name}");
        }
    }

    private static void AppendWars(StringBuilder sb, IWorld world)
    {
        if (world.Wars.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"-- Largest wars (of {world.Wars.Count}) --");
        foreach (var war in world.Wars.OrderByDescending(w => w.DeathCount).ThenByDescending(w => w.EventCount).Take(10))
        {
            sb.AppendLine($"[{war.Id,6}] {war.Name} ({Year(war.StartYear)}-{Year(war.EndYear)}) — {war.DeathCount} deaths, {war.Battles.Count} battles");
        }
    }

    private static void AppendTopFigures(StringBuilder sb, IWorld world)
    {
        if (world.HistoricalFigures.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("-- Most eventful historical figures --");
        foreach (var figure in world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).Take(20))
        {
            string race = string.IsNullOrWhiteSpace(figure.Race.NameSingular) ? "unknown" : figure.Race.NameSingular;
            string lifespan = figure.BirthYear != -1 || figure.DeathYear != -1
                ? $" ({Year(figure.BirthYear)}-{Year(figure.DeathYear)})"
                : "";
            sb.AppendLine($"[{figure.Id,6}] {figure.Name}, {race}{lifespan} — {figure.EventCount} events");
        }
    }

    private static void AppendEventTypes(StringBuilder sb, IWorld world)
    {
        if (world.Events.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("-- Most common event types --");
        var byType = world.Events
            .GroupBy(e => e.Type)
            .OrderByDescending(group => group.Count())
            .Take(25);

        foreach (var group in byType)
        {
            sb.AppendLine($"{group.Count(),8}  {group.Key}");
        }
    }
}
