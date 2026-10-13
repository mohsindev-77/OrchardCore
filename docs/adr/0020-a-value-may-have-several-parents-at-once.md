# ADR-0020: A value may have several parents at once — an axis is a directed acyclic graph

**Status:** accepted, **pending second review**
**Date:** 2026-10-12
**Deciders:** Project lead, on the product owner's confirmation of conflict C1 in
`docs/design/frs-v2.5-dimension-mapping-design.md`

## Context

FRS v2.5 §3 and Rule 3 require that a Branch belongs to one Division **and** one Department at the
same time:

> *"Any dimension can be mapped to one parent dimension, or to more than one."*
> *"If a dimension is mapped to more than one parent dimension, every new value must be assigned to
> one value from each mapped parent before it can be saved."*

Figure 3 draws it: Business Unit, Division, Department and Cost Center are independent; Branch has
two arrows into it, one from Division and one from Department. Figure 9 draws the consequence —
Seef Branch sitting under two parents in the combined organisation tree, labelled *"Branch — 2
parents"*.

**The engine refuses this today, and refuses it by design.** Decision 4 of
`docs/dimension-engine-architecture.md` is *"Links are scoped to a structure"*, and its stated
rationale is that *"one record can sit on several axes at once, which a single parent field makes
impossible"*. The mechanism that delivers it is one parent **per axis**:

```csharp
// DimensionLinkDocument
public string? ParentOn(DateOnly asAt) =>
    Parents.FirstOrDefault(link => link.Range.Contains(asAt))?.ParentRecordId;
```

`Parents` is a list over **time**, not a list of concurrent parents. `InsertLink` closes whatever
entry it displaces the day before. ADR-0005 then builds on that: a closure row's effective range is
*"the intersection of the effective ranges of the links along **the** path from ancestor to
descendant"* — one path, definite article.

So the FRS's central requirement contradicts a recorded decision. Per CLAUDE.md the FRS wins on what
is built, and the conflict is reconciled explicitly rather than absorbed.

### What was measured before choosing

Two things were checked against the code rather than recalled, because they decide whether this is an
additive change or a rebuild.

**`DimensionClosureDocument` already permits several paths.** Its own doc comment says so:

> *"Every ancestor-descendant pair for this node, including the self pair at depth zero. **One
> ancestor can appear more than once with different depths or ranges**, because the path to it can
> change over time."*

`Ancestors` is `IReadOnlyList<ClosureAncestor>` and `ClosureAncestor` is
`(AncestorId, Depth, Range)`. A record reachable by two paths produces two entries, which is
representable today with no change to the document and no change to `DimensionClosureIndex`
— `(StructureId, AncestorId, DescendantId, Depth, EffectiveFrom, EffectiveToInclusive)` already has
room for it.

**`DimensionLinkIndex` already carries one row per parent edge**, keyed
`(StructureId, ChildId, ParentId, EffectiveFrom, EffectiveToInclusive)`. Concurrent edges are more
rows, not a different shape.

The storage chosen in ADR-0005 therefore already generalises. What does not generalise is the
**arithmetic**, and the invariant it rests on.

### The alternative that was rejected first

**One structure per mapped parent dimension.** Put Branch on a Division axis and on a Department
axis; each axis keeps exactly one parent per record, and ADR-0005 is untouched. This is the literal
application of architecture decision 4 and it was the first thing tried on paper.

It fails on the FRS's own requirement. Rule 5 asks for **one combined tree** — *"Business Unit,
Division, Department, Branch, Position, and Employee"* — generated from the mappings with no extra
setup. Figure 9 is one chart with one Seef Branch node and two incoming edges, not two charts.
Building that from several axes means a merge view with no stored identity of its own, a rule for
reconciling the axes' disagreements, and a user who has to answer "which axis did you mean?" about a
question they never asked. It also makes the "which parent is primary" question (D10) a question
about axes rather than about mappings, which is the wrong aggregate for it.

A second alternative, **a per-node exception list** layered on the single-parent model, was rejected
for the reason architecture §6 rejects per-node exceptions generally: *"an exception granted per node
is one nobody can audit."*

## Decision

**One axis is a directed acyclic graph. A record may have several parents effective at once, at most
one per mapped parent dimension.**

`ParentLink` gains a mapping key — the dimension type id of the parent the link satisfies:

```csharp
internal sealed record ParentLink(
    string? ParentRecordId,
    EffectiveRange Range,
    string? MappingKey = null);
```

**The invariant moves from "one parent per date" to "one parent per date per mapping key."** Every
piece of range arithmetic `InsertLink` performs today continues to hold — within a key. In
particular ADR-0005's addendum is unchanged in substance: a backdated move still **splits** rather
than overwriting, and it now splits within its own key, so a correction to a Branch's Division does
not touch its Department.

**`MappingKey` is nullable, and that is load-bearing.** Every `ParentLink` already stored
deserialises with it null. Null means *the one unkeyed parent slot*, which is exactly what a
pre-FRS tenant has: a Department under a Division, with no mapping declared anywhere. An unkeyed link
is treated as the parent for every mapping that has no keyed link of its own. So:

- no stored link is rewritten;
- no tenant's closure changes until somebody declares a mapping and places a value under it;
- Zenith, Crescent and every customer tenant resolve identically on every date, before and after.

That rule is stated once, on `DimensionLinkDocument`, and is the whole of what makes this additive.

### What changes, precisely

| | Today | After |
| --- | --- | --- |
| `ParentOn(asAt)` | the single parent | **replaced** by `ParentsOn(asAt)` → list, and `ParentOn(asAt, mappingKey)` → one. A caller wanting *the* parent asks for the **primary mapping** (D10) |
| `ComputeAncestorsAsync` | walks one chain up, intersecting ranges | walks **every** chain up, breadth-first, intersecting along each path, emitting one `ClosureAncestor` per `(ancestor, depth, range)` reached |
| All descendants of a node | rows where `AncestorId = node` | **`DISTINCT DescendantId`** — a node reachable twice appears twice |
| Employees under a node | assignment joined to closure | **`DISTINCT EmployeeId`**, enforced in the query. This is D1's "organisation-wide totals count UNIQUE employees", made a property of the engine rather than a rule callers must remember |
| `GetDepthAsync` | the depth on the root pair | the **shortest** path, and renamed to say so. Used for indentation, never for a rule |
| Cycle detection | "is the proposed parent already a descendant", across every date | **unchanged in statement.** It already runs against the closure, which after this change holds every path, so A→B by one mapping and B→A by another is refused without new code — and needs a test per ordered mapping pair, which it does not have today |

### Schema

One additive column, `DimensionLinkIndex.ParentDimensionTypeId`, in a new `UpdateFromNAsync` with
the upgrade test ADR-0009 requires. It is **not back-filled**: deriving a key from the parent's
current dimension type would assert a mapping nobody declared. Existing rows get empty, which is the
unkeyed slot.

`ParentLink.MappingKey` and `StructureDocument.Mappings` are document properties and need no
migration at all — a document written before them deserialises to the default, and in both cases the
default is the pre-FRS behaviour.

## Consequences

**This changes the architecture document.** §4's closure maintenance describes one path per pair and
must describe several; its query table gains a `DISTINCT` on two rows. §2's `DimensionLink` — *"one
parent-child edge, scoped to a structure and effective-dated"* — gains "and scoped to a mapped parent
dimension". Neither alters a decision in that document; both make explicit what the FRS forces.
ADR-0005's "the path" becomes "each path", and its addendum gains a sentence saying the split is
per key.

**Storage grows with the number of root-to-node paths.** A doubly-parented level roughly doubles the
closure rows for that level and everything beneath it. For the FRS's own shape — one doubly-parented
Branch level — that is a constant factor of about two, bounded by the cycle rule. It is not
unbounded, and it is not free.

**Two things become urgent rather than noted.** Architecture open question 6 — the retention policy
for closure rows on a long-lived tenant — is now load-bearing, as ADR-0005 already warned when it
made closure rows dated. And the §4 acceptance criterion, *descendant resolution under 200
milliseconds at 5,000 records and 5,000 employees*, must be **re-measured against a seeded
doubly-parented tenant** before the slice that implements this is accepted. If it fails, that is a
finding to bring back, not something to pre-solve here by guessing at an index.

**One defect becomes much easier to write and much harder to see.** A value drawn twice in the
combined tree makes every count beneath it double, and both numbers stay plausible. The mitigations
are specific: the renderer keeps a set of already-drawn record ids and draws the second edge to the
existing node; `DISTINCT` is in the query rather than in a caller's loop; and the FRS demo recipe
exists so a reviewer can check figure 9's numbers by eye.

**What is deliberately not constrained.** A value may still exist with no parent at all, even when
its dimension has a required mapping. That is the unplaced state; it arrives from imports and from a
retired parent, and the engine already models and reports it. Rule 3 governs the **Add form** — *"it
cannot be saved on its own"* — not the existence of a row. An import that produces an unmapped Branch
puts it in the unplaced panel with a reason, which is strictly more useful than failing the import.
This is the distinction a future reader is most likely to collapse, which is why it is in the
decision and not only in the design document.

**Pending second review**, per the definition of done's rule for a change that carries a migration
and reverses a recorded decision.
