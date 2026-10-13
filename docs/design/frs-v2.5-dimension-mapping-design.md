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
flagged and reconciled by a new or amended ADR rather than absorbed quietly. There are four.

| | The FRS says | We decided | Proposed reconciliation |
| --- | --- | --- | --- |
| **C1** | A Branch belongs to one Division **and** one Department at the same time (§3, figure 3, Rule 3) | A record has at most one parent per axis; several parents means several axes (ADR-0005, architecture decision 4) | **ADR-0020** — one axis becomes a directed acyclic graph. The storage already supports it; the arithmetic and three service methods do not |
| **C2** | The system ships a ready-made list of 14 dimensions (§2, figure 2) | `base.recipe.json` ships **Cost centre only**, on the stated reasoning that "what a customer calls their levels is theirs to decide, and a base recipe that guessed would leave every tenant deleting three types before starting" | **ADR-0023** — ship them, because the FRS's own Rule 6 (rename) and D3 (hide) are the answer to that objection: a tenant renames or hides rather than deletes |
| **C3** | Every dimension is its own menu item (§1.2, D3) | Per-type menu entries shipped in prompt 4 session D and were **withdrawn by the product owner on sight** — the menu read *Branchs, Cost centres, Departments, Departments, Departments, Divisions…* | **ADR-0024** — bring them back, with the three things that caused the withdrawal fixed: an authored bilingual plural (no suffix guessing), a fixed order, and one entry per *dimension* rather than per type-per-structure |
| **C4** | Delete is on the toolbar; a used value becomes Inactive and the user is told why (D4) | Architecture §6 has three outcomes — Clean (delete), Referenced-by-history-only (retire), Referenced-by-live-data (**blocked**) | **ADR-0025** — keep the three-way assessment, change what the screen does with the third: offer "make inactive from…" as the next action instead of a refusal with no way forward |

Three smaller divergences are recorded in the gap table (A6, A7, B4) rather than here: the Alias
field, the "Position Title" entry in figure 2, and the figure 9 node with two parents of the *same*
dimension.

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
│               ┌─────▼─────┐      ┌────────▼┐  │                              │
│               │  Finance  │      │  Retail │  │  CURRENTLY VIEWING           │
│               └─────┬─────┘      └────┬────┘  │   MENA Region                │
│                     └──────┬──────────┘       │   118 employees in this      │
│                    ┌───────▼────────┐         │   branch                     │
│                    │  Seef Branch   │ 2 parents                              │
│                    └───────┬────────┘         │                              │
│                   ┌────────▼────────┐         │                              │
│                   │Retail Supervisor│Position │                              │
│                   └────────┬────────┘         │                              │
│            ┌───────────────┴────────┐         │                              │
│      ┌─────▼──────┐          ┌──────▼─────┐   │                              │
│      │Ahmed Salman│ Employee │Fatima Noor │   │                              │
│      └────────────┘          └────────────┘   │                              │
└───────────────────────────────────────────────┴──────────────────────────────┘
```

Three things the mock implies and this design makes explicit:

- **Seef Branch is drawn once, with two incoming edges.** Not twice. Drawing it twice would make
  every count below it double, which is precisely what D1 exists to prevent.
- **The counts in "View by" are per dimension.** The *Department* row counts every employee whose
  position rolls up to some department, counting each person once; the *Branch* row does the same
  for branches. Two rows can legitimately disagree (not everybody is under a branch). The
  *Full Organisation* row is unique employees across the whole chart. That is D1, stated.
- **Employees are leaves and are not a dimension** (D6). They come from the employee record and
  appear under their position.

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
| A5 | Step 2 — the record maps to parent nodes in *other* dimensions | Containment permits it; a record can hold **one** parent per structure | `DimensionLinkDocument.ParentOn` returns a single `ParentLink` | **Conflict (C1)** — ADR-0020 |
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
| **3** Mapped to >1 parent ⇒ one value from **each** before save | Containment permits a parent; it never **requires** one. `AddUnitAsync` happily creates a unit with no parent (that is the "unplaced" state, a deliberate feature) | **Missing** — needs cardinality, ADR-0021 |
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
| **D5b** | Position mapped to a grade; employee holds a dated step | — | **Missing** |
| **D5c** | Job Title is a dimension positions point to | `job-title` ships as a lookup list **bound to nothing**, deliberately, "because a code-keyed job title is a position by another name" | **Partial** — the session-B design predicted this exact move and left the door open |
| **D5d** | Lookup lists remain for non-organisational values | Ten lists ship; D5's list and B2's shipped list differ slightly — see §7.1 | **Done**, with one reconciliation |
| **D6** | Employees stay employee records and appear as leaves of the org tree | They are employee records; they are not drawn on any tree | **Partial** |

## 1.4 Things the FRS needs that nothing in it mentions

| | | Verdict |
| --- | --- | --- |
| Alias, as a stored field | `DimensionRecordPart` has eleven standard fields and none of them is an alias | **Missing** — new column, new migration, new upgrade test |
| A pre-configured dimension set | `base.recipe.json` ships `cost-centre` only, deliberately | **Conflict (C2)** |
| "Position Title" as a dimension | Figure 2 lists *Position Title* and *Job Title* as separate dimensions while the feedback note says Position is the one master | **Conflict** — see Q-c and §4.3 |
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
Rule 3 is compulsory: *a Branch must name one Division and one Department before it can be saved.*

```csharp
// StructureDocument gains one list. Containment is untouched.
public IReadOnlyList<StructureMapping> Mappings { get; set; } = [];

public sealed record StructureMapping(
    string ChildDimensionTypeId,
    string ParentDimensionTypeId,
    MappingCardinality Cardinality);

public enum MappingCardinality
{
    Optional,     // may name one; may name none. What every existing pairing becomes.
    ExactlyOne,   // Rule 3: must name exactly one before save
    OneOrMore,    // Q-f: must name at least one, may name several
}
```

**Why a separate list rather than a flag on `StructureContainment`.** Three reasons. Self-nesting is
a containment pair (`Department → Department`) and is never a *mapping* in the FRS's sense — a
Department is not required to name a parent Department. A pairing can be permitted without being a
mapping at all, which is what every structure written before today is. And a mapping needs a stated
order (figure 8's groups are numbered A, B, …) that containment has no place for; the list's own
order supplies it.

**Existing tenants are untouched by construction.** A structure written before this field exists
deserialises `Mappings` to empty, and empty means *every declared pairing is optional* — which is
exactly what the engine does today. Zenith and Crescent continue to behave identically until somebody
opens the Mapping screen and ticks something. This is the same "empty means unconstrained" rule
`EmployeeAttachableDimensionTypeIds` already uses, and the same rule ADR-0008's 11 October addendum
applies to a recipe row that states nothing.

## 2.2 Several parents at once — the real change (ADR-0020)

Today a record has at most one parent per structure per date. `DimensionLinkDocument.Parents` is a
list, but it is a list **over time**: `ParentOn(asAt)` takes the first entry whose range contains the
date, and `InsertLink` closes whatever it displaces the day before.

For a Branch to belong to a Division **and** a Department at once, entries must be allowed to overlap
— but only across different mappings.

```csharp
internal sealed record ParentLink(
    string? ParentRecordId,
    EffectiveRange Range,
    string? MappingKey = null);   // the parent's dimension type id; null = the legacy single slot
```

**The invariant moves from "one parent per date" to "one parent per date *per mapping key*".** Every
rule, every displacement and every piece of range arithmetic that `InsertLink` performs today
continues to hold — within a key. Backdated moves still split rather than overwrite (ADR-0005's
addendum), because splitting is now per-key arithmetic over the same `ParentLink` ranges.

**`MappingKey` is nullable and that is load-bearing.** Every `ParentLink` already stored deserialises
with it null. Null means "the one unkeyed parent slot", which is precisely what a pre-FRS tenant has:
a Department under a Division, with no mapping named. Zenith and Crescent keep working with no data
written. A tenant that later declares mappings gets keyed links for new placements; the unkeyed link
remains valid and is treated as the parent for every mapping that does not have a keyed link of its
own. That rule is stated once, in `DimensionLinkDocument`, and is what makes this migration additive
rather than a rewrite.

### What this does to the three readers

| Reader | Today | After |
| --- | --- | --- |
| `ParentOn(asAt)` | the single parent | **replaced** by `ParentsOn(asAt)` → a list, and `ParentOn(asAt, mappingKey)` → one. Callers that genuinely want "the" parent (the list's Parent column) ask for the **primary mapping** — Q-d |
| `ComputeAncestorsAsync` | walks one chain up | walks **every** chain up, breadth-first, intersecting ranges along each path, and emits one `ClosureAncestor` per (ancestor, depth, range) reached. Already expressible: the document explicitly allows one ancestor at several depths |
| cycle detection | "is the proposed parent already a descendant" | unchanged in meaning, and it must already consider **every** path — the existing check runs against the closure, which after this change holds every path. It must also continue to run across **every date**, which it does today |

### The closure index after the change

**No schema change.** `DimensionClosureIndex` is `(StructureId, AncestorId, DescendantId, Depth,
EffectiveFrom, EffectiveToInclusive)` and a DAG simply produces more rows for the same descendant.

| Question | Query today | After |
| --- | --- | --- |
| All descendants of a node | rows where `AncestorId = node` | **`DISTINCT DescendantId`** — a node reachable by two paths appears twice |
| All ancestors of a node | rows where `DescendantId = node` | unchanged in shape; returns more rows, which is the point |
| Is A under B | one row lookup | unchanged |
| Depth of a node | the depth on its root pair | **ambiguous now.** `GetDepthAsync` returns the **shortest** path and is renamed to say so; the tree view uses it only for indentation |
| Employees under a node | assignment joined to closure on `DescendantId` | **must distinct on employee**, which is D1's org-total rule enforced in the query rather than left to a caller |

**Storage growth** is the honest cost. A value with two parents contributes roughly twice the closure
rows, and the whole subtree beneath it does too. For the FRS's own shape — Business Unit → Division →
Department → Branch → Position, with Branch doubly parented — that is a constant factor of about two
on one level, not an explosion. It is bounded by (number of paths from root to node), which the
cycle rule already keeps finite. The 200ms-at-5,000-records acceptance criterion in architecture §4
must be re-measured against a seeded doubly-parented tenant before this slice is accepted; if it
fails, that is a finding, not something to design around in advance. **This also makes architecture
open question 6 (retention of closure rows) materially more urgent**, as ADR-0005 already warned.

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

## 2.5 How D1's counting works

Three different numbers, computed three different ways, and the design says which is which so that
two screens cannot disagree.

| Number | Where it appears | How it is computed |
| --- | --- | --- |
| **At a node** | the Branch screen's headcount column; a card's count | assignments whose `NodeId` is that record, on that date. Unchanged, already batched |
| **Under a node** | the tree's "118 employees in this branch" | assignments joined to closure on `DescendantId`, **`DISTINCT EmployeeId`**. A person reachable by two paths is one person |
| **View by `<dimension>`** | the side panel | for each value of that dimension, count-under as above; **sum them**. A value with two parents is a *child* counted twice at the parent level — which is D1 exactly: "a value counts under EACH of its parents, but only within that parent dimension's own View by" |
| **Full organisation** | the panel's first row | `DISTINCT EmployeeId` over the whole axis. D1's "organisation-wide totals count UNIQUE employees" |

The panel's rows can therefore legitimately differ from each other and from the total, and the screen
says so in a sentence rather than leaving a reader to decide which number is the broken one. That
sentence is part of the deliverable, not a nicety: two plausible numbers that disagree with nothing
explaining why is the failure mode this whole engine is written against.

## 2.6 Cycle and date rules

| Rule | Today | After |
| --- | --- | --- |
| No cycles, across every date | enforced; a node may never become its own ancestor on any date | **unchanged in statement**, broader in effect: with several parents, A→B and B→A can now be reached by two different mappings and must still be refused. The check already runs against the closure, which holds every path, so this follows rather than needing new code — and it needs a test per mapping pair, which it does not have |
| Parent must be effective when the child is | advisory, not blocking | unchanged |
| A mapping's parent must be of the mapped dimension | — | **new**: a Branch's Division slot may only hold a Division. Falls out of `ContainmentRules.Decide`, which is already the one implementation both the validator and every picker read |
| Rule 3 — one from each required mapping | — | **new**, in `IDimensionValidator`, so the Add form, the recipe step and any future API are refused in the same words |
| Effective ranges must not overlap | per employee per structure (assignments); per record per structure (links) | links become **per record per structure per mapping key** |
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
Organisation axis, containment map after the FRS work:

   business-unit → division
   division      → department, branch
   department    → department        (self-nest: HR → Recruitment)
   department    → branch
   branch        → position
   department    → position
   position      → position          (self-nest: THE REPORTING LINE)

Mappings (the required half):
   branch    mapped to division     ExactlyOne
   branch    mapped to department   ExactlyOne
   position  mapped to branch       ExactlyOne   (or department — Q-h)
   position  mapped to grade        ExactlyOne   (§4)
   position  mapped to job-title    Optional
```

The reporting line is the `position → position` diagonal. The Position screen's tree view renders it
and nothing else, which is figure 4.1.4 exactly: POS → EXE → CEO, COO → RDIR; MGT → BMGR → BSUP.

**Two kinds of edge on one record, and they do not collide.** A Branch Supervisor position has a
parent Position (Branch Manager, the reporting line) *and* a parent Branch (Seef, the org placement).
Under §2.2 those are two `ParentLink`s with different `MappingKey`s, concurrent, both legal. On the
Position screen the tree is built from the `position` key only; on the combined tree, every key is
followed. One storage mechanism, two readings, and neither needs to know about the other.

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

## 4.3 Q-c — Job Title vs Position Title vs Position

The FRS creates this problem itself: figure 2 lists **Job Title** and **Position Title** as separate
dimensions, while the feedback note says *"the system should maintain Positions as the main master
and not duplicate/separate out from the Org. Role Setup."* Three nouns, one thing.

**Recommendation — two masters, not three, and the third is a name:**

| Noun | What it is | Where it lives |
| --- | --- | --- |
| **Position** | A post somebody holds. Has a code, a reporting line, an org placement, a grade, an approved headcount, and employees. Org Role is this | A dimension type. The master |
| **Job Title** | The occupational classification several positions share — *Engineer*, *Supervisor* — used for grouping, salary benchmarking and reporting | A dimension type. Positions map to it, Optional |
| **Position Title** | What this particular post is called — *Retail Supervisor, Seef* | **The Position record's own bilingual name.** Not a master |

So figure 2's *Position Title* row is dropped as a dimension. That is a divergence from the FRS's own
figure and it is flagged rather than absorbed: **Q-c** asks the product owner to confirm. If they want
the row visible, it ships hidden by default under D3's hide mechanism and the design is unaffected —
which is a good reason to build D3's hide switch early.

The session-B design predicted this exact decision and left room for it: *"a code-keyed job title is
a position by another name, and introducing one as a side-effect of a lookup list would settle that
question by accident."* It is now being settled deliberately.

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

**`job-title` ships as a lookup list bound to nothing**, deliberately. It becomes the seed for the
Job Title dimension through the same import, and the list is then left in place, unbound, for a
tenant that has not moved. Nothing has to be deleted for this to be correct.

**What the Job details section shows afterwards:** Employment type, Work location, Effective from,
Effective to, Probation ends. Grade and Job title leave it and appear in the read-only block at the
top of the employee record, sourced from the position. The `BagPart` alternate
(`ContentPart-Employee-BagPart.Edit.cshtml`) is already generic over the sections and needs no change
— the Records README anticipated this: *"it will: the FRS work on positions moves grade off the job
details section."*

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

Order: *Organisation tree* at a fixed low position; dimensions by `MenuOrder` then by the authored
plural; *Settings* at a fixed high position. Explicit numeric positions, never
`LocalizedString.PrefixPosition()`, which orders by the caption and so gives an Arabic reader a
different order — a defect already found once and recorded in `AdminMenu`'s own remarks.

`OrganisationMenuTenantTests` and `OrganisationMenuBrowserTests` currently assert *exactly three
entries, in order, in both languages*. They are rewritten, not deleted, to assert the new contract:
tree first, settings last, one entry per shown dimension, authored plurals, same order in both
languages, and **nothing pluralised by rule**.

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

Rendered as B3 draws it. Three engineering notes:

- **One form, one write.** Saving calls `IDimensionService.AddUnitAsync`, which already writes the
  record and its placement together and validates them together, so a value can never exist having
  failed to reach its parents. It gains the N keyed placements of §2.2; the all-or-nothing property
  is what makes Rule 3 honest.
- **The groups come from the mapping, in its stated order**, and are numbered A, B, … per figure 8.
  A dimension with no mapping renders no block at all (Rule 4), with no empty heading left behind.
- **Each group's options are that parent dimension's values**, filtered by `ContainmentRules.Decide`
  read forwards — the same decision function the validator reads backwards, which is ADR-0010's whole
  lesson. The picker never offers a parent the save would refuse.
- **Edit does not offer the mapping groups.** Changing which Division a Branch belongs to is a
  **move**: it rewrites what every historical report under that branch resolves to, it needs
  `MoveDimensionRecords`, and it needs the dated dry run that already exists. The Edit form carries a
  link — *"Seef is mapped to Bahrain (Division) and Retail Network (Department). Change a
  mapping…"* — to the move screen, with the mapping pre-selected. This keeps the existing split
  between "edit a name" and "reorganise the company", which `Permissions.cs` exists to express.

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

```
Mapping — Organisation                      [ axis: Organisation ▾ ]  ← only if >1 axis

  Which dimensions this organisation uses, in reading order
     Business Unit · Division · Department · Branch · Position · Grade · Job Title
     [ + add a dimension ]

  Which may sit at the top
     ☑ Business Unit    ☐ Division    ☐ Department    …

  What may sit under what, and what is required
     ┌───────────────┬──────┬──────┬──────┬──────┬──────┐
     │ parent ↓ child│ Div  │ Dept │ Brnch│ Posn │ Grade│
     ├───────────────┼──────┼──────┼──────┼──────┼──────┤
     │ Business Unit │ ☑ ◦  │ ☐    │ ☐    │ ☐    │ ☐    │
     │ Division      │ ☐    │ ☑ ●  │ ☑ ●  │ ☐    │ ☐    │
     │ Department    │ ☐    │ ☑ ◦  │ ☑ ●  │ ☑ ◦  │ ☐    │
     │ Branch        │ ☐    │ ☑ ◦  │ ☐    │ ☑ ●  │ ☐    │
     │ Position      │ ☐    │ ☐    │ ☐    │ ☑ ◦  │ ☐    │
     └───────────────┴──────┴──────┴──────┴──────┴──────┘
       ☑ permitted     ● required (one from this dimension)     ◦ optional
     [ build from a simple chain ]
```

The grid is the existing one with a second state per ticked cell. The existing rules carry over
unchanged: it is server-rendered first and kept in step by `structures.js`, so it works with no
script; a post with no ticks at all is read as "describe this as a chain" and the map is derived; a
change that live placements rely on previews its impact first (`PlanLevelChangeAsync`) and refuses
outright, naming the record at fault, if a live placement would be invalidated.

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
- **Draws a doubly-parented value once**, with two incoming edges (figure 9's dashed lines). The
  renderer keeps a set of already-drawn record ids per level; the second edge is drawn to the
  existing node. Without that the counts below it double, which is D1's whole concern.
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

The fourteen, from figure 2, reconciled against D5/D6 and Q-c:

```
business-unit  division  department  branch  section  position  grade  grade-step
job-title  employees-group  projects  sponsorship  territory  warehouse
+ cost-centre (already shipped)
```

Dropped, with the reason recorded in the ADR: **Employees** (D6 — employees are records, not a
dimension) and **Position Title** (Q-c — it is the Position record's name). **Added**, because §4.1.4
and figure 9 need it and figure 2 omits it: **Position** (Q-g). **Split from Grade Steps**: `grade`
and `grade-step` (Q-a, option B).

## 6.5 Demo recipes

`organisation-designer-zenith` and `organisation-designer-crescent` and their two employee recipes are
**extended, not replaced**, because `DemoCompanyRecipesTenantTests` and
`DemoEmployeeRecipesTenantTests` assert every tree parent by parent, both unit counts, the matrix on
one person, each head shape and a clean re-run, and those assertions are worth keeping.

| Recipe | Added |
| --- | --- |
| `…-zenith` | `position` and `grade`/`grade-step` types; mappings `position → department` ExactlyOne and `position → grade` ExactlyOne; a dozen positions with reporting lines and approved headcounts; grades G5–G9 with five steps each |
| `…-crescent` | the same, plus the doubly-parented case the FRS is about — a **Branch mapped to a Division and a Department**, which is figure 3 made real and which the closure tests then exercise for free |
| `…-zenith-employees` | each person's assignment repointed at a position; the matrix preserved (primary at a departmental position, secondary at a team position); a dated step each |
| `…-crescent-employees` | the same, plus one employee under the doubly-parented branch, so D1's counting has a real case |

`tools/generate-demo-employees.sh` is where the figures are edited and stays the one place.

A **third** demo recipe is proposed: `organisation-designer-frs-demo`, which builds exactly figure 9 —
KKT Group / Cayman HQ / MENA Region / Finance / Retail Network / Seef Branch (two parents) / Retail
Supervisor / Ahmed Salman and Fatima Noor. Small, named for what it is, never referenced by
`base.recipe.json` or any edition, and it lives in the module's own `Recipes/` folder so it appears on
`/Admin/Recipes` — per `recipes/demo/README.md`, which explains at length why the repository root does
not work for this. It is what a reviewer runs to compare the screen against the FRS page by page.

## 6.6 Recipe steps and export (ADR-0008, ADR-0011)

| Step | Change |
| --- | --- |
| `dimension-types` | `pluralEn` / `pluralAr`, `showInMenu`, `menuOrder`. Absent means "not stated" and leaves the tenant's value alone, which is ADR-0008's existing rule and the reason `isActive`/`sortOrder` are nullable in the lookup-list step |
| `structures` | a `mappings` block: child code, parent code, cardinality. Stating `mappings` together with a chain-shaped `levelTypeCodes` is **permitted** (they describe different things); stating it twice is refused naming the conflict, as `containment` + `allowSkipLevel` already is |
| `dimension-records` | `aliasEn` / `aliasAr`; and a record's placement becomes **a list** of `{ parentCode, mappingTypeCode }` rather than a single parent code. A bare `parentCode` keeps working and means the unkeyed slot — old recipes apply unchanged, which is the property that keeps every shipped demo and every customer file valid |
| `employee-assignments` | unchanged. A position is a record and a placement is a placement |
| Export (`DimensionsDeploymentStep`) | emits the new fields; the **records-parents-first** walk becomes a topological order over *every* parent a record has ever had on *any* mapping. `DimensionExportRoundTripTenantTests` gains the doubly-parented case and compares closure on dates either side of every change — which is exactly what that test exists to catch |

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

**ADR-0020 — A value may have several parents at once: an axis is a directed acyclic graph.**
FRS §3 and Rule 3 require a Branch to belong to one Division *and* one Department simultaneously.
`DimensionLinkDocument.ParentOn` returns a single parent per date and ADR-0005's closure intersects
ranges along *the* path, so this is a change to a recorded decision, not an extension of it.
`ParentLink` gains a nullable `MappingKey` — the parent's dimension type id — and the invariant moves
from "one parent per date" to "one parent per date per mapping key"; null is the legacy unkeyed slot,
so every stored link stays valid and every existing tenant behaves identically until a mapping is
declared. The closure index needs **no schema change** (`DimensionClosureDocument.Ancestors` is a list
and its own comment already allows one ancestor at several depths); what changes is the arithmetic in
`ComputeAncestorsAsync`, the `DISTINCT` on descendant and employee queries, and `GetDepthAsync`
becoming explicitly the *shortest* path. Storage grows with the number of root-to-node paths, which
makes architecture open question 6 (retention) urgent rather than noted, and the 200ms acceptance
criterion must be re-measured against a doubly-parented seed. Amends architecture §4 and ADR-0005.

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
line as the `position → position` diagonal and its org placement as a second keyed link under
ADR-0020. The employee's hold on it is an ordinary `EmployeeAssignment`: dated, allocatable,
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

**ADR-0025 — Delete deletes, and a used value is made inactive with the reason on the screen.**
D4 gives Delete two outcomes; architecture §6 gives deletion three, of which the third — referenced by
live data — is **blocked**. The assessment is kept exactly as it is, including the rule that the
blockers are always named and never a generic failure; what changes is that *blocked* becomes a screen
state carrying the next action ("make inactive from…") rather than a dead end. The reason is D4's own:
a refusal a user cannot act on is what the decision is written against. Retirement keeps its existing
three-way question about the units underneath, on every structure the record sits on. A real delete
becomes the one operation in this module that destroys a record, so it gains a mandatory audit event
with a reason and the acting user — the same standard `MoveCancelled` already holds.

**ADR-0026 — Grades, steps and job titles are dimensions; the employee's step is a dated assignment.**
D5 makes Grade Steps and Job Title dimensions, which supersedes slice B2's binding of
`JobDetails.GradeCode` to the `grade` lookup list. Nothing is migrated and nothing is deleted: ADR-0016
made binding an editor setting over a plain `TextField`, so unbinding is two settings and every stored
value stays readable, exportable and auditable while the field is retired from the editor (ADR-0009 —
a migration never destroys data). The lists become the seed for an **operator-run import**, following
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

| | Risk | Mitigation |
| --- | --- | --- |
| 1 | **The DAG breaks the 200ms descendant-resolution acceptance criterion.** Closure rows grow with the number of root-to-node paths | Measure before accepting the slice, on a seeded tenant with a doubly-parented level at 5,000 records. If it fails, that is a finding and a design conversation — not something to pre-solve by guessing at an index |
| 2 | **A doubly-parented value is drawn twice and every count below it doubles.** The most likely visible defect in the whole FRS, and both numbers stay plausible | D1 made explicit in §2.5; `DISTINCT` enforced in the query rather than left to callers; the FRS demo recipe exists so a reviewer can check figure 9's numbers by eye |
| 3 | **Cycle detection has a wider surface.** A→B by one mapping and B→A by another is a cycle nothing currently tests for | The check already runs against the closure, which holds every path, so this follows rather than needing new code — but it needs a test per ordered mapping pair, property-style, the way `DimensionContainmentTenantTests` already does for containment |
| 4 | **Removing the Organisation designer would delete seven dated operations.** "No Data Entry!" is about the enquiry report | §5.7 keeps both and says so explicitly. A reviewer who wants the designer gone should say so, with the rebuild cost visible |
| 5 | **Rule 3 made retrospective would invalidate live data.** Ticking "required" on a tenant with untidy branches | Rule 3 governs the Add form, not existing rows (§2.6). The Mapping screen previews the impact and warns; it does not refuse |
| 6 | **The menu erodes again.** It was withdrawn once for exactly this | Authored plurals, one entry per dimension, a hide switch, and `OrganisationMenuTenantTests` rewritten rather than deleted |
| 7 | **Figure 2's list and D5/D6 disagree** (Employees, Position Title, no Position) | Q-c, Q-g; ADR-0023 records what was dropped and why, so the decision is readable rather than inferred from a recipe |
| 8 | **Two slices change the employee read surface** (F6's derived fields, §8.1's type split) | They are the same slice, deliberately |
| 9 | **The Grade move touches live data in B2 tenants** | Nothing is migrated; the lists stay, the field is retired not removed, and the move is an operator tool with a dry run |

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
| **Q-c** | Job Title vs Position Title vs Position — figure 2 lists two of them as dimensions while the feedback note says Position is the one master | **D9 — Position is the master; Job Title is a dimension positions map to (Optional); Position Title is the Position's own name. A `position-title` row ships only if asked for, hidden by default.** As recommended | **Position is the master; Job Title is a dimension positions map to; Position Title is the Position record's own name** (§4.3). If the row must stay visible, it ships hidden |
| **Q-d** | When a value has two parents and a report needs exactly one, which is primary? | **D10 — declared per mapping on the Mapping screen.** As recommended; `StructureMapping.IsPrimary` in ADR-0021 | **Mark one mapping per child dimension as primary on the Mapping screen** — "a Branch's Division is its primary parent". Declared, not derived: deriving it from mapping order would make a screen's layout decide payroll attribution |
| **Q-e** | Unit heads (ADR-0012): keep as a dated appointment, or model "head" as a position? | **D11 — keep the dated appointment. ADR-0012 stands; revisit in prompt 5 when approval routing makes the cost of either answer visible.** ADR-0027 stays unwritten | **No recommendation — this is genuinely balanced.** A head is a post, which argues for a position; ADR-0012 exists because a head need not be a *member*, which an assignment cannot express. ADR-0027 is reserved for the answer |
| **Q-f** | Figure 9 shows Seef Branch with two parents that are **both Departments** (Finance and Retail Network), while figure 3 maps Branch to Division *and* Department and Rule 3 says "one value from each mapped parent". May a value have several parents within **one** mapped dimension? | **D12 — figure 9 is a loose mock. Default cardinality is `ExactlyOne` (Rule 3); `OneOrMore` is available per mapping.** Radios for the first, checkboxes for the second | The storage supports both (`OneOrMore`). **Please confirm which figure 9 means** — a mock drawn loosely, or a real requirement |
| **Q-g** | Figure 2's dimension list has **no Position row**, yet §4.1.4 shows a Position tree and figure 9 puts Position above Employee | **D13 — ship Position.** As recommended | **Ship it.** §4.1.4 and the feedback note both require it; figure 2 looks like an omission |
| **Q-h** | Figure 3 maps Position to **Branch**. Crescent has departments that are not under a branch, and Zenith has no branches at all. Is Position mapped to Branch, to Department, or to both (whichever the customer uses)? | **D14 — per tenant. Zenith maps Position → Department and Team; Crescent maps Position → Branch and Department. `AtLeastOneAcross` is not added unless a slice proves it is needed** | **Both, each Optional, with at least one required** — which is a cardinality `MappingCardinality` does not express today and would be a fourth member (`AtLeastOneAcross`). Only worth adding if the answer is "both" |
| **Q-i** | Two small ones. Does approved headcount need to be **dated** ("what was it last March")? And **which browser test was the intermittent failure** — nothing in the repository records one (§8.2) | **D15 — undated for now, stated as a limitation.** On the flake: one of three full-suite runs had a single browser test fail, and the test was not named in the report. The §8.2 helper fix lands in **F1** regardless, since it is a real defect on its own | Undated for the first slice, stated as a limitation. For the flake, the helper fix in §8.2 is worth doing regardless |

### One answer the design did not anticipate (D17)

The menu duplication that caused the per-type entries to be withdrawn — *Departments, Departments,
Departments* — **came from the demo data, not from the menu mechanism**: three demo organisations
each defined their own Department and Division dimension types in one tenant. §5.1 treats "one entry
per dimension type rather than per type per structure" as the fix, and that is still right, but it is
only half of it. The other half is that **the demo recipes must share the standard dimension types**
rather than prefixing their own, so each dimension genuinely exists once. Record codes stay prefixed
— that is what keeps the three companies' *values* from colliding — and only the type codes are
shared. A test with base + Zenith + Crescent loaded into one tenant asserts each dimension appears in
the menu exactly once. This lands in **F2** with the menu and in **F9** with the demo rebuild.

## 10.3 Slice plan

Each slice is one session: model, then services, then screens, then tests, stopping for review after
each — CLAUDE.md's working pattern and the build order the architecture already argued for. The order
below puts the smallest valuable thing first and keeps every slice independently reviewable and
independently shippable.

| | Slice | What lands | What a reviewer can click and see at the end |
| --- | --- | --- | --- |
| **F1** | **Alias, Status, and the dimension screen's two views** | `AliasEn`/`AliasAr` on the part and the index (`UpdateFrom9Async` + upgrade test); validator; recipe step and export fields; the Tree/List toggle; the Alias column; the five toolbar buttons. Plus the browser-helper fix (§8.2) | Organisation → Dimension types → *Department* → **View units** → a tree of departments with Code, Name, Alias, Status, and a toggle to a flat list. No model change beyond one column |
| **F2** | **The menu (D3, ADR-0024)** | Authored bilingual plural, `ShowInMenu` and `MenuOrder` on the dimension type editor; the menu provider; *Organisation tree* reserved at the top and *Settings* at the bottom; `OrganisationMenuTenantTests` rewritten | The Organisation menu lists this tenant's dimensions, correctly pluralised in both languages, in the same order in each; ticking a box hides one |
| **F3** | **Mapping, with one parent (ADR-0021)** | `StructureDocument.Mappings`; `MappingCardinality`; Rule 3 and Rule 4 in `IDimensionValidator`; the Mapping screen with its required/optional column and its impact preview; the Add form's numbered groups; `UpdateFrom11Async`; recipe `mappings` block | Settings → **Mapping** → mark `Department → Division` required → Organisation → *Department* → **Add** → the form shows a numbered Division group and refuses Save until one is chosen |
| **F4** | **Several parents at once (ADR-0020)** | `ParentLink.MappingKey`; `ParentsOn`; `ComputeAncestorsAsync` over a DAG; `DISTINCT` on descendants and employees; cycle tests per mapping pair; `UpdateFrom10Async`; export's topological walk; the performance re-measurement | Mark `Branch → Division` **and** `Branch → Department` required; add Seef under Bahrain and Retail Network; Seef appears under both on the designer and once on the Branch screen's own tree; the Crescent demo still resolves identically on every historical date |
| **F5** | **The shipped dimensions and Position (ADR-0022, ADR-0023)** | The `dimension-types` step in `base.recipe.json`; the Position type with `approvedHeadcount`; the `position → position` reporting line; vacancies, batched; the Position list's three columns | A **new** tenant's Organisation menu already lists Business Unit, Division, Department and Position; Organisation → *Position* → a reporting tree CEO → COO → Regional Director, with Approved / Filled / Vacant columns |
| **F6** | **Employees join through a position** | `EmployeeAttachableDimensionTypeIds = [position]`; `GetOrgPlacementsAsync`; the employee record's derived block; the list's position column; the placement screens' vocabulary. **Plus the `EmployeeSummary` split (§8.1), and the employee leaves on F8's tree** | People → Employees → a person → their position, and under it the division, department and cost centre, none of them typed; changing the position changes all of them; last year's record still shows last year's. Organisation → Organisation tree now reaches Ahmed Salman and Fatima Noor under Retail Supervisor |
| **F7** | **Grades, steps and job titles (ADR-0026)** — *gated on Q-a and Q-b* | The Grade axis; `position → grade`; the employee's dated step; unbinding `grade`; retiring `GradeCode` from the editor; the operator import tool with its dry run | Organisation → *Grade Steps* → G7 with five steps; a position graded G7; an employee on G7-S3 from January, with G7-S2 still showing for last year |
| **F8** | **The Organisation tree enquiry report (FRS §5)** — *runs after F4, per D18* | The new read-only controller and views; re-root with breadcrumb and Clear selection; the View-by panel and its batched counts; the `…-frs-demo` recipe. **Employee leaves are F6** | Organisation → **Organisation tree** → figure 9's unit levels on screen, with the FRS demo recipe applied; click MENA Region and the chart narrows; Clear selection restores it; the panel's counts match the FRS's |
| **F9** | **Demo recipes, export and D4** | Zenith and Crescent extended with positions, grades and the doubly-parented branch; `DimensionExportRoundTripTenantTests` extended; the Delete screen's three outcomes (ADR-0025) and the new audit event | Apply the Zenith recipe to a fresh tenant and the whole model is there; export it, import it into a second tenant, and every historical date agrees; press Delete on a used value and get a sentence and a date box |

**Approved order (D18): F1 → F2 → F3 → F4 → F8 → F5 → F6 → F7 → F9.**

The proposal was F1…F9 in numeric order with a note that F8 could be pulled forward; the review took
that option. **F8 moves to directly after F4**, so the combined tree lands as soon as the DAG it is
built on exists — it is the single most persuasive screen in the FRS and there is no reason to hold
it behind positions. It ships there **without employee leaves**, and **F6 adds them** when employees
start joining through a position.

**F7 was gated on Q-a and Q-b**, which are now answered (D7, D8), so the gate is lifted. Nothing else
depends on it.

**If fewer slices are wanted:** F1 and F2 merge cleanly, and F8 and F9 do. F3 and F4 should **not**
merge — F3 is a rule and a screen, F4 is a change to a recorded storage decision with a performance
gate on it, and reviewing them together would bury the second.

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

**Build order from here: F1 first, and F1 only.** Its scope is the row in §10.3, plus the browser
helper fix in §8.2.
