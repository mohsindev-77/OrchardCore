# ADR-0022: Position is the one master, and an employee joins the organisation by holding one

**Status:** accepted, **pending second review**.
**Date:** 2026-10-14
**Deciders:** Project lead, on the product owner's decisions D2, D20, D26 and D27.
**Rests on:** ADR-0005, ADR-0010, ADR-0012, ADR-0028. **Amended by nothing yet.**

## Context

Three names and one thing. FRS figure 2 lists **Job Title** and **Position Title** as separate
dimensions; §1.2's feedback note says *"the Org. Role setup is equivalent to Positions masters. The
system should maintain Positions as the main master and not duplicate/separate out from the Org.
Role Setup."*; and customers use *designation* for the same idea. Decision **D20** settles it:
**Position, Job Title and Designation are one concept, always called Position** — *المنصب* in
Arabic — and neither extra row ships.

What a Position has to be able to do is the whole of this decision:

| FRS / decision | What it asks for |
| --- | --- |
| §4.1.4, figure 9 | its own list of values, its own reporting tree, and a place in the organisation |
| D2, D26 H | *"employees join the organisation by being assigned a position"*, with division, department, branch and cost centre **derived** from it |
| D2, D26 B | a position holds **many** employees |
| D26 D | **one Position per job**, not per post: three HR managers share one |
| D26 E | an **approved headcount**, with vacancies = approved − filled |
| D26 C | a **set of allowed grades**, which are its levels |
| §1.2 | every value dated, with a code, bilingual names and an alias |

## Decision

### Position is a dimension type. No new aggregate, no new table, no new service for the record

Every row of that table but two is what a dimension type already is: a list of dated, coded,
bilingual, aliased values with a parent on an axis, a screen, a recipe step, an export and an audit
trail. So Position ships as a `DimensionTypeDocument` with
`StandardRole = DimensionTypeRole.Position`, and **every write goes through `IDimensionService`** —
created and placed by `AddUnitAsync`, reparented by `MoveAsync`, retired by `RetireAsync`.

**`IPositionService` is a reader, not a second aggregate.** It composes reads that already exist
into the two answers the screens need, so the Positions list, the two pickers and slice F6's
employee form cannot each put them together slightly differently. It writes nothing.

The two rows that are **not** free are dealt with separately and deliberately:

- **the reporting line** needs a second axis, which is **ADR-0028**;
- **the allowed grades** need a reference table, because a link gives a record one parent per axis
  in either direction and *"which positions allow SE-1"* is many-to-many. Also ADR-0028.

### Approved headcount is an attribute on the shipped type

Named `ApprovedHeadcount`, of kind Number, not required. The attribute mechanism already ships
through the recipe, exports and renders on the Add/Edit form, so a tenant that wants *budgeted cost*
beside it adds an attribute rather than asking for a column.

**Not a field on `DimensionRecordPart`**, which every generated dimension content type carries: a
Division and a Cost centre would each grow a headcount field that means nothing.

Three consequences, each stated rather than designed around:

- **Absent means 1** (D26 E), because the common case is a post held by one person and a default of
  zero would make every new position read as over-established the moment somebody filled it.
- **Vacant is approved − filled and is negative when a post is over-established.** Not clamped:
  clamping would hide the one condition the number exists to surface.
- **It is undated** — an attribute has no effective range — so *"what was the approved headcount last
  March"* is **not answerable**. That is **D15**, a stated limitation recorded in the module README,
  and making it dated is a model change of the same shape as the dated name history.

### One Position per job, not per post

*HR Manager* is one position with an approved headcount of 3 and grades HRM-1..HRM-3; *Labourer* is
one with 44. **This is what makes the headcount a number worth having**: a position per person makes
every approved headcount 1 and every vacancy 0 or 1, which is a column that can only ever repeat
what the row already says. A holder's level is their **grade** within the position (D26 C), not a
position of their own.

### An employee holds a position through an ordinary `EmployeeAssignment`

Nothing new. Dated with an `EffectiveRange`; many employees per position because nothing constrains
a node to one; concurrent rows for a split allocation, with `IsPrimary` and `AllocationPercent`
already enforcing *exactly one primary* and *allocations total 100*; `ReallocateAsync` states the
whole picture from a date; and every closed row stays. The configuration change is one line on the
structure — `EmployeeAttachableDimensionTypeIds = [position]` — which is what makes every picker,
every placement screen and the designer's *Place an employee here…* offer positions and nothing
else, because all three read `PermitsEmployeesAt` through one implementation.

**The employee record still holds no department field.** What the employee screen shows is computed
on read, as at a date, from two dated tables — the position's assignment and the position's ancestors
— which is the whole point of the architecture's one rule.

**That line, and the derived block, are slice F6.** This ADR is written now because the model it
describes is what F5+F7 built and what F6 writes against; the *employee side* of it is not built, and
until it is, every position's **Filled** count is 0.

### What a Position is not

- **Not a user and not a permission set** (D26 B). A Position carries no permissions and an Access
  role carries no headcount. The product says *Access roles* for Orchard's roles precisely so that
  "his role is HR Manager" and "give him the HR role" stop sharing a word.
- **Not a unit head.** ADR-0012 stands: a head appointment is its own dated record, carries no
  allocation, is counted by no headcount, and the head need not be a member of the unit they head.
  **D11** moved *"should a head be a position"* to prompt 5 and **ADR-0027 stays unwritten**.
- **Not a free-text field.** D26 F: the employee's Position is a drop-down over the Position
  dimension and there is no free-text position anywhere. A **full** position is offered with a
  warning rather than withheld, because refusing it would stop the product recording what an
  organisation has actually done; a **retired** one is withheld, because that is not a post anybody
  can be put into.

## Consequences

**Figure 2 loses two rows and gains none.** No Job Title dimension, no Position Title dimension, no
designation field. What a post is called is the Position record's own bilingual name.

**Every user-facing *Job title*, *Designation* and *Org role* becomes *Position* / *المنصب***, in the
slice that owns the screen it is on — **D22**. F5+F7 owns the dimension type's shipped name and has
done it; **F6 owns the employee record and the job details section** and has not. `JobDetails.JobTitle`
and the `job-title` lookup list are untouched by this slice and are retired, never deleted, by F6.

**The Positions list gains four columns that appear on no other dimension's list** — Approved,
Filled, Vacant and Allowed grades — conditioned on the declared role. D25 says the *All &lt;plural&gt;*
screens do not change, and four empty columns on every list would be exactly that change;
`UnchangedScreensTenantTests` asserts their absence everywhere else.

**Nothing about the Zenith matrix breaks.** An engineer with a primary at their home department and a
secondary at a site team becomes an engineer with a primary at a *departmental* position and a
secondary at a *team* position. The rows, the rules and the roll-ups are identical; what changes is
which record the row points at. The `DISTINCT EmployeeId` guarantee (D1, §2.5) is what keeps that
person counted once.

**Pending second review**, per the definition of done's rule for a change carrying a migration, a new
recipe step and a change to a shipped recipe.
