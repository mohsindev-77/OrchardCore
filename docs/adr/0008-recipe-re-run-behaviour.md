# ADR-0008: Recipe re-run behaviour — per-step idempotency, not cross-step atomicity

**Status:** accepted
**Date:** 2026-10-05
**Deciders:** Project lead

## Context

`dimension-types`, `structures` and `dimension-records` each validate their own JSON array as one
batch and write nothing until every row in it has passed — so a single step is already
all-or-nothing. The recipe as a whole is not: each step commits independently, so a failure in
`dimension-records` after `dimension-types` and `structures` have already succeeded leaves their
writes in place. This surfaced from a real report: running the organisation designer demo recipe
from `/Admin/Recipes` failed on `dimension-records`, leaving `demo-division`, `demo-department`,
`demo-section` and `demo-org` behind. The admin screen's error was unhelpful on its own —
`OrchardCore.Recipes.Services.RecipeExecutor` replaces whatever a step reports with a generic
"Unexpected error occurred while executing the '{0}' step." regardless of cause — but the
underlying gap it exposed was real: a bare retry hit `dimension-types`' own duplicate-code check
and failed differently, with the leftovers never cleaned up automatically.

Two ways to close that gap were on the table.

**Cross-step atomicity** — wrap the whole recipe execution in one YesSql session that only commits
once every step has succeeded — was considered and rejected. It needs the steps to share a
transaction boundary that nothing in this module currently controls: `IRecipeExecutor` manages
shell scopes itself (`RecipeDescriptor.RequireNewScope`, per ADR-0006's note on the executor), and
reaching across that to force one commit for an entire recipe is a change to how every recipe in
the application behaves, not only this module's three steps — risking `OrchardCore.Deployment` and
any other recipe source. It also does not match how a human re-applies a recipe in practice: a
recipe is normally re-run precisely *because* something partway through failed, and the operator's
expectation is "finish the job", not "discard what already worked and start over".

**Per-step idempotency** is the decision taken instead: a row naming a code that already exists is
compared against the tenant's data, field by field, and either skipped — exactly matching content,
nothing written, no error — or the step fails, naming the code and exactly what differs. A
dimension record's comparison includes where every placement the row names currently has it, not
only its own fields, since a record can match on name and type while sitting under the wrong
parent.

## Decision

**Re-running a recipe must be safe. Each of the three steps treats an existing row as a merge
candidate, never as something to silently overwrite:**

- Content matches exactly → skip, no write, no error.
- Content differs → fail the step, naming the code and the field(s) that differ, writing nothing
  from that step. A silent overwrite is never an option: the step cannot tell whether a divergent
  row in the tenant is the thing the recipe is trying to establish or a change someone made
  deliberately since.
- Within a step, the existing all-or-nothing batch validation is unchanged — a mix of matching,
  mismatched and genuinely new rows in one step still validates and writes as one unit.

The consequence that matters most in practice: the demo recipe's own leftovers from a failed run
are not an obstacle to fixing the problem and re-running — `dimension-types` and `structures`
recognise their own prior output as already correct and skip it, and only `dimension-records`,
which never wrote anything on the failed run, has anything left to do.

## Consequences

A recipe step's JSON is now closer to a declared desired state than a one-shot script: applying it
twice, or applying it after a partial failure, converges on the same result rather than erroring a
second time for a different reason or duplicating data. `DimensionsRecipeStepsTenantTests` covers
all three: running the demo recipe twice succeeds and changes nothing; leftovers from a deliberately
broken run do not block a fixed re-run; and a same-code row with different content fails naming the
difference, writing nothing in that step, and leaving the tenant's existing data untouched.

Comparison is scoped to the fields a row actually states. `dimension-types` rows carry no attribute
schema (see the module README), so an existing type's schema is not part of the comparison; a type
that needs one is still built through the admin screen, not the recipe.

Cross-step atomicity remains available to revisit if a future recipe genuinely needs "all steps or
none" — nothing in this decision forecloses it — but it is not the default, and no recipe in this
module depends on it.

---

## Addendum, 7 October 2026: attribute schemas and attribute values are compared too

**Status:** accepted
**Deciders:** Project lead

### Context

The original decision recorded that `dimension-types` rows carry no attribute schema, so an
existing type's schema was not part of the comparison, and that a type needing one was built
through the admin screen. ADR-0011 reversed that: an export that cannot carry a Project's start
date does not reproduce the tenant it came from, so both steps now carry attributes —
`dimension-types` a schema, `dimension-records` the values.

Anything a row states has to be compared, or a re-run silently diverges from the file that
describes it.

### Decision

**Attributes follow the same rule as every other field: identical → skip, different → fail naming
what differs.**

- `dimension-types` compares the schema **in order**, by name, kind, required flag and both label
  halves. A difference is reported as *"the attribute schema differs from the tenant's existing
  type"* — the schema is an ordered whole and a per-field diff of it would be noise.
- `dimension-records` compares each attribute **value** the row states, and names the attribute
  individually: *"the attribute 'BranchCode' is 'LHR-001' in the tenant but 'LHR-002' in the
  recipe"*. A value is the sort of thing an operator can go and look at, so the message says which
  one.

**Both comparisons are scoped to what the row actually states**, which is the rule the original
decision already set for names. A row with no `attributes` key says *nothing* about attributes
rather than claiming there are none, so every recipe written before this addendum still means
exactly what it said and still re-runs clean. An attribute the row *does* state and the tenant does
not hold is a difference, because that is the row making a claim the tenant contradicts.

Comparison goes through `RecipeNameComparison`, so null and empty are the same value and
surrounding whitespace is not part of one — the same normalisation names already use, and for the
same reason: a recipe carrying `null` and a tenant holding `""` describe the same absent value, and
reporting them as different would refuse a re-run of a correct recipe.

### Consequences

A recipe is now a complete description of a tenant's organisation rather than most of one, which is
what makes the ADR-0011 round trip meaningful: the exporter writes attributes, the importer applies
them, and this comparison is what stops the second run of the same file from either failing or
quietly writing something different.

Attribute **values are not dated**, so there is no history to compare — they are fields on a
record's content part with no effective range, unlike its name, its placement and its existence.
If a customer ever needs "what was this branch's cost centre last March", that is a model change,
not a comparison change, and this addendum will need revisiting with it.

---

## Addendum — sort order (2026-10-07)

### Context

Sibling order on the chart is `SortOrder`, then English name. Nothing set `SortOrder`, so every
record sat at zero, the name was the only clause that ever applied, and every tree came out
alphabetical — Zenith's recipe lists *Engineering, Projects, Corporate* and the designer drew
*Corporate, Engineering, Projects*. Records are now given a sort order when they are created: one
past the highest in the tenant, so creation order is the order, and for an import that is the order
the file lists them in.

That makes sort order something a recipe can state, and therefore something this ADR has to say how
to compare.

### Decision

**`sortOrder` is compared only when the row states one**, exactly like every other field here.

A row that says nothing about sort order is not claiming the record has none; it is saying the
order the recipe lists it in is good enough, and that order is already what the record got. Treating
an absent `sortOrder` as "must be zero" would fail the second run of every recipe this module
ships, because the first run is precisely what gave those records non-zero orders.

A row that *does* state one and disagrees with the tenant fails the step naming both numbers, the
same shape as every other difference.

### Consequences

An export states `sortOrder` on every record, including zero, because an export reproduces a tenant
rather than expressing an intent — and a round trip that rebuilt the same tree in a different
left-to-right order would have reproduced something else. A hand-written recipe normally states
nothing and gets its own listing order, which is the thing its author can actually see.

---

## Addendum — employee attachment, and the one case where absent is not a difference (2026-10-11)

**Status:** accepted, **pending review** — this widens the original decision and a reviewer may
prefer the alternative recorded below.

### Context

The Zenith and Crescent demo recipes describe structures that declare no
`employeeAttachableTypeCodes`, so every type on them may hold employees. That is correct by the
model — `StructureDocument.EmployeeAttachableDimensionTypeIds` documents empty as *unconstrained*,
not as *nothing may hold employees* — and it is wrong as a demonstration: "Place an employee
here…" appears on Engineering Division, and an employee placed at a Division is counted by it and
again by every roll-up beneath it. The demos should show the constraint, so the recipes now state
it.

That makes `employeeAttachableTypeCodes` something a `structures` row states, and therefore
something this ADR has to say how to compare. Applying the original rule literally:

- a fresh tenant gets the structure with the constraint; fine;
- **a tenant that already applied the old recipe fails the step**, because the row now states a
  value and the tenant holds a different one.

Every existing demo tenant — every developer's, and the reference tenants — would be unable to
re-run the recipe that built it. That is the behaviour the original decision chose on purpose for a
*divergent* row, and the question is whether this row is divergent.

### Decision

**Empty is not a different value for this field; it is the absence of one. A row that states
`employeeAttachableTypeCodes` against a structure holding none fills it in rather than failing.**

Three outcomes instead of two, for this field alone:

| Tenant holds | Recipe states | Outcome |
| --- | --- | --- |
| the same set | the set | skip, as before |
| a different, non-empty set | the set | **fail**, naming both, as before |
| nothing | a set | **fill it in**, and say so |

The argument is narrow and rests on the model rather than on convenience. ADR-0008 refuses to
overwrite because "the step cannot tell whether a divergent row in the tenant is the thing the
recipe is trying to establish or a change someone made deliberately since". For this field it can:
empty is documented, in the model and in the module README, as *the structure is not expressing the
constraint* — the state every structure written before the field existed deserialises to, and the
state the v5 migration deliberately left them in. There is no decision there to overwrite. A
customer who means "this axis constrains nothing" and a customer who has never been asked are
indistinguishable because the model says they are the same thing.

A non-empty set is a real answer and is still refused, which is the half that matters: a tenant
that has ticked Branch and Department does not silently become Department because a recipe said so.

### Alternative considered: a new recipe file

Shipping `organisation-designer-zenith-v2.recipe.json` and leaving the old file alone would have
honoured the original rule exactly. It was rejected because the demo recipes are reference data
rather than a customer's: the files are named in fixtures, in the browser suite, in the module
README and in `DemoCompanyRecipesTenantTests`, and a second file would double all of that to avoid
a conflict that the model says is not a conflict. The cost would be carried for ever by every
reader who has to work out which of two files is current.

**If a reviewer disagrees that empty is an absence, the alternative is the fallback** — this
addendum is the thing to overturn, and the comparison in `StructuresRecipeStep.DifferencesFrom` is
the one place that implements it.

### Consequences

`structures` is now the only step that can write to a row it did not create. That is worth naming
plainly, because it is a real widening: the step's contract was "create or skip or fail", and it is
now "create, fill a gap, skip, or fail". The fill goes through `IStructureService.UpdateAsync`, so
it is validated and audited exactly as an administrator's own edit on the structures screen would
be — it is not a direct document write — and it changes nothing except the one field.

`DimensionsRecipeStepsTenantTests` covers all four outcomes, including the one that matters most:
a tenant whose structure already names a *different* set still fails, writing nothing.

---

## Addendum — a lookup list's missing entry is a gap to fill, not a difference (2026-10-12)

**Status:** accepted, **pending second review** — this widens the original decision a second time,
along the line the `employeeAttachableTypeCodes` addendum already drew.

### Context

Slice B1 shipped `lookup-lists` with the original rule applied literally, and the module README
recorded the cost as a known limitation:

> A stated entry the tenant does not hold is the row making a claim the tenant contradicts, so the
> step fails naming it. The consequence is that shipping `grade` with a sixth entry to a tenant
> that has five means the operator adds the sixth through the screen.

Slice B2 is what makes that unaffordable. The standard lists — job title, grade, employment type,
document type, nationality, country, currency, relationship, contact type — now ship as a recipe,
and they are the kind of reference data that grows: a new document type when a country changes its
visa regime, a grade when a customer adds a band, a nationality that was simply missed. Under the
original rule the second version of that recipe cannot be applied to any tenant that has the
first, and the only path is an administrator retyping entries from a file they are holding.

Worse, the failure is total rather than partial: a step that fails writes nothing, so one missing
entry blocks every other list in the same file.

### What makes an entry different from an attribute value

The original decision refuses to overwrite because "the step cannot tell whether a divergent row in
the tenant is the thing the recipe is trying to establish or a change someone made deliberately
since". That reasoning is exactly right for a value that exists, and it says nothing about one that
does not.

An entry the tenant does not hold is not a divergence. There is no decision to overwrite, no
administrator's edit to discard, and nothing stored anywhere that names it — ADR-0014 guarantees
the last of those, because an entry cannot be removed, only retired. So an entry absent from a
tenant's list has never been in it, which is the same shape of reasoning the
`employeeAttachableTypeCodes` addendum used: the state is an absence rather than an answer.

The inverse is still a real answer and is still refused. A tenant whose `G3` reads "Grade three"
when the file says "Grade 3", or whose `G9` is retired when the file says it is offered, has made a
decision; silently reverting it is the behaviour this ADR exists to prevent.

### Decision

**For `lookup-lists`, an entry the recipe names and the tenant's existing list does not hold is
added. An entry that exists and differs still fails the step, naming what differs.**

Five outcomes for a list the tenant already has:

| Tenant holds | Recipe states | Outcome |
| --- | --- | --- |
| the same entry, same names, same active state, same order | the entry | skip |
| **no such entry** | the entry | **add it**, and say so |
| an entry whose names, active state or order differ | the entry | **fail**, naming the entry and the difference |
| entries the recipe does not name | — | left alone; not a difference |
| a different list name | a name | **fail**, naming both |

The list's own bilingual name is unchanged by this addendum: it is a value that exists, so a
difference in it still fails.

**The fill goes through `ILookupListService.UpdateAsync`**, so it is validated and audited exactly
as an administrator's own edit on the lookup list screen would be — it is not a direct document
write. One `LookupListChanged` entry per list the step fills, carrying the before and after, which
is what makes "where did this entry come from" answerable.

**The step says what it filled.** A recipe that silently changes a tenant is the thing an operator
cannot review; the added entries are reported through the recipe execution so that applying a pack
and reading what it did are the same action.

### Why not simply let the recipe state the whole list

The obvious alternative — treat the file as the desired state and make the tenant match it — was
rejected for the reason ADR-0014 gives for refusing removal at all. Entries cannot be removed, so a
"whole list" semantic could only ever add and amend, and amending is precisely the half this
addendum keeps refusing. What would be gained is the ability to rename an entry from a recipe,
which is not something a pack should do to a customer who has renamed it themselves.

### Consequences

`lookup-lists` joins `structures` as a step that can write to a row it did not create. Its contract
is now "create, fill a gap, skip, or fail", and the module README's known limitation is removed
rather than reworded, because the limitation is gone.

A pack can be revised and re-applied, which is what the starter lists need in order to be
maintainable at all. A customer's own entries beside the shipped ones are untouched either way.

**If a reviewer disagrees that a missing entry is an absence**, this addendum is the thing to
overturn, and `LookupListsRecipeStep.DifferencesFrom` together with its `MissingEntries` companion
is the one place that implements it.

`LookupListsRecipeStepTenantTests` covers all five outcomes, including the two that matter most: a
tenant that already holds an extra entry of its own has nothing added and nothing fail, and a
tenant whose entry genuinely differs still fails with nothing written.

---

## Addendum — a menu order of zero, and a strictly additive axis (2026-10-14)

**Status: accepted, pending second review.** Two more gaps a recipe may fill, found the same way the
last one was: by a recipe that could not be re-applied to the tenant that most needed it.

### What went wrong

Manual testing on a tenant built from an earlier version of the Zenith demo:

```
Configuration → Recipes → "WorkMate demo: Zenith Engineering & Construction"
  fails at 'dimension-types':
    zenith-division:   menuOrder 0 in tenant vs 10 in recipe
    zenith-department: allowsSelfNesting True vs False; menuOrder 0 vs 20
    zenith-project:    menuOrder 0 vs 40
    zenith-team:       allowsSelfNesting True vs False; menuOrder 0 vs 50
```

Slice F5+F7 had put the new positions and grades into that same file, so the tenants that already
had the organisation to hang them on were **exactly the tenants that could never receive them**.
This ADR's own sentence — *existing tenants are unaffected unless the recipe is re-applied* —
turned out to describe the case that fails rather than the case that is safe.

### Decision 1 — a stored `menuOrder` of zero is a gap, not a difference

`DimensionTypeDocument.MenuOrder` arrived with slice F2. **No tenant created before it could have
authored a value**, and zero is what every type born before it deserialises to. So a row stating an
order against a tenant holding zero is filling a gap, and the step fills it through
`IDimensionTypeService.UpdateAsync` — validated and audited exactly as an edit on the Dimension
types screen would be, changing that one field and reading everything else back from the type.

This is the same argument `Migrations.UpdateFrom11Async` makes for an unauthored plural and the
11 October addendum makes for an empty employee-attachment set.

**The risk, stated rather than hidden.** Zero is *also* a legitimate order somebody could type to
put a dimension first, and nothing can tell the two apart. It is accepted because the cost of being
wrong is a menu position a customer changes in one field, and the cost of refusing is a recipe
nobody can re-apply. A tenant that has deliberately ordered its menu has almost certainly given its
other entries non-zero orders, which are still compared and still refused.

**Only the order.** A plural, a `showInMenu` or a role that differs is still a difference, because
for each of those the tenant's value is one somebody could have meant.

### Decision 2 — a structure row may say what to **add**, in a form of its own

`StructuresRecipeStep` compared levels, root types and containment by equality, so a row stating one
more level than the tenant held was "the levels differ" and the recipe was refused. That made *"add
a dimension to an existing organisation"* — which is what a recipe extending a demo does —
inexpressible as a recipe at all.

A `structures` row may now state `addLevelTypeCodes` and `addContainment` **instead of** describing
the structure. Such a row carries nothing but the code and those two keys: it names levels to append
and pairings to permit, and says nothing whatever about the levels, pairings, names, roots,
strictness, employee attachment or short name already there. Missing levels and pairings are added;
everything else is read back from the structure as it stands.

**This replaces an inferred rule that was wrong, and the way it was wrong is the argument for the
explicit form.** The first attempt inferred "additive" from a full restatement that happened to be a
superset — tenant's levels a prefix of the row's, every root still a root, every pairing still
permitted. It was refused by the very tenant it was written for. That tenant's map carries
`department → department` and `team → team`, self-pairs
`StructureContainmentDerivation.FromChain` derived from the old `allowsSelfNesting` flags and
`Migrations.UpdateFrom4Async` wrote. No current row restates them, so the tenant was not a subset of
anything and the whole recipe was refused. **Inferring an instruction from a description was the
mistake.** A description can only ever be compared against what is there, and a tenant is entitled
to hold things no description mentions. An instruction does not have that problem, so the recipe
says it outright.

**Mixing the two forms is refused.** A row that both instructed and described — "add Position" and
"the levels are exactly these" — would have two answers to the same question, and merging them would
mean the recipe said something nobody wrote. Same refusal, for the same reason, as stating both
`allowSkipLevel` and an explicit `containment`.

**What an additive row refuses, each naming the thing at fault:** a structure that does not exist,
because such a row extends an axis another recipe created and never invents one; a type code that
names no type; and a pairing whose parent or child is neither already a level nor one being added,
because an inert rule on a containment grid is a rule somebody will later read as permission. That
last is the "genuine contradiction" case — a pairing the tenant's own shape could never honour is
named rather than written and ignored.

**Widening is neither free nor silent.** The change goes through `IStructureService.UpdateAsync`,
which runs `PlanLevelChangeAsync` first and refuses the whole change — naming the record at fault —
if a live placement would be invalidated, and audits it as `StructureChanged` with the map before
and after: validated and audited exactly as an administrator's own edit on the structures screen.
What a recipe still cannot do is take something away.

**Idempotent by construction.** A level or pairing already present is not added twice, and a row
that computes the set the tenant already holds returns before writing — so a re-run is a no-op in
the audit trail as well as in the data.

### Decision 3 — `allowsSelfNesting` is still refused, and no migration aligns it

Deliberately, for three reasons, and the first settles it.

**Nothing can establish what set it.** No shipped version of the Zenith recipe ever wrote `true` —
checked back to the commit that added the file. No migration writes the flag. The only other writer
was a self-nesting checkbox on the Dimension types screen that ADR-0010's addendum removed. So on
that tenant the value is as likely to be somebody's deliberate tick as a stale default, and a
migration that flipped it would be the product overwriting a customer's configuration to make a
demo file re-appliable.

**Nothing reads it.** Since that addendum the structure's containment map is the only authority on
self-nesting; the one remaining reader is a chain-shaped `structures` row, which Zenith's is not.
The divergence is inert — it changes nothing a reader sees or a rule enforces.

**Nothing needs it.** The positions-and-grades recipe does not restate those types, so it applies
regardless, which is the outcome that was actually wanted.

A migration would also have had to hard-code demo-recipe type codes into the product's schema
history, where ADR-0009 makes them permanent.

### Consequences

`dimension-types` joins `structures` and `lookup-lists` as a step that can write to a row it did not
create. Its contract is now **create, fill a gap, skip, or fail**.

`structures` gains a fifth: **add to**. It is worth keeping the two apart in the reader's mind — a
describing row is checked against the tenant and a gap in it may be filled; an additive row is
carried out. Every other step in the product describes, and should stay that way; this one has an
instruction because extending somebody else's axis is not a thing a description can express.

**One related defect was fixed at the same time**, because the split recipe could not work without
it: `dimension-records` resolved a placement's parent only among records named in the *same file*,
so its message *"parents must be listed before their children"* was enforcing the much stronger
*"parents must be listed"*. A parent already in the tenant is now resolved from it, which is what
lets one recipe hang something off a tree another built. The file still wins where both define a
code.

**If a reviewer disagrees that a stored zero is an absence**, this addendum is the thing to
overturn, and `DimensionTypesRecipeStep.FillsTheMenuOrderGap` is the one place that implements it.
For the additive row it is `StructuresRecipeStep.AddToAsync`, reached only from
`StructureStepEntry.IsAdditive`.

`ZenithPositionsRecipeTenantTests` covers it end to end on a tenant built from the pre-F1 recipe
taken out of git — self-nesting on Department and Team, menu orders of zero, and the two derived
self-pairs the real tenant carries. The new recipe applies and brings grades, positions and the
reporting chart; positions sit under the departments that were already there; the tenant's own
self-pairs and absent short name survive untouched; a re-run changes nothing; a tenant left
half-done by an earlier failed attempt, with the two dimension types already created, is picked up
where it left off; an additive row naming a structure that is not there is refused by name; the main
recipe now gets past menu order and still refuses on `allowsSelfNesting` and **only** that; and a
menu order somebody really did choose is still refused.

### Postscript — a demo-data change that this ADR deliberately refuses (2026-10-15)

Manual testing pointed out that Zenith's **Site Supervisor** carried MGR-1 and MGR-2 — the
technology manager's grades. Sharing a ladder says two posts are the same level, which is the one
thing a grade exists to say, so the recipe now ships SS-1 and SS-2 and points Site Supervisor at
them.

**On a tenant that already applied the earlier version, the `dimension-references` step refuses that
one row, and should.** `{MGR-1, MGR-2}` is an answer somebody could have meant — it is exactly what
a customer who had adjusted the demo would hold — so it is a *difference*, not a gap, and nothing in
this ADR lets a recipe overwrite a decision. The message names both sets. The repair is two clicks
on the Site Supervisor page: Edit, tick SS-1 and SS-2, untick the MGR grades.

Recorded because the temptation is to widen the gap rule until the demo re-applies cleanly, and
that would trade a two-click repair on one demo row for a recipe that can silently rewrite a
customer's grade ladder. The whole of this ADR is the argument against that trade.
