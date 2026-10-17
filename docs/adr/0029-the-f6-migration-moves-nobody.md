# ADR-0029: The F6 migration moves nobody — existing unit placements stay valid

**Status: accepted, pending second review.** Built in slice F6a.
**Date:** 2026-10-15
**Deciders:** Project lead, on the product owner's decision **D28 C**.
**Rests on:** ADR-0009, ADR-0012, ADR-0016, ADR-0022, ADR-0028, decisions D2, D26 D, D26 H.
**Amends:** nothing.

## Context

Slice F6 makes an employee's place in the organisation **derived from the position they hold**
(D2, D26 H). The mechanism is one line of configuration: the organisation structure's
`EmployeeAttachableDimensionTypeIds` gains the Position type, after which every picker, every
placement screen and the designer's *Place an employee here…* offer positions — all three read
`PermitsEmployeesAt` through one implementation, so they change together.

**Every tenant that exists today has employees placed directly in units.** Zenith's demo has about
160, including matrix cases with a primary at a home department and a secondary at a site team;
Crescent has its own; a real tenant has whatever it has. Every one of those rows is dated, valid,
and feeding unit headcounts, the designer, the employee list and ADR-0012's head appointments.

So the question this ADR answers is not *"can employees hold positions"* — F5+F7 settled that — but
**what happens on upgrade to the people who are already placed.**

## Decision

**The migration moves nobody.** It does three things, all additive:

1. Adds the Position dimension type to `EmployeeAttachableDimensionTypeIds` **on each organisation
   structure that already has `position` as a level**. A tenant that has not put Position on its
   chart is not changed at all — that is the honest ADR-0028 state, and the Add form already says
   so.
2. Adds the nullable grade column to the assignment index (**ADR-0026**).
3. Leaves every assignment row exactly as it is.

**Existing unit placements stay valid — as history and as the present.** HR assigns positions going
forward. The demo tenants get theirs through a **separate additive recipe** nobody runs by accident.

### The option that was rejected, and why

**A generated placeholder position per unit** (*"Civil — unassigned"*), with every current employee
reallocated into it, was considered. It is the only option that makes `Filled` meaningful on day one
and populates the derived block everywhere. It is rejected for four reasons, any one of which would
be enough:

- **It is a silent move of real data.** It writes a dated reallocation for 160 people that no human
  asked for, on a date the migration has to invent — today? the join date? the assignment's start? —
  and every choice is wrong for somebody.
- **It invents master data a tenant cannot delete.** Forty positions named after departments, each
  with an approved headcount nobody set. ADR-0016 means they can only ever be retired, and only
  after re-assigning everybody out of them.
- **It contradicts D26 D.** A placeholder per unit is a position per *unit*, which is neither a job
  nor a post, and its approved headcount means nothing. D26 D exists precisely to stop positions
  being minted per-something-else.
- **It destroys information while keeping rows.** *"Ahmed is in Civil"* and *"Ahmed holds Civil —
  unassigned, which sits in Civil"* are different statements, and after the migration nobody can
  tell which one the tenant originally made.

The cost of not doing it is that `Filled` stays 0 until somebody does the work. **That is honest:
nobody has been assigned a position.** What changes at F6 is that the column *means* something
rather than being structurally zero.

### The mixed state, and the two things that make it liveable

After upgrade a tenant has **unit placements and position placements side by side**, and the
containment map permits both. That is a real cost of this decision and it is paid for on screen
rather than hidden:

- a **"No position yet"** filter on the Employees list, so HR can work the backlog through;
- a **one sentence** on every affected employee's page — *"No position assigned. This employee is
  placed directly in Civil."* — rather than an empty derived block that reads as a bug.

Both are required by **D28 C**. Neither is a migration.

## Consequences

**What an existing tenant sees the day after upgrade**, stated precisely because that is the thing
this ADR exists to guarantee:

| | Before | After |
| --- | --- | --- |
| Every existing placement | in a unit, dated | **identical** — same rows, dates, unit, allocation, primary flag |
| Unit headcounts, the designer, the org tree | n | **n.** Nothing moved, so nothing re-counts |
| Unit heads (ADR-0012) | unchanged | **unchanged.** A head is not an assignment and this slice does not touch one |
| Employees list | unit column populated | unit column **unchanged**; Position and Grade read "—" |
| An employee's page | placement section | one sentence saying they are placed directly in a unit, and how to give them a position |
| Positions list | Filled 0 | **Filled 0** — and now because nobody holds one, not because nobody can |
| Position page → Holders | "No one holds this position yet." | the same sentence, now true by fact rather than by construction |

**Headcount must count a person once across both shapes.** Under this decision a department can
contain employees placed directly in it *and* employees placed in positions inside it. The
`DISTINCT EmployeeId` guarantee (D1, architecture §2.5) is what makes the roll-up correct, and it
already holds — but it has never been exercised against two shapes of row at once, so **it gets its
own test** in F6a. This is the most likely place the slice double-counts somebody.

**Reversibility.** A tenant that decides it wanted the placeholder positions after all can have them
— by creating positions and reallocating, through the ordinary screens, with dates a human chose.
The reverse is not true: a migration that had moved everybody cannot be un-moved, because the
original statement is gone. **This decision is the one that keeps both futures open**, which is the
same argument the form designer's §9 made about positions themselves.

**If a reviewer disagrees**, the thing to overturn is the rejection of the placeholder option, and
the question to settle first is what effective date a generated reallocation would carry. There is
no answer to that question that is right for every employee, which is why it is rejected rather
than deferred.
