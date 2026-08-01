using System.Diagnostics;
using System.Text;
using LegendsViewer.Backend.Legends.Events;
using LegendsViewer.Backend.Legends.Interfaces;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Full text search over the rendered prose of every event in the world.
///
/// There is no index: the text only exists once <c>Print</c> has run, so each query renders the
/// events it has to examine. Narrowing with <c>fromYear</c>/<c>toYear</c>/<c>eventTypes</c> skips
/// the rendering entirely for the events it excludes, which is what makes a repeated query cheap.
/// </summary>
public sealed class EventSearchService
{
    public const int DefaultLimit = 25;

    /// <param name="query">
    /// Null or empty selects everything the filter admits. That is not a special case bolted on: with
    /// no text to match, the prose never has to be produced, so "what happened in 290" costs a
    /// property comparison per event instead of a full render.
    /// </param>
    public string Search(IWorld world, string? query, EventFilter filter, int limit)
    {
        bool matchAll = string.IsNullOrEmpty(query);
        var stopwatch = Stopwatch.StartNew();
        var matches = new List<WorldEvent>();
        int rendered = 0;
        int totalMatches = 0;

        foreach (WorldEvent worldEvent in world.Events)
        {
            if (!filter.Matches(worldEvent))
            {
                continue;
            }

            if (!matchAll)
            {
                rendered++;
                string prose = PlainText.Strip(worldEvent.Print(link: false));
                if (!prose.Contains(query!, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            totalMatches++;
            if (matches.Count < limit)
            {
                matches.Add(worldEvent);
            }
        }

        stopwatch.Stop();

        var sb = new StringBuilder();
        sb.AppendLine(matchAll ? "=== Events ===" : $"=== Event search: \"{query}\" ===");
        if (!filter.IsEmpty)
        {
            sb.AppendLine($"Filter: {filter.Describe()}");
        }
        sb.AppendLine(matchAll
            ? $"Matches: {totalMatches} of {world.Events.Count} events in {stopwatch.ElapsedMilliseconds} ms (filtered without rendering)"
            : $"Matches: {totalMatches} (rendered {rendered} of {world.Events.Count} events in {stopwatch.ElapsedMilliseconds} ms)");
        if (totalMatches > matches.Count)
        {
            sb.AppendLine($"Showing the first {matches.Count}; raise limit or narrow the filter for the rest.");
        }
        sb.AppendLine();

        foreach (WorldEvent worldEvent in matches.OrderBy(e => e.Year).ThenBy(e => e.Seconds72))
        {
            sb.AppendLine(DossierBuilder.FormatEvent(worldEvent, pov: null));
        }

        return sb.ToString();
    }
}
