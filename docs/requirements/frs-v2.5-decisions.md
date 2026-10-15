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
