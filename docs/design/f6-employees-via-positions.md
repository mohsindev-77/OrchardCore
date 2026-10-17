# Slice F6 — employees join the organisation through a position

**Status: PROPOSAL. Nothing in this document is built.** No code, no migration, no recipe and no
test until the product owner approves it.

**Date:** 15 October 2026
**Author:** engineering, for review
**Answers:** D2, D6, D22, D26 D–I, and the employee half of ADR-0022 and ADR-0028
**Rests on:** ADR-0005, ADR-0006, ADR-0008, ADR-0009, ADR-0011, ADR-0012, ADR-0014, ADR-0016,
ADR-0019, ADR-0022, ADR-0028
**Owes:** **ADR-0026** (the employee's dated grade, and the unbinding of `GradeCode`) and
**ADR-0029** (what the F6 migration does to existing placements) — both written at the start of the
slice that implements them, not retrospectively

---

## 0. The one-paragraph version

Today an employee is placed **directly in a unit**. After F6 they are placed **in a position**, and
the unit is *derived* from where that position sits. Nothing about the assignment mechanism changes —
same table, same dated rows, same split rules, same `ReallocateAsync` — only **which record the row
points at**. That is ADR-0022's claim, and the whole of this slice is making it true on the employee
side, plus the consequences: two drop-downs on the form, a derived block on the page, a dated
*Change position* action, real `Filled` counts, and an honest answer for the ~160 Zenith employees
and every real tenant's people who are placed in units today.

**The recommendation on existing data is option (a) plus (c): existing unit placements stay valid
and untouched as history and as the present; nothing is moved; HR assigns positions going forward;
and the demo tenants get their positions through a separate additive recipe.** Section 5 argues it
and says exactly what an existing tenant sees the day after upgrade.

---

## 1. What is already true, and what this slice adds

F5+F7 shipped the whole position side. It deliberately left two things, and they are the two this
slice starts from:

| Already shipped | Still false today |
| --- | --- |
| The `position` dimension type with `StandardRole = Position` and its `ApprovedHeadcount` attribute | `EmployeeAttachableDimensionTypeIds` does not include `position`, so no employee can be placed in one |
| The `reporting-line` axis (ADR-0028) | **Every position's `Filled` is 0**, and the position page's *Holders* section says "No one holds this position yet." |
| The `grade` dimension, the `grades` axis, and a position's **allowed grades** reference table | `JobDetails.JobTitle`, `JobDetails.GradeCode` and the `job-title` and `grade` lookup lists are untouched (D22) |
| `IPositionService` — `SearchSelectableAsync`, `GetAllowedGradesAsync`, `GetEstablishmentAsync`, `GetUnitsAsync`, `GetReportingLineAsync` | Nothing on the employee side calls any of them |
| The position and grade **pickers** (`workmate-position-picker`, `workmate-grade-picker`) | They appear only on the *position's* own Add/Edit form, never on an employee's |

**The single configuration change that turns it on** is one line on the tenant's organisation
structure: `EmployeeAttachableDimensionTypeIds` gains the Position type. Every picker, every
placement screen and the designer's *Place an employee here…* read `PermitsEmployeesAt` through one
implementation, so all three change together and none of them is edited. Section 5 is entirely about
the fact that this one line is **not** safe to apply blindly to a tenant with 160 people already
placed in departments.

---

## 2. The employee form (brief item 1)

### 2.1 Where the two controls live, and why not on the editor

**The employee editor (`EmployeesAdmin/Edit`) stays read-only about placement, and the writes stay on
`EmployeePlacementAdminController`.** This is not new and it is not a convenience: that controller's
own header records the reason, and it is the same reason the organisation designer's actions each
have their own small dated form. *A display driver's `UpdateAsync` has nowhere to put a required
effective date — it is handed a posted model and asked to write it.* Every write in this slice is
dated and nothing is defaulted (CLAUDE.md rule 3), so each operation gets its own form with its own
date.

So the two drop-downs D26 F and D26 G describe appear on **three** screens, all reached by clicking:

| Screen | Route | What it asks |
| --- | --- | --- |
| **New employee → Position** | `Employees → New` (step 2 of the create wizard, or the first thing the new record's page offers) | Position, Grade, effective date, allocation; optional additional positions |
| **Change position** | `Employees → <person> → Placement → Change position` | the same four, dated, replacing section 3's *Change placement* |
| **Change grade** | `Employees → <person> → Placement → Change grade` | grade only, within the same position, dated |

On the **Create** form the position block is offered inline, because a new employee with no position
is the state this slice exists to stop being normal — but it is **optional**, and leaving it blank
creates an unplaced employee exactly as today. Making it required would make the migration in
section 5 impossible to live with and would break every tenant that hires before HR has built its
position list.

### 2.2 Position — the control

A `workmate-position-picker` bound to the employee's assignment, which **already exists** and
already does almost all of this (F5+F7 built it for the position's own *Reports to*). What F6 adds
is one parameter and one rendering rule:

- **Active as at the effective date, retired hidden.** `SearchSelectableAsync` already resolves on a
  date; the control already re-asks when the date field beside it changes (`date-field`).
- **Each option shows name, unit and filled/approved.** `PositionOption` already carries
  `UnitPath` and `Establishment`; the picker already renders all three.
- **A full position warns and is allowed** (D26 F). The warning is rendered **beside the control on
  change** (from the `full` flag the picker endpoint already returns) **and again on save** as a
  notification, because somebody who ignored the first should still be told. It is never a
  validation error — `IDimensionValidator` must not refuse it, for the reason D26 F gives: refusing
  would stop the product recording what an organisation has actually done.

**Nothing new is needed in `IPositionService`.** This is worth stating because it is the test of
whether F5+F7 was designed right: if F6 needs a new read, the pickers were built to the wrong
question.

### 2.3 Grade — the control

A `workmate-grade-picker` in **mode two**, which F5+F7 built and which currently has no caller on
the employee side: `position-field` names the position control beside it, the list is *only* that
position's allowed grades, it is **empty and disabled until a position is chosen**, and it refreshes
on every change of the position. All of D26 G, already built, already browser-tested.

Two rules F6 adds, both in `IDimensionValidator` (CLAUDE.md rule 5 — in the service, not only the
controller):

- **A grade not in the chosen position's allowed set is refused.** Unlike the full-position warning,
  this one is a genuine contradiction: the grade is not a level of that post.
- **Changing position requires choosing a grade of the new position** (D26 G). The old grade is not
  carried across. The form enforces it by clearing the grade when the position changes; the
  validator enforces it on the write, which is what covers a recipe, an import and a replayed post.

**And the one that is deliberately *not* a rule:** removing a grade from a position an employee
currently holds **warns and never breaks that record** (D26 G). The stored grade keeps resolving;
what changes is what is offered next time. This is already how the position editor behaves — F6 adds
the warning text naming how many current holders are affected.

### 2.4 How the employee's grade is stored — and why this needs ADR-0026

D26 G says *"the employee's grade is dated, like every other fact about where they sit."* There are
two ways to store a dated grade and the choice is load-bearing, so it is an ADR rather than a
paragraph.

| | **(i) A second assignment row on the `grades` axis** | **(ii) A field on the position assignment row** |
| --- | --- | --- |
| Storage | nothing new — `EmployeeAssignment` on `grades` | a new nullable `GradeRecordId` on `EmployeeAssignment`, which is a schema change |
| Dating | free, and independent of the position | tied to the position row: a grade change *within* a position means ending and reopening the position row |
| "Who is on SE-2 today" | an indexed read that already exists | a scan, or a new index |
| Split allocation | a person with two positions has two grades, correctly, because each axis row is separate — but *which grade goes with which position* is not expressible | correct by construction: the grade is on the row |
| An increment (D26 G) | one new row on `grades`, position row untouched | the position row must be closed and reopened, which **rewrites the position history for a change that is not a position change** |
| Roll-up risk | a grade is a *reference* not a parent (D26 C, D21) — but an assignment on the `grades` axis **would** put employees under grades in the closure index, which D26 C explicitly says must not happen | none |

**Recommendation: (ii), a nullable `GradeRecordId` on `EmployeeAssignment`.** The deciding row is
the last one. D26 C is unambiguous that *"no employee rolls up to a grade through their position"*,
and option (i) creates exactly that roll-up as a side effect of using the assignment table for
something that is not a placement. Option (ii)'s cost — an increment is a dated change to the grade
field, not to the position — is a cost we can pay with a second small screen (*Change grade*), and
the split-allocation case is answered correctly rather than ambiguously.

The migration is a new `UpdateFromNAsync` adding one nullable column to the assignment index; every
existing row keeps a null grade, which reads as *"no grade recorded"* and is the truth. **ADR-0026
records this, with the alternative and why it was rejected.**

### 2.5 Main and additional positions

**Unchanged mechanism, and that is the point.** `ReallocateAsync` already states the whole set of
concurrent placements from a date, already enforces *allocations total 100* and *exactly one
primary*, and the `employee-assignments` recipe step already calls it. A person with a primary at a
departmental position and a secondary at a site-team position is the Zenith matrix with a different
record on the end of the row — ADR-0022 says so and this slice is where it becomes true.

The *Change position* form is therefore the **existing** `Change` form with its unit picker replaced
by a position picker and a grade column added. One form, one write path, one set of messages.

---

## 3. Derived, read-only (brief item 2)

### 3.1 On the employee page

A single **Organisation** block, read-only, resolved as at the page's date, from two dated reads
that already exist — the position assignment, and the position's ancestors on the organisation axis.
**The employee record still holds no department field** (architecture section 2, enforced by
`EmployeeFieldNames.ForbiddenOnSections`, which already forbids `Department`, `Division`,
`CostCentre` and friends on *any* designer-added field).

| Row | Where it comes from |
| --- | --- |
| **Position** | the primary assignment's record, linked to its page |
| **Grade** | the assignment's `GradeRecordId`, linked, with its effective date |
| **Department / Division / Business unit / Cost centre** | the position's ancestor path on the organisation axis, **one row per ancestor, labelled with that ancestor's own dimension name** |
| **Reports to** | section 3.3 |
| **Additional positions** | one row each with its allocation % |

**The four organisation rows are not four hard-coded fields.** This matters and is easy to get
wrong. There is no `Division` concept in the engine — there is a tenant's own containment chain, and
Zenith's happens to be Division → Department. Crescent's has Regions and Branches; Khaleej's has
Campus and College. So the block renders **the ancestor path, each step labelled with its dimension
type's name**, which gives *Division: Engineering Division*, *Department: Civil* on Zenith and the
right thing everywhere else. Cost centre is the same: it is a dimension a tenant may or may not have
on the chain, and if it is not on the chain the row is simply absent.

**A consequence to state:** a tenant whose chart has no Department level will not see a Department
row. That is correct and is the same honesty the position page's *Not placed* badge already shows.

### 3.2 Where this block sits on the form — and the Form Designer

**In the fixed core / assignment area, above the designed sections, and rendered by its own shape
rather than by a hard-coded list.** Section 9 of this document is entirely about why.

### 3.3 Reports to — and the rule for several holders

*Reports to* is the **holder of the parent position on the reporting line**. Three cases, and the
third is the one the brief asks us to propose a rule for:

1. **The parent position has exactly one holder** → that person, linked.
2. **The parent position is vacant** → show the **position** name with **"(vacant)"** after it, per
   the brief. Linked to the position's page, because that is where somebody goes to do something
   about it.
3. **The parent position has several holders** (the normal case under D26 D — *HR Manager* has
   three) → **show the position name with the count, and list the holders beneath it on the employee
   page; on the Employees *list* show the position name alone.**

   **Why not pick one.** Any rule that picks a single person — longest-serving, highest grade,
   alphabetically first — invents a line manager the customer never stated, and it would be wrong
   silently. Three people hold *Manager Technology*; which of them a given engineer reports to is a
   fact nobody has recorded, and the honest answer is "the Manager Technology post, held by these
   three". The alternative the customer may eventually want is a **named line manager**, and
   `EmployeePart.LineManagerEmployeeId` **already exists** for exactly that — so the rule is:

   > **Reports to** shows the *position*, and names the holder only when there is exactly one. Where
   > `LineManagerEmployeeId` is set, the page shows that person as **Line manager** on a row of its
   > own, beside *Reports to* rather than instead of it.

   Two facts, two rows, neither guessed. If the product owner would rather have a single derived
   name, the question that has to be answered first is *"which of the three?"*, and that is a
   business rule we should record as a decision rather than choose here.

### 3.4 The Employees list columns

The brief asks for: **Number, Name, Position, Grade, Reports to, Department, Division, Business
unit, Hired, Status.**

Three engineering notes, each of which changes what we build:

- **Division / Business unit are not columns the engine can name.** Same argument as section 3.1. The
  proposal is **one *Organisation* column showing the primary position's unit path** (`Engineering
  Division › Civil`), plus **per-dimension columns offered as optional columns** the way the
  dimension list's attribute columns already are — so a tenant whose chart has a Division level can
  turn on a Division column and one that has a Region can turn on Region. Hard-coding four columns
  would be right for Zenith and wrong for Crescent, and `UnchangedScreensTenantTests` exists because
  we have made that mistake once already.
- **Every one of these is an N+1 waiting to happen.** The list draws 50 rows; Position, Grade,
  Reports to and the unit path are four reads *per row* if written naively. All four must be
  **batched for the page** — `GetPrimaryPlacementsAsync`, `GetAncestorPathsAsync`,
  `GetEstablishmentAsync` and a batched holder read all already take `IReadOnlyList<string>`. A
  per-row read is the single most likely way this slice ships something slow, and the test plan
  pins the query count.
- **Sorting and filtering by Position or Grade needs an index.** The assignment index exists;
  sorting the *employee* list by a *position's name* is a join the current `EmployeeIndex` cannot
  do. **Proposal: ship the columns unsorted in F6a, and add sort/filter in F6c with a decision
  about whether that justifies a denormalised column on `EmployeeIndex`** — which would be a cache
  of derived data and therefore needs its own argument.

---

## 4. Change position (brief item 3)

**Replaces *Change placement* on the employee's placement screen**, same controller, same
`ReallocateAsync`, renamed and re-pickered.

| Action | What it writes |
| --- | --- |
| **Change position** (promotion, transfer) | the old position row's `EffectiveRange` ends **the day before** the new one starts; a new row opens with the new position *and a grade from its set*. History kept; nothing deleted (ADR-0016). |
| **Change grade** (increment) | the position row's grade changes **as its own dated action**: the current row ends the day before, a new row opens with the same position and the new grade. The position is unchanged and the UI says so. |
| **End position** | existing *End* — closes the rows on the axis from a date. |

**The "review access roles" reminder** is a **notification placeholder** shown after a successful
position change: *"<Name>'s position has changed. Review their access roles."* It links nowhere yet
and changes nothing, exactly per D26 I — *"changing somebody's position raises a reminder rather
than changing anything by itself, because what permissions a post implies is a customer's policy and
not a derivation."* The security slice turns the text into a link. It is rendered from a single
constant so that slice has one place to change.

---

## 5. Existing data — the most important part (brief item 5)

### 5.1 What is actually on a tenant today

Zenith ~160 employees, Crescent its own, demo a handful — **all placed directly in units**
(`zenith-dept-civil`, `zenith-team-site-a`, …) through `employee-assignments` rows on the
organisation axis, including the matrix cases: a primary at a home department and a secondary at a
site team. Every one of those rows is dated, valid, and feeding the designer's headcounts, the
employee list's unit column and ADR-0012's head appointments.

### 5.2 The three options, weighed

**(a) Existing unit placements stay valid; HR assigns positions going forward.**

*What it does:* the migration adds Position to `EmployeeAttachableDimensionTypeIds` and **nothing
else**. Employees already in units stay in units. New assignments can name a position *or* a unit,
because the containment map now permits both.

- **Nothing is lost and nothing moves.** Strongest possible compliance with ADR-0009 and ADR-0016.
- Unit headcounts, the designer, the matrix, the heads and every existing report keep working
  **unchanged on day one** — which is the property that matters for a live tenant.
- **Cost:** the tenant is in a mixed state. Some people are in units, some in positions, and the
  derived block is empty for the first group. Section 5.4 is about making that state legible rather
  than confusing.
- **Cost:** `Filled` stays 0 until somebody does the work. That is honest — nobody *has* been
  assigned a position — but it is not what a demo should look like.

**(b) A generated placeholder position per unit, and current employees moved into it.**

*What it does:* for every unit holding employees, the migration creates a position (*"Civil —
unassigned"*) and reallocates every employee from the unit to it.

- `Filled` is immediately meaningful and the derived block populates everywhere.
- **It is a silent move of real data, and the brief forbids silent moves.** It writes a dated
  reallocation for 160 people that no human asked for, on a date the migration has to invent (today?
  the original join date? the assignment's start?), and every one of those is wrong for somebody.
- **It invents master data.** A tenant opens Positions and finds 40 positions it did not create,
  named after its departments, each with an approved headcount nobody set. Deleting them is not
  possible (ADR-0016) — only retiring, after re-assigning everybody.
- It also **contradicts D26 D**: a placeholder per unit is a position per *unit*, which is neither a
  job nor a post, and its approved headcount means nothing.
- **It destroys information even while keeping rows.** "Ahmed is in Civil" and "Ahmed holds Civil —
  unassigned, which sits in Civil" are not the same statement, and after the migration you cannot
  tell which one the tenant originally made.

**(c) Demo recipes repointed in a separate additive recipe; real tenants handled by (a) or (b).**

*What it does:* a new `organisation-designer-zenith-employee-positions.recipe.json`, in the shape
that finding #2 taught us — **additive, idempotent, requiring the Zenith organisation, the Zenith
positions and the Zenith employees, and restating nothing else** — which reallocates the demo's
employees onto the demo's positions. Real tenants are untouched by it, because it is a demo file
they choose to run.

- The demo shows the slice working, with real `Filled` counts and a populated derived block.
- Nothing happens to a real tenant that nobody clicked.
- It is **not an option on its own** — it says nothing about real data — which is why the brief
  offers it alongside (a) or (b).

### 5.3 Recommendation: **(a) + (c)**

**The migration moves nobody.** It does exactly three things, all additive:

1. Adds the Position dimension type to `EmployeeAttachableDimensionTypeIds` on **each organisation
   structure that already has `position` as a level**. A tenant that has not added Position to its
   chart is not changed at all, which is the ADR-0028 state and is correct.
2. Adds the nullable `GradeRecordId` column to the assignment index (section 2.4).
3. Retires the `job-title` lookup list and unbinds `JobDetails.GradeCode` (section 6) — both
   append-only, both keeping every stored value.

**And the demo gets (c): a separate additive recipe**, so the product owner's own tenant can see the
slice working with one click and without the product having decided anything on their behalf.

**Why not (b), in one sentence:** it is the only option that writes dated rows about real people
that nobody asked for, and "no silent moves" is the brief's own constraint.

### 5.4 What an existing tenant sees the day after upgrade

Stated precisely, because this is what the brief asks for:

| | Before | Day after upgrade |
| --- | --- | --- |
| **Every existing employee's placement** | in a unit, dated | **identical.** Same rows, same dates, same unit, same allocation, same primary flag |
| **The Employees list** | unit column populated | unit column **populated exactly as before**; the new **Position** and **Grade** columns read **"—"**; *Reports to* reads "—" |
| **An employee's page** | placement section | an **Organisation** block that says *"No position assigned. This employee is placed directly in Civil."* with a link to *Assign a position* |
| **Unit headcounts, the designer, the org tree** | n | **n, unchanged.** Nothing moved, so nothing re-counts |
| **Unit heads (ADR-0012)** | unchanged | **unchanged.** Heads are not assignments and this slice does not touch them |
| **Positions list** | Filled 0 | **Filled 0 still**, because nobody holds one yet — and the column now *means* something rather than being structurally zero |
| **Position page → Holders** | "No one holds this position yet." | the same sentence, now true-by-fact rather than true-by-construction |
| **Job details section** | shows Job title and Grade Code | Job title shown **read-only, marked as a historical value**, with a line saying where Position now comes from (D22 step 3). Grade Code the same. **Nothing deleted** |
| **`job-title` lookup list** | listed | **retired** — hidden from the lists screen, every entry preserved (ADR-0014: retired, never deleted) |
| **Placement screen** | *Change placement* | ***Change position*** and ***Change grade***, plus the existing *Place* and *End*. The unit picker still offers units on a tenant whose chart permits them |

**The mixed state is made legible, not hidden.** The Employees list gets a filter — *Has a position
/ No position yet* — so HR can work through the backlog, and the employee page says in one sentence
what state that person is in. That is the whole cost of option (a), and it is a sentence rather than
a migration.

---

## 6. Job title and grade fields (brief item 6, D22)

Exactly D22, which is already written and needs no re-deciding. What F6 owns:

1. **`JobDetails.JobTitle` is retired from the editor, not removed from the type.** It stays a
   stored `BilingualTextField`. Same shape as `DimensionRecordPart.HeadEmployeeId` after ADR-0012
   and `AllowsSelfNesting` after ADR-0010's addendum: a stored property nothing reads and no screen
   writes.
2. **The employee's Position is shown read-only**, from the dated assignment (section 3.1).
3. **Where a tenant has typed values and no positions yet**, the old field is still the only answer
   and the screen shows it **marked as a historical value**, with a line saying where Position now
   comes from. This is D22 step 3 verbatim and is what makes option (a) liveable.
4. **The typed values seed the Position dimension through an operator-run import** — profile,
   propose, dry run, apply — the same mechanism §4.4 describes for grades. **Not in F6.** What the
   right positions are is a business question and not a migration's to guess. F6 ships the *"you
   have 23 distinct job titles and no positions"* notice that makes somebody go and do it; the
   importer is its own slice.
5. **The `job-title` lookup list is retired** through `ILookupListService`'s existing retire path.
   It is bound to nothing, so no screen changes.
6. **`JobDetails.GradeCode` and the `grade` lookup list are superseded.** Same treatment: the field
   stays stored and read-only, the list is retired, the employee's grade comes from the assignment.
7. **The relabelling.** Every user-facing *Job title* / *Designation* / *Org role* → **Position** /
   **المنصب**, in both PO files. `LocalisationResourceTests` and `UserVisibleStringTests` already
   fail the build on a missing or orphaned entry, so this is enforced rather than remembered.

---

## 7. Counts (brief item 4)

| Question | Answer | Mechanism |
| --- | --- | --- |
| A position's **Filled** | employees holding it on the date, **distinct** | `CountEmployeesAtAsync`, which already does `DISTINCT EmployeeId` (D1, §2.5) — so a split allocation counts once |
| The position page's **Holders** | employee, grade, since | the empty section F5+F7 shipped, now filled. `PositionHolderViewModel` already has that exact shape |
| **Unit headcounts** | roll up through each position's unit | **for free.** A position is a node on the organisation axis with a parent, so the closure index already rolls an employee at a position up to its department. No new code |
| The **reporting line** | **never counts** | **for free, and by construction.** An assignment names a structure and nothing ever writes one on `reporting-line` (ADR-0028). The position page already draws filled/approved there instead of employees |
| **Unit heads** | unchanged | ADR-0012 stands. A head is not an assignment, carries no allocation, is counted by no headcount, and need not be a member. **D11 moved "should a head be a position" to prompt 5 and ADR-0027 stays unwritten** |

**The one that deserves a test rather than a sentence:** after F6, a department's headcount must
count a person *once* whether they are placed in the department directly (legacy) or in a position
inside it (new) — because under option (a) a tenant has both at once. That is a `DISTINCT` over two
shapes of row and it is the single most likely place this slice double-counts somebody.

---

## 8. The small fix (brief item 7)

**Confirmed as a real defect.** On a grouped *All &lt;plural&gt;* list,
`DimensionRecordListViewModel.DimensionTypeId` is `only?.DimensionTypeId ?? string.Empty` — so when
the group holds more than one type (two Position types on the demo tenant: base's and Zenith's) the
**Add** button posts an empty `dimensionTypeId` and the Create form cannot know what it is creating.

**Fix:** when the group holds more than one type, **Add** opens a small *Which organisation?* step
listing them with their disambiguating organisation name — the same choices `GroupChoicesAsync`
already builds for the list's Organisation filter — and then goes to the form. With one type it goes
straight to the form, unchanged. No new screen concept: it is the same "refuse to guess, offer the
choice" the list already does for its tree view (`TreeNeedsAStructure`).

---

## 9. The dynamic employee form (brief item 9) — how F6 fits B3–B7

This is the item most likely to be got wrong by accident, so it is stated as rules F6 must obey.

### 9.1 The Form Designer's own design already anticipated this slice

`docs/design/prompt-04-session-b-form-designer.md` §9 is about positions, and it got the answer
right before positions existed:

> *"The link to a position would be an assignment, not a field — and that is already the platform's
> answer to this shape of question… when positions become real, they become either a dimension type
> with a structure of their own, or an aggregate with its own dated assignment table — and in
> **neither** case is the right answer a field on a form."*

**F6 is that future arriving, in exactly the shape §9 predicted.** Which means:

- **Nothing in B1–B2 has to be undone.** §9's deliberate non-choices — not naming a lookup list
  `position`, not making `job-title` mandatory, not adding a `PositionCode` field to any section —
  are precisely why D22's retirement is clean. Had B2 bound `JobTitle` to a code-keyed list, F6
  would be migrating a half-built position concept instead of retiring a text field.
- **The palette rule survives and is now satisfiable.** §9's rule is *"the form designer must never
  be the place a cross-record relationship is invented; every field type that points at another
  record points at an aggregate that already has a service and a dated model."* Position now has
  one. So **a position picker becomes a legitimate future palette entry** — but **F6 does not add
  it**, because a designer-added position field would be an undated field pointing at a dated
  aggregate, which is the thing §9 forbids. If a tenant wants a *second* position-shaped fact, the
  answer is another assignment axis, not a field.

### 9.2 The three rules F6 must obey

**Rule 1 — Position, Grade and the derived organisation block live in the fixed core / assignment
area, never in a section.** They are rendered by the employee page's own shape, above the
designed sections. `EmployeeFieldNames.ForbiddenOnSections` already forbids `Department`,
`Division`, `CostCentre`, `Unit` and friends on any designer-added field, and **F6 adds `Position`,
`PositionCode`, `PositionId`, `Grade`, `GradeCode`, `GradeId` and `ReportsTo` to that set** — so a
tenant cannot build a shadow copy of the fields this slice derives. That is one line in an existing
guard, and `EmployeePartTests` already fails the build when the declared set and the type disagree.

**Rule 2 — the form's section list is never hard-coded.** The employee page must render:

```
[ fixed core: identity ]
[ fixed core: organisation — Position, Grade, derived units, Reports to ]   ← F6 adds this
[ EmployeeProfilePart — designer-added record-level fields ]                ← B3 adds this
[ for each section in <the tenant's sections, in their order> ] … [ end ]    ← the six today, more after B3
```

The loop is already over `EmployeeSections.All`, whose own doc comment says *"these are the standard
sections, not the only ones — a tenant adds its own through the form designer in session B, which
produces more content types the same way"*, and whose `Stereotype` exists *"so that a picker, a
report or session B's designer can ask for 'an employee section' without a hard-coded list of six
type names that would be wrong the moment a tenant adds a seventh."*

**F6 must therefore not introduce a seventh hard-coded entry, and must not reorder that list in
code.** The organisation block is **not a section** — it is part of the fixed core — so it does not
go in `EmployeeSections.All` at all. Writing it as a seventh section would be the mistake B3 then
has to undo.

**Rule 3 — the organisation block renders from data, not from a fixed four rows.** Section 3.1's
"one row per ancestor, labelled with that ancestor's dimension name". This is the same discipline
and it pays twice: it is correct for a tenant whose chart is not Zenith's, and it means a tenant
that *adds* a level to its chart gets a row for it without a code change.

### 9.3 What F6 makes easier and harder for B3–B7

**Easier:**

- **D22's retirement is the worked example B3's safety rules need.** *Retire a field, keep the data,
  keep it readable* is §5.3's "safety valve that makes 'never remove' liveable" — F6 does it to two
  real fields on a live tenant, which is a much better test of the rule than a designed field
  nobody has typed into.
- **ADR-0006's handler trap gets found early.** §5.2 flags that `EmployeeHandler` lacks the
  `ActivatingAsync` repair that `DimensionRecordHandler` has, and that *"a recipe can publish a form
  and then create employees in the same shell scope"* produces an item with no parts welded. **F6's
  demo recipe creates employees in a scope where the Position type was just configured**, which is
  the same trap one step removed. **Recommendation: fix `EmployeeHandler` in F6**, where there is a
  recipe that can reproduce it, rather than leaving it on B3's list.
- **The fixed-core/designed boundary gets drawn and tested before anything is designed.** Rule 1's
  guard additions are cheap now and awkward later.

**Harder — and both are worth saying out loud:**

- **The employee page gets denser.** A derived organisation block plus six sections plus
  `EmployeeProfilePart` plus designer sections is a long page. B4's screen work should assume the
  organisation block is **collapsible and first**, and F6 should build it that way so B4 does not
  have to restructure it.
- **`EmployeeProfilePart` now has a neighbour with similar-looking fields.** An administrator who
  wants "Grade" on the record and does not realise there is one will try to add it. Rule 1's guard
  refuses it — but the *message* matters: it must say *"Grade is part of the employee's position;
  see Placement"* rather than *"forbidden field name"*. That is a string, and it is on F6.

### 9.4 When B3–B7 resume, and what an admin can do at the end of each

**Recommendation: straight after F6, before F8/F9** — as the brief suggests, and for a reason F6
supplies: F6 is the slice that finally fixes the *shape* of the employee record (fixed core +
derived organisation + sections), and B3's compiler has to be written against a settled shape. B8/F9
(the enquiry report and export) read that shape; building them first means reading a shape that is
about to change.

| | Slice | What an administrator can do when it ends |
| --- | --- | --- |
| **B3** | definition model + compiler, `EmployeeProfilePart`, safety rules, `EmployeeHandler` repair | *Nothing by clicking* — it is model and tests. The honest answer, and §10.3's own reason for ordering it this way |
| **B4** | the designer screens | **Add a "Visa number" field to the Documents section**, give it English and Arabic labels and help text, mark it required, reorder it, publish — and see it on every employee's editor. **Add a whole "Visa details" section** with its own fields, which appears as a new block on the employee page |
| **B5** | the rule language | **Say "show Visa details only when Nationality is not Saudi"**, and *"visa expiry must be after visa issue"* — enforced on the server, not only in the browser |
| **B6** | recipe step and export | **Export a tenant's form definitions and import them into another** — so a configured tenant becomes a starter |
| **B7** | request forms | **Design a request form** with an approval definition reference; prompt 5 consumes it |

---

## 10. Recipes, export and tests (brief item 8)

### 10.1 Recipes

- **`employee-assignments` needs no new step.** It already names a unit by code and
  `ReallocateAsync` already writes the row; a position *is* a dimension record with a code. What it
  gains is **the optional `gradeCode` on a change**, which is additive and which a file written
  before F6 simply omits — the ADR-0008 rule that a row's comparison is scoped to what it states.
- **ADR-0008 idempotency**: a re-run whose assignment set matches is skipped; one that differs fails
  naming the difference. Already how the step behaves; the grade is one more compared field.
- **Export (ADR-0011)**: the deployment step already emits `employee-assignments` behind
  `IncludeAssignments`. It gains the grade on each row, stated only where there is one — the same
  judgement `aliasEn` gets, and for the same reason.
- **Demo: `organisation-designer-zenith-employee-positions.recipe.json`** — STEP 3 OF 3, additive,
  idempotent, requiring Zenith's organisation **and** its positions **and** its employees, restating
  nothing else, applicable to the product owner's existing tenant. Built the way finding #2 taught
  us, and tested against a tenant assembled from the earlier recipe versions.

### 10.2 Test plan

| Level | What it covers |
| --- | --- |
| **Unit** (`WorkMate.Dimensions.Tests`, `WorkMate.Records.Tests`) | the grade-in-allowed-set rule; the full-position warning is *not* a refusal; grade cleared on position change; `EmployeeFieldNames` guard rejects `Position`/`Grade`/`ReportsTo`; the derived-ancestor labelling |
| **Integration** | assignment to a position writes one row and `Filled` becomes 1; split allocation across two positions totals 100 with one primary and counts the person **once**; *Change position* ends the old row the day before; *Change grade* leaves the position untouched; **a department counts a person once whether placed directly or via a position**; the reporting line never counts anybody; the migration adds Position to attachables and **moves nobody**; D22's retired fields still resolve; the Employees list is a **fixed number of queries for 50 rows** |
| **Browser** | the two drop-downs by clicking — Grade disabled until Position chosen, refreshed on change, full-position warning visible; Change position end-to-end; the derived block on the employee page; the Employees list columns; the item-7 *Which organisation?* step |
| **Arabic** | every new string; the derived block's dimension-name labels in Arabic; *Position* / **المنصب** replacing *Job title* on the job details section; the "(vacant)" reports-to |
| **Recipe** | the demo positions recipe applies to a tenant built from the earlier Zenith recipes, is a no-op on re-run, and is refused with a named error if applied before the positions recipe |

### 10.3 Slice plan — F6 is too big for one session

| | Slice | What the product owner can click and see at the end |
| --- | --- | --- |
| **F6a** | **Model + migration + the assignment grade.** `GradeRecordId`, the attachable-types migration, the validator rules, `EmployeeHandler`'s ADR-0006 repair, **ADR-0026** and **ADR-0029** written. No new screens | **Assign an employee to a position** through the *existing* placement screen (the unit picker now offers positions, because the containment map permits them) and watch a position's **Filled go from 0 to 1** and its **Holders** section list that person |
| **F6b** | **The employee form and the derived block.** Position and Grade pickers on Create and on *Change position* / *Change grade*; the organisation block; *Reports to*; the "review access roles" reminder | **Hire somebody into HR Manager at grade HRM-2 in one form**, see Department / Division / Reports to derived on their page, promote them, and see the history |
| **F6c** | **The lists, D22 and the small fix.** Employees list columns and the has-a-position filter; `JobTitle`/`GradeCode` retired and relabelled; `job-title` and `grade` lists retired; item 7 | **An HR-standard employee list**, the *No position yet* filter to work the backlog through, and *Position* where *Job title* used to be |
| **F6d** | **Recipes, export, demo.** The grade on `employee-assignments` both ways; the Zenith employee-positions recipe | **One click on "Zenith — employees into positions"** and the whole demo is populated: real Filled counts, real reporting lines, real derived organisation |

If the slices have to be fewer, **F6a+F6b merge cleanly** and F6d can fold into F6c. **F6a should
not be merged into anything** — it carries the migration and the two ADRs.

---

## 11. What this proposal does *not* decide, and what I need from you

1. **Reports to with several holders** (§3.3). I propose *"show the position, name the holder only
   when there is one, and keep `LineManagerEmployeeId` as the named-manager answer"*. If you want a
   single derived name, we need a business rule for *which of the three*.
2. **The employee's grade: option (ii)** (§2.4). This is **ADR-0026** and it carries a migration, so
   it needs the second reviewer the definition of done requires.
3. **Existing data: option (a)+(c)** (§5.3). This is **ADR-0029**. If you would rather have (b),
   say so now — it is a different migration, not a variation on this one.
4. **The Employees list's organisation columns** (§3.4): one *Organisation* path column plus
   optional per-dimension columns, rather than four fixed columns the engine cannot name.
5. **Sorting by Position/Grade is deferred to F6c** and may need a denormalised column, which would
   be derived data on an index and needs its own argument.
6. **The job-title importer is not in F6** (§6 step 4). F6 ships the notice; the importer is its
   own slice.
7. **B3–B7 resume straight after F6**, before F8/F9 (§9.4).

**Nothing is built until these are answered.**
