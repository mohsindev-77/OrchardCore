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
