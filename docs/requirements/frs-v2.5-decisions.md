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

## D5 Grades and steps — AMENDED BY D20, **SUPERSEDED IN PART BY D26 C**
> **D26 C removes the step.** There is no `grade-step` dimension, no step control and no step on an
> employee. A Position carries a **set** of allowed grades and the employee's **grade** — not a step
> within one — is the dated fact. The three struck-through bullets below are kept so a reader meeting
> "G7-S3" in an earlier document can find out what happened to it.

- ~~"Grade Steps" is one dimension with its own tree: grades as parents, steps as children
  (G7 -> G7-S1..S5). Refined by **D7**: one dimension in the menu and on screen, two dimension
  types underneath.~~ **Struck out by D26 C.** One dimension, **Grade**, with no step level.
- A position **references** a GRADE. **Amended by D19:** a reference and not a parent — see **D21**.
  **Amended again by D26 C:** a **set** of grades rather than a range.
- ~~The employee holds a STEP, chosen only from the steps of the position's grade(s); dated
  (G7-S2 from Jan 2025, G7-S3 from Jan 2026). Steps optional for clients that do not use them.~~
  **Struck out by D26 C.** The employee holds a **grade**, chosen only from the position's allowed
  grades, dated. What was optional about steps is now simply absent.
- Promotion = new position or a new grade within the same one (**D26 D**); HR picks a grade the new
  position allows.
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

## D7 Grade Steps is two dimension types behind one menu entry (Q-a) — **SUPERSEDED BY D26 C**
> **D26 C ships one dimension type, `grade`, and no `grade-step`.** The menu entry reads **Grades**
> / **الدرجات**. The answer below was the right one to the question as it stood — "is a grade a node
> at depth 0, or a type of its own" — and D26 removes the question by removing the second level. The
> reasoning is kept because its argument against depth rules is the reason a *group* above SE-1..SE-3
> is an ordinary grade value on a self-nesting axis rather than a level.

Option B of the design. Two dimension types — `grade` and `grade-step` — with `grade -> grade-step`
in the containment map and `grade-step` mapped to `grade`. The menu carries **one** entry,
"Grade Steps", whose tree shows grades at the top level and steps indented under them.

This is a liberal reading of D5's "one dimension": one dimension **on screen and in the menu**,
built from two dimension types. Taken because the alternative — one self-nesting type where a grade
is a node at depth 0 — makes "is this a grade or a step" a depth rule, which breaks the first time a
customer puts a band above their grades.

## D8 Grade attaches to the position, not the person (Q-b) — **AMENDED BY D26 C AND D26 G**
- A position carries a **set of allowed grades** (D26 C), not a mapping with a cardinality.
- ~~**Grade ranges are allowed**: a mapping set to `OneOrMore` lets a position carry G7 and G8.~~
  **Replaced by D26 C**: a set, which expresses the non-contiguous case a range cannot.
- ~~**Steps are optional by not configuring them.**~~ **Struck out by D26 C** — there are no steps.
- **The employee's dated grade is an `EmployeeAssignment` on a Grade axis**, not a field on the
  employee. Dated, historical, audited and exportable with no new storage. D26 G adds that it is
  chosen **only** from the position's allowed grades, and that the control is disabled until a
  position is chosen.

What this decision got right and D26 keeps: **the grade belongs to the post, and the person's grade
is a dated assignment rather than a field.**

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

## D18 Slice order — REVISED BY D19 AND D20, **SUPERSEDED BY D27**
> **D27 carries the current order:** `F1 ✓ → F2 ✓ → F5+F7 → F6 → F8 → F9`. F3 is dropped, and F5 and
> F7 are one slice. The table below is the order as it stood on 13 October.

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

## D21 A position's grade is a reference, not a parent — **CONFIRMED, AND AMENDED BY D26 C**

> **Confirmed.** Question Q-k is answered: a grade is **never** a level of the organisation tree.
> **Amended** in one respect only — the reference is a **set** of grades, not a range, and
> `OneOrMore` as a mapping cardinality goes with ADR-0021 (**D27**). The rest of this decision is
> what D26 C rests on.

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

---

# D24 — polish from manual testing, and the tree on a value's page

Recorded 14 October 2026 by Mohsin Hasnain, after testing slice F2 on an existing tenant.
**Status: product owner requirements.** The organisation designer stays unchanged, and the
"All *plural*" screens stay as they are (list view by default). ADR-0024 is amended.

## D24 A Existing tenants get the shipped plurals, without anybody editing anything

A tenant created before the dimension type editor had a plural field reads **Department**,
**Division** and **All Department** down its whole Organisation menu. Every one of those is the
correct fallback and every one of them looks wrong.

**`Migrations.UpdateFrom11Async` fills an unauthored plural from a frozen table** —
`StandardDimensionPlurals` — for the names the product ships:

| | |
| --- | --- |
| Division → Divisions · Department → Departments · Section → Sections · Team → Teams | Region → Regions · Branch → **Branches** · Project → Projects |
| Cost centre → Cost centres · Position → Positions | Grade step → Grade steps · Business unit → Business units |

with the Arabic plural looked up by the **Arabic** name, so a tenant whose Division is قطاع gets
القطاعات and one whose Division is قسم gets الأقسام. Keying Arabic off the English name would have
handed one of them the other's noun.

**"Unauthored" means blank *or the singular repeated*.** The second is the one that matters:
`UpdateFrom10Async` cleared session D's guesses by writing the singular as a visible placeholder, so
that is the state every tenant that has upgraded is actually in, and a step that only filled blanks
would have reached none of them. A plural somebody typed is never touched (ADR-0009).

**For any other name, the caption is phrased rather than guessed.** Nothing derives a word — the
whole of ADR-0024. A dimension with no plural keeps its singular as its entry, and the entry that
opens the whole dimension reads **"All Workstream units"** rather than *All Workstream*, which is
broken English the moment the singular is put in a sentence.

## D24 B Two values of the same name are told apart by their parent

Crescent has two departments called **Credit** — one in the Lahore branch, one in Karachi — and two
called **Operations** beside them, because a code is unique within a tenant and a name is not. The
menu listed *Credit (Crescent)* twice.

**When two values in a group read the same, the caption gains the parent:** *Credit – Lahore Main
Branch*. Asked **per language**, because a tenant can translate one of two same-named units and not
the other, and a disambiguator where there is nothing to disambiguate is noise on every row.

**The organisation bracket stays, but only when the group spans more than one structure**, and it
now carries the structure's **short name**: *Civil (Zenith)*, not *Civil (Zenith Engineering &
Construction)*.

`StructureDocument.ShortName` is a new authored bilingual field on the structure editor — **this is
question Q-l, answered**. Authored and never derived, for the reason the plural is: a rule that
shortened a company name would shorten one wrongly, and the wrong one would be a customer's own
name on every row of their menu. Empty falls back to the full name. The reference recipes ship
*Zenith*, *Crescent* and *Demo*.

**The full caption is the link's tooltip**, so a sidebar that truncates still answers on hover.
Orchard Core 3.0.1's `MenuItem` has nowhere to put a title — verified with `tools/api-probe` — and
the admin theme's templates are compiled into its own assembly, so this is a script this module
ships and requires on admin pages, not a change to Orchard.

## D24 C The value page shows the Arabic name where it belongs

The Arabic name rendered as a `dir="rtl"` **block**, which fills the column and pushes its text to
the far edge, so it read as something detached from the Name row. It is a **span** now, under the
English name, as the list screens already do it.

## D24 D The value page separates who is here from who is below

**"0 here" was misleading** on a division whose people sit in its departments — and the engine
guarantees that case, because a unit with units under it may not hold employees at all.

The page now reads **"N directly · M including units below"**, each with the correct plural form in
both languages (Arabic has six), and each number linking to the employee list — the second with
**Include sub-units** ticked, so the list agrees with the number beside it. A number of zero is not a
link.

**M is distinct, not summed**: one person placed in two units below this one is one person, which is
D1's `DISTINCT EmployeeId` rule.

## D24 E NEW — a tree of everything under a value

Opening a value that has children shows **"Units under *name*"** below the details:

- **Every dimension, not just this one** — the opposite of the dimension's own Tree/List screen,
  which Rule 2 scopes to one dimension. A reader who opened *Corporate* wants the departments in it
  and the sections in those.
- As at today, **active only by default**, with an **Include retired** toggle. Retiring a unit closes
  both its record and its placement, so showing retired ones is a different, undated question —
  `IDimensionGraphService.GetDescendantsEverAsync`.
- Indented, **expand/collapse per node** plus **Expand all / Collapse all**.
- Each row: the **name**, linking to that unit's own page; its **dimension**; and its **employee
  count including sub-units**, with the direct count on hover.
- **A leaf shows no section at all** — not an empty table and not a "nothing here" line.
- Works in Arabic and RTL.

**Batched, with no per-node round trips.** One closure read for the subtree, one batched
ancestor-path read for every node's parent, one batched direct headcount, and one read of the
subtree's assignments rolled up in memory as sets of employee ids. The tree is capped at 500 units
and the roll-up at 5,000 placements; both say so on screen rather than printing a number that is
quietly short.

The same row contract as the dimension list's tree view — depth, parent and child count as data
attributes — so **one collapse script serves both screens** rather than a second being written.

---

# D25 — three views of a value's "Units under this one"

Recorded 14 October 2026 by Mohsin Hasnain, before committing the F2 polish.
**Status: product owner requirements.** ADR-0024 is amended again.

**Two screens do not change, and both are now guarded by a test.** The **organisation designer**
stays exactly as it is. The **"All *plural*"** screens stay exactly as they are — no new views
there. `UnchangedScreensTenantTests` asserts both: that the list screen has none of the value
page's controls and still has its own five-button toolbar and Tree/List toggle, and that the
designer still renders its editable cards with their ⋯ menus and its own nine card actions.

## D25 A A switch at the top right of that section

**Hierarchy | Tree | List**, in that order, the active one highlighted. It appears only where the
section does, so a **leaf value has no section and no switch**.

## D25 B List

A flat table of every descendant: **Code, Name** (link to its page), **Dimension, Parent, Status,
Employees** (including units below). **Sortable by column** — the headings are links carrying the
whole state, sorted on the server, so a sorted list is a URL somebody can send and it works with no
JavaScript. No column named means **tree order**, which is what the other two views show.

The **Parent** column is the point of this view: it is flat, so the indentation that carries
containment elsewhere is not there to carry it.

## D25 C Tree

Unchanged — today's indented rows with expand/collapse and Expand all / Collapse all.

## D25 D Hierarchy

A **read-only** top-down org chart. The value itself is the root box; one box per descendant,
connected by lines. Each box shows the **name** (link to that value's page), **code**, **dimension**
and an **employee badge** (including units below, direct count on hover).

- Expand/collapse per node, **zoom − / Fit to screen / +**, horizontal scroll for wide charts,
  mirrored in Arabic/RTL.
- **No editing actions and no ⋯ menus.** Editing stays in the organisation designer and on the
  value pages. The menu element is *absent* rather than hidden — the same way the designer treats a
  reader without permission.

**The designer's layout is reused and its behaviour is not.** The boxes are the designer's own
`li.designer-node > .designer-card + ul.designer-children` markup inside a `.designer-surface`, so
the connector lines, the row levelling and the RTL mirroring all come from
`organisation-designer.css` — every rule of which is already written in logical properties. Three
new CSS rules were added and **no existing selector was touched**. The designer's *script* is not
reused: it drags, fetches and edits, none of which a read-only chart should be able to do, so the
expand, zoom and fit are a few lines in `workmate-dimension-records`.

## D25 E Common

| | |
| --- | --- |
| **Default** | **Tree** — what the section already was. A reader who had learnt it should not have to find it again |
| **In the URL** | `?view=hierarchy\|tree\|list`, so a view is linkable and the back button returns to the one before |
| **Remembered** | The last choice is kept in `localStorage`, in `try`/`catch`. A URL that names a view is an explicit choice and is **never** overridden; a remembered one is applied with `location.replace`, so restoring a preference does not put a page the reader never asked for into their history |
| **Include retired** | Applies to all three |
| **Reads** | Batched, through the closure index, nothing per node. One subtree read feeds all three views — the hierarchy's nesting is shaped from the same rows as the tree, so they cannot disagree about what is under what |
| **Labels** | Bilingual: *Hierarchy* / **الهيكل**, *Tree* / **شجرة**, *List* / **قائمة** |

Tested on a chart of **fifty-odd units across four levels**, in all three views.

---

# D26 and D27 — positions, grades, access roles, and the end of the Mapping slice

Recorded 14 October 2026 by Mohsin Hasnain, from the manager's review of the F2 polish.
**Status: product owner decisions.** Both override earlier recorded decisions, and each names what
it supersedes. Binding for design and implementation.

## D26 Positions, grades, users and access roles

### D26 A One concept, one word

**Position = Designation = Job title.** On screen it is always **Position**; in Arabic
**المنصب**. This restates **D20** and adds nothing to it — it is here because the manager asked the
question again, and the answer should be findable from the decision that was asked about.

### D26 B Orchard Roles are "Access roles", and they are not Positions

Three things a customer runs together, kept apart in the product:

| | What it is | Where it lives |
| --- | --- | --- |
| **Position** | the post a person holds — HR Manager, Labourer | the Position dimension |
| **User** | a login | Orchard's user store |
| **Access role** | a permission set | Orchard's roles, relabelled |

**On screen, Orchard Roles read "Access roles" / أدوار الصلاحيات.** The bare word *Role* is exactly
the ambiguity D20 exists to remove: a customer saying "his role is HR Manager" means his position,
and a customer saying "give him the HR role" means his permissions. Neither sentence is wrong; the
product has to stop using one word for both.

A Position carries no permissions and an Access role carries no headcount. What joins them is a
person: an employee holds a position and, if they are given system access, a user holds access roles.

### D26 C Grades belong to Positions — SUPERSEDES D5, D7 and part of D21

**What is superseded.** D5 made *"Grade Steps"* one dimension with grades as parents and steps as
children (G7 → G7-S1..S5). D7 built that as two dimension types, `grade` and `grade-step`, behind
one menu entry. D21 let a position reference a grade **range** (G7–G8) through `OneOrMore`.

**The decision: there is no grade step, and a position has a *set* of allowed grades.**

- Grades are values of **one** dimension — **Grade** — created like any other dimension value:
  SE-1, SE-2, SE-3, PSE-1, PSE-2, PSE-3, HRM-1, HRM-2, HRM-3, LAB-1, LAB-2.
- **Each Position is given a set of allowed grades.** Software Engineer → SE-1, SE-2, SE-3.
  Principal Software Engineer → PSE-1, PSE-2, PSE-3.
- **A position's grades are its levels.** There is no second level below a grade, so the
  `grade-step` dimension type is **not built** and the *Grade Steps* menu entry does not exist.

**What survives from D21, and it is the important half.** An allowed grade is a **reference, not a
parent**. It never enters the closure index, no grade appears on the organisation tree, and no
employee rolls up to a grade through their position. What changes is only the shape of the
reference: a **set** of grades rather than a range. A range says "any grade between these two",
which needs grades to be ordered; a set says which grades, which needs nothing of them and expresses
the non-contiguous case a range cannot.

**Optional grouping, never a level.** A tenant that wants an *SE* heading above SE-1..SE-3 can have
one, because grades sit on their own axis and the Structures containment map can permit a Grade
under a Grade. That is a tenant's choice and the shipped data is flat. It is **not** the step
concept returning under another name: a group is a grade value like any other, and a position may
allow it or not.

### D26 D One Position per job — SUPERSEDES the "three HR Managers = three Positions" note

**What is superseded.** A note raised in review — *"three HR Managers are three Positions"* — read a
Position as one **post** rather than one **job**. It is not written down in this file, which is why
it is written down here: it had been said often enough to be acted on. It also pulls against **D2**'s
own *"a position holds MANY employees"*, and D2 is the half that survives.

**The decision: people doing the same job share one Position, and their level is their grade.**

- *HR Manager* is **one** Position, with allowed grades HRM-1/HRM-2/HRM-3 and an approved headcount
  of 3. Each of the three holders has their own grade.
- *Labourer* is **one** Position with an approved headcount of 44.
- A new Position is created for a **different job**, never for another person doing the same one.

This is what makes approved headcount mean something. A Position per person makes every headcount 1
and every vacancy 0 or 1, which is a column that can only ever repeat what the row already says.

### D26 E A Position has an approved headcount

`Approved headcount`, defaulting to **1**. **Filled** is the number of employees holding the
position on the date; **Vacant** is approved − filled, shown as a negative where a position is
over-established rather than clamped to zero. Undated, per **D15**, and still a stated limitation.

### D26 F Position is a drop-down on the employee, never free text

- The employee's Position is chosen from the Positions created in the **Position dimension**:
  active as at the effective date, retired ones hidden, searchable, each option showing its **name**,
  its **unit** and its **filled/approved** count.
- **No free-text position anywhere** — not on the employee, not on a section, not in a form.
- **A full position (filled ≥ approved) warns and is allowed.** Refusing would stop the product
  recording what an organisation has actually done, which is not the engine's call to make. The
  warning is the point: somebody should know.

### D26 G Grade is a drop-down of the chosen Position's allowed grades

- The Grade control lists **only** the chosen Position's allowed grades. It is **empty and disabled
  until a Position is chosen**, and it is refreshed when the Position changes.
- **The employee's grade is dated**, like every other fact about where they sit.
- **Changing the Position requires choosing a grade of the new Position.** A grade of the old one is
  not carried across, because it is not one of the new position's levels.
- **Removing a grade from a Position an employee currently holds warns and never breaks that
  employee's record.** The stored grade stays and keeps resolving; what changes is what is offered
  next time.

### D26 H Holding a Position is what puts an employee in the organisation

Confirms **D2** and FRS §1.2. Department, division, business unit and cost centre are **derived**
from the Position's placement; **Reports to** comes from the Position's reporting line. A main
position and additional ones are allowed, with an allocation percentage, which is the existing
`EmployeeAssignment` split. **The employee side is built in F6**; F5+F7 prepares and tests the two
reads it needs and the two controls it is drawn with.

### D26 I "Give system access" is optional, and is its own slice

A toggle on employee creation: a new or existing **User**, **one User ↔ one Employee**, existing
**Access roles** or a new one where the operator holds *Manage access roles*. Login is **disabled
until the employee is Activated**, and disabled again on **Suspend** or **Exit**. Changing somebody's
position raises a **"review access roles"** reminder rather than changing anything by itself, because
what permissions a post implies is a customer's policy and not a derivation.

**Built in the security slice**, not in F5+F7. Recorded here because it is the reason D26 B draws the
line it does.

## D27 Slice F3 is dropped; ADR-0021 is withdrawn

**The decision: the Settings → Mapping screen is not built.**

The **Structures containment map (ADR-0010) stays the single source of truth** for which dimension
may sit under which. The Add form's **Place under** picker and the **Organisation designer** keep
enforcing it through `ContainmentRules.Decide`, exactly as they do today.

**ADR-0021 is marked *Withdrawn — superseded by D27*, and its text is kept.** What it designed —
`StructureDocument.Mappings`, `MappingCardinality`, Rule 3 in `IDimensionValidator`, the
`StructureIndex.MappingCount` migration — was **never built**, so no tenant and no line of code
changes when it goes. The text stays for the reason ADR-0020's does: a reader meeting "mapping is
containment plus a cardinality" in the design document needs to find out what happened to it, and the
analysis of *why* obligation cannot live on the containment grid is correct and worth having if
required parents are ever asked for again.

**What goes with it.** FRS Rule 3 — *"every new value must be assigned to one value from each mapped
parent before it can be saved"* — is **not built**. A parent stays optional on every dimension, which
is what the engine does today and what the unplaced panel already reports on. Rule 4 is unaffected: a
dimension the containment map gives no parent for shows no parent step.

### The new slice order

```
F1 ✓ → F2 ✓ → F5+F7 (Positions and Grades) → F6 (employees via positions) → F8 → F9
```

| | Slice | Changed by D26 / D27? |
| --- | --- | --- |
| **F1** ✓ | Alias, Status, the two views | — |
| **F2** ✓ | The menu, value pages, the three views | — |
| ~~F3~~ | ~~Mapping~~ | **Dropped by D27** |
| ~~F4~~ | ~~The DAG~~ | Deleted by D19 |
| **F5+F7** ✓ | **Positions and Grades, as one slice** | D26 merges them: a position's allowed grades cannot be built without grades, and grades have little to belong to without positions. **Grades first.** Shipped 14 October 2026, with **ADR-0022** (Position is the one master) and **ADR-0028** (a Position sits on two axes) |
| **F6** | Employees join through a position | Gains D26 F and D26 G — the two drop-downs — and keeps D22 |
| **F8** | The Organisation tree enquiry report | Was "runs after F3"; F3 is gone, so it runs after F6 |
| **F9** | Demo recipes, export and Delete | — |

**ADR-0026 is restated** rather than written as D5/D7 described it: grades are a dimension, there is
no grade-step dimension, a position's allowed grades are a set of references, and the employee's
dated grade is an assignment on the Grade axis.

### What F5+F7 actually shipped, and what it left

**Built, 14 October 2026.** The Grade dimension and its axis; the reference table that a position's
allowed grades live in (`UpdateFrom12Async`); the declared **role** on a dimension type, which is how
the product finds its Position and Grade dimensions without guessing from a code or a name; the
Position dimension with its approved headcount; the **reporting-line** axis (**ADR-0028**);
`IPositionService` and the two reads F6's drop-downs are built on; the Add/Edit form's *Reports to*
and *Allowed grades*; the Positions list's Approved / Filled / Vacant / Allowed grades; a position
page that opens on its reporting line with a filled/approved badge; and the Zenith demo.
**ADR-0022** and **ADR-0028** are written, both *accepted, pending second review*.

**Deliberately not built, and both are F6's.** `EmployeeAttachableDimensionTypeIds` is unchanged, so
employees still attach where they did and **every position's Filled count is 0** — repointing them
here would have moved every demo employee's placement as a side effect of adding positions.
`JobDetails.JobTitle`, `JobDetails.GradeCode` and the `grade` and `job-title` lookup lists are
untouched, per **D22**.

**Still owed.** **ADR-0023** — *WorkMate ships the pre-configured dimension set* — is **not** written:
F5+F7 shipped Position and Grade because the slice needed them, and the other eleven rows of figure
2 have not shipped, so the ADR would be describing something that does not exist. It is owed by
whichever slice ships the rest. **ADR-0026** is owed by **F6**, which is where the employee's dated
grade and the unbinding of `GradeCode` land.

---

# D28 — F6 design review: employees join the organisation through a position

Recorded 15 October 2026 by Mohsin Hasnain, answering the seven open questions in
`docs/design/f6-employees-via-positions.md` §11.
**Status: product owner decisions. Binding for slice F6.**

## D28 A Reports to names a holder only when there is exactly one — and the line manager is seeded from it

The proposal's rule is accepted: **Reports to** shows the *parent position* on the reporting line,
and names the holder only when the position has exactly one. A vacant parent shows the position with
**"(vacant)"**. **Several holders shows `Reports to: <position> (n holders)`.**

Nothing guesses which of several holders somebody answers to. What the product does instead:

- **One holder** → the employee's **Line manager** is **defaulted to that holder** when the position
  is assigned. It is a default, not a derivation: the field stays editable and nothing overwrites a
  value somebody has set.
- **Several holders** → the Line manager picker **lists the parent position's current holders
  first**, with everybody else still searchable below them. The product offers the likely answer
  without asserting it.

`EmployeePart.LineManagerEmployeeId` is the field; it already exists. **Reports to** and **Line
manager** are two rows on the page, because they are two facts: one is about the post, one is about
the person.

## D28 B The employee's grade is a column on the assignment row

Option (ii) of §2.4 is accepted: a **nullable `GradeRecordId` on the assignment row**, not a second
assignment on the grades axis. The deciding argument is D26 C's — an assignment on the grades axis
would put employees *under* grades in the closure index, which D26 C forbids in terms.

Written up as **ADR-0026**, *accepted pending second review*.

## D28 C Existing data: nothing moves

Options **(a) + (c)** are accepted. The migration adds Position to the attachable types and **moves
nobody**. Existing unit placements stay valid, as history and as the present; HR assigns positions
going forward; the demo tenants get theirs through a **separate additive recipe**.

Option (b) — a generated placeholder position per unit — is **rejected**: it writes dated rows about
real people that nobody asked for, invents master data a tenant cannot delete (ADR-0016), and
contradicts **D26 D**, since a placeholder per unit is a position per *unit* and is neither a job nor
a post.

Two things make the resulting mixed state liveable, and both are required:

- a **"No position yet"** filter on the Employees list, so HR can work the backlog through;
- a **one-sentence explanation on every affected employee's page** — *"No position assigned. This
  employee is placed directly in &lt;unit&gt;."* — rather than an empty block.

Written up as **ADR-0029**, *accepted pending second review*.

## D28 D The organisation columns are the tenant's own dimensions

Accepted as proposed. **One Organisation path column**, each step labelled with that ancestor's own
dimension name, plus **optional per-dimension columns** a tenant turns on — the way the dimension
list's attribute columns already work. Four fixed columns named Division / Department / Business unit
/ Cost centre would be right for Zenith and wrong for Crescent, whose chart has Regions and Branches.

## D28 E Sorting and filtering by Position or Grade is F6c

Accepted. The columns ship unsorted in F6b; sort and filter land in **F6c**, with a decision then
about whether they justify a denormalised column on `EmployeeIndex` — which would be derived data on
an index and needs its own argument.

## D28 F The job-title importer is not in F6

Accepted. F6 ships only the **notice** — *"you have N distinct job titles and no positions"*. The
profile / propose / dry run / apply importer **D22 step 4** describes is its own slice. What the
right positions are is a business question and not a migration's to guess.

## D28 G Session B resumes straight after F6

Accepted. **B3 → B7 run after F6 and before F8/F9.** F6 is the slice that settles the shape of the
employee record — fixed core, derived organisation, sections — and B3's compiler has to be written
against a settled shape. F8 and F9 read that shape, so building them first would mean reading one
that is about to change.

---

# D29 — Three engineering decisions accepted with D28

Recorded 15 October 2026 by Mohsin Hasnain, with **D28**. **Status: product owner decisions.**

## D29 A The ADR-0006 handler repair is pulled forward into F6

`EmployeeHandler` lacks the `ActivatingAsync` repair `DimensionRecordHandler` carries. The form
designer's own design (§5.2) flags it as a slice B3 task and names the failure: *a recipe can
publish a form and then create employees in the same shell scope*, and `IContentManager.NewAsync`
reads the **cached** definition, producing a content item with no parts welded that silently
discards everything set on it.

**F6 fixes it**, because F6 is the first slice with a recipe that creates employees in a scope where
content definitions have just changed — so it is the first slice that can reproduce it. A silent,
high-likelihood data-loss failure is not left on a later slice's list when an earlier slice can
prove the fix.

## D29 B Position, Grade and Reports to are forbidden on designer-added fields

`EmployeeFieldNames.ForbiddenOnSections` already refuses `Department`, `Division`, `CostCentre`,
`Unit` and friends on any section or form field, because the employee record holds no department by
design. **F6 adds `Position`, `PositionCode`, `PositionId`, `Grade`, `GradeCode`, `GradeId` and
`ReportsTo`** to that set, so a tenant cannot build a shadow copy of the facts this slice derives.

The refusal **must say why**: *"Grade is part of the employee's position; see Placement"* rather
than *"forbidden field name"*. A guard that refuses without explaining teaches somebody to work
around it.

## D29 C The organisation block is fixed core, and the section list stays a loop

The derived block — Position, Grade, the unit ancestry, Reports to — is part of the **fixed core**
and is rendered by the page's own shape. It is **not** a seventh entry in `EmployeeSections.All`,
and nothing in F6 hard-codes the section list or its order.

`EmployeeSections` already says why: *"these are the standard sections, not the only ones"*, and its
stereotype exists *"so that a picker, a report or session B's designer can ask for 'an employee
section' without a hard-coded list of six type names that would be wrong the moment a tenant adds a
seventh."* Writing the organisation block as a section is the mistake B3 would then have to undo.

The block is built **collapsible and first**, so slice B4's screen work does not have to restructure
a page that is about to grow `EmployeeProfilePart` and designer-added sections beneath it.

---

# D30 — An employee joins the organisation only through a position

Recorded 15 October 2026 by Mohsin Hasnain, with slice F6b.
**Status: product owner decision. Binding.** Written up as **ADR-0030**.

## The decision

**An employee joins the organisation ONLY through a position.** The position's place in the
organisation structure gives the employee their department, division, company and so on. **Nobody
picks an organisation unit for an employee.** The organisation chain is **computed** from the
position, never stored separately.

This is **D2** and **D26 H** stated as a rule the product enforces rather than a shape it permits.
D2 said employees *join the organisation by being assigned a position*; until now the engine also
allowed them to be placed directly in a unit, and both were valid. D30 closes that: on a structure
that has positions, a unit is not somewhere an employee goes.

## How it reconciles with ADR-0029

**It does not reopen ADR-0029, and the distinction is between a new placement and an existing one.**

ADR-0029 decided that the F6 migration **moves nobody**: a tenant's existing unit placements stay
valid, as history and as the present, and HR assigns positions going forward. That decision stands
in full. D30 is a rule about what may be **written from now on**, not a judgement on what is
already there.

| | Existing unit placement | New placement |
| --- | --- | --- |
| On a structure **with** positions | **Kept.** Shown as *"No position yet — &lt;unit path&gt;"*, with a **Choose position** action | **Refused** on a non-position node, server-side |
| On a structure **without** positions | Kept | Allowed, exactly as today |

So the two decisions answer different questions and neither bends. ADR-0029 is why nothing moves;
D30 is why nothing new arrives the old way. The gap between them — a tenant holding both shapes at
once — is the state ADR-0029 already describes and the one the *"No position yet"* label exists to
make legible. **F6d closes it** by moving the Zenith demo people onto positions, after which the
recipe sets `zenith-org`'s employee-attachable set to positions only.

## How it reconciles with D26 and D28

**D26 H** is confirmed, not amended: *"Department, division, business unit and cost centre are
derived from the Position's placement."* D30 adds that there is no longer another way to get them.

**D26 B** is the reason this is safe to make exclusive: a Position carries no permissions, so
routing everybody through one does not route anybody's access through one. The access-roles notice
(D26 I) stays a reminder.

**D28 D** already settled how the derived chain is shown: **the tenant's own dimension names**, one
row per ancestor, never four hard-coded words. D30 makes that block the only answer rather than one
of two, which is what lets it be rendered as a fact instead of a field.

**D28 C's two liveability requirements become more important, not less.** The *"No position yet"*
filter and the one-sentence explanation on an affected employee's page are what stop D30 reading as
"the product has lost my data". They are required by D28 C and restated here.

## Where the rule is applied

D30 is one rule and it is read in one place — `IPositionService.PermitsEmployeesAtAsync`, which
combines the structure's attachable set with this decision. Four callers resolve it there: the unit
and position **pickers**, the **Place screen**, the organisation **designer's card menu**, and the
**validator** on the write.

The designer is the one that had to change in F6b and the reason is worth recording, because it is
the shape of mistake this decision invites. A card asked `StructureDocument.PermitsEmployeesAt`,
which knows the attachable set and nothing about what a Position is. ADR-0029 deliberately *keeps*
departments in that set so existing rows stay valid — so a department's card went on offering
**Place an employee here…**, the form opened, and the save came back *"choose a position instead"*.

> On a structure that has positions, a unit or department card no longer offers the action; a
> position's card offers it and opens the Place screen with that position already chosen. Nothing
> else in the designer changes — the cards, the layout, the other menu entries, zoom and drag are
> untouched. On a structure with no positions, nothing changes at all.

### Addendum, slice F6b polish 3 — a reporting line and a grade ladder are not places

**Date:** 17 October 2026. **Rests on:** ADR-0028, ADR-0026, D26 C.

The rule above asks *"may an employee be attached at this node?"* and presumes the structure is
somewhere people go. Two structures are not, and both got through:

- **The reporting line**, which got through by being right about everything else. Every node on it
  **is** a position, so the test "on an axis with positions, is this node a position?" answered
  **yes** for every card. The Zenith reporting chart offered **Place an employee here…**, and the
  old Place screen opened — headed *"Place an employee in CEO › Director Technology › Manager
  Technology"*, with an allocation percentage and a **main placement** checkbox — for the axis
  ADR-0028 makes a post's *answer-to* chain rather than anywhere somebody sits. The write would
  have gone through.
- **The grade ladder**, which got through by the opposite door: no Position level at all, so the
  rule said nothing about it, and the attachable set — unconstrained, as any structure written
  before these rules is — said yes. D26 C and ADR-0026 make a grade a reference **carried on the
  placement row**, not a node anybody is placed at; an assignment here would roll employees up
  under grades in the closure index.

**The decision.** A structure employees may be placed on is one that is **neither**:

| Shape | Not a place because |
| --- | --- |
| every level is a **Position** | it is a reporting line — what each post answers to (ADR-0028) |
| every level is a **Grade** | it is a grade ladder — a grade is a reference, not a placement (D26 C) |
| **no levels at all** | there is nowhere on it to put anybody |

**Asked of the shape, never of the code or the name.** A tenant may rename anything and the demo
tenants prefix everything, so a rule written against the word *"reporting"* would hold on the demo
data and nowhere else.

**Still one rule, read in one place.** The shape test is stated once and
`IPositionService.PermitsEmployeesAtAsync` asks it first — so the **designer's card menu**, the
**pickers** and the **Place screen** stop offering it — and `IDimensionValidator` asks the same
question on the **write**, so the screen, the `employee-assignments` recipe step and any later
import refuse it in the same words. A designer that merely stopped offering the action would leave
the old shape reachable by every other route, which is exactly how it was reachable here.

**Blocking, not advisory** (`DimensionRule.StructureDoesNotHoldEmployees`). This is not an unusual
shape somebody might legitimately mean, the way a departmental secretary under a container unit is:
the numbers it produces stay plausible while being wrong.

**Nothing already written is touched.** ADR-0029: the rule runs on the write. An audit of the
development tenant found **no** such placement to begin with — all 176 assignments are on
`zenith-org`, and `zenith-reporting` and `zenith-grades` hold none.

**An ordinary organisation axis is untouched**, including a position-based one: its levels are
divisions and departments **and** positions, so neither test fires. A tenant with no positions is
likewise unaffected.

## What does not change

- **Allocation and the main-placement flag stay in the model.** A second concurrent position — an
  acting assignment, a secondment — is the matrix case `ReallocateAsync` already expresses (D26 H),
  and D30 says nothing against it. What F6b does is **hide both controls on the ordinary screen**,
  which always means main and 100 per cent. Building the second-position screen is a later slice.
- **Unit heads are untouched.** ADR-0012 stands: a head appointment is its own dated record, carries
  no allocation, is counted by no headcount, and the head need not be a member of the unit they
  head. A head is still appointed at a **unit**, not at a position. **D11** moved *"should a head be
  a position"* to prompt 5 and **ADR-0027 stays unwritten**.
- **Tenants and structures with no positions keep today's screens exactly.** D30 is scoped to a
  structure that has positions; a tenant that has not built any sees no change at all.

---

# D31 — The line manager follows the position, and a position must say what it reports to

**Status: accepted.** Built in slice F6b polish.
**Date:** 17 October 2026
**Narrows:** D28 A. **Rests on:** D26 B, D26 D, D26 I, D28 A, D30, ADR-0005, ADR-0012, ADR-0028,
ADR-0030.

## The problem, in two halves

**The employee form asked who somebody reports to twice.** A free *Line manager* picker listing
every employee in the tenant A–Z sat **above** the *Job and organisation* block — which had just
derived, from the chosen position's reporting line, who the post answers to. The two controls were
unrelated, and the free one was the one people filled in. A record could therefore assert a line
manager that the reporting line on the same screen contradicted, and nothing said so.

**A position could be saved without saying what it reports to.** The *Reports to* control's empty
option read *"— reports to nobody —"*, so a form nobody had touched looked like a deliberate claim
to be the top of the reporting line. It was not even that: the save wrote **no reporting-line row at
all**, so the post sat on that axis nowhere, every chart drawn from it was short a box, and every
position beneath it answered *"top of the line"*.

## The decision

### A. The line manager follows the position

It lives **inside** the block, after Position, Grade and *Reports to*, and is derived from the
holders of the post above on the reporting line:

| The post above | What the control does |
| --- | --- |
| exactly one holder | **shown, not asked** — *"<name> (<post>)"* |
| several holders | a list of **those holders and nobody else**, and an answer is **required** |
| vacant, or the top of the line | **no line manager**, and it says so |
| no position chosen | hidden, with *"Follows the position."* |

It **updates when the position changes**, which is a server redraw: the block is rendered from the
chart, so following the position means a round trip. The picker asks for one when a position is
chosen — and only then, never while somebody is typing in its search box.

**This narrows D28 A** and is the reason D31 exists rather than being an implementation note. D28 A
said that with several holders *"the picker lists them first and the operator chooses"* and that
with none *"the field is left optional"*; it treated `LineManagerEmployeeId` as a free, independent
fact beside *Reports to*. D31 constrains the list to those holders and requires an answer. What is
**unchanged** is D28 A's core refusal: nothing picks between several holders, because any rule that
chose would invent a line manager the customer never stated.

**The override is what makes the constraint affordable.** A *Change…* link opens the any-employee
picker for a dotted line, a matrix or a secondment — real arrangements a product that refused them
would be refusing to record. It is labelled **"Differs from the position's reporting line"** on the
form *and* on the employee's page, so an exception never reads as the ordinary case.

**Storage is unchanged:** `EmployeePart.LineManagerEmployeeId`, exactly as before. Only which
control writes it, and from what.

> **A limitation stated rather than designed around.** `LineManagerEmployeeId` is **undated**. Every
> other fact in the organisation block has an effective range; an override does not, so changing it
> leaves no record of what it was and no way to ask who managed somebody last March. Dating it is a
> model change and an ADR, and is **not** done here. F6b's brief asked this question directly and
> this is the answer: there is no effective date, and none was invented.

### B. A position must say what it reports to

The control starts **unanswered** (*"Choose…"*), then offers **"— Reports to nobody (top of the
reporting line) —"** as a value of its own, then the positions. Saving with nothing chosen is
refused: *"Choose who this position reports to, or choose 'Reports to nobody' for the top of the
reporting line."*

**"Nobody" needs no new storage.** It is a placement row on the reporting-line axis with a null
parent — exactly what the Zenith CEO has today. The three states live where they always did; what
changes is that the form can no longer leave the question unanswered, and that choosing "nobody"
now actually **writes the row** instead of writing nothing.

**Choosing "nobody" when somebody is already there warns and saves**, naming the post already at the
top. An organisation mid-handover, or one with joint chief executives, is entitled to record what it
has — the same judgement D26 F makes about a full position. It is a separate rule
(`SecondTopOfReportingLine`) because `DimensionError.IsAdvisory` is derived from the rule on
purpose.

**Enforced in the validator**, so the screen, the recipe and any later import refuse it in the same
words (CLAUDE.md rule 5). **The shipped recipe is unaffected**: all eight Zenith positions already
state their reporting line, the CEO's as `"parentCode": null` — which is the test of whether this is
a rule about positions or merely a rule about a form.

## What this does *not* do

- **It does not add a Reports to control to the position's Edit form**, and that is deliberate.
  *Reports to* **is a parent**, on a second axis: changing it is a reparenting, which is a dated
  move under `MoveDimensionRecords` with its own dry run (ADR-0005) — which is precisely why the
  organisation parent is not on that form either. Edit therefore **shows** the reporting line,
  read-only, in three honest states — under a named post, at the top of the line, or **on the
  reporting line nowhere** — and links to the organisation designer opened on that axis, where the
  move already lives. The brief asked for the Edit control to preselect "nobody" for a parentless
  position; that is answered by showing the state instead, because a control there would be an
  undated reparent bypassing a permission.
- **It does not change the organisation designer's layout**, its cards, its other menu entries, its
  zoom or its drag.
- **It moves nobody's data** and adds no migration.
- **Tenants and structures with no positions keep today's behaviour exactly**: no reporting line to
  follow means the free line-manager picker is not a second answer to a question already asked — it
  is the only answer there is, and it stays.

## D31 addendum — a vacant post above, and the one row

**Status: accepted.** Built in slice F6b polish 2.
**Date:** 17 October 2026
**Amends:** D31 (the line manager half). **Rests on:** D28 A, D30.

### The defect

Add employee → Zenith → **Software Engineer**, whose post above is **Manager Technology**, which
nobody holds. The screen said:

> Nobody — this post answers to no one on the reporting line

That is false. A Software Engineer under a vacant Manager Technology answers to the **Director
above the gap** — which is how an organisation works while a manager's chair is empty. The sentence
is true at the top of the reporting line and nowhere else, and D31 had one case covering both.

**And it said it twice.** *Reports to* and *Line manager* rendered as two rows from two models, so
they read as two independent facts and invited a reader to wonder how a post could report to one
place and a person answer to another.

### The decision

**1. The two cases are split.** *Top of the line* and *the post above is vacant* are different
facts and get different sentences. A post at the top reads **"Nobody — top of the reporting line"**;
a vacant post above is named and marked **"(vacant)"**, which is something somebody can act on.

**2. One row.** *Reports to* carries the post above, its state, and whoever answers for it:

| Case | The row reads |
| --- | --- |
| one holder | **Manager Technology — Ali Khan (line manager)** |
| several holders | **Site Supervisor (3 holders)**, with the required picker of only those holders |
| vacant | **Manager Technology (vacant)**, with the acting manager below |
| top of the line | **Nobody — top of the reporting line** |

The *Change…* override link stays under that row, unchanged.

**3. The acting line manager.** When the post above is vacant, the reporting line is walked
**upwards** to the first post anybody holds.

- **One holder** → that person, labelled **"acting (&lt;the post they hold&gt;)"**, so a manager two
  levels up never appears unexplained.
- **Several holders** → the picker of exactly those holders, labelled acting. D28 A's refusal to
  pick survives the walk: nothing chooses between them.
- **Nobody held all the way to the top** → no line manager, and the row says **"No one above holds
  a post yet"** — the posts exist and are unfilled, which is a different statement from answering
  to nobody and is the honest end of the walk.

**Derived, never stored.** The walk is a read over the reporting line, recomputed on every render
like the rest of the block. What is saved is what was always saved — the chosen or derived person
in `LineManagerEmployeeId` — so an acting manager is a person on the record, not a second kind of
fact the model has to carry. No migration, no new column.

**4. All four screens show the same row** — Create, Change position, the employee page and the
Placement screen — from one partial. The page's separate *Line manager* row is gone; it was the
second place the wrong sentence lived.

### What does not change

- **D28 A still decides what may be derived**: one holder is a fact, several is not, and nothing
  picks between them. The walk changes *whose* holders are offered, never whether one is chosen.
- **Storage**: `LineManagerEmployeeId`, still undated. D31's limitation stands — an override, and
  now an acting choice, is a present-tense fact with no history.
- **Tenants with no positions** keep today's free line-manager field.

## D31 addendum 2 — a derived line manager is never written down

**Status: accepted.** Built in slice F6b polish 3.
**Date:** 17 October 2026
**Amends:** D31 and its first addendum (the line manager half). **Rests on:** D28 A, D30, ADR-0029.

### The defect

D31 and its first addendum derived the line manager correctly and then **saved the answer**.
`LineManagerEmployeeId` was written on every create and every placement, in every case, and the
read-only screens showed the saved value. A derivation that is written down is a derivation that
has stopped happening:

- **The post above changes hands.** Ali Khan leaves *Manager Technology*, Sara Noor takes it. Every
  engineer under that post still reports to Ali Khan, for ever, on every screen.
- **A vacant post above is filled.** The acting manager two levels up was saved while the chair was
  empty. The chair is filled; the record still names the person who was standing in.

Neither failure announces itself. There is no later moment at which anybody notices the value is
no longer being recomputed.

### The decision

**1. Only a human's answer is stored.** Three cases, and nothing else:

| Stored | When |
| --- | --- |
| **Chosen** | somebody picked among **several** holders of the post above |
| **ChosenActing** | somebody picked among several **acting** holders, the post above being vacant |
| **Override** | somebody pressed *Change…* and named a person off the reporting line |

**One holder, and one acting holder, store nothing at all.** The chart already says who they are, so
the record repeating it adds no information and subtracts correctness. They are derived on every
read.

**2. One read path.** `IEmployeeOrganisationService.GetLineManagerAsync(employeeId, asAt)` returns
*who*, *on what footing*, and *why*. The Create form, the Edit form, the employee page, the
Placement screen, the lists and any other module go through it. There is no second way to ask who
manages somebody, because a second way is how two screens come to disagree about one person.

The footings are `None`, `Stored`, `Derived`, `Acting`, `Chosen`, `ChosenActing`, `Override` and
`NeedsChoice`. A stored value is shown as stored for the last four only.

**3. What happens when a stored answer stops being true.**

- **Chosen, and that person no longer holds the post above** (as at the date asked about) → the
  screen reads **"Needs a new line manager — &lt;name&gt; no longer holds &lt;position&gt;"**, with the
  picker of the current holders. **Never a silent fallback.** Picking another holder would be the
  product inventing a line manager nobody stated, which is exactly what D28 A refuses; doing it
  quietly would also hide that the record needs attention.
- **ChosenActing, and the post above has been filled** → the acting arrangement is over. The stored
  value is simply not obeyed, live derivation takes over, and the value is cleared on the next save.
  Nothing is wrong and nobody needs telling: the real manager is back.
- **Override** → stands through all of it, labelled **"Differs from the position's reporting line."**
  A dotted line is deliberate and survives the post above changing hands, being filled or emptying.

**Where the sentence appears, and where a picker appears instead.** The read-only screens — the
employee page and the Placement screen — show the sentence, because they report what the record
says. The *editable* forms (Add employee, Change position) ask the question fresh about the
position being **considered**, which may not be the one held: a sentence there would name the post
above a position the operator is in the middle of replacing. They show the required picker of the
current holders, so neither screen falls back silently and neither invents an answer.

**4. One stored value says which of the three it is.** `EmployeePart.LineManagerKind` — `None`,
`Chosen`, `ChosenActing`, `Override` — and it is the one thing the chart cannot tell us. Once the
post above moves, all three read as *not a holder*: a departed chosen holder, a stand-in whose
chair has been refilled, and a deliberate dotted line are the same two bytes. The product must
treat them three different ways — ask loudly, ignore quietly, leave alone — so the kind is written
down when the form that knows it saves.

It costs no migration: `EmployeePart` is JSON-serialised inside the content item, so pre-addendum
data deserialises as `None` and is read by comparing the stored person with the holders — which is
right for everything the migration keeps. It is deliberately **not** on `EmployeeIndex`: nothing
queries by it, and a column serves a query.

**5. An employee who holds no position is untouched.** Every pre-F6 record and every tenant with no
positions is in that state. Their stored line manager is shown exactly as today, with **no override
label**: it is not an exception to a reporting line that does not exist, and nothing is derived for
them.

### Existing data

A one-time, append-only migration step — `WorkMate.Records.Migrations.UpdateFrom5Async`, over
`ILineManagerBackfill`. **It moves nobody** (ADR-0029) and touches exactly one field,
`LineManagerEmployeeId`. For each employee who has a stored line manager:

| Outcome | Rule |
| --- | --- |
| **cleared** | holds a position, and the stored person is the **sole** holder of the post above (or of the acting post above a vacant one) — a written-down derivation, which starts being recomputed and shows the same answer |
| **kept as chosen** | holds a position, and the stored person is **one of several** holders |
| **kept as override** | holds a position, and the stored person is **not among** the holders — including a post at the top of the line, where there is no list |
| **untouched** | holds **no position** |

The step reports the four counts to the log. It is safe to re-enter: it writes nothing where the
comparison does not match.

**One honest limitation.** Data written before this addendum carries no `LineManagerKind`, so a
deliberate pre-addendum override is kept but reads afterwards as *Needs a new line manager* rather
than as an override. The migration does not set the kind, because it cannot know — from
off-the-line data alone, a dotted line and a stale pick are indistinguishable. Asking a human is
the only answer that is not a guess, and is the same answer D28 A gives everywhere else.
Confirming the person through *Change…* records the override properly and the message goes.

### What does not change

- **D28 A still decides what may be derived**: one holder is a fact, several is not, nothing picks
  between them.
- **The first addendum's walk upwards** past a vacant post is unchanged; what changes is that its
  single-holder result is no longer saved.
- **Storage is still `LineManagerEmployeeId`**, still undated, plus one boolean on the same part.
  No new column, no index change.
- **The four screens still show one *Reports to* row** from one partial.

---

## D22 addendum — the retirement, as built in F6c

**Status: accepted.** Built in slice F6c.
**Date:** 18 October 2026
**Implements:** D22 steps 1, 3, 5 and 6. **Rests on:** ADR-0009, ADR-0014, ADR-0016, D26, D30.

D22 was written as design and left four things for the slice that built it to settle. These are the
answers.

**1. How a field is retired: a new editor, not a new setting and not a deletion.**
`ContentPartFieldSettings.Editor` on `JobDetails.JobTitle` and `JobDetails.GradeCode` becomes
`WorkMateRetired`, and this module supplies the two views Orchard then resolves. The field keeps its
type, its stored value, its display name and — on `GradeCode` — its binding to the `grade` list,
which is how a code typed last year still resolves to a name. The same mechanism the lookup editor
already uses, and for the same reason: **no field driver is registered**, so nothing collides with
the one `OrchardCore.ContentFields` has for `TextField`.

**The retired editor renders a hidden input carrying the value.** Orchard's field drivers bind
their editor's model and write the result straight back, so an editor that rendered nothing would
post nothing, the driver would store the empty string, and the second save of a Job details row
would erase the history the first one displayed. Retiring a field by deleting its contents is the
one outcome D22 exists to prevent, and it would be silent and permanent.

**2. A tenant with no positions still types them**, which is D22 step 3 made mechanical rather than
remembered. The retired editor asks the tenant's own axes whether it has positions and renders an
ordinary box when it has none. One definition, no second setting, nothing an operator can get
wrong — and the hard rule that a tenant with no positions keeps today's behaviour is kept by
construction.

**3. The lists are retired at creation, not by a migration step alone.** `job-title` and `grade`
arrive through the `lookup-lists` recipe step, which runs **after** this module's migrations — so a
step that retired them could only ever catch a tenant that already had them, and every new tenant
would load them fresh and unretired. `ILookupListService.CreateAsync` marks a list whose code is in
`StandardLookupLists.Superseded` retired as it is written, which covers the recipe, the
standard-lists action and any later import. `Migrations.UpdateFrom6Async` still retires them for a
tenant that loaded them earlier.

**Retired means not offered, not gone.** `LookupListDocument.IsRetired` changes what is proposed for
a new binding and nothing about what resolves. ADR-0014's rule applied one level up: an entry is
retired rather than deleted because records name it; a list is retired rather than deleted because
fields bind to it and records hold its codes. There is no un-retire.

**4. The names are reserved for new definitions, and the section that has them keeps them.**
`JobTitle` and `GradeCode` go into `EmployeeFieldNames.RetiredOnSections`, which is a **separate set
from `ForbiddenOnSections`** — that one is consulted while this module's own migration writes the
shipped sections, so a name in both would make every brand-new tenant's setup throw. The guard asks
the part as well as the name: `EmployeeJobDetail` may declare them, nothing else may. A second
`JobTitle` published by the form designer would read as the employee's job title in Liquid and in
GraphQL while holding something else, beside the historical one D22 promised to keep legible.

**What is still not done:** D22 step 4, the operator-run import that seeds the Position dimension
from typed values. Unchanged and still its own slice — what the right positions are is a business
question and not a migration's to guess.

---

# D32 — The employees list shows what people hold, derived

**Status: accepted.** Built in slice F6c.
**Date:** 18 October 2026
**Rests on:** D22, D26, D28 D, D30, D31 and its addenda, ADR-0029.

## The decision

The employees list carries **Code, Name, Position, Grade, Reports to, Organisation, Status**, and
every one of the four new columns is **derived on read** rather than stored.

| Column | Where it comes from |
| --- | --- |
| **Position** | the dated assignment's node, when that node is a position (D30) |
| **Grade** | the grade on the same assignment row (ADR-0026) |
| **Reports to** | `IEmployeeOrganisationService.GetLineManagerAsync`'s answer — the **live** line manager, with its footing (D31 addendum 2) |
| **Organisation** | the position's ancestors, labelled with the tenant's **own** dimension names (D28 D) |

**One *Organisation* column, not four.** Division, Department, Business unit and Cost centre would
be right for Zenith and wrong for Crescent. Per-dimension optional columns are the shape the
dimension list already uses and are a later slice; the path answers the question today.

**An employee with no position reads "No position yet — &lt;unit&gt;"**, naming the unit. ADR-0029's
mid-migration state, which D28 C requires be made legible — and a blank cell on a list of two
hundred people is exactly how it stays hidden.

**Filters: by position and by unit**, intersected rather than replacing one another, because "in
this department **and** a Site Supervisor" is what somebody setting both is asking. The unit filter
includes everything below it, which costs nothing: a position is a node on the organisation axis
with a parent, so the closure index already rolls its holder up to the department.

**A tenant with no positions sees none of this.** D30's scope, restated: the columns, the position
filter and the derived sorts are all absent, and that list is exactly what F6a shipped.

## Batched, and what "batched" means precisely

`GetLinesAsync` resolves a page in a number of reads bounded by the **distinct positions** on it and
the **axes** the tenant has — never by the number of rows. Fifty people in one department cost what
one of them costs. It shares one body with `GetLineManagerAsync` for deciding what a stored line
manager means, so an employee's own page and the list she appears on cannot disagree about who
manages her.

**One read is still per row**, and only for employees who have a stored line manager at all: *what
kind* of answer it is lives on the content item and not on `EmployeeIndex`, deliberately (D31
addendum 2). After that addendum's migration the set is people somebody actually chose or
overrode — a minority of any page.

## Sorting by position or grade, and the column that would have made it free

Name is index-backed and orders the whole tenant. **Position and grade are not columns on
`EmployeeIndex` and cannot be.** The F6 design note floated a denormalised column and asked for an
argument; the argument fails:

> A position's name lives on a dimension record and a grade's on an assignment row, and **both
> change without the employee's content item being touched.** YesSql re-indexes on save of the
> indexed document, so renaming a position or transferring somebody would leave the column holding a
> value that is no longer true — a cache nothing invalidates, sorting and filtering by a stale
> answer. Keeping it fresh would mean a write into the employee record from the dimension engine,
> which is the cross-module write-coupling rule 9 exists to prevent.

So the matching employees are resolved and ordered in the module, **up to a cap of 2,000**. Past it
the list keeps its name order and **says so** — *"Too many people to order by position or grade.
Narrow the list first."* Silently sorting the first two thousand would look exactly like a correct
answer, which is the one outcome worth refusing.

**Known limitation, recorded rather than hidden:** on a tenant with more than 2,000 matching
employees, ordering by position or grade requires a filter first. The honest fix is a reporting
read model rather than a column on the record, and that is a later slice's decision.

## F6c also settled, under existing decisions

- **A post's reporting line changes through the designer's dated Move** (ADR-0005). The position
  page's *Change reports to...* opens that screen on the **reporting** axis with the post chosen.
  There is no undated reparent anywhere in the product.
- **"No one above holds a post yet" names the empty chairs** — *"No acting manager — Manager
  Technology, Director Technology and CEO are also vacant"*. True and unactionable became a list of
  posts somebody can go and fill. D31 addendum.
- **A stale stored choice stops the row claiming a line manager.** With one holder above and a
  choice that has gone stale, the row reads the post and the holder without *"(line manager)"*,
  because the sentence directly below says the record needs a new one. Two statements one line
  apart, disagreeing, is what that removes.
- **The Reports-to picker offers the same department first**, fetched rather than re-sorted — see
  `IPositionService.SearchSelectableAsync`. Re-sorting the page would only move the posts that
  happened to land in the first twenty.
- **Add on a grouped "All &lt;plural&gt;" list asks which organisation first**, which is the pending
  item from the F6 design note section 8. One type goes straight to the form, unchanged. It used to
  post an empty dimension type id and answer 404 from a button the screen had just offered.
