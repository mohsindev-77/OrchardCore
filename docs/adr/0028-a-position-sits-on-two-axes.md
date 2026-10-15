# ADR-0028: A Position sits on two axes — its place in the business, and its reporting line

**Status: accepted, pending second review.** Built in slice F5+F7.
**Date:** 2026-10-14
**Deciders:** Project lead, on the product owner's confirmation. Answers the design document's
**Q-j**.
**Amends:** nothing. **Rests on:** ADR-0005, ADR-0010, decisions D19, D21, D26, D27.

## Context

A Position needs to be in two places at once, and the FRS asks for both on the same page.

- **Where it sits in the business.** FRS figure 9 draws Business Unit → … → Department →
  **Position** → Employee. *Software Engineer* belongs to the Civil department, and that is what
  makes an employee holding it part of Civil — D26 H, FRS §1.2. Everything derived about an
  employee's organisation (division, department, business unit, cost centre) is derived through this
  edge.
- **Who it reports to.** FRS §4.1.4 draws the Position tree as a **reporting line**: CEO → Director
  Technology → Manager Technology → Software Engineer. *Reports to* is a fact about the post, not
  about the department, and the two trees are genuinely different shapes — *HR Manager* reports to
  the Director of Technology in the brief's own demo while sitting in a different department.

**These cannot both be a parent link on one axis.** Decision **D19** gives a dimension value exactly
one parent, and ADR-0005 has always scoped a link to a structure with one parent per record per
axis. On 12 October, before D19, this was two concurrent `ParentLink`s with different mapping keys;
that mechanism is withdrawn with ADR-0020 and ADR-0021.

**Two further shapes were considered and neither works.**

*A `reportsTo` attribute on the Position type.* Attributes are undated scalars with no referential
integrity (the Dimensions README: *"an attribute is a field on the record's content part with no
effective range"*). A reporting line needs dates, needs to refuse a loop, and needs to be walked —
"everybody under the Director of Technology" is a closure query. An attribute gives none of that.

*One axis with both edges on it.* This is what the 12 October draft had, and it has a defect the
closure index cannot see around: *"employees under this node"* would have two meanings on one axis
and the index could not tell them apart. Counting staff "under the COO" and counting staff "in the
Corporate division" are different questions and must not share a number.

## Decision

**A Position is one record on two structures.**

| | **Organisation** axis (the primary one) | **Reporting line** axis |
| --- | --- | --- |
| Code | `organisation` — the tenant's existing primary org chart | **`reporting-line`** — new |
| A position's parent | its **unit** — a Department, Branch or whatever the containment map permits | the **position it reports to** |
| Levels | the tenant's own, plus `position` | `position` only |
| Root types | the tenant's own | `position` (the CEO reports to nobody) |
| Containment | the tenant's own, plus *unit* → `position` | `position` → `position` (the diagonal) |
| Employees attach | **yes**, at a position | **no** — nothing is ever placed on this axis |
| Headcount rolls up | yes | **no**, and nothing makes it: an assignment names a structure, and no assignment names this one |
| What draws it | the organisation designer, the enquiry tree (F8) | the Position value page's **Hierarchy / Tree / List** section |

**This is the mechanism ADR-0005 was written for**, quoted from it: *"one record can sit on several
axes at once, which a single parent field makes impossible."* Nothing new is needed in the graph —
two structures, two independent trees, neither aware of the other, and the existing cycle check runs
per axis so a reporting loop is refused by code that already exists.

### What ships in `base.recipe.json`, and what does not

The base recipe states, today and deliberately, that it ships **no organisation shape**: *"every new
tenant gets an organisation chart to fill in, not one somebody else guessed at."* This decision does
not break that rule, and the division is exact:

| Shipped | Not shipped |
| --- | --- |
| the `position` and `grade` dimension **types** (ADR-0023's vocabulary) | `position` as a **level of the `organisation` structure** |
| the **`reporting-line`** structure, with `position` as its only level and its diagonal ticked | any position **value** |
| the **`grades`** structure, with `grade` as its only level and its diagonal ticked (so a tenant *may* group grades — D26 C — while the shipped data is flat) | any grade **value** |

**The reporting line and the grade axis are not the organisation chart**, so shipping them guesses
nothing about the customer. Where a position sits in *their* business is theirs to say, and they say
it by adding `position` to their organisation structure's levels when they build their chart — the
same act as adding Department or Branch.

**A consequence to state plainly:** on a brand-new tenant that has not yet built an organisation
chart, a Position can be created and given a reporting line but has nowhere in the business to sit.
That is the correct and honest state, it is the same state a Department is in, and the screen already
reports it — the unplaced panel, and the value page's per-structure *Unplaced* badge.

### The demo recipes keep their own of both

Zenith gets `zenith-position` and a `zenith-reporting` structure; Crescent, if and when it gets
positions, gets its own. **This is decision D17 as amended, applied before it bites again.** A
dimension type is a level of *every* structure that declares it and `IDimensionService.CreateAsync`
writes a self pair on all of them, so a single shared `position` type across the demo tenant would
put Crescent's positions on Zenith's reporting chart — the exact defect that was found, reverted and
written up while building F2. The menu still reads one entry, because D23 B groups by English
singular name.

### What this does *not* decide

**Whether a unit head is a position.** ADR-0012 stands: a head appointment is its own dated record,
carries no allocation and is counted by no headcount, and the head need not be a member of the unit
they head. **D11** moved that question to prompt 5 and **ADR-0027 stays unwritten.** Nothing here
touches the head screens.

## Consequences

**No new storage for the reporting line.** It is a structure, links and closure rows, all of which
exist. The new storage this slice needs is for something else — a Position's **allowed grades**,
which is a reference and not a parent (D21, D26 C) and is therefore the one thing a second axis
*cannot* express: a grade may be allowed by many positions, and a link gives a record one parent per
axis in either direction.

**The Position value page is the reporting chart, for free.** The page already takes a structure,
already lists every structure the type is on, and already draws that structure's subtree in
Hierarchy, Tree and List (D25). A Position on two axes gets a chooser between *Organisation* and
*Reporting line* with no new screen; what the slice adds is that a Position's page **defaults to the
reporting line**, and that the Hierarchy box carries the establishment badge.

**Two axes means two "Place under" questions on the Add form**, and the existing form asks one per
structure already. The Position form states them as *Place under* (the unit) and *Reports to* (the
position), which is the same write twice, not a new mechanism.

**`EmployeeAttachableDimensionTypeIds` on the reporting axis is left empty**, which means
unconstrained. That is safe and is worth saying why rather than tightening it: an assignment names
the structure it is on, and nothing ever writes an assignment on `reporting-line`, so there is
nothing for the constraint to refuse. Declaring `[]` there and relying on it would be a second
statement of a fact the write path already guarantees.

**Q-j is answered: the Reporting structure ships.** The reasoning is ADR-0023's — a tenant that does
not use reporting lines hides the dimension (D3), which is cheaper than a tenant that needs one
discovering it has to invent an axis.
