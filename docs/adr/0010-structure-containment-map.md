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
