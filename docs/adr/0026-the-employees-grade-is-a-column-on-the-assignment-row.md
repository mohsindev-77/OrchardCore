# ADR-0026: The employee's grade is a column on the assignment row, not a second assignment

**Status: accepted, pending second review.** Built in slice F6a.
**Date:** 2026-10-15
**Deciders:** Project lead, on the product owner's decision **D28 B**.
**Rests on:** ADR-0005, ADR-0009, ADR-0022, ADR-0028, decisions D21, D26 C, D26 G.
**Owed by F6** since D27. **Amends:** nothing.

## Context

**D26 G:** *"the employee's grade is dated, like every other fact about where they sit."*

A dated fact about a person needs somewhere dated to live. The platform has exactly one such place
for "where an employee sits" — the assignment row, with its `EffectiveRange` — and the question is
whether a grade goes **on** that row or becomes a row of its own on the grade axis.

Both are expressible today. `grade` is already a dimension type with its own structure
(`grades`, shipped by `base.recipe.json` per ADR-0028), so writing an assignment on it needs no new
storage at all; and `EmployeeAssignment` is a record with four fields, so adding a fifth is one
nullable column. The choice is not about cost.

## Decision

**A nullable `GradeRecordId` on the assignment row.**

```
AssignmentRow(RecordId, Range, AllocationPercent, IsPrimary, GradeRecordId?)
```

Null means *no grade recorded*, which is the truth for every row written before this slice and for
every placement in a unit rather than a position.

### Why not an assignment on the grades axis

Four reasons, and the first settles it.

**1. It would put employees under grades in the closure index, which D26 C forbids in terms.**

> *"An allowed grade is a **reference, not a parent**. It never enters the closure index, no grade
> appears on the organisation tree, and **no employee rolls up to a grade through their position**."*

An assignment names a structure and a node, and `CountEmployeesAtAsync` counts employees at or below
a node on that structure. Writing assignments on the `grades` axis makes *"how many people are under
SE-2"* a query the engine answers — and once it answers it, something will ask it. The design would
be creating the roll-up D26 C exists to rule out, as a side effect of using the assignment table for
something that is not a placement. **ADR-0012 made the same call about unit heads for the same
reason**: a head is not an assignment because it must not be counted by a headcount, and it got a
table of its own rather than a flag on a row that is counted.

**2. A split allocation becomes ambiguous.** Somebody with a primary at a departmental position and a
secondary at a site-team position has two positions and may have two grades. As rows on the grades
axis there are two grade rows and two position rows and **nothing says which grade goes with which
position**. On the assignment row it is correct by construction: the grade is on the row that names
the position.

**3. An increment would rewrite the position history.** D26 G makes a grade change within a position
an ordinary event. With the grade on the row it is a dated change to one column, and the *Change
grade* screen says so. Had the grade been a separate axis the position row would be untouched —
which sounds better until case 2 is considered, and then it is the thing that makes case 2
unanswerable.

**4. "Which grade does this person hold" would be two reads on two axes that can disagree.** One
read returns the row that already carries both.

### What this costs, stated rather than designed around

**A grade change is a dated change to the assignment row**, so the row is closed the day before and
reopened with the same position and the new grade. The *position* history therefore gains a row for
an event that is not a position change. That is a real cost and it is accepted, because the screen
that produces it says *Change grade* and the row carries both facts, so the history reads correctly:
*"held HR Manager at HRM-1 from January, at HRM-2 from June"* is two rows and is what happened.

**The grade is not validated against the position by the index.** `IDimensionValidator` refuses a
grade outside the chosen position's allowed set on the write (slice F6a), and that is the only
enforcement. A grade later *removed* from the position keeps resolving on rows that already carry
it — which is **D26 G's own rule**: *"removing a grade from a Position an employee currently holds
warns and never breaks that employee's record."*

**A grade on a row whose node is not a position is meaningless**, and is refused on the write rather
than represented. Under ADR-0029 a tenant has unit placements and position placements side by side;
a unit placement carries a null grade.

## Consequences

**Storage.** One nullable column on `EmployeeAssignmentIndex` and one nullable property on
`AssignmentRow`, in a new `UpdateFromNAsync` (ADR-0009: `CreateAsync` is amended only so a brand-new
tenant gets the current schema in one pass; no shipped step's body is edited). Every existing row
keeps a null grade. The upgrade test is the `DimensionsMigrationUpgradeTenantTests` pattern: build
the tables as the previous version left them, migrate, assert the result matches a fresh install,
and then exercise a real assignment through the service rather than asserting a column exists.

**`AssignmentSplitEntry` gains the same optional field**, so `ReallocateAsync` — the one write path
the screens, the recipe step and the importer all go through — carries the grade. A caller that does
not state one writes null, which is what every caller written before this slice does.

**The recipe step and the export carry it, stated only where there is one** — the judgement
`aliasEn` already gets, and for the same reason: writing `"gradeCode": null` against every
assignment in a tenant that records no grades would be noise in every exported file for no fact
gained.

**"Who is on SE-2 today" is not an indexed read.** It is a filter on the assignment index's new
column, which is adequate for the screens that ask it and is not the hot query. If Reporting ever
needs it indexed, that is a new index on an existing column rather than a model change — which is
the property that made this the cheaper decision to reverse if it is ever wrong.

**If a reviewer disagrees**, the thing to overturn is the first reason, and the question to settle
is whether *"how many employees are under grade SE-2"* is a question the engine should be able to
answer. D26 C says no. If that changes, this ADR changes with it.
