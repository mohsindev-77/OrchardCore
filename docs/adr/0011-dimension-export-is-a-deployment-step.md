# ADR-0011: Exporting dimensions is a deployment step that writes the import format

**Status:** accepted
**Date:** 2026-10-07
**Deciders:** Project lead

## Context

`dimension-types`, `structures` and `dimension-records` can build a tenant's organisation from
JSON. Nothing could produce that JSON from a tenant that already had one, so every path that needs
it — promoting a configured structure from test to production, handing a customer their data,
seeding a support tenant that reproduces a defect, or simply proving the importers are complete —
ended at "write the file by hand".

Three mechanisms were available.

## Decision

**An Orchard deployment step (`DimensionsDeploymentStep` + `DimensionsDeploymentSource`), whose
output is exactly the recipe steps the existing importers read.**

Two parts to that, and the second matters more than the first.

### It is a deployment step

Orchard already has the machinery: a plan is a named list of steps, `Tools → Deployments` builds
and downloads it, `ImportController` takes one back, and `IDeploymentTargetProvider` can send one
somewhere else. Writing an export screen of its own would mean a second way to get data out of a
tenant, with its own permission, its own file format and its own place in the admin menu — and an
organisation that could not travel in the same plan as the content types, settings and roles it
belongs with. One step with three switches, rather than three steps, because a structure names its
types and a record names both: the combinations that could not import are refused at the plan
screen rather than produced and left to fail on arrival.

### It writes the import format, not a format of its own

The source emits `dimension-types`, `structures` and `dimension-records` — the same three step
names, with the same field names, that `IRecipeStepHandler` implementations in `Recipes/` already
consume. There is no serialiser pair, no DTO layer, and nothing that only the export understands.

That is the whole design. A bespoke export format would be a second description of the same thing,
and the two would drift — not noisily, but on the one path nobody exercises until they are relying
on it. Writing the import format means the round-trip test exercises the importers as well, and a
field the exporter forgets is a field the round-trip test notices.

It also means the output is readable and hand-editable: an operator can export a tenant, change one
code, and apply it. That is how the demo and reference recipes will be maintained from now on,
rather than by hand.

### What it carries

- **Types**, including the attribute schema. ADR-0008 recorded that the schema was built through
  the admin screen and not through the recipe; that is now reversed, because an export that cannot
  carry a Project's start date does not reproduce the tenant it came from.
- **Structures**, in ADR-0010's explicit form — `rootTypeCodes` and `containment` — never the
  chain-and-skip-flag description. Containment is precisely the thing the level order cannot
  express, so exporting the chain would silently drop every rule a customer added or removed.
- **Records**, with the **full dated placement history**: every move on record for every structure,
  in the order it was made, including the stage-D2 entries that name no parent, and including
  retired units with their end dates. Plus the dated name history where a record has been
  substantively renamed, so earlier periods keep the name they had rather than inheriting today's.
- Retired types, structures and records are all included. A retired unit is what historical
  reporting resolves through; dropping it would break the closure of everything beneath it.

Records are emitted **parents first**, by a depth-first walk over every parent any record has *ever*
had — not over today's tree. The importer resolves a placement's parent by code against records it
has already created, so this is a correctness requirement, and "ever" is what makes a parent a unit
left in March exist before the entry that names it.

## Alternatives considered

**A controller action on the designer — "Export this structure".** Rejected. It is the discoverable
option and the wrong one: it produces a file that only this module can read, in a place unrelated to
every other export the product has, and it cannot be combined with anything. The moment a customer
asks to move a tenant, the organisation would be the one thing that travels separately.

**A CLI or maintenance command.** Rejected as the *primary* mechanism for the same reason, plus it
is unavailable to the people who most need it — a support engineer with admin access to a hosted
tenant and no shell. Nothing here forecloses adding one later over the same source.

**A bespoke export format, with an importer written for it.** Rejected, and this is the decision the
rest follows from. Two descriptions of one thing drift, and the drift is silent: the export is
written once and read on the day it matters. Writing the import format means there is one
description, exercised by every recipe test and every round trip.

## Consequences

The round-trip test — seed, export, import into a fresh tenant, assert equality of records,
attributes, structures, containment and closure on dates either side of every change — is now
possible, and it is the real proof that the importers are complete. It found what hand-reading
could not: the importers already applied multiple dated placements correctly, and did not carry
attribute schemas or name history at all.

`dimension-types` gains an optional `attributes` array and `dimension-records` an optional
`nameHistory` array. Both are optional, so every recipe written before this still means what it
said, and ADR-0008's comparison stays scoped to the fields a row actually states.

`employee-assignments` is deliberately **not** exported: employees do not exist until prompt 4. The
step and its export land together with the employee record, and the shape they must take is
recorded in prompt 4 of the prompt library so the two are built as one thing.

A tenant's organisation is now portable, which makes the demo and reference recipes maintainable by
export rather than by hand — and makes "send us your org chart" a thing a customer can actually do.
