# ADR-0012: The unit head is a dated appointment of its own, not a flag on an assignment

**Status:** accepted
**Date:** 2026-10-08
**Deciders:** Project lead

## Context

Three descriptions of the unit head were in the repository at once, and they did not agree.

- **`docs/dimension-engine-architecture.md` section 2** lists "head employee" among the standard
  fields on a `DimensionRecord`, and `DimensionRecordPart.HeadEmployeeId` has existed since the
  record layer landed, complete with an editor row and a place in
  `DimensionTypeService.StandardFieldNames`.
- **The backlog note of 5 October 2026**, raised while building the designer's chart view and
  repeated on prompt 4 in the prompt library, says the opposite: "an employee assignment flagged as
  head of that unit, **not a field on the dimension record**", effective-dated so that who led a
  unit last March is answerable.
- **`DimensionRecordPart.HeadEmployeeId`'s own doc comment** says "the head is not required to be
  assigned to the unit they head: a general manager heads three departments and belongs to none of
  them. The seed data in architecture section 8 includes that case deliberately."

The first is undated, which is disqualifying on its own: prompt 5 routes approvals to the unit head
and must be able to re-resolve a past period, and an undated field answers every question about the
past with today's answer. So the field had to go either way.

The backlog note's answer — a flag on an assignment row — looked like the obvious replacement. It is
not, and the third description is why: **it cannot express a head who is not a member of the unit**.
`DimensionValidator.ValidateAssignmentAsync` already refuses an allocation of zero ("An allocation
must be greater than zero. Remove the placement instead."), so a head-only marker row is illegal.
The only way to carry the flag is to invent a real allocation for somebody who has none there — and
that allocation is then counted by the unit's headcount and charged to its cost centre. Acting heads
borrowed from another department are ordinary rather than exotic, and a general manager heading
three departments would appear as a third of a person in each.

## Decision

**A head appointment is its own dated record: structure, record, employee, effective range. It is
not an assignment, carries no allocation, is counted by no headcount and is charged to no cost
centre. The head need not be assigned to the unit they head.**

```csharp
internal sealed class HeadAppointmentDocument   // one per structure and record
{
    public IReadOnlyList<HeadTerm> Terms { get; set; }   // (EmployeeId, EffectiveRange)
}
```

Stored in `WorkMate.Dimensions`, beside the link, closure and assignment tables and under the same
rule: nothing outside that module reads it, and `IEmployeeAssignmentService` is the only way in.

The grain is one document per structure and record, for the reason ADR-0005 gives for the other
three: the rule that matters — at most one head per unit per date — is a rule about one unit on one
axis, so it is decidable inside a single document without reading anything else.

### The rules

| Rule | Behaviour |
| --- | --- |
| At most one head per `(structure, record)` per date | Blocked, `DimensionRule.SingleHeadPerUnit`, naming the incumbent and the date their term began |
| The employee exists | Blocked, `DimensionRule.HeadNotEligible` |
| The employee had not already left when the term starts | Blocked, same rule, naming both dates |
| A term that ends mid-way because the head leaves | **Not** a rule. `IEmployeeService.ExitAsync` ends it on the exit date; recording a past leader's term is exactly the history this engine exists to keep |
| The head is assigned to the unit | **Deliberately not a rule.** This is the whole point |

Employee existence is answered through a new `IEmployeeLookup`, declared in `WorkMate.Dimensions`
and implemented in `WorkMate.Records` — the same shape as `IDimensionDeletionBlockerProvider`,
because the dimension engine needs an answer about something another module owns and must not depend
on it. Resolved as a collection, so the engine still starts when `WorkMate.Records` is disabled and
the absence is reportable by name rather than appearing as "no such employee" for everybody.

### The service surface

On `IEmployeeAssignmentService`, not on a service of its own. The decisive consideration is that
there must be exactly one source for "who leads this unit": the organisation designer's card and
prompt 5's approval routing both read `GetHeadAsync`, so what the chart shows and what an approval
routes to cannot disagree. A second service would be a second place for that answer to come from.

`SetHeadAsync`, `ClearHeadAsync`, `GetHeadAsync`, `GetHeadsAsync` (batched for a row of cards),
`GetHeadshipsOfAsync`, `EndHeadshipsOfAsync`, `GetHeadHistoryAsync`. All gated by `AssignEmployees`:
appointing a head is the same kind of act as placing somebody, and a separate permission would split
one capability in two.

### `DimensionRecordPart.HeadEmployeeId` is retired, not deleted

Nothing reads it when resolving a head, and the dimension record editor no longer offers it. **The
property and every stored value stay exactly where they are.** ADR-0009's rule is that a migration
never destroys data, and converting the values would be wrong on the facts as well: the old field is
undated, so there is no term to derive from it, and inventing one would assert that every head had
led their unit since the beginning of time.

The same shape as ADR-0010's addendum and `AllowsSelfNesting`: a stored property that no rule reads
and no screen writes, kept because deleting it would destroy data and would make an existing
tenant's record mean something it did not say.

### Migration

`Migrations.UpdateFrom5Async` (schema version 6) creates `UnitHeadIndex` with two indexes — one for
"who heads this unit on this date", one for "what does this person head", which the exit needs.

A new table, so it is created in the new step and `CreateAsync` is left alone, exactly as
`UpdateFrom1Async` and `UpdateFrom2Async` created the record and graph layers' tables. That is why
this step needs none of the `ColumnExistsAsync` guarding `UpdateFrom3Async` and `UpdateFrom4Async`
need: those add a *column* that `CreateAsync` also produces, so they have to handle a brand-new
tenant and an upgrading one differently. A table is not in `CreateAsync` at all, so both arrive here
without it.

`DimensionsMigrationUpgradeTenantTests.ATenantStuckBeforeTheUnitHeadTableUpgradesCleanlyAndCan
AppointAHead` rolls a tenant back to version 5, drops the table, upgrades, and then appoints a head
through the real service and reads it back — the operation that would throw "no such table" on a
tenant the migration had missed. `UnitHeadIndex` also joins the column-by-column comparison that
every index table in this module is held to.

## Alternatives considered

**A flag on the assignment row, as the backlog note proposed.** Rejected for the reason above: it
cannot express a head who is not a member of the unit, which architecture section 8's seed data
requires explicitly and which acting heads make ordinary. Expressing that case as a split allocation
— 34/33/33 across three departments a general manager heads and works in none of — was considered
and rejected as actively misleading: those numbers would be read as cost shares by payroll and as
headcount by HR, and both readings would be false.

**Relax the "allocation must be greater than zero" rule and carry head-only rows at 0%.** Rejected.
It reopens a settled rule to create a second kind of assignment row that is not an assignment, and
every query over assignments then has to remember which kind it is looking at. The first one that
forgets double-counts a head, silently.

**Keep `HeadEmployeeId` and date it.** Rejected. Dating it means a second dated history on the
record part, parallel to but separate from the dated name history, with its own storage and its own
rules — which is this decision with a worse name and the head's identity on the wrong aggregate. It
also keeps the head on `DimensionRecordPart`, which means every generated dimension content type
carries it whether or not that axis has heads at all.

**A head as an approval-routing concept in `WorkMate.Approvals`.** Rejected, and firmly: the chart
shows the head, so the dimension engine has to know it. Putting it in the approvals module would
either give the designer a dependency on approvals or give the two their own answers.

## Consequences

The designer's chart card shows `Head: <name>` and `Head: Vacant`, and the placeholder `Head: —`
that meant "not built yet" is deleted — once this ships, a dash would be a claim nothing had
checked. Both the head and the employee count are batched reads, so a row of cards costs two queries
rather than two per card.

`IEmployeeService.ExitAsync` closes headships as well as placements, on every axis, on the last day
of service — and `PlanExitAsync` names the units that would be left vacant before anybody commits.
That is the half of an exit that is easiest to forget and worst to get wrong: a leaver who stays
recorded as a head is somebody approvals still route to, and the failure is silent because the chart
and the routing agree with each other.

Prompt 5's "route to the unit head" and its vacant-head rule read `GetHeadAsync`. Nothing else is
permitted to answer that question.

The `employee-assignments` recipe step does **not** carry heads: an appointment is not an assignment
and a row that carried both would be two things. A separate `unit-heads` step, code-based like every
other step, lands with it in stage A3, and the export carries the full dated history for the reason
the placement history is carried — an export that kept only the current head would answer "who led
this unit last March" differently in the imported tenant.
