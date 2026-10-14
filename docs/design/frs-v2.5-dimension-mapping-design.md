# FRS v2.5 — Dimension mapping and tree structure: design proposal

**Status:** design only. No code, migrations, recipes, tests or configuration were changed in the
session that produced it. Nothing here is built until it is reviewed.

**Date:** 12 October 2026 · branch `frs-dimension-mapping`

**Sources, read in this order and all of them read in full:**
`docs/requirements/Dimension_Mapping_and_Tree_Structure_FRS_V2.5_06-10-2026.pdf` (13 pages, nine
figures — the figures are screenshots and were extracted and read, not inferred from the captions);
`docs/requirements/frs-v2.5-decisions.md` (D1–D6); `docs/dimension-engine-architecture.md`;
`docs/adr/0002, 0005, 0008, 0009, 0010, 0011, 0012, 0014, 0019`;
`src/WorkMate.Dimensions/README.md`; `src/WorkMate.Records/README.md`;
`docs/design/prompt-04-session-b-form-designer.md`; `recipes/base.recipe.json`.

---

> # ⚠ Amended 13 October 2026 — read this first
>
> Two product decisions have overruled FRS text that this document was written to honour. They are
> recorded as **D19** and **D20** in `docs/requirements/frs-v2.5-decisions.md`, each quoting what it
> overrides. **Where this document and they disagree, they win.**
>
> | | Decision | What it does to this document |
> | --- | --- | --- |
> | **D19** | **A dimension value has exactly one parent.** Multi-parent mapping (FRS §3, Rule 3, figures 3 and 9) is dropped | **§2.2 is withdrawn** with ADR-0020. §2.1 and §5.5 are amended to one parent, Required or Optional. §2.5's per-parent counting goes; §2.6 loses the DAG cycle case. Conflict **C1 is withdrawn, not reconciled** — ADR-0005 stands and was never departed from. **Slice F4 is deleted** |
> | **D20** | **Position = Job Title = Designation**, one concept, always "Position" / "المنصب" | **§4.3's Job Title dimension is dropped.** No Job Title dimension, no Position Title dimension, no designation field. §3.1's `position → job-title` mapping goes. §4.4 gains the job-title move, which lands in **F6** as **D22** |
>
> **A third, pending product owner confirmation: D21.** A position's link to a **grade** is a
> *reference*, not a parent — it never enters the closure index and never appears on any tree, and
> `OneOrMore` survives there and nowhere else so that a grade **range** (G7–G8) stays expressible.
> The employee's dated grade step stays on its own Grade axis.
>
> **Slice order is now `F1 ✓ → F2 → F3 → F8 → F5 → F6 → F7 → F9`** (D18 as revised). §10.3 carries
> the detail.
>
> Sections not named above are unaffected. The amendments are marked in place as well as listed
> here, so a reader arriving at a section mid-document is not misled by it.

---

## How to read this

Sections **A** to **E** are written for a reviewer who does not read code: what the FRS asks for,
what the product does today, what would change, and what a person would see on screen. Everything
from section **1** onwards is the engineering detail, in the order the brief asks for it.

Two names recur as worked examples because they are the two reference organisations the product
already ships and tests against:

- **Zenith Engineering & Construction** — under a Division sits either a **Department** or a
  **Project**; a Project contains **Teams**. Engineers have a home department and are deployed to
  site teams, so one person is counted by HR under their department and by cost under their project.
- **Crescent Microfinance** — one Division (Head Office) holds **Departments**; its sibling (Branch
  Network) holds **Regions**, then **Branches**, then Departments again. Two of its departments are
  called *Credit* and two are called *Operations*.

---

# A. What the FRS asks for, in one page

The FRS changes how a customer describes their organisation. Today they describe it as **one tree
per axis**: a chart with Divisions at the top, Departments under them, Sections under those. The FRS
describes it as **a set of master lists that point at each other**:

1. **Every kind of unit is its own master list with its own screen.** Business Unit is a list.
   Department is a list. Branch is a list. Each gets a menu item, a tree view and a list view, and
   each shows only its own values. A Department screen never shows a Division.

2. **A list says which other lists it hangs off.** Branch is "mapped to" Division *and* Department.
   That mapping is configuration, set once, and it is what the Add form reads: adding a Branch shows
   Code, Name (English), Name (Arabic), Alias, and then one numbered group per mapped parent list —
   Division first, Department second — and you must pick one value from each before Save is
   accepted. A list with no mapped parent skips that step entirely.

3. **The combined organisation chart is generated, never drawn.** There is no "organisation
   designer" in the FRS. There is one read-only **enquiry report** that follows the mappings from
   Business Unit down through Division, Department, Branch, Position and finally Employee. Clicking
   a node narrows the whole chart to that branch; "Clear selection" restores it. A side panel lists
   *Full organisation, Business Unit, Division, Department, Branch, Position* with an employee count
   against each.

4. **Positions are how people join the organisation.** A Position is a master like the others, with
   its own reporting-line tree (CEO → COO → Regional Director → Branch Manager → Branch Supervisor).
   It is mapped into the organisation like any other value. Assign a position to an employee and the
   employee is thereby in that division, that department, that cost centre and that grade — nobody
   types those in. Org Role and Position are **one** master, not two.

5. **Every value is dated.** An effective start date is mandatory; an end date is optional.

6. **Renaming a list renames it everywhere** — its own screen, the menu, and the combined chart.

## What we already have, in one paragraph

More of this exists than the FRS's framing suggests. The dimension engine already models "a kind of
unit" (`DimensionType`), "a value" (`DimensionRecord`), "what may sit under what" (the containment
map, ADR-0010), dated parent links, a dated closure index that answers "everything under this node
last March" in one query, dated employee placements with split allocations, dated unit heads, a
read-only chart with drill-in and search, per-type value lists with Code / Name / Parent / Status /
Headcount, recipe import, deployment export, audit and bilingual names. What it does **not** have is
four things: a value cannot have **two parents at once**; a mapping cannot be **compulsory**; there
is **no Alias field**; and **Position, Grade and Job Title are not modelled at all**.

## The four genuine conflicts, named

Per CLAUDE.md the FRS wins on *what* is built, and a conflict with an ADR or with the architecture is
flagged and reconciled by a new or amended ADR rather than absorbed quietly. There were four.

**C1 is withdrawn (D19), leaving three.** CLAUDE.md's rule is that the FRS wins *unless the product
owner says otherwise*, and here they have: multi-parent mapping is dropped, so the FRS text that
conflicted with ADR-0005 is simply not built. That is the one outcome a conflict table has no row
shape for — not "the FRS wins" and not "the ADR wins", but "the requirement is withdrawn" — so it is
struck through rather than rewritten.

| | The FRS says | We decided | Proposed reconciliation |
| --- | --- | --- | --- |
| ~~**C1**~~ | ~~A Branch belongs to one Division **and** one Department at the same time (§3, figure 3, Rule 3)~~ | A record has at most one parent per axis; several parents means several axes (ADR-0005, architecture decision 4) | **WITHDRAWN (D19).** The product owner has overruled the FRS text, not the recorded decision: a value has exactly one parent, so there is no conflict to reconcile. ADR-0005 stands as written and was never departed from; **ADR-0020 is withdrawn** and slice F4 is deleted |
| **C2** | The system ships a ready-made list of 14 dimensions (§2, figure 2) | `base.recipe.json` ships **Cost centre only**, on the stated reasoning that "what a customer calls their levels is theirs to decide, and a base recipe that guessed would leave every tenant deleting three types before starting" | **ADR-0023** — ship them, because the FRS's own Rule 6 (rename) and D3 (hide) are the answer to that objection: a tenant renames or hides rather than deletes |
| **C3** | Every dimension is its own menu item (§1.2, D3) | Per-type menu entries shipped in prompt 4 session D and were **withdrawn by the product owner on sight** — the menu read *Branchs, Cost centres, Departments, Departments, Departments, Divisions…* | **ADR-0024** — bring them back, with the three things that caused the withdrawal fixed: an authored bilingual plural (no suffix guessing), a fixed order, and one entry per *dimension* rather than per type-per-structure |
| **C4** | Delete is on the toolbar; a used value becomes Inactive and the user is told why (D4) | Architecture §6 has three outcomes — Clean (delete), Referenced-by-history-only (retire), Referenced-by-live-data (**blocked**) | **ADR-0025** — keep the three-way assessment, change what the screen does with the third: offer "make inactive from…" as the next action instead of a refusal with no way forward |

Three smaller divergences are recorded in the gap table (A6, A7, B4) rather than here: the Alias
field, the "Position Title" entry in figure 2, and the figure 9 node with two parents of the *same*
dimension. **The last two are now settled rather than divergences**: D20 removes Position Title (and
Job Title) as dimensions outright, and D19 removes the figure 9 node's second parent along with
every other second parent.

---

# B. What would change on screen

## B1. The menu

```
Organisation
    Organisation tree              ← new: the read-only enquiry report (FRS §5, figure 9)
    Business Unit                  ┐
    Division                       │
    Department                     │  one item per dimension, fixed order,
    Branch                         │  authored bilingual plural, hideable (D3)
    Position                       │
    Grade Steps                    │
    Job Title                      │
    …                              ┘
    Settings                       ← new sub-group, last (D3)
        Dimension types
        Mapping
        Organisation designer      ← today's editable chart, moved here
```

Today the group has three entries: *Organisation designer, Dimension types, Structures*. After this
work, *Organisation tree* is first, the dimensions are the body of the list, and the two
configuration screens plus the designer are tucked under *Settings*.

## B2. A dimension's own screen

One screen, two views, a toggle in the top-right exactly as figure 4 and the list-view figure show.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Branch — Hierarchy View              [⌗][≣]  Filter  Add  Edit  Delete  ⟳   │
├──────────────────────────────────────────────────────────────────────────────┤
│  CODE            NAME                      ALIAS              STATUS          │
│  ⊟ BRN           Branch (root)             All Branches       ● Active        │
│    ⊟ BRN-BH      Bahrain                   Bahrain Branches   ● Active        │
│        SEEF      Seef                      Seef Branch        ● Active        │
│        MAN       Manama                    Manama Branch      ● Active        │
│    ⊞ BRN-SA      Saudi Arabia              KSA Branches       ○ Inactive      │
└──────────────────────────────────────────────────────────────────────────────┘
```

The **Tree** view indents a value under its parent *of its own dimension* — Department → HR →
Recruitment — and shows nothing of any other dimension. The **List** view is the same values, flat,
with the Status column. Today's per-type list already has Code, Name, Parent, Status and Headcount
and already searches, filters and pages; what is new is the Alias column, the tree rendering, and
the five named toolbar buttons.

**How you reach it:** Organisation → *Branch*. (Today: Organisation → Dimension types → the Branch
row → **View units**. That route is kept — it is where somebody configuring types already is — and
the menu entry becomes the short way.)

## B3. Adding a value

One screen, matching figure 7/8 — the FRS's "two steps" are two *sections of one form*, which is
what its own single-form figure shows.

```
Add New Branch Value
  Code                     [ SEEF                    ]
  Branch Name (English)    [ Seef                    ]
  Branch Name (Arabic)     [ السيف                    ]
  Alias (English)          [ Seef Branch             ]
  Alias (Arabic)           [                         ]        (optional)
  Effective from           [ 2026-10-12 ]  ●required
  Effective to             [            ]   optional

  ⑤ Parent Mapping — this value must be mapped to:
     Ⓐ Division (parent dimension)                    ← one group per mapped parent
        ○ Cayman (Head Office)
        ● Bahrain
        ○ UAE
        ○ Saudi Arabia
     Ⓑ Department (parent dimension)
        ○ Finance
        ○ Human Resources
        ○ Operations
        ● Retail Network
        More mapped parent dimensions, if configured, appear here as additional groups.

                                              [ Cancel ]  [ Save ]
```

Three differences from the mock, each deliberate:

- **Radios, not checkboxes.** "Pick exactly one from this group" is what a radio group *means*. The
  mock draws checkboxes; a set of checkboxes is a set of independent answers and lets a reader build
  something the rule must then refuse. This is the same reasoning the placement-split screen already
  applies to its "exactly one is main" column. Where a mapping permits more than one parent (Q-f),
  that group renders as checkboxes, which is then honest.
- **The effective dates are on the form.** The FRS requires them (§1.2) and the mock omits them.
- **Each group is a searchable picker once the list is long.** Crescent has two departments called
  *Credit*; a flat list of bare names offers a reader two identical rows. The existing unit picker
  already shows every option with its full path — `Crescent › Retail Banking › Credit` — and is
  reused rather than reinvented. Below about a dozen values it renders as the mock's plain list.

**Rule 4 is the absence of this block.** A dimension with no mapped parent shows the top half of the
form and nothing else.

## B4. Delete (D4)

Pressing **Delete** asks the engine what the value is used for and then says one of three things:

| What the engine finds | What the screen does |
| --- | --- |
| Nothing at all — no employees, no children, no links, no history | *"Delete Seef permanently? Nothing refers to it."* → really deletes |
| Referred to only by history | *"Seef cannot be deleted: 14 employees were placed here between 2024 and 2025. It will be made inactive from a date you choose, and every past report will still resolve it."* → a date box and a **Make inactive** button |
| Referred to by live data | The same sentence with the live blockers listed by name — *"3 employees are placed here today; 2 branches sit under it"* — and the same **Make inactive from…** action, which ends the value and asks what should happen to the units under it (the existing three-way: move them, retire them too, or leave them unplaced) |

The third row is the change from architecture §6, which refuses outright. D4 says a used value
becomes inactive and the user is told why; a refusal with no next action is the thing D4 is written
against.

## B5. The Organisation tree (FRS §5, figures 9 and the filtered view)

Read-only. No Add, no Edit, no drag.

```
┌───────────────────────────────────────────────┬──────────────────────────────┐
│  Full Organisation  ›  MENA Region            │  View Options                │
│                               [ × Clear ]     │  Select any node to drill    │
│                                               │  into that branch only.      │
│              ┌──────────────┐                 │                              │
│              │ KKT GROUP    │ Business Unit   │  VIEW BY                     │
│              └──────┬───────┘                 │   Full Organisation   👤 227 │
│         ┌───────────┴───────────┐             │   Business Unit       👤 227 │
│   ┌─────▼─────┐          ┌──────▼──────┐      │   Division            👤 227 │
│   │ Cayman HQ │ Division │ MENA Region │      │   Department          👤 219 │
│   └───────────┘          └──────┬──────┘      │   Branch              👤  96 │
│                     ┌───────────┴────────┐    │   Position            👤 227 │
│               ┌─────▼─────┐      ┌───────▼──┐ │                              │
│               │  Finance  │      │  Retail  │ │  CURRENTLY VIEWING           │
│               └───────────┘      └─────┬────┘ │   MENA Region                │
│                  Department            │      │   118 employees in this      │
│                                 ┌──────▼─────┐│   branch                     │
│                                 │ Seef Branch││                              │
│                                 └──────┬─────┘│                              │
│                              ┌─────────▼────────┐                            │
│                              │ Retail Supervisor│ Position                   │
│                              └─────────┬────────┘                            │
│                      ┌─────────────────┴──────┐│                             │
│                ┌─────▼──────┐          ┌──────▼─────┐                        │
│                │Ahmed Salman│ Employee │Fatima Noor │                        │
│                └────────────┘          └────────────┘                        │
└───────────────────────────────────────────────┴──────────────────────────────┘
```

Three things the mock implies and this design makes explicit:

- **It is a plain tree. Every node has one parent (D19).** The mock draws Seef Branch under two
  departments with dashed edges; that is not built. Seef sits under Retail and nowhere else, so
  nothing is ever drawn twice, no count below a node can double, and the renderer needs no
  already-drawn set to protect itself. The 12 October draft's *"drawn once, with two incoming
  edges"* was the careful answer to a problem that no longer exists.
- **The counts in "View by" are per dimension.** The *Department* row counts every employee whose
  position rolls up to some department; the *Branch* row does the same for branches. Two rows can
  legitimately disagree, because not everybody is under a branch. The *Full Organisation* row is
  unique employees across the whole chart. `DISTINCT EmployeeId` stays in every one of those queries
  — not for the tree, which now has one path per node, but because **an employee may hold several
  positions** and be reached through two different leaves (§2.5).
- **Employees are leaves and are not a dimension** (D6). They come from the employee record and
  appear under their position. **Slice F8 ships the tree without them**; F6 adds them when employees
  start joining through a position.

**How you reach it:** Organisation → *Organisation tree*. Clicking any node re-renders from that
node; the breadcrumb and **Clear selection** go back.

## B6. The employee screen

The job-details section today holds *Job title* (free text, bilingual), *Grade* (bound to the
`grade` lookup list since slice B2), *Employment type*, *Work location* and three dates. After this
work it holds the dates, employment type and work location, and the top of the employee record gains
a read-only block:

```
Position     Retail Supervisor (RSUP)            [ Change position… ]
             Crescent › Branch Network › Bahrain › Seef › Retail Network
Grade        G7  ·  Step G7-S3 from 1 Jan 2026   [ Change step… ]
Job title    Supervisor                          (from the position)
```

Nothing in that block is typed. It is read from the position the employee is assigned to, as at
today's date, and every line of it is a link to the value it came from.

---

# C. What is deliberately not decided here

Per CLAUDE.md, open technical decisions are proposed and stopped on, not taken. This document takes
none of: the front-end approach (the tree and the enquiry report are described as server-rendered
with progressive enhancement, which is what every WorkMate screen already is — if a reviewer wants a
client-side chart library that is a decision, not a detail); the formula engine; the distributed
cache; the search provider. Questions **Q-a** to **Q-i** in section 10 are for the product owner.

---

# D. The shape of the change, in one diagram

```
TODAY                                   AFTER

DimensionType                           DimensionType
  code, bilingual name                    + authored bilingual PLURAL  (revived)
  attribute schema                        + menu order, show-in-menu   (revived)
                                          + IsSystemDefined (14 shipped)

Structure            ── the axis ──     Structure            ── still the axis ──
  Levels (vocabulary)                     Levels
  Containment (parent→child pairs)        Containment          ── unchanged ──
  RootTypes                             + Mapping cardinality  ── NEW: required / optional
  EmployeeAttachableTypes                 EmployeeAttachableTypes  (= [Position] for FRS tenants)

DimensionRecord (content item)          DimensionRecord
  code, EN name, AR name                  + Alias EN / Alias AR      ── NEW column
  effective from / to, active             (unchanged otherwise)

DimensionLink                           DimensionLink
  one parent per date  ◄── the limit      N parents per date, one per mapped
                                          parent dimension  ── the real change

DimensionClosure                        DimensionClosure
  one path per pair                       several paths per pair ── no schema change,
                                          the document already allows it

EmployeeAssignment                      EmployeeAssignment
  employee → unit, dated, %, primary      employee → POSITION, dated, %, primary
                                          and employee → GRADE STEP on a Grade axis
                                          ── no schema change, no new table
```

**The single most important engineering finding in this document:** ADR-0005's storage already
generalises to a value with several parents. `ClosureAncestor`'s own doc comment says *"one ancestor
can appear more than once with different depths or ranges"*, and `DimensionClosureDocument.Ancestors`
is a list. What has to change is the **arithmetic** in three methods and one discriminator on
`ParentLink` — not the tables. That is why this is an additive migration rather than a rebuild.

---

# 1. Gap table

Every FRS requirement and every decision D1–D6, against what exists today.

Legend: **Done** — works now, no change. **Partial** — mechanism exists, needs extending.
**Missing** — nothing exists. **Conflict** — contradicts a recorded decision; reconciliation named.

## 1.1 FRS §1.2 Scope and §4 Functional requirements

| # | Requirement (FRS) | Today | Where | Verdict |
| --- | --- | --- | --- | --- |
| A1 | A hierarchy designer defining relationships between master tables | Structures editor: containment grid, root types, "build from a chain" | `StructuresAdminController`, `Views/StructuresAdmin/Edit.cshtml`, ADR-0010 | **Partial** — exists, is presented as "levels of an axis" rather than "this dimension is mapped to those dimensions", and has no cardinality |
| A2 | All dimensional master tables appear as separate menu items | Three fixed entries; per-type entries shipped and were withdrawn | `Navigation/AdminMenu.cs`; `DimensionTypeDocument.ShowInMenu/PluralName/MenuOrder` are stored, written by `UpdateFrom7Async`, and **read by nothing** | **Conflict (C3)** — ADR-0024 |
| A3 | Each dimension can have unlimited records | Unlimited, paged | `DimensionRecordsAdminController.Index` | **Done** |
| A4 | Step 1 — a record links to its own parent dimension (nesting within itself) | The `X → X` diagonal on the containment grid | ADR-0010 addendum; `ContainmentRules.Decide` | **Done** |
| A5 | Step 2 — the record maps to a parent node in *another* dimension | Containment permits it; a record holds **one** parent per structure | `DimensionLinkDocument.ParentOn` returns a single `ParentLink` | **Done**, after D19. The FRS asked for *several* parents and C1 recorded the conflict; the product owner has withdrawn that, so what the engine already does is what is wanted |
| A6 | Each dimension viewable as a tree showing links | Per-type list has a **Parent** column (full path per structure); the tree is the cross-type designer | `DimensionRecordsAdminController`, `OrganisationDesignerAdminController` | **Partial** — no single-dimension tree rendering |
| A7 | One combined tree built automatically from every mapping | Designer draws **one structure**, cross-type, with lazy-loaded children, search and pan | `OrganisationDesignerAdminController`, `_DesignerNode.cshtml` | **Partial** — it is editable, it is per-structure, and it has no View-by panel or counts |
| A8 | Effective start mandatory, end optional, on every record | Exactly this; `DimensionRecordPartHandler` rejects a missing start and an end before the start | `DimensionRecordPart.EffectiveFrom/EffectiveTo` | **Done** |
| A9 | Positions linked into the org; assigning a position puts the employee in the division, department, cost centre and grade | Employees are placed **directly** at a unit; nothing is derived | `IEmployeeAssignmentService.PlaceAsync` | **Missing** |
| A10 | Position has its own reporting-line tree | — | — | **Missing** |
| A11 | Employee master realigned to the new org setup | Employee record holds no placement at all (by design); placement screen is per-structure unit picking | `EmployeePart`, `EmployeePlacementAdminController` | **Partial** |
| A12 | Org Role = Position, one master, not duplicated | Neither exists | — | **Missing** |
| A13 | Columns Code, Name, Alias, Status | Code, Name, **Parent**, Status (with dates), Headcount, optional attribute columns | `DimensionRecordAdminViewModels` | **Partial** — no Alias field anywhere in the model |
| A14 | Buttons Filter, Add, Edit, Delete, Refresh | Search + three filters; New; Edit; retire/move via the designer; no Refresh | `Views/DimensionRecordsAdmin/Index.cshtml` | **Partial** |
| A15 | Tree / List toggle in the top-right | The designer has a chart/list toggle remembered in a cookie — on the *designer*, not on a dimension screen | `OrganisationDesignerViewModel` | **Partial** — the pattern exists and is reusable |
| A16 | Status badge Active / Inactive | `DimensionRecordPart.IsActive`, plus the dated effective range, both shown | `_UnitAttribute`, the list's Status column | **Done** |

## 1.2 FRS §6 Business rules

| Rule | Today | Verdict |
| --- | --- | --- |
| **1** Every dimension has a tree view | Only the cross-type designer | **Partial** (see A6) |
| **2** A tree view shows only that dimension's values | Nothing shows only one dimension's values as a tree | **Missing** |
| **3** ~~Mapped to >1 parent ⇒ one value from **each**~~ → **a Required mapping must be satisfied before save** (D19) | Containment permits a parent; it never **requires** one. `AddUnitAsync` happily creates a unit with no parent (that is the "unplaced" state, a deliberate feature) | **Missing** — needs cardinality, ADR-0021 as amended. The obligation survives; the plural does not |
| **4** No mapped parent ⇒ skip the step | Already how `AddUnitAsync` behaves | **Done** |
| **5** Combined tree generated from existing linking, no separate setup | True of the designer within one structure | **Partial** |
| **6** Renaming a dimension's label updates its name everywhere | `DimensionTypeDocument.Name` is the single source; every screen reads it; `Code` is separate and immutable (the content type is named for it) | **Done** — and worth saying so, because this is the rule most likely to be assumed broken |

## 1.3 The decisions D1–D6

| | Decision | Today | Verdict |
| --- | --- | --- | --- |
| **D1** | A value with several parents counts under each parent within that parent dimension's own View-by; org totals count unique employees | Counts are per node, batched, correct for a tree. There is no View-by, and `GetEmployeesUnderAsync` has no distinct-employee guarantee across several paths because today there is only one path | **Partial** |
| **D2a** | Position is a dimension with its own reporting tree; Org Role = Position | — | **Missing** |
| **D2b** | A position holds many employees | Already true — nothing limits a unit to one person | **Done** |
| **D2c** | Optional approved headcount; vacancies = approved − filled | — | **Missing** |
| **D2d** | Employees join by a dated position assignment; org fields derived | `EmployeeAssignment` is exactly the dated, allocatable mechanism; what is missing is that it points at a position and that the org fields are read from the position's ancestors | **Partial** |
| **D3** | One menu item per dimension, fixed order, bilingual plurals, hideable; tree first; Settings last | Withdrawn; the three fields survive on the document, inert | **Conflict (C3)** |
| **D4** | Delete really deletes an unused value; a used one goes Inactive with an end date and the user is told why | `AssessDeletionAsync` returns Clean / RetireOnly / **Blocked**, with blockers named | **Conflict (C4)** |
| **D5a** | Grade Steps is a dimension, grades as parents, steps as children | `grade` is a **lookup list** bound to `JobDetails.GradeCode` (slice B2) | **Conflict** — see §4; resolved by ADR-0026 and a documented data move |
| **D5b** | Position **references** a grade (D21 — a reference, not a parent); employee holds a dated step on a Grade axis | — | **Missing** |
| ~~**D5c**~~ | ~~Job Title is a dimension positions point to~~ | `job-title` ships as a lookup list **bound to nothing**, deliberately, "because a code-keyed job title is a position by another name" | **STRUCK OUT BY D20.** Position = Job Title = Designation, one concept. The list is retired (D22); nothing was ever bound to it, so nothing changes on a screen |
| **D5d** | Lookup lists remain for non-organisational values | Ten lists ship; D5's list and B2's shipped list differ slightly — see §7.1 | **Done**, with one reconciliation |
| **D6** | Employees stay employee records and appear as leaves of the org tree | They are employee records; they are not drawn on any tree | **Partial** |

## 1.4 Things the FRS needs that nothing in it mentions

| | | Verdict |
| --- | --- | --- |
| Alias, as a stored field | `DimensionRecordPart` has eleven standard fields and none of them is an alias | **Missing** — new column, new migration, new upgrade test |
| A pre-configured dimension set | `base.recipe.json` ships `cost-centre` only, deliberately | **Conflict (C2)** |
| "Position Title" **and "Job Title"** as dimensions | Figure 2 lists both as separate dimensions while the feedback note says Position is the one master | **Settled by D20** — neither ships. One concept, always *Position* |
| "Employees" as a dimension | Figure 2 lists *Employees* among the dimensions; D6 says employees are not a dimension | **Conflict** — D6 wins; the entry is dropped and the reason recorded |
| A "Position" entry in figure 2 | Figure 2's list has **no Position row**, yet §4.1.4 shows a Position tree and figure 9 puts Position above Employee | **Inconsistency in the FRS** — Q-g |

---

# 2. Data model

## 2.1 Mapping, in the model

**The FRS's "mapping" is the containment map read child-first, plus a cardinality.** ADR-0010 already
stores, per structure, a set of permitted `(parent type → child type)` pairs. Read backwards — "which
types may be *my* parent" — that is exactly figure 3's arrows. Nothing new is needed to express
*which* dimensions a dimension is mapped to.

What is new is **obligation**. Containment today is permissive: *a Branch may sit under a Division*.
Rule 3 is compulsory: *a Branch must name a Division before it can be saved.*

> **Amended by D19.** Rule 3's original words are *"one value from **each** mapped parent"*. A value
> now has exactly one parent, so the rule survives in the singular and the plural does not.

```csharp
// StructureDocument gains one list. Containment is untouched.
// At most one entry per ChildDimensionTypeId — enforced in IStructureService, not on the screen.
public IReadOnlyList<StructureMapping> Mappings { get; set; } = [];

public sealed record StructureMapping(
    string ChildDimensionTypeId,
    string ParentDimensionTypeId,
    MappingCardinality Cardinality);

public enum MappingCardinality
{
    Optional,   // a parent may be chosen or left empty. What every existing pairing becomes.
    Required,   // Rule 3, in the singular: one must be chosen before the value can be saved.
}
```

**Three things the 12 October draft had and this does not**, each for its own reason:

| Gone | Why |
| --- | --- |
| `OneOrMore` | It meant "several parents of one dimension", which D19 removes. The name survives in **D21** for something that is not a parent — a position's **grade reference**, where a range G7–G8 is wanted and is expressible exactly because a reference is not a placement. That is a separate list on the structure and it never enters the closure index or any tree |
| `IsPrimary` | It answered "several parents, one needed, which?". D10 records that the premise is gone |
| `ExactlyOne` as a name | Renamed `Required`. "Exactly one" was a count across a set of parents; with one parent type the only question is whether it may be left empty |

**Why a separate list rather than a flag on `StructureContainment`.** Three reasons. Self-nesting is
a containment pair (`Department → Department`) and is never a *mapping* in the FRS's sense — a
Department is not required to name a parent Department. A pairing can be permitted without being a
mapping at all, which is what every structure written before today is. And a mapping needs a stated
order (figure 8's groups are numbered A, B, …) that containment has no place for; the list's own
order supplies it. **The third reason is weaker after D19** — with one parent there is one group and
nothing to order — but the first two carry the decision on their own.

**Existing tenants are untouched by construction.** A structure written before this field exists
deserialises `Mappings` to empty, and empty means *every declared pairing is optional* — which is
exactly what the engine does today. Zenith and Crescent continue to behave identically until somebody
opens the Mapping screen and ticks something. This is the same "empty means unconstrained" rule
`EmployeeAttachableDimensionTypeIds` already uses, and the same rule ADR-0008's 11 October addendum
applies to a recipe row that states nothing.

## 2.2 ~~Several parents at once — the real change (ADR-0020)~~ — WITHDRAWN (D19)

**This section is withdrawn in full, before any of it was built.** A dimension value has exactly one
parent; ADR-0020 is withdrawn with it; slice F4 is deleted. The text it held — the `MappingKey` on
`ParentLink`, the DAG closure arithmetic, `GetDepthAsync` becoming the shortest path, the
`ParentDimensionTypeId` migration — described work that no longer has a reason to exist.

**It is withdrawn rather than deleted from the repository.** The analysis in
`docs/adr/0020-a-value-may-have-several-parents-at-once.md` is kept there in full, for the reason
that ADR gives: if multi-parent is ever asked for again, the expensive finding — that ADR-0005's
storage already generalises, because `DimensionClosureDocument.Ancestors` is a list whose own comment
permits one ancestor at several depths — is done. Read it there rather than here.

**What survives, and is built.** One thing: the `DISTINCT EmployeeId` guarantee on *"employees under
a node"*. It was introduced here for the DAG and it earns its place without one, because an employee
may hold several positions — a split allocation, the Zenith matrix — and that is a different source
of double counting which one parent per value does not remove.

**What stands instead.** `DimensionLinkDocument.ParentOn(asAt)` keeps returning a single parent. The
closure index keeps one path per `(ancestor, descendant)` pair, so `GetDepthAsync` stays unambiguous,
no node is ever drawn twice, storage stays proportional to the live structure plus its history rather
than to the number of root-to-node paths, and architecture open question 6 — the retention policy for
closure rows — goes back to being a question that should be answered rather than one that must be.

## 2.3 Nesting within a dimension

Department → HR → Recruitment is the `X → X` diagonal on the containment grid, settled by ADR-0010's
addendum: tick the diagonal on that axis and that kind of unit nests inside itself there. **It is not
a mapping** — the Mapping screen does not offer `Department → Department`, because Rule 3's "must
pick one from each" is nonsense applied to a dimension's own root values. The diagonal stays on the
containment grid where it already is, and the dimension's own tree view renders it.

The consequence for the tree view in B2 is worth stating: a dimension's tree has **its own roots** —
the values of that dimension that have no parent *of the same dimension*. Seef has a parent (two, in
fact), and both are Departments or Divisions, so on the Branch screen Seef is a root. That is Rule 2
read exactly: "a tree view only shows the values that belong to that one dimension."

## 2.4 What happens to "Structure"

**It survives, unchanged in purpose and demoted in prominence.** Three reasons, and they are not
sentimental:

1. It is what links are scoped by (ADR-0005), and that scoping is the only reason a record can
   participate in several axes. Removing it would collapse the Cost axis, the Grade axis proposed in
   §4 and the organisation axis into one tree — the exact failure architecture decision 4 exists to
   prevent.
2. It owns containment, roots and employee-attachability, all of which the FRS needs and none of
   which it gives a home to.
3. ADR-0010's whole argument is that *"what may contain a Department"* is an axis-specific fact. The
   FRS does not contradict that; it simply only ever shows one axis.

**What changes is the vocabulary a user sees.** The word "structure" leaves the UI. The Structures
editor becomes **Organisation → Settings → Mapping**, and its containment grid is re-presented
child-first as "this dimension is mapped to…" with a cardinality column beside each. A tenant with
one organisation axis — which is every FRS tenant — never sees the word. A tenant running a Cost axis
as well sees a selector at the top of the Mapping screen, and that is the only place the plural
appears.

The FRS implies one organisation. `StructureDocument.IsPrimaryOrganisation` already names it, at most
one structure carries it, and `base.recipe.json` already creates it. Every new FRS screen — the
per-dimension trees, the Add form's mapping groups, the combined tree — reads the primary
organisation axis and does not ask the user which axis they meant.

## 2.5 How D1's counting works — amended by D19

Three different numbers, computed three different ways, and the design says which is which so that
two screens cannot disagree.

| Number | Where it appears | How it is computed |
| --- | --- | --- |
| **At a node** | the Branch screen's headcount column; a card's count | assignments whose `NodeId` is that record, on that date. Unchanged, already batched |
| **Under a node** | the tree's "118 employees in this branch" | assignments joined to closure on `DescendantId`, **`DISTINCT EmployeeId`** |
| **View by `<dimension>`** | the side panel | for each value of that dimension, count-under as above; **sum them** |
| **Full organisation** | the panel's first row | `DISTINCT EmployeeId` over the whole axis |

**D19 removes one of the two reasons `DISTINCT` is there, and leaves the other.** A value has one
parent, so nobody is reachable by two paths through the *tree* and the "a value counts under EACH of
its parents" half of D1 has nothing to describe. What remains is that **an employee may hold several
positions** — a split allocation, the Zenith matrix — so the same person can be reached through two
different leaves. That is a different source of double counting and one parent per value does not
touch it, which is why the guarantee stays in the query rather than being dropped with the DAG.

The panel's rows can still legitimately differ from each other and from the total — the Department
row counts everybody whose position rolls up to a department, the Branch row everybody under a
branch, and not everybody is under a branch — so the screen still says so in a sentence rather than
leaving a reader to decide which number is the broken one. That sentence is part of the deliverable,
not a nicety: two plausible numbers that disagree with nothing explaining why is the failure mode
this whole engine is written against.

## 2.6 Cycle and date rules

*Amended by D19: the DAG rows are gone, and what is left is a shorter list than the 12 October draft
had.*

| Rule | Today | After |
| --- | --- | --- |
| No cycles, across every date | enforced; a node may never become its own ancestor on any date | **unchanged**, and unchanged in effect too. The 12 October draft noted that with several parents A→B and B→A could be reached by two different mappings and would need a test per mapping *pair*; with one parent there is one path and the existing check and its existing tests cover it |
| Parent must be effective when the child is | advisory, not blocking | unchanged |
| A mapping's parent must be of the mapped dimension | — | **new**: a Branch's Division slot may only hold a Division. Falls out of `ContainmentRules.Decide`, which is already the one implementation both the validator and every picker read — asked once rather than per key, so it needs one test per mapping |
| Rule 3 — a required mapping must be satisfied | — | **new**, in `IDimensionValidator`, so the Add form, the recipe step and any future API are refused in the same words |
| Effective ranges must not overlap | per employee per structure (assignments); per record per structure (links) | **unchanged.** The draft had links becoming "per record per structure per mapping key"; there is no mapping key |
| Code uniqueness within the tenant, including against retired records | enforced | unchanged |

**One rule is deliberately *not* added.** A value is still allowed to exist with no parent at all,
even when its dimension has a required mapping. That is the unplaced state, it arrives from imports
and from a retired parent, and the engine already models and reports it. Rule 3 governs the **Add
form** — "it cannot be saved on its own" — not the existence of a row in the database. An import that
produces an unmapped Branch puts it in the unplaced panel with a reason, which is strictly more
useful than refusing the whole import. This distinction is stated in the ADR because it is the one a
future reader is most likely to collapse.

---

# 3. Positions

## 3.1 Position is a dimension type, and that is the whole of it

Position needs: its own list of values; a tree of its own (the reporting line); a place in the
organisation (mapped to a Branch or a Department); dates; a code; bilingual names; an alias; and to
be pointed at by employees. Every one of those is what a dimension type already is. **No new
aggregate, no new table, no new service.**

```
Organisation axis (the primary one), after D19 and D20:

   containment              business-unit → division
                            division      → department
                            department    → department   (self-nest: HR → Recruitment)
                            department    → branch
                            branch        → position

   mapping (one parent)     branch   under division     Required
                            position under branch       Required     ← Crescent
                                                                       (Zenith: under department)

Reporting axis (a second structure), after D19:

   containment              position → position          THE REPORTING LINE
   mapping                  position under position      Optional     (the CEO has none)

References, which are NOT parents and never reach a tree (D21):

   position → grade         Required, OneOrMore permitted for a range G7–G8
```

**D19 forces a second axis here, and that is the right answer rather than a workaround.** FRS §4.1.4
draws the Position tree as a reporting line — POS → EXE → CEO, COO → RDIR; MGT → BMGR → BSUP — while
figure 9 draws the organisation tree as Business Unit → … → Branch → **Position** → Employee. A
position therefore genuinely needs two places at once: *who it reports to*, and *where in the
business it sits*. On 12 October those were two concurrent `ParentLink`s with different mapping keys.
With one parent per value they cannot be.

They are two structures instead, which is precisely the mechanism ADR-0005 scoped links to a
structure for — *"one record can sit on several axes at once, which a single parent field makes
impossible"* — and which D19 itself points at. Nothing new is needed: a record on two axes is what
the engine has always done, the two trees are independent, and neither needs to know about the other.

| | Organisation axis | Reporting axis |
| --- | --- | --- |
| A position's parent | its Branch (or Department) | the position it reports to |
| Renders | figure 9's combined tree | the Position screen's own tree, §4.1.4 |
| Employees attach | yes — `EmployeeAttachableDimensionTypeIds = [position]` | no |
| Headcount rolls up | yes | **no** — a reporting line is not a containment, and counting staff "under the COO" is a different question from counting them in a division |

That last row is worth stating because it is the thing a reader will assume otherwise. It also
removes an ambiguity the 12 October draft carried: with both edges on one axis, "employees under
this node" had two meanings and the closure index could not tell them apart.

**A question for the product owner falls out of this**, and it is in §10.2 as **Q-j**: does the
reporting axis ship as a second structure in the base recipe, or is it a tenant's to create? Shipping
it means every tenant gets a Reporting structure whether or not they use reporting lines; not
shipping it means the Position screen has no tree until somebody builds one.

## 3.2 Approved headcount and vacancies

D2: "Optional approved headcount per position; vacancies = approved − filled."

**Recommendation: an attribute on the shipped Position dimension type**, named `approvedHeadcount`,
of kind Number. The attribute schema mechanism exists, ships through the recipe step, exports, and
renders on the Add/Edit form with no new code at all. A tenant that does not use positions pays
nothing, and a tenant that wants "budgeted cost" or "reports-to grade" beside it adds an attribute
rather than asking for a column.

The alternative — a nullable `ApprovedHeadcount` on `DimensionRecordPart` — was rejected because that
part is carried by **every** generated dimension content type, so a Division and a Cost centre would
each grow a headcount field that means nothing.

```
Vacancies(position, asAt) = ApprovedHeadcount − count of effective assignments AT that position
                            (the node itself, not its descendants)
```

A new read on `IEmployeeAssignmentService`, batched for a list of positions the same way
`CountEmployeesAtAsync` already is, returning `(approved, filled, vacant)` so the Position list can
render three columns in one query. Negative vacancies — over-establishment — are shown as a negative
and not clamped; clamping would hide the one condition the number exists to surface.

**Known limitation to record in the README:** `approvedHeadcount` is an attribute, and attributes are
undated (the Dimensions README says so explicitly: *"an attribute is a field on the record's content
part with no effective range"*). So "what was the approved headcount last March" is not answerable.
That is acceptable for the first slice and is a real limit; making it dated is a model change of the
same shape as the dated name history, and it should be a question for the product owner (Q-i) rather
than something designed in silently.

## 3.3 The employee → position assignment

**Reuse `EmployeeAssignment` and `ReallocateAsync` unchanged.** This is the strongest reuse in the
document and it is worth being explicit about why it is not a stretch:

| What D2 needs | What `EmployeeAssignment` already is |
| --- | --- |
| dated | `EffectiveRange` per row; mid-month transfer is two rows |
| a position holds many employees | nothing constrains a node to one |
| an employee may hold more than one position | concurrent rows — that is the split-allocation mechanism |
| main vs additional | `IsPrimary`, with "exactly one primary" enforced |
| allocation % | `AllocationPercent`, with "allocations total 100" enforced |
| changing position is one operation | `ReallocateAsync` states the whole picture from a date |
| history preserved | every closed row stays |

The only configuration change is on the structure:
`EmployeeAttachableDimensionTypeIds = [ position ]`. That one line is what makes the placement
screens, the pickers and the designer's "Place an employee here…" offer **positions and nothing
else**, because all three already read `PermitsEmployeesAt` through one implementation.

**Nothing about the Zenith matrix breaks.** An engineer with a primary at their home department and a
secondary at a site team becomes an engineer with a primary at a *departmental* position and a
secondary at a *team* position. The rows, the rules and the roll-ups are identical; what changed is
which record the row points at.

## 3.4 Derived organisation fields

```
Employee → (assignment, dated) → Position → (closure ancestors, dated) → Branch, Department,
                                                                          Division, Business Unit,
                                                                          Cost centre
```

One new read, and it belongs on `IEmployeeAssignmentService` beside the others:

```csharp
Task<IReadOnlyDictionary<string, EmployeeOrgPlacement>> GetOrgPlacementsAsync(
    IReadOnlyList<string> employeeIds, DateOnly? asAt = null, CancellationToken ct = default);

public sealed record EmployeeOrgPlacement(
    DimensionNodeRef Position,
    IReadOnlyList<DimensionNodeRef> Ancestors,   // nearest first, every path
    decimal AllocationPercent,
    bool IsPrimary);
```

Batched, for the reason every read on that interface is batched: the caller is a page of fifty
employees and a lookup per row is fifty queries to draw one column. It is a composition of two
existing batched reads (`GetPrimaryPlacementsAsync` and `GetAncestorPathsAsync`), not a new query.

**The employee record still holds no department field**, and the hard boundary is untouched. What the
employee screen shows is computed on read, as at a date, from two dated tables — which is the entire
point of architecture §2's one rule.

## 3.5 What this changes on the existing screens

| Screen | Change |
| --- | --- |
| **Employee editor** | New read-only *Position / Grade / Job title* block at the top (B6). The Job-details section loses Grade and Job title — see §4.4 |
| **Employee list** | The "Where they sit" column shows the **position** and its derived path. The existing "+N more" disclosure for a split keeps working unchanged; it already shows full paths |
| **Place / Change placement** | The unit picker offers positions, because the structure says so. The split rows, the radio group and the 100% rule are untouched. **"Change placement" is relabelled "Change position"** in the FRS tenant's vocabulary, which is a localisation change, not a code path |
| **Unit heads (ADR-0012)** | Unchanged pending **Q-e**. See §10 |
| **Designer card headcount** | Unchanged at a node; the "under" count gains the distinct-employee guarantee of §2.5 |
| **Unit picker** | Already shows every option with its full path, which is what makes two positions called *Supervisor* tellable apart. Already reused |
| **Employee picker** | Each option carries "where they sit", from `EmployeeUnitNames`. That becomes the position's name; **one place to change**, which is why that service exists |

## 3.6 Rule 2 and employees

D6: employees are not a dimension; they are leaves of the combined tree. So the **Position screen's
tree shows positions, not people** — Rule 2, literally. People appear in two places: the headcount
column on the Position list (a link into the employee list filtered to that position, which already
works), and as leaves of the combined tree in §5.6.

---

# 4. Grades, steps and job titles

## 4.1 Q-a — the shape of the Grade Steps tree

D5 says: *"'Grade Steps' is one dimension with its own tree: grades as parents, steps as children
(G7 → G7-S1..S5)."*

**Option A — one dimension type, self-nesting.** `grade-step`, with the `X → X` diagonal ticked. G7
is a root value; G7-S1 is its child. A reader cannot tell a grade from a step except by depth.

**Option B — two dimension types, one screen.** `grade` and `grade-step`, with
`grade → grade-step` in the containment map and `grade-step` mapped to `grade` as ExactlyOne. The
menu has one entry, *Grade Steps*, whose tree shows both types — grades at the top level, steps
indented under them. That is the figure, and the two types are what every rule downstream needs:
a position maps to a **grade**, an employee holds a **step**, and the containment map is what makes
"a step belongs to a grade" enforceable rather than conventional.

**Recommendation: B.** Option A cannot express "a position is graded G7" without the rule "a grade is
a node at depth 0", which is brittle the first time a customer puts a band above their grades. The
cost of B is that it is a slightly liberal reading of D5's "one dimension" — it is one dimension *on
screen and in the menu*, built from two dimension types. **That is a question for the product owner
(Q-a), not a decision taken here**, because D5 is binding on what is built. If the answer is A, the
only change to the rest of this design is that the "grade" and "step" rules become depth rules and
`MappingCardinality` is applied to the diagonal, which is more code and less safety, and the slice
plan is unaffected.

**Option C — grades as a dimension, steps as an attribute of the employee's assignment** was
considered and rejected: a step would then have no code, no bilingual name, no dates of its own and
no screen, and figure 2 puts *Grade Steps* in the dimension list.

## 4.2 Q-b — where the grade attaches

Three sub-questions, each with a recommendation and none decided here.

**Is grade mapped to the position, or chosen per employee?**
*Recommendation: mapped to the position.* It is what D5 says ("a position is mapped to a GRADE"), it
is what makes a re-grade one edit instead of N, and it is what makes "promotion = new position; grade
follows" true (D5) rather than a thing HR has to remember. A tenant that genuinely grades people
rather than posts can set the mapping to Optional and place the employee on the Grade axis directly —
which works because of the next answer.

**May a position allow a grade range (G7–G8)?**
*Recommendation: yes, and it costs nothing.* `MappingCardinality.OneOrMore` on `position → grade` is
the whole implementation; the storage is §2.2's N keyed links and the Add form renders that group as
checkboxes rather than radios. The employee's step is then validated against the steps of **any** of
the position's grades. The product owner's note already flags this as "to confirm".

**Are steps optional per tenant?**
*Recommendation: yes, by not configuring them.* A tenant that creates grades and no steps has
nothing to pick, and the employee's step control is absent rather than empty. No flag, no setting; the
absence of data is the switch. This is the same judgement `EmployeeAttachableDimensionTypeIds` makes
with "empty means unconstrained".

**Where the employee's dated step is stored.**
*Recommendation: a second axis.* A `grade` **structure** whose levels are `grade` and `grade-step`,
with the employee placed on it by an ordinary `EmployeeAssignment`:

```
Ahmed Salman  —  Grade axis  —  G7-S2  from 2025-01-01 to 2025-12-31
                             —  G7-S3  from 2026-01-01
```

Dated, historical, audited, exportable, and **zero new code**: `GetAllAxesAsync` already answers
"every node this employee touches across every axis". This is also the clearest possible
demonstration of why Structure survives §2.4 — a second axis is exactly what it is for.

The alternative, a dated field on the employee, was rejected for the reason the employee record holds
no department: a field has no dates and a re-step would overwrite rather than record.

## 4.3 Q-c — Job Title vs Position Title vs Position — SETTLED BY D20

The FRS creates this problem itself: figure 2 lists **Job Title** and **Position Title** as separate
dimensions, while the feedback note says *"the system should maintain Positions as the main master
and not duplicate/separate out from the Org. Role Setup."* Three nouns, one thing.

**This recommended two masters. The product owner has settled it at one (D20).**

| Noun | 12 October recommendation | **D20 — binding** |
| --- | --- | --- |
| **Position** | A dimension type. The master | **Unchanged.** The one concept, always called *Position* — **المنصب** in Arabic |
| **Job Title** | A second dimension type; positions map to it, Optional | **Dropped.** No Job Title dimension, nothing maps to one. *Job title* is a label for Position, not a thing beside it |
| **Position Title** | The Position record's own bilingual name, not a master | **Unchanged** |
| **Designation** | not considered | **Dropped.** The same concept under a third name; no designation field anywhere |

So **both** of figure 2's extra rows go, not one. The feedback note had already said one master; this
takes it to its conclusion, and the three nouns a customer uses interchangeably now map to one thing
the product also calls one thing.

What changes in the rest of this document: §3.1 loses `position mapped to job-title`; §4.4 gains the
job-title move alongside the grade move; §6.4's shipped dimension set loses the `job-title` row; §7.1
retires the `job-title` lookup list; and **F7 is grades and steps only**.

The session-B design predicted this and left room for it: *"a code-keyed job title is a position by
another name, and introducing one as a side-effect of a lookup list would settle that question by
accident."* It is now settled deliberately — and the other way from the way this section recommended,
which is why the recommendation is kept above rather than overwritten.

## 4.4 Moving from today's state without losing data

Slice B2 shipped two things that this section changes. Both move cleanly, and the reason is a
property of the mechanism rather than of a script somebody has to get right once.

**`JobDetails.GradeCode` is bound to the `grade` lookup list.** Per ADR-0016 and the Records README,
**binding changes the editor, never the stored shape**: `GradeCode` is still the Orchard `TextField`
it has always been, holding a plain string, and the binding is two settings. So:

1. **Nothing is migrated and nothing is deleted.** The `grade` lookup list stays, its entries stay,
   and every `GradeCode` value already stored stays exactly where it is.
2. **The binding is cleared** — two settings, reversible — and the `GradeCode` field is **retired
   from the editor**, not removed from the type. ADR-0009: a migration never destroys data. The field
   and its values remain readable, exportable and auditable; it simply stops being offered.
3. **The grade a reader sees comes from the position**, via §3.4. Where a tenant has `GradeCode`
   values and no grade dimension yet, the old field is still the only answer and the screen shows it
   with a quiet note that grades have moved — which is strictly better than a blank field and a
   mystery.
4. **The values become the seed for the Grade dimension.** A one-off, operator-run **import**
   (not a migration): read the distinct `grade` lookup entries, propose a `grade` dimension value per
   entry, show it, let a human correct it, then apply. That is architecture §8's four-pass migration
   shape — profile, propose, dry run, apply — applied to a much smaller problem, and it is a *tool*
   because what the right grades are is a business question.

### The job title, after D20 — designed here, built in F6 (D22)

**`job-title` ships as a lookup list bound to nothing**, deliberately. ~~It becomes the seed for the
Job Title dimension~~ — **D20 removes the Job Title dimension**, so there is nothing for it to seed
and the list is **retired** instead, through `ILookupListService`'s existing retire path. ADR-0014
guarantees an entry is only ever retired and never deleted, so a tenant that typed entries into it
keeps them; and since the list is bound to nothing, no screen changes when it goes.

The free-text `JobDetails.JobTitle` field moves the same way the grade does, and for the same reason:

1. **Retired from the editor, not removed from the type.** It stays a stored `BilingualTextField`
   holding whatever anybody typed. A tenant's job-title history *is* those typed values, and deleting
   the field would destroy them (ADR-0009). The same shape as `HeadEmployeeId` after ADR-0012 and
   `AllowsSelfNesting` after ADR-0010's addendum.
2. **The employee's Position is shown read-only**, resolved from their dated position assignment
   through `GetOrgPlacementsAsync` — the same read that gives the derived division, department and
   cost centre. Nothing is typed and nothing is copied forward.
3. **Where a tenant has typed values and no positions yet**, the old field is still the only answer
   and the screen shows it, marked as historical, with a line saying where Position now comes from.
4. **The typed values seed the Position dimension** through the same operator-run import — profile,
   propose, dry run, apply — because what the right positions are is a business question.
5. **The relabelling.** Every user-facing *Job title*, *Designation* and *Org role* becomes
   **Position** / **المنصب**, in both PO files, in the slice that owns the screen: F6 for the employee
   record and the job details section, F5 for the dimension type's own shipped name.

**What the Job details section shows afterwards:** Employment type, Work location, Effective from,
Effective to, Probation ends. **Grade and Job title both leave it** and appear in the read-only block
at the top of the employee record, sourced from the position. The `BagPart` alternate
(`ContentPart-Employee-BagPart.Edit.cshtml`) is already generic over the sections and needs no change
— the Records README anticipated the first half: *"it will: the FRS work on positions moves grade off
the job details section."* D20 makes it both.

---

# 5. Screens

Every screen below is reachable by clicking, and the click path is stated. Nothing is reached by
typing a URL.

## 5.1 The menu (D3)

`Navigation/AdminMenu.cs` gains a dimension-driven middle section. The three fields that are already
on `DimensionTypeDocument` and read by nothing — `ShowInMenu`, `PluralName`, `MenuOrder` — are
**revived rather than re-invented**, which also means `Migrations.UpdateFrom7Async` stops being a
frozen step that writes fields nobody reads.

The three defects that caused the withdrawal, each named and each fixed:

| What went wrong | Fix |
| --- | --- |
| *Branchs* — English plurals were guessed by appending "s" | **The plural is authored**, on the dimension type editor, in both languages. `DimensionTypeMenu.DefaultFor` stays frozen (ADR-0009) and is no longer the source; a type with no authored plural falls back to its **singular**, which is wrong-looking and never wrong |
| Arabic was not pluralised at all (قسم → أقسام changes the word) | Same: authored, both halves |
| *Departments, Departments, Departments* — one entry per type **per structure** | **One entry per dimension type**, which is what the FRS means by "dimension". A type used by three axes is one menu item |
| An unreadably long menu on a tenant with twelve types | `ShowInMenu` is the hide switch D3 asks for, set on the dimension type editor, and the shipped set (§6.2) turns off the ones a starter tenant does not use |

Order: *Organisation tree* at a fixed low position; dimensions by `MenuOrder`, then by the
**English name**; *Settings* at a fixed high position. Explicit numeric positions, never
`LocalizedString.PrefixPosition()`, which orders by the caption and so gives an Arabic reader a
different order — a defect already found once and recorded in `AdminMenu`'s own remarks.

> **Amended while building F2.** This originally said the tiebreak was "the authored plural", which
> is the same defect one level down: two dimensions sharing a `MenuOrder` would have sorted by
> caption, and so differently in each language. The tiebreak is the English name — ordinal, not
> culture-aware — because it is the one value that is the same for every reader. The test compares
> the two cultures' sequences **by code** rather than by caption, which is what makes "the same
> order" a statement with content rather than a comparison of two different lists of words.

`OrganisationMenuTenantTests` and `OrganisationMenuBrowserTests` currently assert *exactly three
entries, in order, in both languages*. They are rewritten, not deleted, to assert the new contract:
tree first, settings last, one entry per shown dimension, authored plurals, same order in both
languages, and **nothing pluralised by rule**.

> **Superseded in part while building F2, by decision D23 and ADR-0024.** Three things in this
> section are no longer what was built:
>
> - **The entry is per dimension *name*, not per dimension *type*.** Types sharing an English
>   singular name are one entry. The row above about "one entry per type" was necessary and not
>   sufficient — see the amended D17 note under §10.2.
> - **The menu's first item is the organisation designer**, unchanged and in the place it occupied
>   before this slice. F2 briefly moved it under Settings; D23 A put it back.
> - **Each entry expands**: *All &lt;plural&gt;*, then the dimension's own active values —
>   alphabetical in the reader's own language, at most fifteen, each suffixed with its organisation
>   when the entry spans more than one — then *Show all (N)*. Clicking a value opens that value's own
>   page. The list screen behind *All &lt;plural&gt;* gains an Organisation column and filter when
>   more than one type is grouped.
>
> What this section gets right and D23 keeps: the authored plural with its singular fallback, the
> `ShowInMenu` switch, explicit numeric positions, and the English-name tiebreak.

## 5.2 A dimension's Tree/List screen

**Click path:** Organisation → *Branch*.
**Also reachable from:** Organisation → Settings → Dimension types → the Branch row → *View units*.
That link is kept. It is where somebody configuring types already is, and removing it would make the
types list a dead end.

`DimensionRecordsAdminController.Index` already serves every type from one screen and already has
search, three filters, paging, a Parent column with full paths, Status with dates, headcount as a
link, and optional attribute columns. What is added:

- **An Alias column**, between Name and Status, per figure 4.
- **A Tree view**, as a second rendering of the same result, built from the dimension's own parent
  links — the `X → X` edges only. Roots are the values with no parent of their own dimension.
- **The toggle**, top-right, the pattern the designer already uses: server-rendered in both states,
  remembered in a cookie the server writes, so it works with no JavaScript. That matters more here
  than on the designer, because this is the screen an HR administrator lives in.
- **The five buttons.** *Filter* opens the existing filter block. *Add* is the existing New. *Edit*
  and *Delete* act on the selected row. *Refresh* re-issues the query — which is not cosmetic: this
  screen can be left open while a recipe or another administrator changes the data underneath it.

**Expand/collapse state is not persisted.** A dimension's own tree is shallow — two or three levels —
and persisting it is a cookie per dimension per user for no benefit a reader would notice.

## 5.3 The Add / Edit form

**Click path:** Organisation → *Branch* → **Add**.

Rendered as B3 draws it, **with one parent group rather than figure 8's stacked set (D19)**. Four
engineering notes:

- **One form, one write.** Saving calls `IDimensionService.AddUnitAsync`, which already writes the
  record and its placement together and validates them together, so a value can never exist having
  failed to reach its parent. The all-or-nothing property is what makes Rule 3 honest.
- **One group, and it is always a radio set.** Figure 8's numbered, stacked groups were a layout for
  an unknown number of parents; with one there is nothing to number and nothing to stack. The
  reasoning in §B3 that chose radios over the mock's checkboxes — *"pick exactly one from this group"
  is what a radio group means* — now has no exception, because the `OneOrMore` case that would have
  rendered as checkboxes is gone. A dimension with no mapping renders no block at all (Rule 4), with
  no empty heading left behind.
- **The group's options are the parent dimension's values**, filtered by `ContainmentRules.Decide`
  read forwards — the same decision function the validator reads backwards, which is ADR-0010's whole
  lesson. The picker never offers a parent the save would refuse.
- **Edit does not offer the mapping group.** Changing which Division a Branch belongs to is a
  **move**: it rewrites what every historical report under that branch resolves to, it needs
  `MoveDimensionRecords`, and it needs the dated dry run that already exists. The Edit form carries a
  link — *"Seef sits under Bahrain (Division). Move it…"* — to the move screen. This keeps the
  existing split between "edit a name" and "reorganise the company", which `Permissions.cs` exists to
  express, and D19 makes the sentence simpler: one parent, one link, no mapping to pre-select.

## 5.4 Delete (D4)

**Click path:** Organisation → *Branch* → select a row → **Delete**.

Renders B4's three outcomes from `AssessDeletionAsync`'s existing `Clean` / `RetireOnly` / `Blocked`.
The engine's assessment does not change; what changes is that `Blocked` is a screen state with an
action rather than a refusal. The write for the second and third is the existing `RetireAsync`,
including its three-way "what happens to the units underneath" question, which is already required
and already covered.

**ADR-0025 records the divergence from architecture §6.** The architecture's "Blocked, with the
specific blockers listed so the user can act" is kept word for word; what D4 adds is *the action to
act with*, on the same screen.

## 5.5 The Mapping settings screen

**Click path:** Organisation → Settings → **Mapping**.

This is today's `StructuresAdmin` editor, re-presented. Four blocks become three:

*Amended by D19 and D20: one parent per dimension, so the "what is required" question moves off the
grid and into a column of its own. No Job Title row.*

```
Mapping — Organisation                      [ axis: Organisation ▾ ]  ← only if >1 axis

  Which dimensions this organisation uses, in reading order
     Business Unit · Division · Department · Branch · Position · Grade
     [ + add a dimension ]

  Which may sit at the top
     ☑ Business Unit    ☐ Division    ☐ Department    …

  Each dimension's parent                          ← one row per dimension, one parent each
     ┌───────────────┬──────────────────┬────────────────────┐
     │ Dimension     │ sits under       │                    │
     ├───────────────┼──────────────────┼────────────────────┤
     │ Business Unit │ — (a root)       │                    │
     │ Division      │ Business Unit ▾  │ ◉ Required ○ Optional │
     │ Department    │ Division      ▾  │ ◉ Required ○ Optional │
     │ Branch        │ Department    ▾  │ ◉ Required ○ Optional │
     │ Position      │ Branch        ▾  │ ◉ Required ○ Optional │
     │ Grade         │ — (a root)       │                    │
     └───────────────┴──────────────────┴────────────────────┘
     The dropdown offers only dimensions the grid below permits as a parent.

  What may sit under what                          ← containment, ADR-0010, unchanged
     ┌───────────────┬──────┬──────┬──────┬──────┬──────┐
     │ parent ↓ child│ Div  │ Dept │ Brnch│ Posn │ Grade│
     ├───────────────┼──────┼──────┼──────┼──────┼──────┤
     │ Business Unit │ ☑    │ ☐    │ ☐    │ ☐    │ ☐    │
     │ Division      │ ☐    │ ☑    │ ☐    │ ☐    │ ☐    │
     │ Department    │ ☐    │ ☑    │ ☑    │ ☑    │ ☐    │   ← Department ☑ Department: self-nest
     │ Branch        │ ☐    │ ☐    │ ☐    │ ☑    │ ☐    │
     └───────────────┴──────┴──────┴──────┴──────┴──────┘
     [ build from a simple chain ]

  References — not parents, never on a tree (D21)
     Position  →  Grade      ◉ Required ○ Optional     ☑ allow a range (G7–G8)
```

**Two blocks rather than one state per cell**, and the reason is D19. With several parents the
required flag genuinely belonged on the cell, because each pairing carried its own obligation. With
one parent per dimension the question is about the *dimension*, not the pairing — "what does a Branch
sit under, and must it?" — and a per-cell marker would invite a reader to tick two and then be
refused. A row per dimension cannot express the invalid state at all, which is the better way to
enforce a rule than catching it afterwards.

**The references block is new, and is deliberately below and apart from the grid.** A grade is not a
level of the organisation (D21): it never enters the closure index, nothing rolls up through it, and
it is the one place `OneOrMore` survives — as *allow a range*, which is what D8 asks for. Putting it
in the grid would make it look like a parent, which is exactly the confusion D21 exists to prevent.

The containment grid is the existing one, unchanged. The existing rules carry over: it is
server-rendered first and kept in step by `structures.js`, so it works with no script; a post with no
ticks at all is read as "describe this as a chain" and the map is derived; a change that live
placements rely on previews its impact first (`PlanLevelChangeAsync`) and refuses outright, naming
the record at fault, if a live placement would be invalidated.

**Making a mapping required is itself a change that needs the dry run.** Ticking `Branch → Division`
required on a tenant with 40 branches, six of which have no division, must say so before it saves —
and then it is a *warning*, not a refusal, because those six are the unplaced panel's job and
refusing would make the rule unreachable for every tenant that has any untidy data. Rule 3 governs
new values; existing values are reported, not rejected.

## 5.6 The Organisation tree (the enquiry report)

**Click path:** Organisation → **Organisation tree**.

A **new controller and new views**, not a mode on the designer. The two screens answer different
questions, are gated differently and would fight each other inside one file:

| | Organisation designer (existing) | Organisation tree (new) |
| --- | --- | --- |
| Purpose | change the structure | read it |
| Scope | one axis, every type | the primary org axis, down to employees |
| Actions | add, rename, retire, move, merge, cancel, heads, place | **none** |
| Permission | `ManageDimensionRecords` or three others | a read permission — see below |
| Drill-in | expand/collapse, pan, zoom | **re-root** on the selected node, with Clear selection |
| Side panel | unplaced units | **View by**, with employee counts |

What it does:

- **Builds from the mappings, with no setup** (Rule 5): roots from `GetRootsAsync`, children from
  `GetChildrenAsync` — which after §2.2 returns children reached by *any* mapping — and employees at
  the leaf positions from `GetAssignmentsAtAsync`.
- **Lazily loads a branch as it opens**, which the designer already does and which is the only way
  this screen survives a 5,000-record tenant.
- **Draws a plain tree (D19).** Every node has one parent, so there is no already-drawn set to keep
  and no second edge to route — the whole of what the 12 October draft needed here is gone, along
  with the defect it existed to prevent. What stays is `DISTINCT EmployeeId` in the counting queries,
  for the different reason §2.5 gives: an employee may hold two positions.
- **Re-roots on click**, showing a breadcrumb (*Full Organisation › MENA Region*) and a **Clear
  selection** button. Re-rooting is a server round trip with the node id in the URL, so the view is
  linkable and the back button works — this is an enquiry report, and a report somebody cannot send a
  colleague a link to is half a report.
- **The View-by panel** lists Full organisation and then one row per dimension in the axis's reading
  order, each with a count computed as §2.5 sets out, and each row filters the tree to that
  dimension's level. The counts are **one batched query for the whole panel**, not one per row.
- **A sentence under the panel** explains why the rows can differ, as §2.5 requires.

**Permission.** The designer is gated on any of four permissions because a reader must be able to see
a chart with no action menus. This screen is read-only by construction, so it takes the same set and
nothing stricter — an eighth permission for "look at the organisation" is not justified, and the
auditor role (which holds only `ViewDimensionHistory`) must be able to open it. Employee leaves are
withheld from a viewer without `ViewEmployees`, asked of the module that owns it through
`IEmployeeLookup.CanViewEmployeeListAsync`, exactly as the designer's headcount link already does.

## 5.7 What happens to the designer and the "View units" link

**Both survive.**

- **The Organisation designer** moves to Organisation → Settings → Organisation designer. It keeps
  every dated operation — add, rename (both kinds), retire with its three-way child disposition,
  move with its backdated-move warning, merge, cancel move, appoint a head, place an employee — and
  keeps drag-to-move. Those operations are the product's safety net; deleting the screen to satisfy
  "No Data Entry!" would delete them, when what the FRS actually asks for is that the *enquiry
  report* has none. A reviewer who wants the designer removed should say so explicitly, because it
  would mean rebuilding seven dated operations onto the per-dimension screens.
- **"View units"** on each Dimension types row stays as written. It is one click from where somebody
  configuring a type already is, and the menu entry is a second way in, not a replacement.

A reader therefore has three routes to the same values — the menu, the types list, and the tree — and
all three land on the same two controllers. There is no fourth implementation.

---

# 6. Migration

## 6.1 The principle

Every change below is additive and append-only (ADR-0009). No shipped `UpdateFromNAsync` body is
touched; `CreateAsync` is updated only so a brand-new tenant gets the current schema in one pass.
Every schema change carries an upgrade test built on
`DimensionsMigrationUpgradeTenantTests` — create the tables as the earlier version left them, run the
migration, assert the result matches a fresh install, then **perform the real operation through the
real service**, which is the half of that pattern that catches what a column check does not. The
`TablesByVersion` table in that file gets a row for every new table.

`WorkMate.Dimensions` is at schema version **9** today (`CreateAsync` → 1, `UpdateFrom1`…`UpdateFrom8`
→ 9), so the next free step is `UpdateFrom9Async`. `WorkMate.Records` is at **5**, next free
`UpdateFrom5Async`.

## 6.2 The schema steps

| Step | Change | Why it is safe |
| --- | --- | --- |
| `UpdateFrom9Async` | `DimensionRecordPartIndex.AliasEn`, `.AliasAr` | Columns `CreateAsync` also produces, so guarded by the existing `ColumnExistsAsync` the same way `UpdateFrom3Async` and `UpdateFrom4Async` are. Existing rows get empty, which is a legitimate "no alias" |
| `UpdateFrom10Async` | `DimensionLinkIndex.ParentDimensionTypeId` | Same guard. Existing rows get empty = the unkeyed legacy slot, which is exactly what §2.2's null `MappingKey` means. **Nothing is back-filled**: deriving the key from the parent's current type would assert a mapping nobody declared |
| `UpdateFrom11Async` | `StructureIndex.MappingCount` | A counter column so the Mapping screen's list can say "7 mappings" without loading every document, mirroring `ContainmentRuleCount` from ADR-0010's migration |

`StructureDocument.Mappings` and `ParentLink.MappingKey` are **document** properties, not columns, so
they need no migration at all — a document written before them deserialises with the default, which
in both cases is the pre-FRS behaviour. That is the same mechanism that made
`EmployeeAttachableDimensionTypeIds` free.

**No data migration.** Nothing moves, nothing is rewritten, nothing is deleted. A tenant on today's
model after these three steps behaves **identically** until somebody opens the Mapping screen. That
is the property to test, and the upgrade test asserts it by resolving the whole Zenith and Crescent
closure on dates either side of every change, before and after.

## 6.3 Zenith, Crescent and any tenant on today's model

| | Before | After the three steps | After a customer configures the FRS model |
| --- | --- | --- | --- |
| Dimension types | 4–5 per tenant | unchanged | plus the shipped set they switch on |
| Structure | 1 org axis | unchanged, `Mappings` empty | mappings declared, some required |
| Links | one parent per record | unchanged, `MappingKey` null | new placements keyed; **old links never rewritten** |
| Closure | one path per pair | unchanged | more rows where a value gains a second parent |
| Employees | placed at departments and teams | unchanged | placed at positions; **old assignment rows kept, closed on the day the position model starts** |
| History | resolves | resolves identically — asserted | resolves: "where did Ahmed sit in March 2025" is still a department, because that is what the row says |

**Moving a live tenant onto positions is an operator tool, not a migration**, and it follows
architecture §8's four passes: *profile* (how many distinct job titles, how many people per
department, who has no job title), *propose* (one position per distinct title per unit, with
headcounts), *dry run* (every employee whose placement would move, with the date), *apply* (one
transaction, closure rebuilt and verified before commit). No migration ever runs without a signed
dry-run report — that is a process rule the architecture already sets and it applies here.

## 6.4 The shipped dimension set (C2 → ADR-0023)

FRS §2: *"The system already ships with a ready-made list of dimensions."* `base.recipe.json`
deliberately ships one.

**Recommendation: ship them, and ship them switched off.** The base recipe's objection — "a base
recipe that guessed would leave every tenant deleting three types before starting" — is answered by
the FRS itself: Rule 6 says a dimension is **renamed**, and D3 says an unused one is **hidden**.
Neither requires deleting anything. So:

- A new step in `base.recipe.json` creates all fourteen as `isSystemDefined: true`, each with an
  authored bilingual name **and plural**, a `menuOrder`, and `showInMenu` set only for the handful a
  starter tenant needs (Business Unit, Division, Department, Position). The rest are present, inert
  and one checkbox from visible.
- **It creates no structure, no mapping and no values.** What a customer calls their levels is still
  theirs; what is shipped is the vocabulary, not the shape. The primary `organisation` structure
  stays as it is: empty, with the comment it already carries.
- Re-running is safe per ADR-0008: an identical row is skipped, a divergent one fails naming the
  difference. A tenant that has already created its own `department` type hits the divergence and is
  told, rather than being overwritten.

The set, from figure 2, reconciled against D5/D6 and **D20**:

```
business-unit  division  department  branch  section  position  grade  grade-step
employees-group  projects  sponsorship  territory  warehouse
+ cost-centre (already shipped)
```

**Dropped**, with the reason recorded in the ADR:

| Row in figure 2 | Why it is not shipped |
| --- | --- |
| **Employees** | D6 — employees are records, not a dimension |
| **Position Title** | D20 — it is the Position record's own bilingual name |
| **Job Title** | **D20** — Position, Job Title and Designation are one concept, always *Position* |

**Added**, because §4.1.4 and figure 9 need it and figure 2 omits it: **Position** (D13). **Split
from Grade Steps**: `grade` and `grade-step` (D7), shown under one menu entry.

## 6.5 Demo recipes

`organisation-designer-zenith` and `organisation-designer-crescent` and their two employee recipes are
**extended, not replaced**, because `DemoCompanyRecipesTenantTests` and
`DemoEmployeeRecipesTenantTests` assert every tree parent by parent, both unit counts, the matrix on
one person, each head shape and a clean re-run, and those assertions are worth keeping.

| Recipe | Added |
| --- | --- |
| `…-zenith` | `position` and `grade`/`grade-step` types; `position` under **Department**, Required (D14); the `position → grade` **reference**, Required (D21); a Reporting structure carrying the position reporting line (§3.1); a dozen positions with approved headcounts; grades G5–G9 with five steps each |
| `…-crescent` | the same, with `position` under **Branch** (D14). ~~Plus the doubly-parented case~~ — **D19 removes it**; there is no Branch under both a Division and a Department, and nothing in the demos is doubly parented. What Crescent still demonstrates, and is the point of it, is a **ragged** tree: Departments under one Division, Regions under its sibling, Departments again four levels down |
| `…-zenith-employees` | each person's assignment repointed at a position; the matrix preserved (primary at a departmental position, secondary at a team position); a dated step each |
| `…-crescent-employees` | the same. ~~Plus one employee under the doubly-parented branch~~ — gone with D19. The counting case that still matters is the **Zenith matrix**: one engineer with a primary position in their home department and a secondary in a site team, which is what `DISTINCT EmployeeId` is actually protecting against (§2.5) |

`tools/generate-demo-employees.sh` is where the figures are edited and stays the one place.

A **third** demo recipe is proposed: `organisation-designer-frs-demo`, which builds exactly figure 9 —
KKT Group / Cayman HQ / MENA Region / Finance / Retail Network / Seef Branch / Retail
Supervisor / Ahmed Salman and Fatima Noor. Small, named for what it is, never referenced by
`base.recipe.json` or any edition, and it lives in the module's own `Recipes/` folder so it appears on
`/Admin/Recipes` — per `recipes/demo/README.md`, which explains at length why the repository root does
not work for this. It is what a reviewer runs to compare the screen against the FRS page by page.

## 6.6 Recipe steps and export (ADR-0008, ADR-0011)

| Step | Change |
| --- | --- |
| `dimension-types` | `pluralEn` / `pluralAr`, `showInMenu`, `menuOrder`. Absent means "not stated" and leaves the tenant's value alone, which is ADR-0008's existing rule and the reason `isActive`/`sortOrder` are nullable in the lookup-list step |
| `structures` | a `mappings` block: child code, parent code, cardinality. Stating `mappings` together with a chain-shaped `levelTypeCodes` is **permitted** (they describe different things); stating it twice is refused naming the conflict, as `containment` + `allowSkipLevel` already is |
| `dimension-records` | `aliasEn` / `aliasAr`, shipped in F1. **A placement keeps its single `parentCode`** — the 12 October draft turned it into a list of `{ parentCode, mappingTypeCode }`, and D19 removes the reason, so every shipped demo and every customer file stays valid because nothing about the format changes |
| `employee-assignments` | unchanged. A position is a record and a placement is a placement |
| Export (`DimensionsDeploymentStep`) | emits the new fields. The **records-parents-first** walk is **unchanged** — the draft made it a topological order over every parent on every mapping, and with one parent the existing walk over every parent a record has ever had is already that order. `DimensionExportRoundTripTenantTests` keeps comparing closure on dates either side of every change, which is what it exists for; it gains the Reporting axis rather than a doubly-parented branch |

ADR-0011's rule is untouched: **export writes the import format**, so there is one description of a
tenant's organisation rather than two that can drift.

---

# 7. Interactions

## 7.1 The form designer, B1/B2 and B3–B7

| | Effect |
| --- | --- |
| **B1 lookup lists** | Untouched as a mechanism. ADR-0014 stands |
| **B2 binding** | `grade` is unbound from `JobDetails.GradeCode` and `job-title` stays unbound. Both lists remain, per §4.4. The `WorkMateLookup` editor, the fallback-to-free-text rule and the unmatched-value preservation are all untouched |
| **D5's list vs B2's shipped list** | D5 names *nationality, document type, employment type, work location, country, currency, relationship, contact type* (plus, in the decisions file, religion, marital status and bank). B2 ships ten, which is D5's first eight plus `grade` and `job-title`. After this work the shipped set is **D5's eight**, with `grade` and `job-title` retained but unbound and marked in the README as superseded by the dimensions. *Religion, marital status and bank are not shipped and never were* — a divergence worth a line in the next product-owner review rather than a silent addition here |
| **B3 the definition model** | Unaffected. The form designer never gains a "position picker": the session-B rule — *"the form designer must never be the place a cross-record relationship is invented"* — is what kept this open, and positions now have a service and a dated model, so a **dimension picker field** becomes buildable. It is named in B's own "after session B" list and stays there |
| **B4–B7** | Unaffected. B4's screens, B5's rule language, B6's recipe step and B7's request forms touch none of this |
| **The Sections screen** (`/Admin/Employees/Sections/Edit/{id}`) | The Job details section loses two fields. The `ContentPart-Employee-BagPart.Edit.cshtml` alternate is already generic over sections and needs no change; `placement.json` needs no change |

**Ordering.** Nothing here blocks B3, and B3 does not block anything here. If both are in flight, the
one interaction to watch is `EmployeeContentDefinitionGuard`: it must continue to refuse any
designer-defined field whose name looks like placement (`PositionCode`, `GradeCode`, `DepartmentCode`),
and the FRS strengthens rather than weakens that rule.

## 7.2 ADR-0019 — cache invalidation after the commit

Three things this work adds are cached or should be, and all three take the existing rule:
**`CacheSignals.SignalNowAndAfterCommitAsync`, never `ISignal.SignalTokenAsync` directly.**
`CacheInvalidationSignalTests` already fails the build on a direct call, in all three modules.

| | Cached? | Invalidated by |
| --- | --- | --- |
| `Mappings` on the structure | yes — it is on `StructureDocument`, already cached by `CachedStructureLookup` | the existing structures signal, on `StructureService` writes. **Free** |
| The menu's dimension entries | yes — they are `DimensionTypeDocument` fields, already cached | the existing types signal. **Free**. This is also what makes Rule 6 ("renaming updates everywhere") instant rather than requiring a restart |
| The combined tree and View-by counts | **no** | Dated graph queries are deliberately uncached until a performance run says otherwise, for the stated reason: an under-invalidated dated query returning yesterday's shape is a correctness risk, and nobody has measured the cost yet. The View-by counts are the most likely first candidate — and the right trigger for caching them is a number from a seeded run, not a hunch |

The write-path rule applies unchanged: a write must load the document it is about to mutate through
the **plain, uncached** lookup, because YesSql's identity map throws if a write hands it a different
instance with the same id.

## 7.3 Permissions and entitlement

**No new permission.** Seven is still seven, and each new capability maps onto an existing one:

| Act | Permission |
| --- | --- |
| Create / edit a value, including choosing its mapped parents on the Add form | `ManageDimensionRecords` |
| Change an existing value's mapped parent | `MoveDimensionRecords` — it is a move |
| Delete or make inactive | `ManageDimensionRecords` (and the retire path's existing checks) |
| Declare or change a mapping, show/hide a dimension in the menu | `ManageStructures` / `ManageDimensionTypes` — both security-critical, both already so |
| Assign an employee to a position; set a grade step | `AssignEmployees` |
| Open the Organisation tree | the designer's existing four-permission set |
| Resolve the tree as at a past date | `ViewDimensionHistory` |

**Entitlement.** `WorkMate.Dimensions` declares no capability constants today. Positions, approved
headcount and vacancies are the first thing in this module plausibly above the Essential edition.
**That mapping is a commercial decision and is not taken here** — the same position `WorkMate.Records`
took for `FormFieldConfiguration`. The design's obligation is to leave the seam: declare
`Capabilities.OrganisationMapping` and `Capabilities.PositionManagement` in the module, call
`IEntitlementService.IsAllowedAsync` in the services (never only in a controller, per rule 5), and
record in the README that nothing is refused until the packaging table is confirmed.

## 7.4 Audit trail

Category `Dimension` has four mandatory events. The new facts fit three of them and need **one more**:

| Event | Carries |
| --- | --- |
| `DimensionTypeChanged` (existing) | the plural, the menu flag and order |
| `StructureChanged` (existing) | the mappings and their cardinalities, before and after |
| `DimensionRecordChanged` (existing) | the alias; a mapped parent added, changed or removed, named with the mapping and the date |
| **`DimensionValueDeleted`** (new, mandatory) | D4's real delete is the one operation in this module that **destroys** a record. `MoveCancelled` is the precedent for an operation that needs a reason recorded with the acting user, and a hard delete needs it more: after it, nothing else anywhere says the value existed |

## 7.5 Bilingual EN/AR

Every new user-visible string goes through the localiser; every new name field is bilingual.

- **Alias is bilingual** — `AliasEn` / `AliasAr`. CLAUDE.md rule 7 says every name field is bilingual
  and an alias is a name. The mock shows one box; two is cheap and the Arabic half is optional per
  ADR-0003's addendum, so an English-only tenant sees no change. Display falls back through
  `BilingualText.Display(en, ar)`, which is the one implementation.
- **The plural is bilingual and authored**, which is §5.1's whole point.
- **Dates on the wire are ISO-8601 and invariant** (`Internal/IsoDate.cs`); dates on screen are the
  reader's culture; names in sentences go through `BilingualDisplay`. The combined tree mirrors under
  `dir="rtl"` using CSS logical properties, as the designer already does — and the tree is the screen
  where that matters most, because it is the one with directional connectors.
- **Menu order is identical in both languages**, asserted by test, because explicit numeric positions
  are used and `PrefixPosition()` is not.

---

# 8. Cleanup to schedule

## 8.1 `EmployeeRecord` means two different things

**The defect.** `IEmployeeService` returns `EmployeeRecord` from three reads, and the record is
populated two different ways:

```csharp
// EmployeeService.cs:1044 — GetAsync, from the content item
private static EmployeeRecord ToRecord(string employeeId, EmployeePart part) => new(
    …, part.DateOfBirth, part.NationalityCode, part.Gender, …, part.PhotoPath);

// EmployeeService.cs:1068 — GetByCodeAsync and ListAsync, from the index row
private static EmployeeRecord ToRecord(EmployeeIndex row) => new(
    …, DateOfBirth: null, NationalityCode: string.Empty, Gender: Gender.Unspecified,
    …, PhotoPath: string.Empty);
```

The reasoning is sound and is written down on the method — those fields are sensitive, a list must not
load a content item per row, and `GetAsync` is where `ViewEmployeeSensitiveData` gets its chance to
apply. **What is wrong is the type.** A caller holding an `EmployeeRecord` cannot tell which kind it
has, and `NationalityCode == ""` is indistinguishable from "this person has no nationality recorded".
The failure is silent and plausible: a report grouping by nationality comes out with everybody in
"unknown" and nothing says why. This becomes more likely as the FRS work adds screens that mix list
reads and detail reads on one page.

**Proposed fix.** Split the type, which makes the distinction unignorable:

```csharp
public sealed record EmployeeSummary(      // from the index — a list, a picker, a card
    string EmployeeId, string Code, string NameEn, string NameAr,
    DateOnly JoinDate, EmploymentStatus Status, DateOnly StatusEffectiveFrom,
    string LineManagerEmployeeId);

public sealed record EmployeeRecord(       // from the content item — the full core
    EmployeeSummary Summary,
    DateOnly? DateOfBirth, string NationalityCode, Gender Gender, string PhotoPath);
```

`ListAsync` and `GetByCodeAsync` return `Page<EmployeeSummary>` / `EmployeeSummary?`; `GetAsync`
returns `EmployeeRecord?`. The compiler then finds every caller that wanted a sensitive field from a
list read — which is the point, and which no amount of documentation achieves.

**Cost:** it touches every call site in `WorkMate.Records` and the handful in `WorkMate.Dimensions`
that resolve employees for cards and pickers. Behaviour-neutral, mechanical, and reviewable on its
own.

**Which slice:** **F6**, where the employee screens are already being changed to show derived org
fields. Doing it there means one review of the employee read surface instead of two. It is small
enough not to need a slice of its own and too noisy to bury in a slice that is not already touching
these files.

## 8.2 The intermittent browser-test flake

**First, honestly: nothing in the repository records an open flake from the last session.** The two
browser flakes that are written down — `NoCardClipsItsNameOrItsCode` (prompt 3) and the
double-fetch/picker races (prompt 4) — are both recorded as diagnosed and fixed, and no test is
skipped, retried or marked. So the diagnosis below is **inferred from reading the suite**, and the
product owner should confirm which test it was before anyone acts on it (**Q-i**).

**The strongest candidate, and it is a real defect either way.** Every browser test that navigates by
menu opens the admin group with a helper of this shape:

```csharp
// tests/WorkMate.Browser.Tests/LookupListClickBrowserTests.cs:265
private static async Task OpenPeopleGroupAsync(IPage page)
{
    var link = page.Locator("a[href*='/Admin/Forms/Index']");
    if (await link.IsVisibleAsync()) return;                    // ← does not wait

    var group = page.GetByRole(AriaRole.Button, new() { NameString = "People" }).First;
    if (await group.CountAsync() > 0 && await group.IsVisibleAsync())  // ← does not wait
        await group.ClickAsync();
}
```

`IsVisibleAsync` and `CountAsync` are Playwright's **immediate, non-retrying** checks. Every other
assertion in the suite uses `Assertions.Expect(...)`, which auto-waits. So this helper asks "is the
menu open yet?" at whatever instant the page happens to be in. If the admin sidebar's own script has
not finished running, both checks are false, the helper clicks nothing and returns, and the **next**
line — an auto-waiting `ClickAsync` on a link inside a still-collapsed group — waits for an element
that will never become visible and fails on timeout. Whether it fires depends on machine load, which
is the signature of an intermittent failure, and the two documented prompt-4 flakes were both found
"by running the browser suite under load".

The same shape appears in seven files:
`LookupListClickBrowserTests` (:269, :276), `OrganisationMenuBrowserTests` (:143, :147),
`DimensionTypeUnitsBrowserTests` (:105), and the four designer suites' `Node(...)` helpers
(`ArabicOptionalBrowserTests` :305, `DesignerActionsBrowserTests` :349,
`DesignerMenuDismissalBrowserTests` :238, `DesignerMoveMergeBrowserTests` :557).

**Proposed fix.** One shared helper, in one place, that waits:

```csharp
// AdminMenu.cs in the browser test project — one implementation, every suite calls it
public static async Task OpenGroupAsync(IPage page, string groupName, string entryHref)
{
    var entry = page.Locator($"a[href*='{entryHref}']").First;

    // Wait for the sidebar itself, not for an arbitrary instant.
    await Assertions.Expect(page.Locator("#ta-left-sidebar")).ToBeVisibleAsync();

    if (await entry.IsVisibleAsync()) return;

    await page.GetByRole(AriaRole.Button, new() { NameString = groupName }).First.ClickAsync();
    await Assertions.Expect(entry).ToBeVisibleAsync();   // the group really did open
}
```

Two properties matter and neither is cosmetic. It waits for the sidebar before asking anything, so
the question is asked at a defined moment rather than a racy one. And it **asserts the group opened**
— so when it does fail, it fails saying "the People group did not open" instead of "timed out waiting
for a link", which is the difference between a five-minute diagnosis and the one that cost prompt 3 a
review cycle.

This is the same defect shape the last three sessions each found once and each wrote down: *two places
that had to agree by hand, fixed by making it one list with two readers.* Here it is one helper
copied into seven files.

**Which slice:** **F1**, the first. It is cheap, it is in the suite that is about to grow by a
dimension-tree test, a mapping test, a delete test and an enquiry-report test, and a flaky helper
multiplied by four new suites is a build nobody trusts.

---

# 9. ADRs

New numbers start at **0020**: 0014 and 0019 are taken, 0015–0018 are reserved by the session-B
design. Each would be written at the start of the slice that implements it, not retrospectively.

**~~ADR-0020 — A value may have several parents at once: an axis is a directed acyclic graph.~~
WITHDRAWN (D19), before any of it was built.** It was written to honour FRS §3 and Rule 3 —
*"mapped to one parent dimension, or to more than one"* — and the product owner has overruled that
text rather than the recorded decision it conflicted with. So **ADR-0005 stands as written and was
never departed from**: one parent per record per axis, one path per closure pair, no `MappingKey`.
Conflict **C1 is withdrawn, not reconciled**, and slice F4 is deleted. The ADR file is kept rather
than removed, for the reason it gives in its own withdrawal note: the expensive finding in it — that
ADR-0005's storage already generalises, because `DimensionClosureDocument.Ancestors` is a list whose
own comment permits one ancestor at several depths — is the part worth having if multi-parent is ever
asked for again, and the costs it weighed are the reasons the single-parent model is the better
trade. One thing from it is kept and built: the `DISTINCT EmployeeId` guarantee, which earns its
place without a DAG because an employee may hold two positions.

**ADR-0021 — Mapping is containment plus a cardinality, and lives on the structure.**
The FRS's "Branch is mapped to Division and Department" is ADR-0010's containment map read
child-first; what containment cannot say is that a Branch *must* name one of each (Rule 3).
`StructureDocument` gains `Mappings` — `(child type, parent type, cardinality)` — kept separate from
`Containment` because a self-nesting diagonal is a permitted pairing and never a mapping, because a
pairing may be permitted without being required, and because figure 8's numbered groups need an order
containment has no place for. Empty means every pairing is optional, which is exactly today's
behaviour, so no existing tenant changes. Rule 3 is enforced in `IDimensionValidator` so the Add form,
the recipe step and any future API are refused in the same words — and it governs the **Add form**,
not the existence of a row: an unmapped value from an import goes to the unplaced panel with a reason
rather than failing the import. Amends ADR-0010.

**ADR-0022 — Position is the one master, and an employee joins the organisation by holding one.**
The FRS's feedback note says Org Role and Position are one master; D2 says employees join by a dated
position assignment and that division, department, cost centre and grade are derived. Position is
modelled as a **dimension type** — no new aggregate, no new table, no new service — with its reporting
line on a **second structure** (§3.1 — D19 gives a value one parent per axis, so the reporting line
and the org placement are two axes, which is what ADR-0005 scoped links to a structure for) and its
org placement under a Branch or a Department. The employee's hold on it is an ordinary `EmployeeAssignment`: dated, allocatable,
main/additional, many-per-position, already enforcing "exactly one primary" and "allocations total
100". `EmployeeAttachableDimensionTypeIds = [position]` is the single line that makes every picker and
every placement screen offer positions and nothing else. Approved headcount is an attribute on the
shipped Position type and is therefore undated, which is a stated limitation (Q-i), not an oversight.
The employee record still holds no placement field; the hard boundary is untouched.

**ADR-0023 — WorkMate ships the pre-configured dimension set, switched off rather than absent.**
FRS §2 says the system ships a ready-made list of dimensions; `base.recipe.json` deliberately ships
`cost-centre` only, reasoning that "a base recipe that guessed would leave every tenant deleting three
types before starting". The FRS answers its own objection: Rule 6 renames and D3 hides, so nothing has
to be deleted. The base recipe creates all of them as system-defined with authored bilingual names and
plurals and `showInMenu` set for a starter few, and **creates no structure, no mapping and no values**
— the vocabulary ships, the shape does not. *Employees* is dropped (D6: employees are records) and
*Position Title* is dropped (it is the Position record's own name, Q-c); *Position* is added, which
figure 2 omits and §4.1.4 requires. Re-running stays safe under ADR-0008: a divergent row fails naming
the difference rather than overwriting what a customer has changed.

**ADR-0024 — A dimension's menu entry comes back, with the three things that killed it fixed.**
Per-type menu entries shipped in prompt 4 session D and were withdrawn by the product owner on sight;
D3 and FRS §1.2 require them. `ShowInMenu`, `PluralName` and `MenuOrder` are still on
`DimensionTypeDocument`, written by the frozen `UpdateFrom7Async` and read by nothing, so this revives
rather than re-invents. Three fixes: the plural is **authored** in both languages instead of guessed
by appending "s" (which produced *Branchs*, and nothing at all in Arabic); there is **one entry per
dimension type** rather than one per type per structure (which produced *Departments, Departments,
Departments*); and `ShowInMenu` is a real switch on the type editor, so a tenant hides what it does
not use. Positions stay explicit numbers, never `PrefixPosition()`, which orders by the caption and so
gives an Arabic reader a different order. `OrganisationMenuTenantTests` is rewritten to assert the new
contract in both languages, not deleted.

> **As written, 13 October 2026.** The middle fix above was necessary and not sufficient: one entry
> per dimension *type* still reads *Departments, Departments, Departments* on a tenant that holds
> three Department types, which the demo tenant does. **Decision D23** makes it one entry per
> dimension **name** — types sharing an English singular name are grouped — and adds the rest of the
> menu's shape: each entry expands to *All &lt;plural&gt;*, its own values and *Show all (N)*, a
> value opens its own page, and the organisation designer stays at the top where it was. The ADR as
> written carries all of that.

**ADR-0025 — Delete deletes, and a used value is made inactive with the reason on the screen.**
D4 gives Delete two outcomes; architecture §6 gives deletion three, of which the third — referenced by
live data — is **blocked**. The assessment is kept exactly as it is, including the rule that the
blockers are always named and never a generic failure; what changes is that *blocked* becomes a screen
state carrying the next action ("make inactive from…") rather than a dead end. The reason is D4's own:
a refusal a user cannot act on is what the decision is written against. Retirement keeps its existing
three-way question about the units underneath, on every structure the record sits on. A real delete
becomes the one operation in this module that destroys a record, so it gains a mandatory audit event
with a reason and the acting user — the same standard `MoveCancelled` already holds.

**ADR-0026 — Grades and steps are dimensions; the employee's step is a dated assignment.**
**No job titles (D20).** D5 makes Grade Steps a dimension, which supersedes slice B2's binding of
`JobDetails.GradeCode` to the `grade` lookup list. Nothing is migrated and nothing is deleted: ADR-0016
made binding an editor setting over a plain `TextField`, so unbinding is two settings and every stored
value stays readable, exportable and auditable while the field is retired from the editor (ADR-0009 —
a migration never destroys data). The `grade` list becomes the seed for an **operator-run import**, following
architecture §8's profile/propose/dry-run/apply, because what the right grades are is a business
question. The employee's dated step is an `EmployeeAssignment` on a **Grade axis** — which is both the
cheapest implementation and the clearest demonstration of why `Structure` survives the FRS. Q-a
(one dimension type or two) and Q-b (grade on the position or the person, and grade ranges) are
**open**; this ADR is written once they are answered, and the recommendations are in §4.

**ADR-0027 — reserved, and deliberately not written.** Q-e asks whether a unit head stays a dated
appointment (ADR-0012) or becomes a position. There is a real argument on each side — a head is a
post, which is a position; and ADR-0012 exists precisely because a head need not be a *member* of the
unit, which an assignment cannot express. **Not decided here**, per CLAUDE.md. Until it is answered,
ADR-0012 stands unchanged and §3.5 leaves the head screens alone.

**Addenda rather than new ADRs**, each one paragraph in the ADR it amends: **ADR-0005** — the
backdated-move split is now per mapping key, and the arithmetic is unchanged within a key.
**ADR-0008** — how `mappings` and the new `dimension-records` placement list compare on re-run, under
the existing "comparison is scoped to what the row actually states" principle. **ADR-0011** — the
export's parents-first walk becomes a topological order over every mapping. **ADR-0010** — the
relationship between `Containment` and `Mappings`, so a future reader does not collapse them.

---

# 10. Risks, open questions and the slice plan

## 10.1 Risks

**D19 closes the top three outright**, which is the clearest measure of what the single-parent model
buys: the two most dangerous risks in the list were both consequences of the DAG.

| | Risk | Mitigation |
| --- | --- | --- |
| ~~1~~ | ~~**The DAG breaks the 200ms descendant-resolution acceptance criterion.**~~ | **Closed by D19.** Closure rows no longer grow with the number of root-to-node paths, because there is one path. The acceptance criterion is measured against the same shape the engine already has |
| ~~2~~ | ~~**A doubly-parented value is drawn twice and every count below it doubles.**~~ Was "the most likely visible defect in the whole FRS, and both numbers stay plausible" | **Closed by D19.** No value has two parents, so no node is drawn twice. `DISTINCT EmployeeId` stays for the *other* double-counting source — an employee holding two positions (§2.5) — which D19 does not touch |
| ~~3~~ | ~~**Cycle detection has a wider surface.**~~ A→B by one mapping and B→A by another | **Closed by D19.** One parent, one path; the existing check and its existing tests cover it, and the per-mapping-pair property test is not needed |
| 4 | **Removing the Organisation designer would delete seven dated operations.** "No Data Entry!" is about the enquiry report | §5.7 keeps both and says so explicitly. A reviewer who wants the designer gone should say so, with the rebuild cost visible |
| 5 | **Rule 3 made retrospective would invalidate live data.** Ticking "required" on a tenant with untidy branches | Rule 3 governs the Add form, not existing rows (§2.6). The Mapping screen previews the impact and warns; it does not refuse |
| 6 | **The menu erodes again.** It was withdrawn once for exactly this | Authored plurals, one entry per dimension, a hide switch, and `OrganisationMenuTenantTests` rewritten rather than deleted. **Slice F2** |
| 7 | ~~Figure 2's list and D5/D6 disagree~~ | **Closed by D20**: Employees, Job Title and Position Title are all settled, and ADR-0023 records what was dropped and why |
| 8 | **Two slices change the employee read surface** (F6's derived fields, §8.1's type split) | They are the same slice, deliberately |
| 9 | **The Grade move touches live data in B2 tenants** | Nothing is migrated; the lists stay, the field is retired not removed, and the move is an operator tool with a dry run |
| **10** | **D19 forces the position reporting line onto a second structure**, and a tenant that never set one up has a Position screen with no tree | §3.1 and **Q-j**: decide whether the Reporting structure ships in the base recipe or is the tenant's to create. Either answer works; silently shipping neither is the failure |
| **11** | **D20's relabelling is spread across slices**, so a tenant mid-upgrade could see *Job title* on one screen and *Position* on another | One PO entry per label, changed in the slice that owns the screen (D22 step 6), and `LocalisationResourceTests` fails the build on an orphaned or missing msgid — so a half-done rename is a build failure rather than a screen somebody has to notice |

## 10.2 Open questions — **all answered 12 October 2026**

The five the brief names, plus four found while reading the PDF and the code.

> **Every question below was answered in the design review of 12 October 2026 and is recorded as
> D7–D18 in `docs/requirements/frs-v2.5-decisions.md`.** The "Recommendation" column is kept as
> written so the reasoning behind each answer stays readable; the **Answer** column is what is
> binding. Where the two differ, the answer wins.

| | Question | Answer | Recommendation as proposed |
| --- | --- | --- | --- |
| **Q-a** | Grade Steps: one dimension type, self-nesting (G7 → G7-S1…S5)? Or two types — Grade and Grade step — shown as one dimension? | **D7 — two types, one menu entry.** As recommended | **Two types, one menu entry** (§4.1). A depth rule breaks the first time a customer puts a band above their grades |
| **Q-b** | Is grade mapped to the position, or chosen per employee? May a position allow a range (G7–G8)? Are steps optional per tenant? | **D8 — position; ranges allowed via `OneOrMore`; steps optional by not configuring them; the dated step is an `EmployeeAssignment` on a Grade axis.** As recommended | **Position**, with `OneOrMore` giving ranges for free, and steps optional by not configuring them (§4.2) |
| **Q-c** | Job Title vs Position Title vs Position | **SETTLED AGAIN, FURTHER, BY D20 (13 Oct).** Position, Job Title and Designation are **one concept**, always *Position* / *المنصب*. No Job Title dimension, no Position Title dimension, no designation field. D9 kept two masters; D20 keeps one | **Position is the master; Job Title is a dimension positions map to; Position Title is the Position record's own name** (§4.3). If the row must stay visible, it ships hidden |
| **Q-d** | When a value has two parents and a report needs exactly one, which is primary? | **NOT APPLICABLE AFTER D19 (13 Oct).** A value has one parent, so every report that needs one already has it. `StructureMapping.IsPrimary` is not built | **Mark one mapping per child dimension as primary on the Mapping screen** — "a Branch's Division is its primary parent". Declared, not derived: deriving it from mapping order would make a screen's layout decide payroll attribution |
| **Q-e** | Unit heads (ADR-0012): keep as a dated appointment, or model "head" as a position? | **D11 — keep the dated appointment. ADR-0012 stands; revisit in prompt 5 when approval routing makes the cost of either answer visible.** ADR-0027 stays unwritten | **No recommendation — this is genuinely balanced.** A head is a post, which argues for a position; ADR-0012 exists because a head need not be a *member*, which an assignment cannot express. ADR-0027 is reserved for the answer |
| **Q-f** | May a value have several parents within **one** mapped dimension? | **SETTLED AT THE STRONGER LEVEL BY D19 (13 Oct).** A value has exactly one parent — not one per mapped dimension, and not two within one. Figure 3 is as loosely drawn as figure 9. `OneOrMore` survives only for a grade **reference** (D21), which is not a parent | The storage supports both (`OneOrMore`). **Please confirm which figure 9 means** — a mock drawn loosely, or a real requirement |
| **Q-g** | Figure 2's dimension list has **no Position row**, yet §4.1.4 shows a Position tree and figure 9 puts Position above Employee | **D13 — ship Position.** As recommended | **Ship it.** §4.1.4 and the feedback note both require it; figure 2 looks like an omission |
| **Q-h** | Figure 3 maps Position to **Branch**. Crescent has departments that are not under a branch, and Zenith has no branches at all. Is Position mapped to Branch, to Department, or to both (whichever the customer uses)? | **D14 — per tenant. Zenith maps Position → Department and Team; Crescent maps Position → Branch and Department. `AtLeastOneAcross` is not added unless a slice proves it is needed** | **Both, each Optional, with at least one required** — which is a cardinality `MappingCardinality` does not express today and would be a fourth member (`AtLeastOneAcross`). Only worth adding if the answer is "both" |
| **Q-i** | Two small ones. Does approved headcount need to be **dated** ("what was it last March")? And **which browser test was the intermittent failure** — nothing in the repository records one (§8.2) | **D15 — undated for now, stated as a limitation.** On the flake: one of three full-suite runs had a single browser test fail, and the test was not named in the report. The §8.2 helper fix lands in **F1** regardless, since it is a real defect on its own | Undated for the first slice, stated as a limitation. For the flake, the helper fix in §8.2 is worth doing regardless |

### Two questions D19 and D20 open — **for the product owner**

| | Question | Why it matters | Recommendation |
| --- | --- | --- | --- |
| **Q-j** | **Does the Reporting structure ship in the base recipe, or is it the tenant's to create?** D19 puts the position reporting line on a second axis (§3.1), because a position's one parent on the organisation axis is its Branch or Department | Ship it and every tenant gets a Reporting structure whether or not they use reporting lines. Do not ship it and the Position screen's tree view is empty until somebody builds one — on a screen FRS §4.1.4 draws as a reporting hierarchy | **Ship it**, on the same reasoning as ADR-0023's shipped dimension set: a tenant that does not use it hides it (D3), which is cheaper than a tenant that needs it discovering it has to invent one. Confirm, because it is a second structure in every tenant |
| **Q-k** | **D21 — is a grade ever a level of the organisation tree?** The design says no: a position *references* a grade, the reference never enters the closure index, and that is what lets a position carry a grade **range** | If grades should appear in the organisation tree, the reference becomes a parent — and then a position cannot have both a grade parent and a branch parent under D19, so it would need a third axis, and the range goes | **No — keep it a reference.** Recorded as D21, **pending your confirmation** |
| ~~**Q-l**~~ **ANSWERED (D24)** | **Does a structure need a short name for the menu?** D23 C suffixes a menu value with its organisation when a dimension spans more than one — the example given was *Civil (Zenith)*, and what F2 first built was *Civil (Zenith Engineering & Construction)*, the structure's authored name | A long company name in brackets on every value of a shared dimension makes the sidebar wide and the entries hard to scan. Nothing abbreviates it: a rule that shortens a customer's company name will shorten one wrongly, which is the same reasoning that stopped the plural being derived | **Yes — an authored `shortName` on the structure**, taken by the product owner as **D24 B**. `StructureDocument.ShortName`, optional and bilingual, on the structure editor, in the `structures` recipe step and in the export; empty falls back to the full name. The reference recipes ship *Zenith*, *Crescent* and *Demo* |

### One answer the design did not anticipate (D17) — **remedy superseded by D23**

The menu duplication that caused the per-type entries to be withdrawn — *Departments, Departments,
Departments* — **came from the demo data, not from the menu mechanism**: three demo organisations
each defined their own Department and Division dimension types in one tenant. §5.1 treats "one entry
per dimension type rather than per type per structure" as the fix, and that is still right, but it is
only half of it.

**As recorded 12 October 2026**, the other half was that the demo recipes must share the standard
dimension types rather than prefixing their own, so each dimension genuinely exists once.

> **Amended while building F2 (13 October 2026).** That was built and **reverted**. A dimension type
> is a *level of every structure that declares it*, and `CreateAsync` writes a self closure pair on
> all of them — so a shared `division` type put every Crescent division on the Zenith axis as a
> parentless root and the Zenith chart drew them. `DemoCompanyRecipesTenantTests` caught it. It is
> ADR-0005 working as designed; the demo tenant, holding two unrelated companies, is the odd case.
>
> A second attempt gave each recipe a company-qualified plural — *Zenith Departments* — which worked
> and was also wrong: a tenant should not read its own company name on every menu entry.
>
> **Decision D23 B is the remedy**: dimension types sharing an **English singular name** become one
> menu entry, and where that entry's values span several charts each value names its own. The demo
> recipes keep their prefixed type codes and author plain plurals. The test D17 asked for exists and
> is stronger — base + Zenith + Crescent + the demo sample, twelve types, **eight** entries.
>
> **D23 also replaces the menu's shape**, which §5.1 describes as it was first built. Each entry now
> expands to *All &lt;plural&gt;*, the dimension's own values and *Show all (N)*; a value opens its
> own page; and the **organisation designer is back at the top of the menu**, where it was before
> slice F2 moved it under Settings. **ADR-0024** carries the whole decision.

## 10.3 Slice plan

Each slice is one session: model, then services, then screens, then tests, stopping for review after
each — CLAUDE.md's working pattern and the build order the architecture already argued for. The order
below puts the smallest valuable thing first and keeps every slice independently reviewable and
independently shippable.

| | Slice | What lands | What a reviewer can click and see at the end |
| --- | --- | --- | --- |
| **F1** ✓ | **Alias, Status, and the dimension screen's two views** | `AliasEn`/`AliasAr` on the part and the index (`UpdateFrom9Async` + upgrade test); validator; recipe step and export fields; the Tree/List toggle; the Alias column; the five toolbar buttons. Plus the browser-helper fix (§8.2) | Organisation → Dimension types → *Department* → **View units** → a tree of departments with Code, Name, Alias, Status, and a toggle to a flat list. No model change beyond one column |
| **F2** ✓ | **The menu (D3, **D23**, ADR-0024)** | Authored bilingual plural, `ShowInMenu` and `MenuOrder` on the dimension type editor (`UpdateFrom10Async` un-guesses session D's plurals); `IDimensionMenuService` + `DimensionMenuComposition`, cached per tenant per day and invalidated by `MenuSignal` (ADR-0019); the menu provider — **designer first**, one **grouped** entry per dimension name, each expanding to *All &lt;plural&gt;*, its values and *Show all (N)*, then *Settings*; the list screen's group scope with its Organisation column and filter; `DimensionRecordsAdminController.Details`, one value's own page | Organisation → *Departments* → *Civil (Zenith …)* opens Civil's own page: code, names, alias, parent, status, head, headcount, with Edit / Move / Retire. *All Departments* lists both companies' departments with an Organisation column. The same order in Arabic, and the designer still first |
| **F3** | **Mapping, with one parent (ADR-0021)** | `StructureDocument.Mappings` (**one entry per child type**, enforced in `IStructureService`); `MappingCardinality` (**Required / Optional**); Rule 3 and Rule 4 in `IDimensionValidator`; the Mapping screen with its parent-per-dimension block, its Required/Optional column and its impact preview; **one** parent group on the Add form; `UpdateFrom11Async`; recipe `mappings` block | Settings → **Mapping** → set Departments parent to Division and mark it Required → Organisation → *Department* → **Add** → the form shows one Division group, a radio set, and refuses Save until one is chosen |
| **F5** | **The shipped dimensions and Position (ADR-0022, ADR-0023)** | The `dimension-types` step in `base.recipe.json`; the Position type with `approvedHeadcount`; the **Reporting structure** carrying the position reporting line (§3.1, Q-j); vacancies, batched; the Position list's three columns | A **new** tenant's Organisation menu already lists Business Unit, Division, Department and Position; Organisation → *Position* → a reporting tree CEO → COO → Regional Director, with Approved / Filled / Vacant columns |
| **F6** | **Employees join through a position** | `EmployeeAttachableDimensionTypeIds = [position]`; `GetOrgPlacementsAsync`; the employee record's derived block; the list's position column; the placement screens' vocabulary. **Plus the `EmployeeSummary` split (§8.1), the employee leaves on F8s tree, and D22 — Job title becomes Position, the `job-title` list retired, the labels renamed in EN and AR** | People → Employees → a person → their position, and under it the division, department and cost centre, none of them typed; changing the position changes all of them; last year's record still shows last year's. Organisation → Organisation tree now reaches Ahmed Salman and Fatima Noor under Retail Supervisor |
| **F7** | **Grades and steps (ADR-0026)** — **no job title** (D20) | The Grade axis; the `position → grade` **reference** with its optional range (D21); the employees dated step; unbinding `grade`; retiring `GradeCode` from the editor; the operator import tool with its dry run | Organisation → *Grade Steps* → G7 with five steps; a position graded G7; an employee on G7-S3 from January, with G7-S2 still showing for last year |
| **F8** | **The Organisation tree enquiry report (FRS §5)** — *runs after F3* | The new read-only controller and views; re-root with breadcrumb and Clear selection; the View-by panel and its batched counts; the `…-frs-demo` recipe. **Employee leaves are F6** | Organisation → **Organisation tree** → figure 9's unit levels on screen, with the FRS demo recipe applied; click MENA Region and the chart narrows; Clear selection restores it; the panel's counts match the FRS's |
| **F9** | **Demo recipes, export and D4** | Zenith and Crescent extended with positions and grades — **no doubly-parented branch** (D19); `DimensionExportRoundTripTenantTests` extended; the Delete screen's three outcomes (ADR-0025) and the new audit event | Apply the Zenith recipe to a fresh tenant and the whole model is there; export it, import it into a second tenant, and every historical date agrees; press Delete on a used value and get a sentence and a date box |

**Order, as revised by D18 after D19: `F1 ✓ → F2 → F3 → F8 → F5 → F6 → F7 → F9`.**

**F4 is deleted.** It was the DAG and nothing else, and D19 removes the DAG. Nothing moves to fill
the gap: F8 simply follows F3, which it always could have — it needed a tree to draw, not a graph,
and the single-parent tree exists the moment F3's mapping does.

**F8 still ships without employee leaves**, and **F6 still adds them** when employees start joining
through a position. The reasoning is unchanged: the combined tree is the single most persuasive
screen in the FRS and there is no reason to hold it behind positions.

**F7 was gated on Q-a and Q-b**, which are answered (D7, D8), so the gate is lifted. D20 narrows it
to grades and steps.

**Three slices got smaller and one got a little larger.** F3 loses the per-parent grid state and the
numbered groups; F8 loses the already-drawn set and the two-incoming-edge renderer; F9 loses the
doubly-parented demo branch. F6 gains D22 — the job-title move and the relabelling — which belongs
there because F6 is already the slice that changes the employee read surface and the job details
section.

**If fewer slices are wanted:** F2 and F3 merge cleanly, and F8 and F9 do. The 12 October note that
F3 and F4 must not merge is moot.

---

## Review outcome — 12 October 2026

All five asks were answered. Recorded as **D7–D18** in `docs/requirements/frs-v2.5-decisions.md`.

1. **Q-a, Q-b, Q-c, Q-f, Q-g** — answered, all as recommended (D7, D8, D9, D12, D13).
2. **Q-d, Q-e, Q-h, Q-i** — answered (D10, D11, D14, D15). Q-e keeps ADR-0012 as it stands and moves
   the question to prompt 5; Q-h declines `AtLeastOneAcross` until a slice proves it is needed.
3. **C1–C4 confirmed** (D16).
4. **Slice order approved with F8 moved to after F4** (D18).
5. **ADR-0020 and ADR-0021 written**, both *accepted, pending second review*.

One thing the design did not anticipate came back with the answers and is now **D17**: the menu
duplication came from the demo data rather than the menu mechanism. See the note under §10.2.

---

## Review outcome — 13 October 2026, and where the plan now stands

**F1 is built and committed.** Alias, the Tree/List views, the toolbar, the shared admin-menu test
helper, `UpdateFrom9Async` and its upgrade test.

**Two product decisions then overruled FRS text**, which is the first time that has happened on this
work. They are **D19** and **D20**, each quoting what it overrides, and the banner at the top of this
document lists everything they change. In short:

| | | Effect here |
| --- | --- | --- |
| **D19** | A dimension value has exactly one parent | **F4 deleted**, §2.2 withdrawn, ADR-0020 withdrawn, C1 withdrawn, risks 1–3 closed. F3, F8 and F9 all get smaller |
| **D20** | Position = Job Title = Designation, one concept | No Job Title or Position Title dimension; §4.3 settled; F7 narrowed to grades and steps; **F6 gains D22** — the job-title move and the EN/AR relabelling |
| **D21** | A position's grade is a **reference**, not a parent | *Pending your confirmation* (**Q-k**). It is what keeps a grade range expressible and keeps grades off the organisation tree |

**Two new questions**, both in §10.2 and both from D19's consequences: **Q-j** (does the Reporting
structure ship in the base recipe?) and **Q-k** (D21's confirmation).

**Build order from here: `F1 ✓ → F2 → F3 → F8 → F5 → F6 → F7 → F9`.**
Next is **F2** — the menu, ADR-0024 — and F2 only.
