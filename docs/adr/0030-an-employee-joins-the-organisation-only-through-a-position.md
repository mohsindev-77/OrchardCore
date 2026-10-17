# ADR-0030: An employee joins the organisation only through a position

**Status: accepted.** Built in slice F6b.
**Date:** 2026-10-15
**Deciders:** Project lead, on the product owner's decision **D30**.
**Rests on:** ADR-0005, ADR-0010, ADR-0012, ADR-0022, ADR-0026, ADR-0028, ADR-0029; decisions D2,
D26 B/G/H, D28 A/C/D.
**Amends:** nothing. **Narrows:** what `ValidateAssignmentAsync` accepts.

## Context

D2 has said since the first review that *"employees join the organisation by being assigned a
position"*, and D26 H confirmed that department, division, business unit and cost centre are
**derived** from where that position sits. The engine, however, has always allowed the other shape
too: an employee could be placed directly at any unit a structure declared employee-attachable, and
F6a added positions to that set without taking anything away.

Two valid shapes for one fact is the condition every other decision in this module has been written
to avoid. Concretely it produces:

- **Two answers to "which department is this person in"** — one read through a position, one read
  off a direct placement — which a report can reach by two paths and get two numbers from.
- **A screen that has to ask which kind of thing you are placing somebody into**, which is a
  question nobody in HR has an opinion about.
- **A derived organisation block that is sometimes a derivation and sometimes empty**, which makes
  it read as broken rather than as a fact.

## Decision

**On a structure that has positions, an employee is placed at a position and nowhere else.**

- The organisation chain — department, division, company, cost centre — is **computed from the
  position's ancestors on that axis, as at the date**. It is never stored on the employee, and
  nothing on any screen offers a unit for an employee to be placed in.
- `IDimensionValidator.ValidateAssignmentAsync` **refuses a new placement at a non-position node**
  of such a structure, naming the node and saying where a position is chosen instead. Enforced in
  the validator rather than only in the UI, so the screen, the recipe step and any later import
  refuse it in the same words — CLAUDE.md rule 5.
- **"A structure that has positions"** means one with at least one level whose dimension type
  declares `StandardRole = Position`. Asked of the axis's shape, never of a code or a name, for the
  reason `IsReportingLineAsync` gives.

### What this does *not* do

**It does not move anybody, and it does not invalidate a row.** ADR-0029 stands in full: existing
unit placements stay valid, as history and as the present. The refusal is on the **write**, so a row
already there is untouched and keeps resolving. An employee in that state is shown as **"No position
yet — &lt;unit path&gt;"** with a **Choose position** action that runs the ordinary dated move.

That is the whole reconciliation, and it is worth stating as a table because the two decisions are
easy to read as contradicting each other:

| | Existing placement | New placement |
| --- | --- | --- |
| Structure **with** positions | kept, labelled, actionable | **refused** at a non-position node |
| Structure **without** positions | kept | allowed, unchanged |

**It does not touch unit heads.** ADR-0012 stands: a head appointment is its own dated record, is
counted by no headcount, and the head need not be a member of the unit they head. Heads are
appointed at **units** and this decision is about assignments. **D11** moved *"should a head be a
position"* to prompt 5 and **ADR-0027 stays unwritten**.

**It does not remove the split.** `AllocationPercent` and `IsPrimary` remain on the row and
`ReallocateAsync` remains the write path. A second concurrent position — acting, secondment — is the
matrix case D26 H already allows. F6b **hides both controls on the ordinary screen**, where the
answer is always main and 100 per cent; the second-position screen is a later slice. Hiding a
control is not removing a capability, and the recipe step and the importer can still state a split.

**It does not apply to a tenant that has no positions.** A structure with no position level is
unaffected, and its screens are exactly what they were. A brand-new tenant has no organisation chart
at all (ADR-0028), so this rule starts applying the day somebody adds Position to their levels —
the same act that makes a position placeable in the first place.

## Consequences

**The derived block becomes a fact rather than a field**, which is what lets it be rendered
read-only and recomputed when the position changes. D28 D settled how: one row per ancestor,
labelled with that ancestor's **own dimension name**, never four hard-coded words — correct for a
tenant whose chart has Regions and Branches rather than Divisions and Departments.

**The Placement screen loses three controls and gains none.** No structure chooser where there is
one eligible structure, no unit picker, no *Within*. What is left is Position, Grade and a date,
which is the whole of what D26 F and D26 G ask for.

**The structure chooser, where it survives, must not offer a grade ladder or a reporting line.**
Both are axes the Position type is a level of, and neither is somewhere an employee goes — nothing
ever writes an assignment on a reporting line (ADR-0028) and a grade is a reference rather than a
placement (D26 C). Offering them was always wrong and is now visible, because the chooser is the
only one left.

**Every screen that offers to place somebody reads one rule, and it is not
`StructureDocument.PermitsEmployeesAt`.** That method answers only half the question: it knows the
tenant's attachable set and nothing about what a Position *is*, because a structure document holds
type ids and the roles live on the types. ADR-0029 deliberately **keeps** the department types in
that set so existing placements stay valid — so the half-answer still says *yes* for a department on
a position-based axis.

The organisation designer was reading that half-answer, and so went on offering **Place an employee
here…** on a department card: the menu entry appeared, the form opened, and the write came back
*"choose a position instead"*. An action that cannot do what it says is worse than no action — the
same judgement the designer already makes about a container that holds nobody.

So the rule lives in one place, `IPositionService.PermitsEmployeesAtAsync`, which is the attachable
set **and** this decision together. The picker, the Place screen, the designer card and the
validator all resolve it there. On an axis with positions a unit card stops offering the action and
a position card offers it, opening the Place screen with that position already chosen; on an axis
with none, the method is exactly `PermitsEmployeesAt` and nothing moves. Nothing else about the
designer changes — the cards, the layout, the other menu entries, zoom and drag are untouched.

**A tenant mid-migration holds both shapes**, deliberately, and that is ADR-0029's cost rather than
this decision's. The *"No position yet"* label and D28 C's filter are what make it legible; F6d
closes it for the demo tenants.

**If a reviewer disagrees**, the thing to overturn is the exclusivity rather than the derivation:
deriving the chain from a position is D2 and D26 H and is not in question. The question this ADR
answers is whether a direct unit placement remains a second legitimate way to say the same thing,
and the argument against is that two valid shapes for one fact is how a report comes to return two
numbers.
