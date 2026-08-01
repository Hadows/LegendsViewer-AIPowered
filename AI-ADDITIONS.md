# AI additions

This fork of [Kromtec/LegendsViewer-Next](https://github.com/Kromtec/LegendsViewer-Next) adds an
**analysis layer**: a read-only API surface that exposes the parsed world to non-UI consumers —
analysis tooling, text exports, and language models — rather than to the Vue frontend.

The API reference lives in [ANALYSIS-API.md](ANALYSIS-API.md). This document explains what was
added, why, and the conventions to preserve when extending it.

## Why it exists

The existing REST API is shaped for a person clicking through a UI, which makes it a poor fit for
programmatic analysis of a large world:

- **Responses carry Vuetify markup.** The prose returned by the DTOs embeds `<span class="v-chip …">`
  and `<a href="/entity/88">`. On a real export that markup is the majority of the payload.
- **Everything is paginated ten at a time.** Reconstructing one figure's history takes many requests.
- **There is no search.** Neither by name, nor over event text, nor over object properties.
- **There is no aggregation.** No base rates, no rankings, so nothing can be judged as common or rare.

The parsing, the object graph and the Dwarf Fortress style prose generation were already there and
are excellent. The analysis layer reuses all of it; it adds no parsing of its own.

## Design constraint: additive only

**No upstream source file was modified.** Every addition is a new file, so aligning this fork with
upstream should never produce a conflict in code. `AnalysisController` takes the `IWorld` singleton,
which the upstream `Program.cs` already registers, so not even dependency injection wiring was
needed.

The single deliberate exception is **`README.md`**, rewritten to say what this fork is — the
convention for a public fork, since visitors otherwise land on a page that gives no hint the
repository differs from the original. Expect a conflict there when syncing, and resolve it in favour
of the fork's version.

Verify the property with:

```bash
git status --short
```

Apart from `README.md`, only new paths should appear. Please keep it that way when extending the
layer.

## What was added

```
LegendsViewer.Backend/Analysis/
  AnalysisController.cs     REST surface under /api/Analysis
  WorldObjectCatalog.cs     maps type names to the lists on IWorld
  ObjectFacets.cs           structured properties of an object (single source, see below)
  ObjectHeader.cs           identity block shared by dossier and digest
  DossierBuilder.cs         full history of one object, as prose
  DigestBuilder.cs          condensed view for objects too large to read whole
  EventFilter.cs            year range and event type filtering
  EventSearchService.cs     full text search over rendered event prose
  FacetStatistics.cs        base rates for property values
  RankingService.cs         rankings by numeric measure
  PlainText.cs              markup stripping safety net

LegendsViewer.Backend.Tests/Analysis/
  AnalysisLayerTests.cs     40 tests, parsing the existing test world once
```

## Conventions

### Output is input

Every answer is written so its own text can be pasted back into the next query, with nothing to
guess and no separate vocabulary to look up:

- event lines are `date [raw type] prose`, and `[raw type]` goes into `eventTypes=`
- fact lines are `Label [key]: value`, and `[key]` goes into `field=`
- `/types`, `/eventtypes`, `/facets` (no `field`) and `/top` (no `by`) all list what the
  corresponding parameter accepts

### `ObjectFacets` is the single source of properties

It feeds both the dossier's "Facts" block and the property search, so what a reader sees is exactly
what they can query back. Numeric facets are additionally picked up by `/top` as rankable measures.
Adding a property there makes it readable, searchable and rankable at once — do not add properties
to `ObjectHeader`, which only groups them for display.

A facet value must be built from the data, never from an object that has no `ToString` override:
the result reads like a value but is the class name, and nothing downstream can tell the difference.
`Facets_NeverPrintATypeNameInsteadOfAValue` asserts it for every site and entity in the test world.

A facet that carries several things at once cannot be aggregated. `position` reads
`Monarch of The Stoked Boots (286-?)`, which makes it 8,932 distinct values over 9,785 occurrences —
`/facets` on it says nothing. `title` therefore carries the bare post, and drops to 652 values that
rank as they should. Keep the composite for reading and searching, and add a bare one for counting.

### The same office has two names

An entity records a post generically (`Monarch`); every event about it uses the caste-specific title
(`King`, `Queen`), and `EntityPosition.GetTitleByCaste` is what maps between them. A reader who saw
the prose searches for the second and a reader who saw the properties searches for the first, so
`position` is indexed under **both** spellings when they differ. That is also why `leader` exists on
`Entity`: `position` answers "what did this figure hold" and nothing answered "who holds this".

### Ownership belongs on the object, not in its events

Who holds a site is the first thing asked of it, and it appears in no event text: the founding event
names the *group* that founded it, and a site that changed hands never mentions its current owner in
prose at all. `Site` therefore carries `owner` (the holding group), `civ` (that group's civilization,
resolved by walking up its parents) and `race`, which is what makes "the dwarven sites" a single
property query — 101 of 822 on the real world — instead of a guess from `sitetype` confirmed dossier
by dossier. `founder` is printed only when it differs from the current owner, so its presence is
itself the signal that the site was taken: Praisearmors reads `founder: The Tomb of Gangs` (dwarven)
against `civ: The Busy Confederacies` (human).

### Prose must never carry markup

`Print(link: false)` already avoids the anchors, and `PlainText.Strip()` is the safety net for any
event class that emits HTML regardless. Tests assert that no `<` reaches the output.

### Denominators travel with numbers

A count without its population invites false conclusions, and the world data is full of traps: a
property such as `goal` is recorded for only 17,741 of 41,080 figures, so dividing by the total
understates every share by more than half. Therefore `/facets` reports `objectsWithField` beside
`objectsInScope` and divides by the former, and `/top` returns total, min, median and max beside the
leaders.

### `-1` means "not recorded"

Render it as `?`, and do not filter out negative years in general: deities and megabeasts have
legitimate negative birth years.

## Measured effect

Against the real export used during development (`region1`, year 300: 237 MB + 90 MB, 494,436
events, 41,080 historical figures):

| | Classic API | Analysis layer |
|---|---|---|
| One figure's history (74 events) | 91 KB of JSON | **14 KB** of prose (−84%) |
| One civilization (3,575 events) | 655 KB dossier | **17 KB** digest (−97%) |
| Whole world overview | not available | 4 KB |

Loading that world costs ~30 s and ~800 MB once per process; every query afterwards is immediate.
Full text event search without filters takes ~3.8 s (it renders all 494,436 events); with
`eventTypes=` it drops to ~40 ms, because the filter skips the rendering.

## Tests

```bash
dotnet test LegendsViewer.Backend.Tests --filter "FullyQualifiedName~AnalysisLayerTests"
```

The suite parses the existing `TestData` world once in `[ClassInitialize]`. It uses no fixtures of
its own and adds no test data.

**Two tests fail on Windows before any change from this fork**, both in the upstream
`BookmarkServiceTests`: `Concurrent_AddBookmark_FromMultipleThreads_ShouldNotCorruptState` and
`Concurrent_AddAndGet_FromMultipleThreads_ShouldHandleGracefully`. `BookmarkService.SaveBookmarksToFile`
calls `File.WriteAllText` without serializing concurrent writers; Windows enforces the share mode and
throws, while the CI runs on Ubuntu where the overlapping writes are permitted. The in-memory state
is safe — it uses `ConcurrentDictionary` — only the persistence is unsynchronized.
