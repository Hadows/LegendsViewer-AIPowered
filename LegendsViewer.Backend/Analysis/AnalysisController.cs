using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Analysis;

/// <summary>
/// Read-only surface aimed at non-UI consumers (analysis tooling, exports, LLMs): plain prose
/// instead of Vuetify markup, whole histories instead of pages of ten.
/// Everything here reads the already parsed <see cref="IWorld"/> singleton and mutates nothing.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AnalysisController(IWorld world) : ControllerBase
{
    private const int DefaultSearchLimit = 25;
    private const int MaxSearchLimit = 200;

    private readonly IWorld _world = world;
    private readonly WorldObjectCatalog _catalog = new(world);

    /// <summary>Type names accepted by the other routes, with how many objects each holds.</summary>
    [HttpGet("types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<List<TypeInfoDto>> GetTypes()
    {
        if (NoWorldLoaded(out var problem))
        {
            return problem;
        }

        return Ok(_catalog.All
            .Select(entry => new TypeInfoDto(entry.Name, entry.Items.Count))
            .OrderByDescending(entry => entry.Count)
            .ToList());
    }

    /// <summary>Event type names accepted by the eventTypes filter, with world wide counts.</summary>
    [HttpGet("eventtypes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<List<TypeInfoDto>> GetEventTypes()
    {
        if (NoWorldLoaded(out var problem))
        {
            return problem;
        }

        return Ok(_world.Events
            .GroupBy(e => e.Type)
            .Select(group => new TypeInfoDto(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ToList());
    }

    /// <summary>Overview of the loaded world, as plain text.</summary>
    [HttpGet("summary")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult GetSummary()
    {
        if (NoWorldLoaded(out var problem))
        {
            return problem;
        }

        return Text(new WorldSummaryBuilder().Build(_world, _catalog));
    }

    /// <summary>Finds world objects by name across every type, or within a single type.</summary>
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SearchResultDto> Search(
        [FromQuery] string q,
        [FromQuery] string? type = null,
        [FromQuery] int limit = DefaultSearchLimit)
    {
        if (!TryScope(type, out var searched, out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query 'q' must not be empty.");
        }

        limit = Math.Clamp(limit, 1, MaxSearchLimit);
        string term = q.Trim();
        var hits = new List<(int Rank, WorldObject Object, string Type)>();

        foreach (var (typeName, items) in searched)
        {
            foreach (WorldObject item in items)
            {
                if (string.IsNullOrEmpty(item.Name) || !item.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int rank = item.Name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0
                    : item.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1
                    : 2;
                hits.Add((rank, item, typeName));
            }
        }

        var results = hits
            .OrderBy(hit => hit.Rank)
            .ThenByDescending(hit => hit.Object.EventCount)
            .Take(limit)
            .Select(hit => new SearchHitDto(
                hit.Type,
                hit.Object.Id,
                hit.Object.Name,
                PlainText.Strip(hit.Object.Type),
                hit.Object.EventCount,
                $"/api/Analysis/dossier/{hit.Type}/{hit.Object.Id}"))
            .ToList();

        return Ok(new SearchResultDto(term, hits.Count, results.Count, results));
    }

    /// <summary>
    /// Searches the structured properties of world objects (goal, race, position, affiliation, ...)
    /// rather than their names or their events. The searchable fields are exactly the ones the
    /// dossier prints in its "Facts" block, where each label carries its query key in brackets.
    /// </summary>
    [HttpGet("objects/search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<PropertySearchResultDto> SearchObjectProperties(
        [FromQuery] string q,
        [FromQuery] string? type = null,
        [FromQuery] string? field = null,
        [FromQuery] int limit = DefaultSearchLimit)
    {
        if (!TryScope(type, out var scope, out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query 'q' must not be empty.");
        }

        limit = Math.Clamp(limit, 1, MaxSearchLimit);
        string term = q.Trim();
        string? wantedField = string.IsNullOrWhiteSpace(field) ? null : field.Trim();

        var hits = new List<(int Rank, WorldObject Object, string Type, Facet Facet)>();

        foreach (var (typeName, items) in scope)
        {
            foreach (WorldObject item in items)
            {
                foreach (Facet facet in ObjectFacets.For(item))
                {
                    if (wantedField != null && !facet.Field.Equals(wantedField, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!facet.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int rank = facet.Value.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    hits.Add((rank, item, typeName, facet));
                    break;
                }
            }
        }

        var results = hits
            .OrderBy(hit => hit.Rank)
            .ThenByDescending(hit => hit.Object.EventCount)
            .Take(limit)
            .Select(hit => new PropertyHitDto(
                hit.Type,
                hit.Object.Id,
                hit.Object.Name,
                hit.Facet.Field,
                hit.Facet.Value,
                hit.Object.EventCount,
                $"/api/Analysis/dossier/{hit.Type}/{hit.Object.Id}"))
            .ToList();

        return Ok(new PropertySearchResultDto(term, wantedField, hits.Count, results.Count, results));
    }

    /// <summary>
    /// Base rates for property values. Without <c>field</c> returns a <see cref="FacetFieldsDto"/>
    /// listing the queryable fields; with it a <see cref="FacetDistributionDto"/> with, for every
    /// value, the objects that carry it, the raw occurrences, its rank and its share.
    ///
    /// The share divides by the objects that carry the field, not by every object in scope: a
    /// multi-valued field such as <c>deity</c> is held by a fraction of the objects, and dividing by
    /// the whole scope would understate every value.
    /// </summary>
    [HttpGet("facets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult GetFacets(
        [FromQuery] string? type = null,
        [FromQuery] string? field = null,
        [FromQuery] int limit = 50)
    {
        if (!TryScope(type, out var scope, out var failure))
        {
            return failure;
        }

        limit = Math.Clamp(limit, 1, 1000);
        string? wantedField = string.IsNullOrWhiteSpace(field) ? null : field.Trim();

        if (wantedField == null)
        {
            return Ok(FacetStatistics.Fields(scope, type, limit));
        }

        var distribution = FacetStatistics.Distribution(scope, type, wantedField, limit);
        return distribution == null
            ? BadRequest($"No values for field '{wantedField}' in this scope. Call /api/Analysis/facets without 'field' to list the available ones.")
            : Ok(distribution);
    }

    /// <summary>
    /// Ranks objects by a numeric measure. Without <c>by</c> returns the measures available in the
    /// scope; with it a <see cref="RankingDto"/> naming the leaders and reporting the distribution
    /// (total, min, max, median) so a top position can be read against the spread.
    /// </summary>
    [HttpGet("top")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult GetTop(
        [FromQuery] string? type = null,
        [FromQuery] string? by = null,
        [FromQuery] string? order = null,
        [FromQuery] int limit = 20)
    {
        if (!TryScope(type, out var scope, out var failure))
        {
            return failure;
        }

        limit = Math.Clamp(limit, 1, MaxSearchLimit);

        if (string.IsNullOrWhiteSpace(by))
        {
            return Ok(RankingService.Measures(scope, type));
        }

        bool ascending = "asc".Equals(order, StringComparison.OrdinalIgnoreCase);
        var ranking = RankingService.Rank(scope, type, by.Trim(), ascending, limit);

        return ranking == null
            ? BadRequest($"No numeric measure '{by}' in this scope. Call /api/Analysis/top without 'by' to list the available ones.")
            : Ok(ranking);
    }

    /// <summary>
    /// Breaks one property down by another: groups objects by the categorical facet <c>field</c>
    /// and, when <c>measure</c> names a numeric one, reports total, min, max, median and mean of it
    /// within each group.
    ///
    /// <c>/facets</c> and <c>/top</c> each read a single property; a question over two at once
    /// ("age at death by caste", "war deaths by attacker race") had no route through the API and
    /// forced the join to be redone outside it. <c>field</c> accepts any facet key, <c>measure</c>
    /// any name <c>/top</c> accepts.
    /// </summary>
    [HttpGet("crosstab")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult GetCrossTab(
        [FromQuery] string? type = null,
        [FromQuery] string? field = null,
        [FromQuery] string? measure = null,
        [FromQuery] int limit = 50)
    {
        if (!TryScope(type, out var scope, out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            return BadRequest("Give the facet to group by in 'field'. Call /api/Analysis/facets without 'field' to list the available ones.");
        }

        limit = Math.Clamp(limit, 1, 1000);
        var table = CrossTabService.Build(scope, type, field.Trim(), measure?.Trim(), limit);

        if (table == null)
        {
            return BadRequest($"No values for field '{field}' in this scope. Call /api/Analysis/facets without 'field' to list the available ones.");
        }

        // Naming a measure no object carries is a typo, not an empty result: say so rather than
        // returning a table of plain counts that silently answers a different question.
        if (!string.IsNullOrWhiteSpace(measure) && table.ObjectsWithMeasure == 0)
        {
            return BadRequest($"No numeric measure '{measure}' in this scope. Call /api/Analysis/top without 'by' to list the available ones.");
        }

        return Ok(table);
    }

    /// <summary>Full text search over the rendered prose of every event in the world.</summary>
    [HttpGet("events/search")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult SearchEvents(
        [FromQuery] string? q = null,
        [FromQuery] int limit = EventSearchService.DefaultLimit,
        [FromQuery] int? fromYear = null,
        [FromQuery] int? toYear = null,
        [FromQuery] string? eventTypes = null)
    {
        if (NoWorldLoaded(out var problem))
        {
            return problem;
        }

        limit = Math.Clamp(limit, 1, MaxSearchLimit);
        var filter = EventFilter.Parse(fromYear, toYear, eventTypes);

        // 'q' alone, or a filter alone, both narrow the world. Neither would mean rendering all of it.
        if (string.IsNullOrWhiteSpace(q) && filter.IsEmpty)
        {
            return BadRequest("Give 'q', or at least one of 'fromYear', 'toYear' and 'eventTypes'. Without either this would return every event in the world.");
        }

        return Text(new EventSearchService().Search(_world, q?.Trim(), filter, limit));
    }

    /// <summary>History of one world object as plain prose, in a single response.</summary>
    [HttpGet("dossier/{type}/{id:int}")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult GetDossier(
        [FromRoute] string type,
        [FromRoute] int id,
        [FromQuery] int maxEvents = DossierBuilder.DefaultMaxEvents,
        [FromQuery] int? fromYear = null,
        [FromQuery] int? toYear = null,
        [FromQuery] string? eventTypes = null)
    {
        if (!TryFind(type, id, out string resolved, out WorldObject? worldObject, out var failure))
        {
            return failure;
        }

        var filter = EventFilter.Parse(fromYear, toYear, eventTypes);
        return Text(new DossierBuilder().Build(resolved, worldObject!, maxEvents, filter));
    }

    /// <summary>
    /// Condensed view of one world object: type breakdown, activity over time and only the events
    /// whose type is rare for that object. Use this when the full dossier is too large to read.
    /// </summary>
    [HttpGet("digest/{type}/{id:int}")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult GetDigest(
        [FromRoute] string type,
        [FromRoute] int id,
        [FromQuery] int maxNotableEvents = DigestBuilder.DefaultMaxNotableEvents,
        [FromQuery] int? fromYear = null,
        [FromQuery] int? toYear = null,
        [FromQuery] string? eventTypes = null)
    {
        if (!TryFind(type, id, out string resolved, out WorldObject? worldObject, out var failure))
        {
            return failure;
        }

        var filter = EventFilter.Parse(fromYear, toYear, eventTypes);
        return Text(new DigestBuilder().Build(resolved, worldObject!, maxNotableEvents, filter));
    }

    /// <summary>Resolves the optional type parameter to the lists to walk: one type, or all of them.</summary>
    private bool TryScope(string? type, out IEnumerable<(string Name, IReadOnlyList<WorldObject> Items)> scope, out ActionResult failure)
    {
        scope = [];

        if (NoWorldLoaded(out failure))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            scope = _catalog.All;
            return true;
        }

        if (!_catalog.TryResolve(type, out string resolved, out var items))
        {
            failure = BadRequest($"Unknown type '{type}'. See /api/Analysis/types for the accepted names.");
            return false;
        }

        scope = [(resolved, items)];
        return true;
    }

    private bool TryFind(string type, int id, out string resolved, out WorldObject? worldObject, out ActionResult failure)
    {
        resolved = string.Empty;
        worldObject = null;

        if (NoWorldLoaded(out failure))
        {
            return false;
        }

        if (!_catalog.TryResolve(type, out resolved, out var items))
        {
            failure = BadRequest($"Unknown type '{type}'. See /api/Analysis/types for the accepted names.");
            return false;
        }

        worldObject = _catalog.Find(items, id);
        if (worldObject == null)
        {
            failure = NotFound($"No {resolved} with id {id}.");
            return false;
        }

        failure = null!;
        return true;
    }

    private ContentResult Text(string content)
    {
        return Content(content, "text/plain; charset=utf-8");
    }

    private bool NoWorldLoaded(out ActionResult problem)
    {
        if (_world.Events.Count > 0 || _world.HistoricalFigures.Count > 0)
        {
            problem = null!;
            return false;
        }

        problem = Conflict("No world is loaded. Load one via POST /api/Bookmark/loadByFullPath first.");
        return true;
    }
}

public sealed record TypeInfoDto(string Type, int Count);

public sealed record SearchHitDto(string Type, int Id, string Name, string Detail, int EventCount, string Dossier);

public sealed record SearchResultDto(string Query, int TotalMatches, int Returned, List<SearchHitDto> Results);

public sealed record PropertyHitDto(string Type, int Id, string Name, string Field, string Value, int EventCount, string Dossier);

public sealed record PropertySearchResultDto(string Query, string? Field, int TotalMatches, int Returned, List<PropertyHitDto> Results);
