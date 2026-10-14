# Decisions on Dimension Mapping FRS v2.5 (06-10-2026)

Recorded 12 October 2026 by Mohsin Hasnain. Status: agreed by the team, pending formal manager
confirmation. These answer the open questions raised by the FRS gap analysis and are binding for
design and implementation unless the product owner changes them.

## D1 Headcount — SUPERSEDED IN PART BY D19
**As recorded 12 October 2026**, when a value could have several parents: a value counts under EACH
of its parents, but only within that parent dimension's own "View by" view (FRS figure 9 side
panel). A view sums one dimension only, so nobody is double counted inside a view.
Organisation-wide totals count UNIQUE employees.

**As amended by D19 (13 October 2026).** A value has exactly one parent, so the first half has
nothing to describe: there is one path from a value to the root, every employee is reached once, and
"counts under each of its parents" cannot arise.

What survives, and is still worth stating because it is what the side panel means:

- **A "View by" row sums one dimension only.** The Department row counts every employee whose
  position rolls up to some department; the Branch row does the same for branches. Two rows can
  legitimately disagree, because not everybody is under a branch.
- **The organisation total counts unique employees**, which with one parent per value is the same
  number as the sum — but the queries still say `DISTINCT` on employee, because an employee may hold
  several positions (a split allocation, the Zenith matrix) and that is a different source of
  double counting which D19 does not remove.

The `DISTINCT EmployeeId` guarantee therefore stays in the engine. It was introduced for the DAG and
it earns its place without one.

## D2 Positions
- Position is a dimension with its own reporting-line tree (FRS 4.1.4). Org Role = Position, one master.
- A position holds MANY employees (FRS figure 9: Retail Supervisor has 2 employees).
- Optional "Approved headcount" per position; vacancies = approved - filled. Recruitment later.
- Employees join the organisation by being assigned a position (dated). Division, department,
  branch, cost centre etc. are DERIVED from the position's mapping (FRS 1.2).

## D3 Menu
Every dimension appears as its own menu item under Organisation (FRS 1.2), once, with correct
bilingual plural names, in a fixed order, and a client can hide dimensions it does not use.
The earlier clutter came only from three demo organisations loaded into one tenant.
Proposed shape:
  Organisation
    Organisation tree (read-only enquiry, FRS 5)
    <one item per dimension>
    Settings: Dimension types, Mapping

## D4 Delete
The UI offers Delete. A value never used (no employees, children, links or history) is really
deleted. A value that is or was used is set Inactive with an end date, and the user is told why.

## D5 Grades and steps — AMENDED BY D20
- "Grade Steps" is one dimension with its own tree: grades as parents, steps as children
  (G7 -> G7-S1..S5). Refined by **D7**: one dimension in the menu and on screen, two dimension
  types underneath.
- A position **references** a GRADE (optionally a grade range, e.g. G7-G8). **Amended by D19:**
  this is a reference and not a parent — see **D21**.
- The employee holds a STEP, chosen only from the steps of the position's grade(s); dated
  (G7-S2 from Jan 2025, G7-S3 from Jan 2026). Steps optional for clients that do not use them.
- Promotion = new position; grade follows; HR picks a step in the new grade.
- ~~Job Title is a dimension that positions point to (e.g. "Engineer" used by several positions).~~
  **Struck out by D20.** Position, Job Title and Designation are one concept, always called
  **Position**. There is no Job Title dimension and nothing points at one.
- Lookup lists (Form Designer B1/B2) remain for simple non-organisational values: nationality,
  document type, employment type, religion, marital status, bank, relationship. The `job-title`
  list B2 shipped is retired by D20; it is bound to nothing, so retiring it loses nothing.

## D6 Employees
Employees remain employee records (People -> Employees). They appear as the leaf level of the
organisation tree under their position (FRS figure 9), not as a dimension with values.

---

# D7 onwards — answers to the design review of 12 October 2026

Recorded 12 October 2026 by Mohsin Hasnain, answering the questions raised in
`docs/design/frs-v2.5-dimension-mapping-design.md` section 10.2.
**Status: agreed by the team, pending formal product owner confirmation.**
Binding for design and implementation unless the product owner changes them. Each carries the
question number it answers, so the design document and these decisions can be read together.

## D7 Grade Steps is two dimension types behind one menu entry (Q-a)
Option B of the design. Two dimension types — `grade` and `grade-step` — with `grade -> grade-step`
in the containment map and `grade-step` mapped to `grade`. The menu carries **one** entry,
"Grade Steps", whose tree shows grades at the top level and steps indented under them.

This is a liberal reading of D5's "one dimension": one dimension **on screen and in the menu**,
built from two dimension types. Taken because the alternative — one self-nesting type where a grade
is a node at depth 0 — makes "is this a grade or a step" a depth rule, which breaks the first time a
customer puts a band above their grades.

## D8 Grade attaches to the position, not the person (Q-b)
- A position is mapped to a grade. Cardinality `ExactlyOne` by default.
- **Grade ranges are allowed**: a mapping set to `OneOrMore` lets a position carry G7 and G8.
- **Steps are optional by not configuring them.** A tenant that creates grades and no steps has
  nothing to pick, and the step control is absent rather than empty. No flag and no setting; the
  absence of data is the switch.
- **The employee's dated step is an `EmployeeAssignment` on a Grade axis**, not a field on the
  employee. Dated, historical, audited and exportable with no new storage.

## D9 Position is the master (Q-c) — SUPERSEDED BY D20
**As recorded 12 October 2026**, this named three nouns and kept two of them: Position as the master
dimension, **Job Title** as a second dimension positions map to, and Position Title as the Position
record's own name.

**D20 collapses it to one.** Position, Job Title and Designation are one concept. There is no Job
Title dimension, nothing maps to one, and the second row of that table is struck out. What survives
unchanged is the third row — *Position Title* is the Position record's own bilingual name and never
a master — and the first, which D20 restates in stronger terms.

## D10 The primary parent is declared, not derived (Q-d) — NOT APPLICABLE AFTER D19
This existed to answer "when a value has several parents and a report needs exactly one, which?".
**D19 removes the premise**: a value has exactly one parent, so every report that needs one already
has it and there is nothing to declare.

`StructureMapping.IsPrimary` is therefore **not built**. The design document's §B5, §2.5 and the
Mapping screen's primary marker go with it. Recorded rather than deleted because a reader meeting
"primary parent" in the 12 October design document needs to know what happened to it.

## D11 The unit head stays a dated appointment (Q-e)
ADR-0012 stands unchanged: a head appointment is its own dated record, carries no allocation, is
counted by no headcount, and the head need not be a member of the unit they head. "Head as a
position" is **revisited in prompt 5**, when approval routing is built and the cost of either
answer is visible. The reserved ADR-0027 is not written until then.

## D12 Figure 9 is a loose mock (Q-f) — SETTLED BY D19
The question was whether a value may have several parents **within one** mapped dimension, because
FRS figure 9 draws Seef Branch under two Departments. **D19 settles it at the stronger level**: a
value has exactly one parent, full stop — not one per mapped dimension, and certainly not two within
one. Figure 9 is a loose mock, as this decision already said, and now so is figure 3.

`MappingCardinality.OneOrMore` survives for one thing only and it is not a parent: a **grade range**
on a position, which is a reference rather than a placement. See **D21**.

## D13 Position ships with the pre-configured set (Q-g)
Figure 2's dimension list omits Position while FRS 4.1.4 shows a Position tree and figure 9 puts
Position above Employee. The omission is treated as an error in the figure. Position ships.

## D14 Position's mapping into the organisation is per tenant (Q-h) — NARROWED BY D19
There is no platform answer to "is a Position under a Branch or under a Department" — it depends on
whether the customer has branches. **Which one it is** remains the tenant's to declare on the
Mapping screen; **that it is one** is now D19's.

Demo recipes declare one parent each, not two:
- **Zenith** — Position under **Department**. (Teams are a Project's children; a position in a site
  team belongs to the department that staffs it, and the team placement is the *employee's*
  secondary assignment, which is where the Zenith matrix already lives.)
- **Crescent** — Position under **Branch**.

**`AtLeastOneAcross` is not added**, and after D19 there is nothing it could mean: a cardinality
spanning several parent mappings presupposes several parent mappings.

## D15 Approved headcount is undated for now (Q-i)
Approved headcount is an attribute on the shipped Position dimension type, and attributes carry no
effective range. So "what was the approved headcount last March" is **not answerable**, and that is
recorded as a stated limitation in the module README rather than designed around. Making it dated is
a model change of the same shape as the dated name history, and is a later decision.

## D16 The four reconciliations C1-C4 are confirmed
The design document's four conflicts between the FRS and a recorded decision are confirmed as
stated, and are to be recorded as ADRs rather than absorbed:

| | Reconciliation | ADR |
| --- | --- | --- |
| **C1** | A value may have several parents at once; an axis becomes a directed acyclic graph | **ADR-0020**, written now, accepted pending second review |
| **C2** | WorkMate ships the pre-configured dimension set, switched off rather than absent | ADR-0023, at slice F5 |
| **C3** | A dimension's menu entry comes back, with authored plurals and a hide switch | ADR-0024, at slice F2 |
| **C4** | Delete really deletes an unused value; a used one is made inactive with the reason on screen | ADR-0025, at slice F9 |

**ADR-0020 and ADR-0021 are written now**, before slice F3, because F3 and F4 rest on them.

## D17 Demo organisations share the standard dimension types — REMEDY SUPERSEDED BY D23
The menu clutter that caused the per-type menu entries to be withdrawn in prompt 4 session D —
*Branchs, Cost centres, Departments, Departments, Departments, Divisions, Divisions, Divisions,
Projects, Regions, Sections, Teams* — came from **three demo organisations each defining their own
Department and Division types in one tenant**, not from the menu mechanism.

**The diagnosis stands. The remedy does not.**

**As recorded 12 October 2026:** every demo recipe uses the standard, shipped dimension types rather
than prefixing its own; prefixed *record* codes stay, and only the *type* codes are shared.

**As amended 13 October 2026, after building it.** Sharing the type codes was implemented in slice
F2 and **reverted**. A dimension type is a **level of every structure that declares it**, and
`IDimensionService.CreateAsync` writes a self closure pair on all of them — so a shared `division`
type put every Crescent division on the **Zenith** axis as a parentless root, and the Zenith chart
drew Crescent's divisions. Eight integration tests failed; `DemoCompanyRecipesTenantTests` named it:

> Expected `ChildCodesAsync(services, "zenith-org", null)` to be equal to `{zenith-div-engineering,
> zenith-div-projects, zenith-div-corporate}`, but `{…, crescent-div-head-office,
> crescent-div-branch-network}` contains 2 item(s) too many.

That is **ADR-0005 working exactly as designed** — one record, several axes — and it is the demo
data that is the odd case: two unrelated organisations in one tenant, which no real tenant is. D17
did not anticipate the cost because the cost is not in the menu.

**A second attempt** gave each recipe a company-qualified authored plural — *Zenith Departments*,
*Crescent Departments* — which worked and was also wrong: a tenant reading its own company name on
every menu entry, to work around a defect in the menu. It was reverted too.

**The remedy is now D23 B:** dimension types sharing an English singular name become **one menu
entry**, and where that entry's values span more than one chart each value names its own. The
duplication is resolved where it was visible and where it costs the graph nothing. Every demo recipe
keeps its prefixed type codes and its plain plurals.

The test D17 asked for exists and is stronger: `OrganisationMenuTenantTests` loads base + Zenith +
Crescent + the demo sample — twelve dimension types — and asserts **eight** menu entries.

## D18 Slice order — REVISED BY D19 AND D20

**As recorded 12 October 2026:** `F1 -> F2 -> F3 -> F4 -> F8 -> F5 -> F6 -> F7 -> F9`.

**As revised 13 October 2026.** D19 deletes F4 outright — the whole slice was the DAG — and D20
takes job titles out of F7. Nothing moves to make room; the order is the same list with one entry
gone.

```
F1 ✓ -> F2 ✓ -> F3 -> F8 -> F5 -> F6 -> F7 -> F9
```

| | Slice | Changed by D19 / D20? |
| --- | --- | --- |
| **F1** ✓ | Alias, Status, and the dimension screen's two views | Shipped 12 October 2026 |
| **F2** ✓ | The menu — one expandable item per dimension **name** (D3, D17 as amended, **D23**) | Reshaped by D23: grouped entries, values under each, a value's own page, the designer back at the top |
| **F3** | Mapping — **one** parent group on the Add form | Simpler: one group, always a radio set |
| ~~F4~~ | ~~Several parents at once — the DAG~~ | **Deleted by D19** |
| **F8** | The Organisation tree enquiry report, plain tree, **without employee leaves** | Simpler: a tree, not a graph; no node drawn twice |
| **F5** | The shipped dimensions and Position | **No Job Title or Position Title dimension** (D20) |
| **F6** | Employees join through a position, **the enquiry tree gains its employee leaves**, **and Job title becomes Position** (D20, see **D22**) | Gains the job-title migration and the relabelling |
| **F7** | Grades and steps — **no job title** | Narrowed by D20 |
| **F9** | Demo recipes, export and Delete | **No doubly-parented demo branch** (D19) |

F3 keeps its ADR (**ADR-0021**, amended for a single parent). F4's ADR (**ADR-0020**) is withdrawn.

## Superseded
D5's "'Grade Steps' is one dimension with its own tree" is refined by **D7**: one dimension in the
menu and on screen, two dimension types underneath. D5's intent is unchanged.

---

# D19 onwards — two product decisions that override the FRS

Recorded 13 October 2026 by Mohsin Hasnain.
**Status: agreed by the team, pending formal product owner confirmation.**

These two are different in kind from D1–D18. Those answered questions the FRS left open or drew
loosely. **These two overrule text the FRS states plainly**, so each quotes what it overrides. Per
CLAUDE.md the FRS wins on what is built *unless the product owner says otherwise* — and here they
have.

## D19 A dimension value has exactly one parent

**What the FRS says, and this overrides:**

> §3 — *"Any dimension can be mapped to one parent dimension, **or to more than one**. This is what
> allows the same structure to fit different organizations without changing the system itself."*
>
> Rule 3 — *"If a dimension is mapped to **more than one** parent dimension, every new value must be
> assigned to one value from each mapped parent before it can be saved."*
>
> Figure 3 — Branch with two arrows into it, from Division and from Department, captioned *"mapped
> to 2 parents"*. Figure 9 — *"Seef Branch · Branch — 2 parents"*.

**The decision: a dimension value has exactly one parent.** Multi-parent mapping is dropped. Figure
3's two arrows, figure 9's two dashed edges and Rule 3's "from each" are not built.

A dimension type is mapped to **at most one** parent dimension type, and that mapping is either
**Required** or **Optional**:

| | What it means on the Add form |
| --- | --- |
| **Required** | One parent must be chosen before the value can be saved. This is Rule 3, read for one parent — the rule survives, the plural does not |
| **Optional** | A parent may be chosen or left empty |
| **No mapping** | The parent step is skipped entirely. This is Rule 4, unchanged |

### What this changes

**ADR-0005 stands as written.** "Links are scoped to a structure" and one parent per record per axis
was never in question; **C1 no longer applies**, and the conflict it recorded is withdrawn rather
than reconciled. `DimensionLinkDocument.ParentOn(asAt)` keeps returning a single parent, the closure
index keeps one path per pair, and `ParentLink` gains no mapping key.

**ADR-0020 is withdrawn**, not deleted. Its text is kept because the analysis in it is correct and
because a reader who meets "the organisation axis is a DAG" in the 12 October design document needs
to find out what happened to it.

**ADR-0021 is amended** rather than withdrawn: mapping is still containment plus a cardinality, and
the cardinality is now Required / Optional over one parent type.

**Slice F4 is deleted** (see D18). F3 ships one parent group on the Add form, always a radio set —
"pick exactly one from this group" is what a radio group means, and with one group there is no case
where checkboxes are the honest control.

### Why this is the right trade

Recorded because the FRS's own sentence gives the reason for the other answer — *"this is what
allows the same structure to fit different organizations"* — and that reason is real.

What is given up is a Branch belonging to a Division **and** a Department simultaneously. What is
bought is that every number has one path to the root: a headcount cannot be reached twice, a
roll-up cannot double, a report never has to be told which parent it meant, and the closure index
does not grow with the number of root-to-node paths. The engine already answered the "same
structure, different organisations" need a different way — a **ragged containment map**
(ADR-0010), which is what lets Crescent put Departments under one Division and Regions under its
sibling, and Departments again four levels down. That flexibility is untouched.

A customer who genuinely needs a value on two axes still has the mechanism ADR-0005 was built for:
**two structures**. That is one record participating in two trees, which is a different and
better-defined thing from one tree with two edges into a node.

## D20 Position = Job Title = Designation

**What the FRS says, and this overrides:**

> Figure 2 — the pre-configured dimension list contains **both** *Job Title* and *Position Title*
> as separate rows.
>
> §1.2 feedback note — *"the Org. Role setup is equivalent to Positions masters. The system should
> maintain Positions as the main master and not duplicate/separate out from the Org. Role Setup."*

The feedback note already said one master; figure 2 still listed two more nouns. **This decision
takes the note to its conclusion and removes both rows.**

**The decision: Position, Job Title and Designation are one concept, always called "Position".**

| | |
| --- | --- |
| English | **Position** |
| Arabic | **المنصب** |

- **No Job Title dimension.** Nothing maps to one, nothing references one.
- **No Position Title dimension.** What a post is called is the Position record's own bilingual
  name, which is what D9's third row already said.
- **No designation field anywhere** — not on the employee, not on a section, not in a form.
- **Org Role is Position**, which the feedback note already settled.

Every user-facing label reading *Job title*, *Designation* or *Org role* becomes **Position** in
English and **المنصب** in Arabic, in the slice that owns the screen it is on.

### What exists today and has to move

Slice B2 shipped two things this touches, and **neither is lost** — see **D22** for the design and
**F6** for when it happens.

| Today | After |
| --- | --- |
| `JobDetails.JobTitle`, a free-text `BilingualTextField` on the job details section | Retired from the editor, kept in storage. The employee's **Position** is shown read-only from their dated position assignment |
| The `job-title` lookup list, shipped by B2 and **bound to nothing** | Retired / hidden. It was never offered to a reader, so retiring it loses nothing — and ADR-0014 retires rather than deletes, so a tenant that typed entries into it keeps them |

The session-B design predicted this exact outcome and left room for it: *"a code-keyed job title is
a position by another name, and introducing one as a side-effect of a lookup list would settle that
question by accident."* It is now settled deliberately, the other way.

## D21 A position's grade is a reference, not a parent

**Pending product owner confirmation.** The team's reading; flag it in review.

D19 makes a value's single parent its place in the tree. A position has exactly one parent — a
Branch, or a Department, per **D14** — and that is its place in the organisation. Its **grade** is a
different kind of fact and must not compete for the same slot:

- **A grade reference never enters the closure index and never appears on any tree.** A position
  graded G7 does not sit "under" G7, no employee rolls up to G7 through their position, and the
  Organisation tree has no grade level in it. Grades have their own screen and their own tree
  (**D7**), reached from the menu like any other dimension.
- **`OneOrMore` survives here and nowhere else.** A position may reference a grade **range** —
  G7–G8 — which is **D8**, and it is expressible precisely because a reference is not a placement.
  A value cannot be in two places in a tree; it can perfectly well point at two grades.
- **The employee's dated grade step stays on its own Grade axis**, as an ordinary
  `EmployeeAssignment` (**D8**). Dated, historical, audited and exportable with no new storage, and
  on a different structure from the organisation — which is exactly what ADR-0005 scoped links to a
  structure for.

So the Mapping screen gains a distinction it did not have: **this dimension's parent** (one, Required
or Optional, on the tree) and **this dimension's references** (any number, off the tree). The grade
reference is the first and so far only one.

**What to confirm:** that a grade is never a level of the organisation tree, and that a grade range
on a position is wanted at all. If the answer is that grades *should* appear in the organisation
tree, this becomes a parent and the range goes.

## D22 Job title becomes Position — the move, designed now and built in F6

**Design only.** Nothing here is built until slice **F6**.

The rule is ADR-0009's and ADR-0016's: **append-only, nothing lost, and the old values stay
readable.**

1. **`JobDetails.JobTitle` is retired from the editor, not removed from the type.** It stays a
   stored `BilingualTextField` holding whatever anybody typed. A tenant's history is exactly the
   typed values it already has, and deleting the field would destroy them. The same shape as
   `DimensionRecordPart.HeadEmployeeId` after ADR-0012 and `AllowsSelfNesting` after ADR-0010's
   addendum: a stored property nothing reads and no screen writes.
2. **The employee's Position is shown read-only**, resolved from their dated position assignment
   through `GetOrgPlacementsAsync` — the same read that gives the derived division, department and
   cost centre. Nothing is typed and nothing is copied.
3. **Where a tenant has typed values and no positions yet**, the old field is still the only answer
   and the screen shows it, marked as a historical value, with a line saying where Position now
   comes from. Better than a blank field and a mystery.
4. **The typed values seed the Position dimension** through the same operator-run import that
   **D5**/§4.4 describes for grades — profile, propose, dry run, apply — because what the right
   positions are is a business question and not a migration's to guess.
5. **The `job-title` lookup list is retired**, through `ILookupListService`'s existing retire path
   (ADR-0014: an entry is only ever retired, never deleted). It is bound to nothing, so no screen
   changes.
6. **The relabelling.** Every user-facing *Job title* / *Designation* / *Org role* becomes
   **Position** / **المنصب**, in both PO files, in the slice that owns the screen. F6 owns the
   employee record and the job details section; F5 owns the dimension type's own shipped name.

The `EmployeeSections` README already anticipated step 1: *"it will: the FRS work on positions moves
grade off the job details section."* The same is now true of the job title beside it.

---

# D23 — the Organisation menu, as the product owner wants it

Recorded 13 October 2026 by Mohsin Hasnain, after reviewing the slice F2 build.
**Status: product owner requirements. These override the earlier F2 brief, D3's proposed shape and
D17's remedy.** Written up as **ADR-0024**.

## D23 A The organisation designer is unchanged, and first

Slice F2 moved *Organisation designer* from the top of the Organisation menu to a new *Settings*
sub-group. Nothing asked for that. It is **back at the top**, with the same click path
(Organisation → Organisation designer), the same screen and **every card action unchanged** —
`add`, `rename`, `move`, `cancelmove`, `merge`, `sethead`, `placeemployee`, `viewemployees`,
`retire`.

`OrganisationMenuTenantTests.TheDesignerCardActionsAreUnchanged` asserts the action list by name and
`TheDesignerEntryStillOpensTheDesigner` clicks the entry in a real browser, so "unchanged" is a
statement rather than a hope.

## D23 B One entry per dimension *name*, not per dimension *type*

**Do not share the demo dimension types.** The revert was right — see **D17** as amended, and
ADR-0005: a shared type is a level of every chart that declares it, so each company's chart grew the
others' units.

**Group the menu instead.** Dimension types sharing an **English singular name**, compared
case-insensitively, become **one** entry, captioned with the authored plural. All types named
*Department* are one entry reading *Departments* / *الإدارات*.

**The company-qualified captions go.** *Zenith Departments* and *Crescent Departments* were an
attempt to disambiguate the menu by renaming the data, and a tenant should not read its own company
name on every menu entry. Every demo recipe now authors the plain plural, identical across the types
that share an entry.

The grouping is a **menu concept only**. It is not stored, it owns no records, and three Department
types remain three types with three separate trees.

## D23 C Each dimension expands, and a value opens its own page

| | |
| --- | --- |
| **"All *plural*"**, first | The existing Tree/List screen, showing the values of **every** type in the group. When more than one type is grouped it gains an **Organisation column** and an **Organisation filter** — the filter is on the dimension type, because each type in a group is one organisation's |
| **then the active values** | By name **in the reader's language**, alphabetical. Where the group spans more than one chart each value is suffixed with its organisation in brackets — *Civil (Zenith Engineering & Construction)* |
| **at most 15**, then **"Show all (N)"** | N is the whole dimension, which is what the list screen behind it contains. Sorted then cut, so *Show all* means "and the rest after these" |

**Clicking a value opens that value's own page**: code, name EN/AR, alias, dimension, parent, status
and dates, head, employees (with a link to the full employee list), and **Edit / Move / Retire**
buttons. Read-only — every button links to the screen that already owns the rule, so there is no
second write path.

Every screen is reachable by clicking, in both languages.

**The bracket disambiguates organisations, not values.** Crescent has two departments called
*Credit*, one per branch, and both read *Credit (Crescent Microfinance)*. A name is not unique within
a tenant; a code is, and the list screen shows it.

## D23 D The menu is cached per tenant and invalidated on every write that shapes it

Per ADR-0019 — `CacheSignals.SignalNowAndAfterCommitAsync`, never a bare `ISignal.SignalTokenAsync`.
A value **added, renamed, moved or retired** drops the cached menu, and so does a change to a
dimension type or to a structure's levels.

`OrganisationMenuTenantTests.ANewDepartmentIsInTheMenuImmediately` writes and reads **in one scope**,
which is the half a deferred-only signal cannot serve: the request that adds a department is the
request that then renders the menu.

The date is part of the cache key, because the menu lists the values effective **today**.

## D23 E "Position in the menu" is renamed "Menu order"

**D20** makes *Position* a product noun — the post an employee holds — so a label using the word in
its ordinary English sense is exactly the ambiguity D20 exists to remove. English **Menu order**,
Arabic **ترتيب القائمة**.

## D23 F Order of the Organisation menu

```
Organisation designer
(Organisation tree)        ← position reserved; slice F8
<dimension groups, in the tenant's own fixed order>
Settings
    Dimension types
    Structures
    (Mapping)              ← position reserved; slice F3
```

**The same order in English and Arabic**, which is why positions are explicit numbers and never
`LocalizedString.PrefixPosition()`. The *values* under a dimension are the deliberate exception and
are alphabetical in the reader's own language.

Tested with base + Zenith + Crescent + the demo sample loaded: twelve dimension types, eight entries.

## Open question this raises — Q-l

The organisation bracket uses the **structure's authored name**, so Zenith's departments read
*Civil (Zenith Engineering & Construction)* where D23 C's example wrote *Civil (Zenith)*. Nothing
abbreviates it, on the same reasoning that stopped the plural being derived: a rule that shortens a
customer's company name will shorten one wrongly.

**If a short form is wanted it is a field on the structure, authored like the plural.** Flagged for
the product owner; nothing is built for it.
