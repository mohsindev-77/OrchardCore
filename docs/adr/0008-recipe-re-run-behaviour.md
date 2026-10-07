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
