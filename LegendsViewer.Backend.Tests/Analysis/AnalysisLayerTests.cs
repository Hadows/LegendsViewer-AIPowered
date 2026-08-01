using System.Text;
using LegendsViewer.Backend.Analysis;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Parser;
using LegendsViewer.Backend.Legends.Various;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Tests.Analysis;

[TestClass]
public class AnalysisLayerTests
{
    private static World _world = null!;
    private static WorldObjectCatalog _catalog = null!;

    [ClassInitialize]
    public static async Task Setup(TestContext context)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _world = new World();
        var legendsPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Xah_Atho-00005-01-01-legends.xml");
        await _world.ParseAsync(legendsPath, null, null, null, null);
        _catalog = new WorldObjectCatalog(_world);
    }

    [TestMethod]
    public void Strip_RemovesMarkupAndDecodesEntities()
    {
        string stripped = PlainText.Strip("<a href=\"/hf/1\" title=\"a&#13;b\">Urist</a> &amp; friends");

        Assert.AreEqual("Urist & friends", stripped);
    }

    [TestMethod]
    public void Strip_HandlesNullAndEmpty()
    {
        Assert.AreEqual(string.Empty, PlainText.Strip(null));
        Assert.AreEqual(string.Empty, PlainText.Strip("   "));
    }

    [TestMethod]
    public void Catalog_ResolvesTypeNameRegardlessOfCaseAndSeparators()
    {
        foreach (string alias in new[] { "HistoricalFigure", "historicalfigure", "historical_figure", "HISTORICAL-FIGURE" })
        {
            Assert.IsTrue(_catalog.TryResolve(alias, out string name, out var items), $"'{alias}' should resolve");
            Assert.AreEqual("HistoricalFigure", name);
            Assert.AreEqual(_world.HistoricalFigures.Count, items.Count);
        }
    }

    [TestMethod]
    public void Catalog_RejectsUnknownType()
    {
        Assert.IsFalse(_catalog.TryResolve("Dragonslayer", out _, out _));
        Assert.IsFalse(_catalog.TryResolve("", out _, out _));
    }

    [TestMethod]
    public void Catalog_FindsObjectById()
    {
        _catalog.TryResolve("Site", out _, out var sites);
        var expected = _world.Sites[5];

        var found = _catalog.Find(sites, expected.Id);

        Assert.AreSame(expected, found);
        Assert.IsNull(_catalog.Find(sites, 999_999));
    }

    [TestMethod]
    public void Dossier_ContainsHeaderAndEventsWithoutMarkup()
    {
        var figure = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 0);

        StringAssert.Contains(dossier, figure.Name);
        StringAssert.Contains(dossier, $"-- Events ({figure.EventCount}) --");
        Assert.IsFalse(dossier.Contains('<'), "Dossier must not contain HTML markup");
        Assert.IsFalse(dossier.Contains("&#"), "Dossier must not contain HTML entities");
    }

    [TestMethod]
    public void Dossier_TruncatesAndSaysSo()
    {
        var figure = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();
        Assert.IsTrue(figure.EventCount > 2, "Test world should have a figure with more than two events");

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 2);

        StringAssert.Contains(dossier, $"-- Events (2 of {figure.EventCount}, truncated");
    }

    [TestMethod]
    public void Dossier_HandlesObjectWithoutEvents()
    {
        var orphan = _world.HistoricalFigures.FirstOrDefault(hf => hf.EventCount == 0);
        if (orphan == null)
        {
            Assert.Inconclusive("Test world has no event-less historical figure");
            return;
        }

        string dossier = new DossierBuilder().Build("HistoricalFigure", orphan, 0);

        StringAssert.Contains(dossier, "No recorded events.");
    }

    [TestMethod]
    public void Dossier_PrefixesEveryEventWithItsRawType()
    {
        var figure = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();
        string expectedType = figure.Events[0].Type;

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 0);

        StringAssert.Contains(dossier, $"[{expectedType}]");
    }

    [TestMethod]
    public void EventFilter_MatchesOnYearRangeAndType()
    {
        var worldEvent = _world.Events.First();
        var filter = EventFilter.Parse(worldEvent.Year, worldEvent.Year, worldEvent.Type);

        Assert.IsTrue(filter.Matches(worldEvent));
        Assert.IsFalse(EventFilter.Parse(worldEvent.Year + 1, null, null).Matches(worldEvent));
        Assert.IsFalse(EventFilter.Parse(null, worldEvent.Year - 1, null).Matches(worldEvent));
        Assert.IsFalse(EventFilter.Parse(null, null, "no such type").Matches(worldEvent));
    }

    [TestMethod]
    public void EventFilter_TreatsBlankInputAsNoFilter()
    {
        var filter = EventFilter.Parse(null, null, "  ,  ");

        Assert.IsTrue(filter.IsEmpty);
        Assert.AreEqual(string.Empty, filter.Describe());
        Assert.IsTrue(filter.Matches(_world.Events.First()));
    }

    [TestMethod]
    public void EventFilter_TypeMatchIsCaseInsensitive()
    {
        var worldEvent = _world.Events.First();

        Assert.IsTrue(EventFilter.Parse(null, null, worldEvent.Type.ToUpperInvariant()).Matches(worldEvent));
    }

    [TestMethod]
    public void Dossier_AppliesFilterAndSaysSo()
    {
        var figure = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();
        string onlyType = figure.Events.GroupBy(e => e.Type).OrderBy(g => g.Count()).First().Key;
        int expected = figure.Events.Count(e => e.Type == onlyType);
        Assert.IsTrue(expected < figure.EventCount, "Need a type that does not cover every event");

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 0, EventFilter.Parse(null, null, onlyType));

        StringAssert.Contains(dossier, $"Filter: types [{onlyType}]");
        StringAssert.Contains(dossier, $"-- Events ({expected} of {figure.EventCount} matching) --");
    }

    [TestMethod]
    public void Dossier_ReportsWhenFilterMatchesNothing()
    {
        var figure = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 0, EventFilter.Parse(null, null, "no such type"));

        StringAssert.Contains(dossier, "-- Events (0) --");
        StringAssert.Contains(dossier, "No events match the filter");
    }

    [TestMethod]
    public void Digest_IsFarSmallerThanDossierAndKeepsTheRareEvents()
    {
        var figure = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 0);
        string digest = new DigestBuilder().Build("HistoricalFigure", figure, 0);

        Assert.IsTrue(digest.Length < dossier.Length, "Digest should be smaller than the full dossier");
        StringAssert.Contains(digest, "-- Event types (");
        StringAssert.Contains(digest, "-- Notable events (");
        Assert.IsFalse(digest.Contains('<'), "Digest must not contain HTML markup");

        // The rarest type is the one the digest exists to surface.
        string rarest = figure.Events.GroupBy(e => e.Type).OrderBy(g => g.Count()).First().Key;
        StringAssert.Contains(digest, $"[{rarest}]");
    }

    [TestMethod]
    public void Digest_HandlesObjectWithoutEvents()
    {
        var orphan = _world.HistoricalFigures.FirstOrDefault(hf => hf.EventCount == 0);
        if (orphan == null)
        {
            Assert.Inconclusive("Test world has no event-less historical figure");
            return;
        }

        string digest = new DigestBuilder().Build("HistoricalFigure", orphan, 0);

        StringAssert.Contains(digest, "No recorded events.");
    }

    [TestMethod]
    public void EventSearch_FindsProseAndReportsCounts()
    {
        // Pick a distinctive fragment out of a real rendered event, then search for it.
        var target = _world.Events.First(e => PlainText.Strip(e.Print(link: false)).Length > 40);
        string prose = PlainText.Strip(target.Print(link: false));
        string fragment = prose.Substring(prose.Length / 2, 20);

        string result = new EventSearchService().Search(_world, fragment, EventFilter.None, 10);

        StringAssert.Contains(result, $"=== Event search: \"{fragment}\" ===");
        StringAssert.Contains(result, $"of {_world.Events.Count} events");
        StringAssert.Contains(result, $"[{target.Type}]");
    }

    [TestMethod]
    public void EventSearch_FilterSkipsRenderingOfExcludedEvents()
    {
        var target = _world.Events.First();

        string result = new EventSearchService().Search(_world, "the", EventFilter.Parse(null, null, target.Type), 5);

        int expected = _world.Events.Count(e => e.Type == target.Type);
        StringAssert.Contains(result, $"rendered {expected} of {_world.Events.Count} events");
    }

    [TestMethod]
    public void EventSearch_ReportsNoMatchesWithoutFailing()
    {
        string result = new EventSearchService().Search(_world, "zzzz-not-in-any-legend", EventFilter.None, 10);

        StringAssert.Contains(result, "Matches: 0");
    }

    [TestMethod]
    public void EventSearch_WithoutQueryReturnsWhatTheFilterAdmits()
    {
        // "What happened in year N" has no word to search for: the filter is the whole question.
        int year = _world.Events.First(e => e.Year > 0).Year;

        string result = new EventSearchService().Search(_world, null, EventFilter.Parse(year, year, null), 10);

        int expected = _world.Events.Count(e => e.Year == year);
        StringAssert.Contains(result, "=== Events ===");
        StringAssert.Contains(result, $"Matches: {expected} of {_world.Events.Count} events");
    }

    [TestMethod]
    public void EventSearch_WithoutQuerySkipsRenderingEntirely()
    {
        var target = _world.Events.First();

        string result = new EventSearchService().Search(_world, null, EventFilter.Parse(null, null, target.Type), 5);

        // The "rendered N of M" wording belongs to text search; with no text there is nothing to render.
        StringAssert.Contains(result, "filtered without rendering");
        Assert.IsFalse(result.Contains("rendered ", StringComparison.Ordinal), "Reported a render count for a query that renders nothing");
    }

    [TestMethod]
    public void EventSearch_RejectsAQueryWithNoQueryAndNoFilter()
    {
        var controller = new AnalysisController(_world);

        var result = controller.SearchEvents(null, 10, null, null, null);

        var badRequest = (BadRequestObjectResult)result;
        StringAssert.Contains((string)badRequest.Value!, "every event in the world");
    }

    [TestMethod]
    public void Facets_ExposeTheFieldsTheDossierPrints()
    {
        var figure = _world.HistoricalFigures.First(hf => ObjectFacets.For(hf).Count > 0);
        var facets = ObjectFacets.For(figure);

        string dossier = new DossierBuilder().Build("HistoricalFigure", figure, 0);

        // Every facet must be reachable from the dossier, key included: what you read is what you query.
        foreach (var facet in facets)
        {
            StringAssert.Contains(dossier, $"[{facet.Field}]");
            StringAssert.Contains(dossier, facet.Value);
        }
    }

    [TestMethod]
    public void Facets_NeverContainMarkup()
    {
        foreach (var item in _world.HistoricalFigures.Take(50).Cast<LegendsViewer.Backend.Legends.WorldObject>().Concat(_world.Entities).Concat(_world.Sites))
        {
            foreach (var facet in ObjectFacets.For(item))
            {
                Assert.IsFalse(facet.Value.Contains('<'), $"Facet {facet.Field} of {item.Name} contains markup");
            }
        }
    }

    [TestMethod]
    public void Facets_NeverPrintATypeNameInsteadOfAValue()
    {
        // A facet built from an object without a ToString override renders its namespace instead of
        // its data, which reads as a value and is silently useless. Nothing may carry the assembly name.
        foreach (var item in _world.Sites.Cast<LegendsViewer.Backend.Legends.WorldObject>().Concat(_world.Entities))
        {
            foreach (var facet in ObjectFacets.For(item))
            {
                Assert.IsFalse(facet.Value.Contains("LegendsViewer.Backend"),
                    $"Facet {facet.Field} of {item.Name} prints a type name: {facet.Value}");
            }
        }
    }

    [TestMethod]
    public void Facets_SiteCoordinatesArePoints()
    {
        var site = _world.Sites.FirstOrDefault(s => ObjectFacets.For(s).Any(f => f.Field == "coordinates"));
        Assert.IsNotNull(site, "The test world has no site with coordinates");

        string value = ObjectFacets.For(site).First(f => f.Field == "coordinates").Value;

        foreach (string point in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = point.Split(',');
            Assert.AreEqual(2, parts.Length, $"Coordinate '{point}' is not a pair");
            Assert.IsTrue(int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _), $"Coordinate '{point}' is not numeric");
        }
    }

    [TestMethod]
    public void Facets_SiteExposesTheCivilizationThatHoldsIt()
    {
        // "Which sites belong to the dwarves" must be one property query. Without these the only
        // route is guessing from the site type and confirming each hit against its founding event.
        var owned = _world.Sites.Where(s => s.CurrentCiv != null).ToList();
        Assert.IsTrue(owned.Count > 0, "The test world has no site with a current civilization");

        foreach (var site in owned)
        {
            var facets = ObjectFacets.For(site);
            Assert.AreEqual(site.CurrentCiv!.Name, facets.First(f => f.Field == "civ").Value);

            // Race is dropped rather than printed as "Unknown", so it is only asserted when known.
            if (site.CurrentCiv.Race != CreatureInfo.Unknown)
            {
                Assert.AreEqual(site.CurrentCiv.Race.NameSingular, facets.First(f => f.Field == "race").Value);
            }
        }
    }

    /// <summary>
    /// An entity holding one post named generically and by caste. The test world records no
    /// positions at all, so the fixture is built rather than found.
    /// </summary>
    private LegendsViewer.Backend.Legends.WorldObjects.Entity CrownedEntity()
    {
        var entity = new LegendsViewer.Backend.Legends.WorldObjects.Entity([], _world) { Name = "The Test Civ" };
        entity.EntityPositions.Add(new EntityPosition(
            [
                new Property { Name = "id", Value = "1" },
                new Property { Name = "name", Value = "monarch" },
                new Property { Name = "name_male", Value = "king" },
                new Property { Name = "name_female", Value = "queen" },
            ], _world));
        return entity;
    }

    [TestMethod]
    public void Rankings_ScopeExcludesTypesTheMeasureCannotApplyTo()
    {
        var controller = new AnalysisController(_world);

        // "worshippers" only exists on HistoricalFigure, so counting every river and site beside it
        // would give a denominator no share can be read against.
        var result = controller.GetTop(null, "worshippers", null, 5);
        if (result is not OkObjectResult ok)
        {
            Assert.Inconclusive("The test world records no worshippers");
            return;
        }

        var payload = (RankingDto)ok.Value!;
        Assert.AreEqual(_world.HistoricalFigures.Count, payload.ObjectsInScope);
    }

    [TestMethod]
    public void Facets_TitleCarriesTheBarePostWithoutEntityOrYears()
    {
        var figure = new LegendsViewer.Backend.Legends.WorldObjects.HistoricalFigure { Name = "Testfigure", Caste = "Female" };
        figure.Positions = [new HfPosition(CrownedEntity(), 100, null, 1, "monarch")];

        var titles = ObjectFacets.For(figure).Where(f => f.Field == "title").Select(f => f.Value).ToList();

        // "position" already carries entity and years, which is what makes its base rates useless.
        CollectionAssert.AreEquivalent(new[] { "Monarch", "Queen" }, titles);
    }

    [TestMethod]
    public void Facets_PositionIsIndexedUnderBothSpellingsWhenCasteRenamesIt()
    {
        // A post an entity calls "Monarch" is named "King" or "Queen" by every event about it. A
        // search phrased either way has to find the same figure.
        var figure = new LegendsViewer.Backend.Legends.WorldObjects.HistoricalFigure { Name = "Testfigure", Caste = "Male" };
        figure.Positions = [new HfPosition(CrownedEntity(), 100, null, 1, "monarch")];

        var positions = ObjectFacets.For(figure).Where(f => f.Field == "position").Select(f => f.Value).ToList();

        CollectionAssert.AreEquivalent(
            new[] { "Monarch of The Test Civ (100-?)", "King of The Test Civ (100-?)" },
            positions);
    }

    [TestMethod]
    public void Facets_PositionIsNotDuplicatedWhenCasteKeepsTheName()
    {
        var entity = new LegendsViewer.Backend.Legends.WorldObjects.Entity([], _world) { Name = "The Test Civ" };
        entity.EntityPositions.Add(new EntityPosition(
            [new Property { Name = "id", Value = "1" }, new Property { Name = "name", Value = "mayor" }], _world));

        var figure = new LegendsViewer.Backend.Legends.WorldObjects.HistoricalFigure { Name = "Testfigure", Caste = "Male" };
        figure.Positions = [new HfPosition(entity, 100, null, 1, "mayor")];

        var facets = ObjectFacets.For(figure);

        Assert.AreEqual(1, facets.Count(f => f.Field == "position"));
        Assert.AreEqual(1, facets.Count(f => f.Field == "title"));
    }

    [TestMethod]
    public void Facets_EntityNamesWhoHoldsItsOffices()
    {
        var entity = CrownedEntity();
        var holder = _world.HistoricalFigures[0];
        entity.EntityPositionAssignments.Add(new EntityPositionAssignment(
            [
                new Property { Name = "id", Value = "1" },
                new Property { Name = "position_id", Value = "1" },
                new Property { Name = "histfig", Value = holder.Id.ToString() },
            ], _world));

        var leaders = ObjectFacets.For(entity).Where(f => f.Field == "leader").ToList();

        Assert.AreEqual(1, leaders.Count);
        Assert.AreEqual($"Monarch: {holder.Name}", leaders[0].Value);
    }

    [TestMethod]
    public void Facets_SitePopulationIsBothReadableAndRankable()
    {
        // Populations come from -world_sites_and_pops.txt, which many exports omit — the real world
        // in df-install has none. The facet is built here rather than found, so that the shape is
        // still covered when the file is missing.
        var site = new LegendsViewer.Backend.Legends.WorldObjects.Site([], _world) { Name = "Testsite" };
        site.Populations.Add(new Population(null, new CreatureInfo("dwarf"), 120));
        site.Populations.Add(new Population(null, new CreatureInfo("human"), 30));

        var facets = ObjectFacets.For(site);

        Assert.AreEqual(2, facets.Count(f => f.Field == "population"));
        Assert.AreEqual("150", facets.First(f => f.Field == "populationtotal").Value);
    }

    [TestMethod]
    public void Facets_SiteFounderOnlyAppearsWhenItDiffersFromTheOwner()
    {
        foreach (var site in _world.Sites)
        {
            var founder = site.OwnerHistory.FirstOrDefault()?.Owner;
            var facets = ObjectFacets.For(site);

            if (founder != null && founder != site.CurrentOwner)
            {
                Assert.AreEqual(founder.Name, facets.First(f => f.Field == "founder").Value);
            }
            else
            {
                Assert.IsFalse(facets.Any(f => f.Field == "founder"),
                    $"{site.Name} repeats its owner as the founder");
            }
        }
    }

    [TestMethod]
    public void PropertySearch_FindsObjectAndSaysWhichValueMatched()
    {
        var figure = _world.HistoricalFigures.First(hf => ObjectFacets.For(hf).Any(f => f.Field == "race"));
        string race = ObjectFacets.For(figure).First(f => f.Field == "race").Value;

        var controller = new AnalysisController(_world);
        var result = controller.SearchObjectProperties(race, "HistoricalFigure", "race", 200);

        var payload = (PropertySearchResultDto)((OkObjectResult)result.Result!).Value!;
        Assert.IsTrue(payload.TotalMatches > 0);
        Assert.AreEqual("race", payload.Field);
        Assert.IsTrue(payload.Results.All(hit => hit.Field == "race"));
        Assert.IsTrue(payload.Results.All(hit => hit.Value.Contains(race, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void PropertySearch_RestrictedToAFieldIgnoresOtherFields()
    {
        var figure = _world.HistoricalFigures.First(hf => ObjectFacets.For(hf).Any(f => f.Field == "race"));
        string race = ObjectFacets.For(figure).First(f => f.Field == "race").Value;

        var controller = new AnalysisController(_world);
        var result = controller.SearchObjectProperties(race, "HistoricalFigure", "goal", 200);

        var payload = (PropertySearchResultDto)((OkObjectResult)result.Result!).Value!;
        Assert.AreEqual(0, payload.TotalMatches, "A race value must not match when the search is pinned to 'goal'");
    }

    [TestMethod]
    public void PropertySearch_RejectsEmptyQueryAndUnknownType()
    {
        var controller = new AnalysisController(_world);

        Assert.IsInstanceOfType<BadRequestObjectResult>(controller.SearchObjectProperties("  ", null, null, 10).Result);
        Assert.IsInstanceOfType<BadRequestObjectResult>(controller.SearchObjectProperties("x", "Nonsense", null, 10).Result);
    }

    [TestMethod]
    public void Facets_DistributionCountsObjectsNotOccurrences()
    {
        var controller = new AnalysisController(_world);
        var payload = (FacetDistributionDto)((OkObjectResult)controller.GetFacets("HistoricalFigure", "race", 1000)).Value!;

        Assert.IsTrue(payload.Values.Count > 0, "The test world should have races");
        Assert.AreEqual(_world.HistoricalFigures.Count, payload.ObjectsInScope);

        foreach (var entry in payload.Values)
        {
            int expected = _world.HistoricalFigures.Count(hf => ObjectFacets.For(hf).Any(f => f.Field == "race" && f.Value == entry.Value));
            Assert.AreEqual(expected, entry.Objects, $"Wrong object count for race '{entry.Value}'");
        }
    }

    [TestMethod]
    public void Facets_ShareDividesByObjectsCarryingTheField()
    {
        var controller = new AnalysisController(_world);
        var payload = (FacetDistributionDto)((OkObjectResult)controller.GetFacets("HistoricalFigure", "race", 1000)).Value!;

        Assert.IsTrue(payload.ObjectsWithField > 0);
        Assert.IsTrue(payload.ObjectsWithField <= payload.ObjectsInScope);

        foreach (var entry in payload.Values)
        {
            Assert.AreEqual(entry.Objects / (double)payload.ObjectsWithField, entry.Share, 0.0001, $"Wrong share for '{entry.Value}'");
        }

        // Race is single valued, so the shares of all values must add up to exactly one object each.
        Assert.AreEqual(payload.ObjectsWithField, payload.Values.Sum(v => v.Objects));
        Assert.AreEqual(payload.Occurrences, payload.Values.Sum(v => v.Occurrences));
    }

    [TestMethod]
    public void Facets_RanksValuesFromMostToLeastCommon()
    {
        var controller = new AnalysisController(_world);
        var payload = (FacetDistributionDto)((OkObjectResult)controller.GetFacets("HistoricalFigure", "race", 1000)).Value!;

        for (int i = 0; i < payload.Values.Count; i++)
        {
            Assert.AreEqual(i + 1, payload.Values[i].Rank);
            if (i > 0)
            {
                Assert.IsTrue(payload.Values[i - 1].Objects >= payload.Values[i].Objects, "Values must be ordered by object count");
            }
        }
    }

    [TestMethod]
    public void Facets_MultiValuedFieldReportsMoreOccurrencesThanObjects()
    {
        var controller = new AnalysisController(_world);
        var fields = (FacetFieldsDto)((OkObjectResult)controller.GetFacets("HistoricalFigure", null, 1000)).Value!;

        var multiValued = fields.Fields.FirstOrDefault(f => f.Occurrences > f.Objects);
        if (multiValued == null)
        {
            Assert.Inconclusive("Test world has no historical figure holding several values of one field");
            return;
        }

        Assert.IsTrue(multiValued.ValuesPerObject > 1, "A multi-valued field must average more than one value per object");
        Assert.IsTrue(multiValued.Objects <= fields.ObjectsInScope, "Object count must never exceed the scope");
    }

    [TestMethod]
    public void Facets_ExposeWorshipFromTheDeitySide()
    {
        // Entity.Worshipped counts religions; the believers are recorded on the deity itself.
        var deity = _world.HistoricalFigures.FirstOrDefault(hf => hf.WorshippingFigures?.Count > 0);
        if (deity == null)
        {
            Assert.Inconclusive("Test world has no deity with recorded worshippers");
            return;
        }

        var facets = ObjectFacets.For(deity);
        var worshippers = facets.FirstOrDefault(f => f.Field == "worshippers");

        Assert.AreNotEqual(default, worshippers, "A worshipped figure must expose the 'worshippers' facet");
        Assert.AreEqual(deity.WorshippingFigures!.Count.ToString(), worshippers.Value);
    }

    [TestMethod]
    public void Facets_KeepDeityFlagSeparateFromTheEntityDeityField()
    {
        // 'deity' on Entity is a name, 'isdeity' on HistoricalFigure is a flag: distinct keys,
        // otherwise a search without a type would mix two different questions.
        var deity = _world.HistoricalFigures.FirstOrDefault(hf => hf.IsDeity);
        if (deity == null)
        {
            Assert.Inconclusive("Test world has no deity");
            return;
        }

        var fields = ObjectFacets.For(deity).Select(f => f.Field).ToList();

        CollectionAssert.Contains(fields, "isdeity");
        CollectionAssert.DoesNotContain(fields, "deity");
    }

    [TestMethod]
    public void Facets_WithoutFieldListsQueryableFields()
    {
        var controller = new AnalysisController(_world);
        var payload = (FacetFieldsDto)((OkObjectResult)controller.GetFacets("HistoricalFigure", null, 1000)).Value!;

        Assert.AreEqual(_world.HistoricalFigures.Count, payload.ObjectsInScope);
        CollectionAssert.Contains(payload.Fields.Select(entry => entry.Field).ToList(), "race");

        var race = payload.Fields.First(entry => entry.Field == "race");
        Assert.AreEqual(race.Objects, race.Occurrences, "Race holds one value per figure");
        Assert.AreEqual(1.0, race.ValuesPerObject, 0.0001);
    }

    [TestMethod]
    public void Facets_UnknownFieldExplainsHowToListThem()
    {
        var controller = new AnalysisController(_world);

        var result = controller.GetFacets("HistoricalFigure", "no-such-field", 10);

        var badRequest = (BadRequestObjectResult)result;
        StringAssert.Contains((string)badRequest.Value!, "without 'field'");
    }

    [TestMethod]
    public void Top_RanksByIntrinsicEventCount()
    {
        var controller = new AnalysisController(_world);
        var ranking = (RankingDto)((OkObjectResult)controller.GetTop("HistoricalFigure", "events", null, 5)).Value!;

        var expected = _world.HistoricalFigures.OrderByDescending(hf => hf.EventCount).First();
        Assert.AreEqual(expected.EventCount, ranking.Results[0].Value);
        Assert.AreEqual(expected.Id, ranking.Results[0].Id);
        Assert.AreEqual(1, ranking.Results[0].Rank);
        Assert.AreEqual(_world.HistoricalFigures.Count, ranking.ObjectsWithMeasure, "Every figure has an event count");
    }

    [TestMethod]
    public void Top_ReportsDistributionAlongsideTheLeaders()
    {
        var controller = new AnalysisController(_world);
        var ranking = (RankingDto)((OkObjectResult)controller.GetTop("HistoricalFigure", "events", null, 3)).Value!;

        var counts = _world.HistoricalFigures.Select(hf => (double)hf.EventCount).OrderBy(v => v).ToList();
        Assert.AreEqual(counts.Sum(), ranking.Total);
        Assert.AreEqual(counts[0], ranking.Min);
        Assert.AreEqual(counts[^1], ranking.Max);
        Assert.AreEqual(counts[^1], ranking.Results[0].Value, "The leader must hold the maximum");
        Assert.AreEqual(3, ranking.Returned);
    }

    [TestMethod]
    public void Top_AscendingOrderStartsFromTheMinimum()
    {
        var controller = new AnalysisController(_world);
        var ranking = (RankingDto)((OkObjectResult)controller.GetTop("HistoricalFigure", "events", "asc", 3)).Value!;

        Assert.AreEqual("asc", ranking.Order);
        Assert.AreEqual(ranking.Min, ranking.Results[0].Value);
    }

    [TestMethod]
    public void Top_RanksByANumericFacet()
    {
        var measure = "kills";
        var expected = _world.HistoricalFigures
            .Select(hf => (hf, Value: ObjectFacets.For(hf).FirstOrDefault(f => f.Field == measure).Value))
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => (x.hf, Value: double.Parse(x.Value)))
            .OrderByDescending(x => x.Value)
            .ToList();

        if (expected.Count == 0)
        {
            Assert.Inconclusive("Test world has no figure with notable kills");
            return;
        }

        var controller = new AnalysisController(_world);
        var ranking = (RankingDto)((OkObjectResult)controller.GetTop("HistoricalFigure", measure, null, 5)).Value!;

        Assert.AreEqual(expected.Count, ranking.ObjectsWithMeasure, "Only figures carrying the facet may be ranked");
        Assert.AreEqual(expected[0].Value, ranking.Results[0].Value);
        Assert.IsTrue(ranking.ObjectsWithMeasure <= ranking.ObjectsInScope);
    }

    [TestMethod]
    public void Top_WithoutMeasureListsTheAvailableOnes()
    {
        var controller = new AnalysisController(_world);
        var measures = (List<MeasureDto>)((OkObjectResult)controller.GetTop("HistoricalFigure", null, null, 20)).Value!;

        var names = measures.Select(m => m.Measure).ToList();
        CollectionAssert.Contains(names, "events");
        CollectionAssert.Contains(names, "eventcollections");
        // Non numeric facets must never be offered as a measure.
        CollectionAssert.DoesNotContain(names, "race");
        CollectionAssert.DoesNotContain(names, "goal");
    }

    [TestMethod]
    public void Top_UnknownMeasureExplainsHowToListThem()
    {
        var controller = new AnalysisController(_world);

        var badRequest = (BadRequestObjectResult)controller.GetTop("HistoricalFigure", "race", null, 5);

        StringAssert.Contains((string)badRequest.Value!, "without 'by'");
    }

    [TestMethod]
    public void Summary_ReportsWorldNameAndTotals()
    {
        string summary = new WorldSummaryBuilder().Build(_world, _catalog);

        StringAssert.Contains(summary, _world.Name);
        StringAssert.Contains(summary, $"Total events: {_world.Events.Count}");
        StringAssert.Contains(summary, "HistoricalFigure");
        Assert.IsFalse(summary.Contains('<'), "Summary must not contain HTML markup");
    }

    [TestMethod]
    public void Summary_RendersUnrecordedYearsAsQuestionMark()
    {
        // -1 is the model default for "not recorded"; it must never surface as a literal -1.
        var era = _world.Eras.FirstOrDefault(e => e.StartYear == -1 || e.EndYear == -1);
        if (era == null)
        {
            Assert.Inconclusive("Test world has no era with an unrecorded year");
            return;
        }

        string summary = new WorldSummaryBuilder().Build(_world, _catalog);

        StringAssert.Contains(summary, "?");
        Assert.IsFalse(summary.Contains("--1"), "Unrecorded years must not render as '-1'");
    }
}
