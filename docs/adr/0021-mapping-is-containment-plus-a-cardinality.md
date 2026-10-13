# ADR-0021: Mapping is containment plus a cardinality, and it lives on the structure

**Status:** accepted, **pending second review**
**Date:** 2026-10-12
**Deciders:** Project lead, on the product owner's confirmation of the design review of
12 October 2026 (decisions D10, D12, D14)

## Context

FRS v2.5 §3 introduces *mapping*: *"Any dimension can be mapped to one parent dimension, or to more
than one."* Figure 3 draws Branch with arrows from Division and from Department. Rule 3 then makes it
compulsory:

> *"If a dimension is mapped to more than one parent dimension, every new value must be assigned to
> one value from each mapped parent before it can be saved."*

and Rule 4 makes its absence meaningful:

> *"If a dimension has no mapped parent, the parent-assignment step is skipped."*

**Half of this already exists.** ADR-0010 stores, per structure, an explicit containment map — a set
of permitted `(parent type → child type)` pairs, plus the set of types that may be roots — and that
map is the only authority on what may sit under what. Read child-first — *which types may be my
parent* — that is exactly figure 3's arrows. Nothing new is needed to express **which** dimensions a
dimension is mapped to.

**The half that does not exist is obligation.** Containment is permissive: *a Branch may sit under a
Division*. `AddUnitAsync` will happily create a Branch with no parent at all, and that is a
deliberate feature — the unplaced state, which imports and retired parents both produce and which the
unplaced panel reports with a reason. Rule 3 is a different kind of statement: *a Branch must name
one Division and one Department before it can be saved.*

A third thing is missing and is easy to overlook: **order**. Figure 8 numbers the groups on the Add
form — Ⓐ Division, then Ⓑ Department — and says why: *"a dimension may be mapped to more than two
parents; a two-column layout would not scale, while an added group simply appears below the ones
before it."* The containment map is a set of pairs and has no place to carry a reading order.

## Decision

**A mapping is a `(child type, parent type, cardinality)` triple stored on the structure, beside
containment and separate from it.**

```csharp
// StructureDocument gains one list. Containment is untouched.
public IReadOnlyList<StructureMapping> Mappings { get; set; } = [];

public sealed record StructureMapping(
    string ChildDimensionTypeId,
    string ParentDimensionTypeId,
    MappingCardinality Cardinality,
    bool IsPrimary);

public enum MappingCardinality
{
    Optional,     // may name one; may name none. What every pairing is today.
    ExactlyOne,   // Rule 3. The default when a mapping is declared (D12).
    OneOrMore,    // at least one, several permitted. Grade ranges (D8).
}
```

The list's own order is the order figure 8's groups appear in.

### Why a separate list rather than a flag on `StructureContainment`

Three reasons, and the first is the one that settles it.

**A self-nesting diagonal is a permitted pairing and is never a mapping.** `Department → Department`
makes HR → Recruitment possible; it must never make the Add form ask "which parent Department does
this Department belong to?", because a dimension's own root values have none. Folding mapping into
containment would put the diagonal on the Mapping screen, which is precisely the kind of question on
the wrong screen that ADR-0010's own addendum was written to fix.

**A pairing may be permitted without being a mapping.** Every structure written before today is in
exactly that state, and must stay in it.

**A mapping has a stated order; containment is a set.** Figure 8 numbers its groups, and the FRS
gives a reason that is about scaling rather than aesthetics.

### Empty means every pairing is optional

A structure written before this field exists deserialises `Mappings` to empty. **Empty means no
mapping is declared, which means every declared containment pairing behaves exactly as it does
today** — permitted, never required. Zenith, Crescent and every customer tenant are unchanged until
somebody opens the Mapping screen and ticks something.

This is the same rule `StructureDocument.EmployeeAttachableDimensionTypeIds` already states — *"empty
means this axis does not constrain it"* — and the same rule ADR-0008's 11 October addendum applies to
a recipe row that states nothing. It is written down a third time here because it is what makes this
decision additive, and because the alternative reading ("empty means nothing is permitted") would
break every existing tenant on the first save.

### Rule 3 is enforced in the validator, and it governs the form

`IDimensionValidator` gains the rule, so the Add form, the `dimension-records` recipe step and any
future API are refused **in the same words from the same code** — the rule ADR-0010 established when
the validator and the picker were found disagreeing about the one case the flag existed for.

**But the rule governs the Add form, not the existence of a row.** Rule 3's own wording is *"before
it can be saved"*, and the FRS is describing a creation screen. A value with no parent remains legal:

- an import that produces an unmapped Branch puts it in the **unplaced panel with a reason**, which
  is strictly more useful than failing the whole import;
- a value whose parent is retired becomes unplaced through no act of its own, and already carries the
  `OrphanedByParentRetirement` badge naming the parent and the date;
- moving a unit off the tree is a supported operation with a dated entry of its own (stage D2).

Refusing to *store* an unmapped value would break all three. This is the distinction a future reader
is most likely to collapse, and it is the reason this paragraph is in the decision rather than only
in the design document.

### Declaring a mapping on a tenant that already has data

Ticking `Branch → Division` as required on a tenant with forty branches, six of which have no
division, **previews the impact and warns**. It does not refuse. The existing
`IStructureService.PlanLevelChangeAsync` dry-run-then-apply shape carries it, the same way a level
change and a containment change already do.

Warning rather than refusing, because refusing would make the rule unreachable for every tenant with
any untidy data — which is every tenant with real data — and because those six branches are the
unplaced panel's job and it already does it.

### The primary mapping (D10)

`IsPrimary`, at most one per child dimension type, declared on the Mapping screen: *"a Branch's
Division is its primary parent"*. It answers the question a flat report has to ask — which single
parent to attribute to — for the per-dimension list's Parent column, for payroll attribution, for
approval routing and for any export that carries one parent per row.

**Declared rather than derived from the list order**, because deriving it would let the layout of a
configuration screen decide payroll attribution, and because the two facts change for different
reasons: somebody reordering the Add form's groups is not making a decision about cost.

## Consequences

**ADR-0010 is amended, not superseded.** Its containment map remains the single authority on what may
sit under what, `ContainmentRules.Decide` remains the one implementation read by the validator and
every picker, and the diagonal remains a cell of the grid. What is added sits beside it and answers a
question it was never asked: *must* this pairing be satisfied, and in what order are the choices
presented. The relationship is recorded as a one-paragraph addendum in ADR-0010 so a reader arriving
there is not left to infer it.

**The Mapping screen is the Structures editor, re-presented.** Its four blocks become three; the
containment grid gains a second state per ticked cell (permitted / required) and a primary marker.
Everything that already holds carries over: server-rendered first and kept in step by `structures.js`
so it works with no script; a post with no ticks read as "describe this as a chain"; a change that
live placements rely on previewed before it saves. The word *structure* leaves the UI — the screen
becomes **Organisation → Settings → Mapping** — but the aggregate, and the reason ADR-0005 scoped
links to it, are untouched.

**One new rule falls out for free and must still be tested.** A mapping's parent must be of the
mapped dimension: a Branch's Division slot may only hold a Division. That is `ContainmentRules.Decide`
applied per key rather than once, so it needs no new implementation — and it needs a test per mapping,
which it does not have.

**What this does not decide.** Whether a cardinality can span several mappings — *"a Position must be
under a Branch **or** a Department"* — is left out deliberately (D14). `AtLeastOneAcross` is a fourth
enum member that nobody has yet needed; a tenant expresses the same intent today by making both
mappings Optional and accepting that the engine will not enforce the disjunction. If a slice proves
it is needed, it is an additive enum member and an additive validator rule, which is a small ADR
rather than a reopening of this one.

**Pending second review**, per the definition of done's rule for a change carrying a migration and a
new recipe-step field.
