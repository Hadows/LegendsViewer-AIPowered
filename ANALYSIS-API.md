# Analysis API

Read-only API added by this fork, under `/api/Analysis` on the backend port (`15421`). It exposes
the parsed world as plain prose and as aggregates, for consumers that are not the Vue frontend.

For why it exists and how it is built, see [AI-ADDITIONS.md](AI-ADDITIONS.md).

## Before anything else: load a world

The analysis routes read the in-memory world; they never load one. Use the existing endpoint:

```bash
curl -X POST http://localhost:15421/api/Bookmark/loadByFullPath \
     -H "Content-Type: application/json" \
     -d '"C:\\path\\to\\region1-00300-01-01-legends.xml"'
```

This costs roughly 30 s and 800 MB for a large export, once per process. Until it completes, every
analysis route answers **409 Conflict** with an explanatory message rather than an empty result.

## Conventions

**Two response formats.** Everything meant to be *read* is `text/plain`: summary, dossier, digest,
event search. Everything meant to be *processed* is JSON: type lists, name and property search,
facets, rankings.

**Output is input.** Answers quote the vocabulary of the next query. Event lines are
`date [raw type] prose` and that raw type is a valid `eventTypes=` value; fact lines are
`Label [key]: value` and that key is a valid `field=` value.

**Discovery routes.** `/types`, `/eventtypes`, `/facets` without `field` and `/top` without `by`
each list what the corresponding parameter accepts. An unknown value answers **400** and points at
the matching discovery route.

**Type names** ignore case and separators: `HistoricalFigure`, `historicalfigure` and
`historical_figure` are the same. Where `type` is optional, omitting it searches every type.

**Shared event filters**, accepted by `dossier`, `digest` and `events/search`:

| Parameter | Meaning |
|---|---|
| `fromYear` | earliest year, inclusive |
| `toYear` | latest year, inclusive |
| `eventTypes` | comma separated raw event type names |

---

## Discovery

### `GET /types`

World object types accepted by the other routes, with their counts.

```json
[{ "type": "HistoricalFigure", "count": 41080 }, { "type": "Site", "count": 822 }]
```

### `GET /eventtypes`

Event type names accepted by `eventTypes=`, with world-wide counts, most frequent first.

---

## Reading

### `GET /summary`

Overview of the loaded world as text: totals per type, main civilizations, eras, largest wars, most
eventful figures, most common event types. About 4 KB on a large world — the intended starting point.

Note: it derives the last year from the events rather than from `IWorld.CurrentYear`, which is
unreliable, and reports the discrepancy when the two disagree.

### `GET /dossier/{type}/{id}`

The full history of one object as prose, in a single response: identity, facts, event collections,
then every event.

| Parameter | Default | Notes |
|---|---|---|
| `maxEvents` | 1000 | `0` means no limit |
| `fromYear`, `toYear`, `eventTypes` | — | shared filters |

```bash
curl "http://localhost:15421/api/Analysis/dossier/HistoricalFigure/24707?maxEvents=0"
```

```
=== Corud Boottowns ===
Type: HistoricalFigure (id 24707)
Classification: Human

-- Facts --
Race [race]: Human
Goal [goal]: Bathe World In Chaos
Positions [position]: Sacred Dust of The Dead Coven (236-?); Monarch of The Stoked Boots (283-?)

-- Events (34) --
0283-10-22  [entity overthrown]  In 283, early winter, (22nd of Moonstone) Corud toppled …
```

Answers **404** for an unknown id, **400** for an unknown type.

### `GET /digest/{type}/{id}`

Condensed alternative for objects whose dossier is too large to read: event type breakdown, event
collections grouped by kind, an activity histogram over time, and only the *notable* events.

Notable is defined by rarity rather than by a hand-written list of interesting types: an event
qualifies when its type occurs at most `max(3, 1% of the object's events)` times for that object. An
object's routine — festivals, job changes, membership churn — sinks by itself, and the events that
happened once surface.

| Parameter | Default | Notes |
|---|---|---|
| `maxNotableEvents` | 80 | `0` means no limit |
| `fromYear`, `toYear`, `eventTypes` | — | shared filters |

On the most documented civilization of the test world this returns 17 KB where the dossier returns
655 KB.

---

## Searching

The three searches cover three different surfaces and are not interchangeable. A goal or an
affiliation appears in no event, so only the property search finds it; a deed appears in no property,
so only the event search finds it.

### `GET /search` — by name

| Parameter | Default | Notes |
|---|---|---|
| `q` | required | substring, case-insensitive |
| `type` | all types | restrict to one type |
| `limit` | 25 | maximum 200 |

Ranks exact matches first, then prefix matches, then by event count.

```json
{
  "query": "Stoked Boots",
  "totalMatches": 13,
  "returned": 1,
  "results": [{
    "type": "Entity", "id": 88, "name": "The Stoked Boots",
    "detail": "Civilization", "eventCount": 3575,
    "dossier": "/api/Analysis/dossier/Entity/88"
  }]
}
```

### `GET /objects/search` — by property

Searches the structured properties shown in a dossier's "Facts" block: goal, race, positions,
affiliations, worshipped deities, and so on.

| Parameter | Default | Notes |
|---|---|---|
| `q` | required | substring, case-insensitive |
| `type` | all types | restrict to one type |
| `field` | all fields | the key printed in brackets, e.g. `goal` |
| `limit` | 25 | maximum 200 |

Each hit reports **which** field and value matched, so a result is self-explaining. An object is
counted once even when several of its values match.

This is the route for questions about ownership, which no event answers. Sites carry `owner`, `civ`
and `race` for whoever holds them now, plus `founder` when that differs, `region` for where they sit,
and `population`/`populationtotal` — the latter being the only measure of a site's *size*, since an
event count measures how well it was documented. Populations come from `-world_sites_and_pops.txt`,
so they are absent from exports that ship only the two XML files. Entities carry `leader`:

```bash
curl -s "http://localhost:15421/api/Analysis/objects/search?q=Dwarf&type=Site&field=race"
```

### `GET /events/search` — by event text

Full text search over the rendered prose of every event.

| Parameter | Default | Notes |
|---|---|---|
| `q` | — | substring, case-insensitive; optional when a filter is given |
| `limit` | 25 | maximum 200 |
| `fromYear`, `toYear`, `eventTypes` | — | shared filters |

Returns text: a header with the match count and the time spent, then the matching event lines.

`q` and the filters both narrow the world, so either alone is enough; only asking for **neither** is
a **400**, since that would return every event there is. Omitting `q` is how a year is read —
`?fromYear=290&toYear=290` — and it is the cheap direction: with no text to match the prose is never
rendered, so the query costs a comparison per event rather than a full render.

There is no index — the text only exists once the prose has been rendered, so each query renders the
events it examines. On a 494,436 event world an unfiltered query takes about 3.8 s; adding
`eventTypes=` brings it to about 40 ms, because excluded events are never rendered. Narrow first
when you can.

---

## Aggregating

### `GET /facets` — how common is a value

Base rates for object properties.

| Parameter | Default | Notes |
|---|---|---|
| `type` | all types | restrict to one type |
| `field` | — | omit to list the queryable fields |
| `limit` | 50 | maximum 1000 |

Without `field`, returns the fields available in the scope with `objects`, `occurrences`,
`distinctValues` and `valuesPerObject` — the last one making multi-valued fields visible at a glance.

With `field`, returns the value distribution:

```json
{
  "field": "goal", "label": "Goal",
  "objectsInScope": 41080,
  "objectsWithField": 17741,
  "distinctValues": 13,
  "values": [
    { "rank": 1, "value": "Attain Rank In Society", "objects": 3808, "occurrences": 3808, "share": 0.2146 }
  ]
}
```

`share` divides by `objectsWithField`, not by `objectsInScope`: most properties are recorded for only
part of the objects, and dividing by the whole scope understates every value. Both denominators are
returned so the choice is visible rather than assumed.

`objects` counts distinct objects, `occurrences` counts raw entries. They are equal for
single-valued fields and diverge for multi-valued ones such as `position` or `sphere`.

### `GET /top` — who holds the maximum

Rankings by a numeric measure. This is the complement of `/facets`: that one orders by how many
objects share a value, this one orders by the value itself.

| Parameter | Default | Notes |
|---|---|---|
| `type` | all types | restrict to one type |
| `by` | — | omit to list the available measures |
| `order` | `desc` | `asc` for minima |
| `limit` | 20 | maximum 200 |

Measures are the two intrinsic counts (`events`, `eventcollections`) plus every facet whose value
parses as a number. Adding a numeric property to `ObjectFacets` therefore makes it rankable without
touching this route. A non-numeric field answers **400**.

`objectsInScope` counts only the types the measure applies to. Ranking `worshippers` across all
types reports 41,080 — the historical figures — not the 89,732 objects of every kind, because a
denominator that mixes deities with rivers cannot be read against.

```json
{
  "by": "worshippers", "label": "Worshipped by (figures)", "order": "desc",
  "objectsInScope": 41080, "objectsWithMeasure": 30,
  "total": 140853, "min": 1335, "median": 4392, "max": 13346,
  "results": [
    { "rank": 1, "type": "HistoricalFigure", "id": 116, "name": "The Confident Yells", "value": 13346 }
  ]
}
```

The distribution travels with the leaders on purpose: a first place is not interpretable without the
spread. Here the top deity holds three times the median — a genuine lead, not a monopoly.

### `GET /crosstab` — one property broken down by another

`/facets` and `/top` each read a single property. A question phrased over two at once — *age at
death by caste*, *war casualties by attacker race*, *sites per civilization* — has no answer in
either, and the only remaining route is to pull the objects and redo the join outside the API. For
the classic DTOs that means parsing ids out of HTML anchors, which is exactly what this layer exists
to avoid.

| Parameter | Default | Notes |
|---|---|---|
| `type` | all types | restrict to one type |
| `field` | — | **required**, the facet to group by; any key `/facets` lists |
| `measure` | none | numeric measure to aggregate; any name `/top` accepts |
| `where` | none | one or more `field:value` clauses, comma separated, combined with AND |
| `limit` | 50 | maximum 1000 groups |

Without `measure` the groups carry object counts only. With it, each group also reports `total`,
`min`, `max`, `median` and `mean`, and the groups are ordered by `total` instead of by size.

**`where` is not a convenience.** Caste names are chosen per creature and every race reuses the
obvious ones, so `field=caste` on the whole world puts dwarves, humans and elves in the same `Male`
group and the result reads like a fact about one population when it describes four. The value is
matched whole and case insensitively — a substring match would let `race:Elf` also take the dark
elves, which is the confusion the restriction exists to prevent. A `where` missing either half
answers **400** rather than being dropped, since a silently ignored restriction answers a wider
question than the one asked — and one malformed clause fails the whole call, not just itself.

Several clauses matter more than they look. Verifying a lifespan means looking at the figures who
died of old age **and** belong to one race: with a single clause the median silently measures how
violent the world is instead. In this world dwarves cap at 150-170 years yet their median age at
death is 34, because almost nobody reaches the cap.

```bash
curl -s "http://localhost:15421/api/Analysis/crosstab?type=HistoricalFigure&field=caste&measure=ageatdeath&where=race:Orc,deathcause:OldAge"
curl -s "http://localhost:15421/api/Analysis/crosstab?type=War&field=attackerrace&measure=deathcount"
```

```json
{
  "field": "caste", "fieldLabel": "Caste",
  "measure": "ageatdeath", "measureLabel": "Age at death",
  "where": "race:Orc,deathcause:OldAge",
  "objectsInScope": 51696, "objectsWithField": 51691, "objectsWithMeasure": 37056,
  "groups": 3,
  "results": [
    { "rank": 1, "value": "Male", "objects": 21920, "objectsWithMeasure": 15146,
      "total": 751624, "min": 0, "max": 383, "median": 36, "mean": 49.63 }
  ]
}
```

Two denominators, for the same reason `/facets` has them: `objectsWithField` is how many objects
carry the grouping property at all, `objectsWithMeasure` how many of those also carry the measure.
They differ whenever a measure is recorded for only part of a group — an age at death exists only
for the dead — and dividing by the wrong one understates every group.

A multi-valued facet puts its object in every one of its groups, so the group counts can add up to
more than `objectsWithField`. Naming a `measure` that no object in scope carries answers **400**
rather than silently returning plain counts, which would look like an answer to a different
question.

---

## A worked sequence

```bash
# 1. where am I
curl -s "http://localhost:15421/api/Analysis/summary"

# 2. did this kind of thing ever happen
curl -s "http://localhost:15421/api/Analysis/events/search?q=toppled+the+government"

# 3. who was involved
curl -s "http://localhost:15421/api/Analysis/digest/HistoricalFigure/24707"

# 4. is the trait that stands out actually rare
curl -s "http://localhost:15421/api/Analysis/facets?type=HistoricalFigure&field=goal&limit=10"
curl -s "http://localhost:15421/api/Analysis/top?type=HistoricalFigure&by=worshippers&limit=10"

# 5. does it hold once split by the property that might explain it
curl -s "http://localhost:15421/api/Analysis/crosstab?type=HistoricalFigure&field=race&measure=ageatdeath"
```

Step 4 is the one worth insisting on. Steps 2 and 3 surface something striking; only the base rate
and the ranking tell you whether it is striking or merely common. Step 5 is what separates a real
pattern from a mixture: a world-wide median hides that two populations behind it may have nothing
in common.

## Status codes

| Code | When |
|---|---|
| 200 | success |
| 400 | empty `q`, or unknown `type`, `field` or `by` — the message names the discovery route |
| 404 | unknown id on `dossier` or `digest` |
| 409 | no world loaded |
