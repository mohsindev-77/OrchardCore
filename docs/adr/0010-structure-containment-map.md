# ADR-0010: Containment is a per-structure map, not a chain of level ordinals

**Status:** accepted
**Date:** 2026-10-06
**Deciders:** Project lead

## Context

A structure was an *ordered list* of dimension types, and every containment rule was arithmetic on
the resulting ordinals, in one method (`DimensionValidator.ValidateLevelRulesAsync`):

```csharp
if (parentLevel >= childLevel)                                  → ParentTypeNotPermitted
if (childLevel - parentLevel > 1 && !structure.AllowSkipLevel)  → LevelSkipping
```

Three customer shapes arrived that a chain cannot express, all of them ordinary:

- **Zenith Engineering & Construction** — under a Division, either a **Department** or a
  **Project**; a Project then contains Teams. Department and Project are siblings, not one above
  the other.
- **Crescent Microfinance** — under one Division (Head Office) sit **Departments**; under another
  (Branch Network) sit **Regions**, then Branches, then Departments again. Two different types at
  one level, and the same type reachable at two different depths.
- **Al Noor Holding** — uniform Country → Business Unit → Department, which the chain *did* handle,
  and which must keep working unchanged.

A chain cannot represent a branch. Putting Department at ordinal 1 and Project at ordinal 2 asserts
that one is above the other, which is false, and that false assertion is then what decides what
level-skipping permits. Measured on the real engine with levels `[Division, Department, Project,
Team]`, strict:

```
allowSkipLevel=False | Project under Division = refused(LevelSkipping)   ← shape impossible
allowSkipLevel=True  | Team under Division    = ALLOWED                  ← must be refused
```

Both halves are wrong. With skipping off the customer's shape cannot be built at all; with skipping
on the rule collapses to "anything below may sit under anything above", which is not a rule.

Two further defects fell out of the same reading:

- `AllowsSelfNesting` is a property of the **dimension type**, so Section-inside-Section on the
  Organisation axis forces it on the Cost axis too.
- `GetPermittedChildTypeIdsAsync` ignored `IsStrict` entirely, so on the one kind of axis that flag
  exists for, the picker and the validator disagreed about what was allowed.

## Decision

**A structure carries an explicit containment map — a set of permitted `(parent type → child type)`
pairs, plus the set of types that may be roots — and that map is the only authority on what may sit
under what.**

```csharp
public IReadOnlyList<string> RootDimensionTypeIds { get; set; } = [];
public IReadOnlyList<StructureContainment> Containment { get; set; } = [];
```

Crescent, which was impossible, becomes:

```
roots:    Division
Division: Department, Region      ← two types at one level
Region:   Branch
Branch:   Department              ← the same type reachable at two depths
```

The surrounding settings keep their jobs, narrowed:

| Setting | After |
| --- | --- |
| `Levels` | Kept, demoted to **vocabulary and display order**: which types this axis uses, in what reading order. It defines the rows and columns of the editor's grid and the order of every type picker. It no longer decides containment. |
| `AllowSkipLevel` | Kept on the document, **no longer read by the validator**, shown read-only as converted. Its defect was being a wildcard; as explicit edges a customer can delete the ones they did not want. |
| `IsStrict` | Kept and now honoured by the **pickers as well as the validator**. Strict: an undeclared pair is refused. Non-strict: an undeclared pair is advisory, and the picker offers it. |
| `AllowsSelfNesting` (per type) | Kept as a **veto, not a grant**. Self-nesting now needs the type's flag *and* an explicit `X→X` edge on the structure, which is what finally makes it expressible per axis. |

One decision function, `ContainmentRules.Decide`, is called by the validator and by every picker, so
the two cannot drift apart again. A property test asserts they agree over every ordered pair of
types in a structure's vocabulary, strict and non-strict.

`DimensionRule.LevelSkipping` is retained in the enum and is no longer emitted. Audit entries
already reference it, and a persisted enum member is a contract.

Having no parent remains unconstrained. A record with no parent is either a root of the chart or a
unit in the unplaced panel, and `RootDimensionTypeIds` decides which — it does not forbid a
non-root type from being parentless, because moving a unit off the tree is a supported operation.

### Migration

`UpdateFrom4Async` (schema version 5) adds `StructureIndex.ContainmentRuleCount` and derives the
map for every existing structure:

- adjacent pairs `(Levels[i] → Levels[i+1])`;
- plus every `(Levels[i] → Levels[j])`, `j > i+1`, when `AllowSkipLevel`;
- plus `(T → T)` for each level type whose `AllowsSelfNesting` is set;
- roots = `[Levels[0]]`.

That is *exactly* the set the old arithmetic permitted, so every existing tenant's permitted
placements are identical before and after. Nothing is widened and nothing is narrowed, and no live
placement becomes invalid. The upgrade test asserts it pairwise: for every ordered pair of level
types, the derived map's answer equals the v4 arithmetic's answer.

The recipe step accepts the old form (`levelTypeCodes` + `allowSkipLevel`, derived through the same
function the migration uses — one implementation, two callers) and a new explicit form
(`rootTypeCodes` + `containment`). Supplying `containment` together with `allowSkipLevel` is refused
naming the conflict rather than silently merged.

## Alternatives considered

**Per-dimension-type map, global across structures** — the literal form the requirement was first
phrased in, and the one rejected most deliberately. A record participates in several axes by design
(ADR-0005: scoping links to a structure is what allows it), so `Department` is a global noun while
*what may contain a Department* is an axis-specific fact. A type-global map makes the Cost axis
inherit the Organisation axis's containment, and gives a tenant no way to say "Section nests inside
Section on the org chart but not on the cost structure" — which is one of the two defects this ADR
exists to fix. Per-structure is the smallest model that is actually correct, and reads identically
on screen.

**Per-level alternates** — each ordinal lists several permitted types. Expresses "two types at one
level" but not asymmetric descent: Crescent's Department-under-Head-Office and Region-under-Branch-
Network would both be level-2 alternates, so Branch-under-Department comes back. Better than a
chain, still wrong.

**Turn `IsStrict` off everywhere** — the zero-code option. Rejected: it abandons the validator's
purpose rather than fixing it, and the customers asking for ragged shapes are asking for *more*
precision, not less.

**Keep skipping and add per-pair exceptions** — a deny-list over the wildcard. Rejected: the
resulting permitted set is the difference of two things a user has to hold in their head at once,
and the architecture's own objection to per-node exceptions ("an exception granted per node is one
nobody can audit") applies with equal force to per-pair exceptions layered on a wildcard.

## Consequences

The editor gains a grid — the types in the structure down the side and across the top, a checkbox
where a pair is permitted — plus a top-level-types selector, and a "build from a simple chain"
button that fills both from the level order so the common one-chain case stays a single action.
Ragged and mixed-type shapes are expressible for the first time, and a tenant can narrow what
skipping had widened.

Containment is read from the structure document, which is already cached per tenant, so the pickers
lose their per-record index query: `GetPlacementTargetsAsync` goes from one `GetPermittedChildTypeIds`
call per node on the axis to one set computed once and a filter.

Level *order* no longer carries meaning the engine enforces, only meaning a reader uses. A structure
whose grid contradicts its level order is legal and will work; it will also look odd, which is the
right pressure.

---

## Addendum — the containment map is the only authority for self-nesting (2026-10-07)

### Context

The original decision kept `AllowsSelfNesting` on the dimension type as a **veto, not a grant**:
self-nesting needed the type's flag *and* an `X → X` edge on the structure. That looked like a
conservative choice — it reused an existing switch and could only ever narrow what the new map
permitted — and on the one screen that matters it was not conservative at all.

On the structures grid the diagonal cell was rendered disabled whenever the type's flag was off. A
customer trying to say "a Department may contain a Department" on Crescent, or "a Team may contain
a Team" on Zenith, found the cell greyed out on the screen whose whole purpose is to answer that
question, with no way to discover that the answer lived on the dimension types screen — a different
screen, about a different thing, reached by a different menu item. The tooltip named it; nobody
reads a tooltip they have no reason to hover over. It was reported as a bug, twice.

The deeper problem is that the veto contradicts this ADR's own argument. The reason containment is
per structure is that a record participates in several axes and "what may contain a Department" is
an axis-specific fact. *"May a Department contain a Department"* is the same kind of fact: a bank
nests its departments inside branch departments and a contractor does not, and the same tenant may
run both axes. A per-type veto re-imposes exactly the global answer the rest of this decision
rejects, and it does so for one cell of the grid.

### Decision

**The structure's containment map decides self-nesting, like every other pairing. The dimension
type is not consulted when a placement is decided.**

- Every diagonal cell in the grid is tickable like any other cell. Ticking it allows that type to
  nest inside itself **on that structure only**.
- `ContainmentRules.Decide` loses its `childTypeAllowsSelfNesting` parameter and its
  `SelfNestingVetoedByType` outcome. An undeclared diagonal is an undeclared pairing: refused on a
  strict axis, advisory on one that is not, which is what every other cell already does.
- `DimensionRule.SelfNesting` is no longer emitted, and joins `DimensionRule.LevelSkipping` as an
  enum member kept because audit entries written before this addendum name it.
- The "Allow a record of this type to sit under another record of the same type" checkbox is gone
  from the dimension types editor, along with the **Self-nesting** column on its list.

**The stored `AllowsSelfNesting` property stays**, and is not migrated away. It keeps exactly one
job: deriving a map from a **chain** description. A `structures` recipe row written as
`levelTypeCodes` + `allowSkipLevel` has nothing else to derive its diagonal from, and the v5
migration read structures the same way. Removing the property would leave such a row applying
cleanly while quietly meaning something narrower than it did — the worst of the available failures,
because nothing reports it.

That is also why this is not an append-only migration that drops the column. ADR-0009's rule is
that a migration never destroys data, and here the data is still load-bearing for an input format
we still accept. The property is written by the recipe step and by nothing else: the editor no
longer offers it, and the edit action now carries the stored value through unchanged so that saving
a type cannot silently clear it.

### Why permitted placements do not change

The v5 migration (`UpdateFrom4Async`) already derived `X → X` edges for precisely the types whose
flag was set. So for every structure that has been through it, "the map alone" and "the map plus
the veto" permit the same pairs — the veto could only ever have removed an edge the derivation
never wrote. `DimensionsMigrationUpgradeTenantTests.TheDerivedContainmentMapPermitsExactlyWhatThe
LevelArithmeticDid` is the proof: its "after" reading previously applied the veto on top of the map
and now reads the map alone, and the pairwise comparison against version 4's arithmetic still
passes for both values of `allowSkipLevel`.

For a structure created from a chain *today*, the same derivation runs with the same input, so the
answer is the same. For a structure created from an explicit grid, the type was never consulted on
anything except the diagonal, and the diagonal is now the grid's to state.

### Consequences

A tenant that wants self-nesting on one axis ticks one cell on that axis, and nothing else in the
tenant changes. A tenant that had a type-wide veto has it as unticked diagonals on each structure,
which is the same restriction written where it can be read and changed.

New dimension types are created with the flag off and nothing ever turns it on, so over time it
becomes a field that is false everywhere except in tenants seeded from a pre-addendum recipe. That
is the point at which it can be dropped with a migration that genuinely destroys nothing — which is
a decision for whoever retires the chain-shaped `structures` row, not for this addendum.
