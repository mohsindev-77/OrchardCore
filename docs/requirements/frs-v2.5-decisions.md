# Decisions on Dimension Mapping FRS v2.5 (06-10-2026)

Recorded 12 October 2026 by Mohsin Hasnain. Status: agreed by the team, pending formal manager
confirmation. These answer the open questions raised by the FRS gap analysis and are binding for
design and implementation unless the product owner changes them.

## D1 Headcount when a value has two (or more) parents
A value counts under EACH of its parents, but only within that parent dimension's own "View by"
view (FRS figure 9 side panel). A view sums one dimension only, so nobody is double counted inside
a view. Organisation-wide totals count UNIQUE employees.

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

## D5 Grades, steps and job titles
- "Grade Steps" is one dimension with its own tree: grades as parents, steps as children
  (G7 -> G7-S1..S5).
- A position is mapped to a GRADE (optionally a grade range, e.g. G7-G8 - to confirm).
- The employee holds a STEP, chosen only from the steps of the position's grade(s); dated
  (G7-S2 from Jan 2025, G7-S3 from Jan 2026). Steps optional for clients that do not use them.
- Promotion = new position; grade follows; HR picks a step in the new grade.
- Job Title is a dimension that positions point to (e.g. "Engineer" used by several positions).
- Lookup lists (Form Designer B1/B2) remain for simple non-organisational values: nationality,
  document type, employment type, religion, marital status, bank, relationship.

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

## D9 Position is the master; Job Title is a dimension; Position Title is a name (Q-c)
| Noun | What it is |
| --- | --- |
| **Position** | The post somebody holds: code, reporting line, org placement, grade, approved headcount, employees. Org Role is this. A dimension type, and the master |
| **Job Title** | The occupational classification several positions share — Engineer, Supervisor. A dimension type; positions map to it, **Optional** |
| **Position Title** | What this particular post is called. **The Position record's own bilingual name**, not a master |

A `position-title` dimension row ships **only if the product owner asks for it**, and then hidden by
default under D3's hide switch. The FRS's figure 2 lists it; the feedback note's "Positions as the
main master and not duplicate/separate out from the Org. Role Setup" governs.

## D10 The primary parent is declared, not derived (Q-d)
Where a value has several parents and a report needs exactly one — the per-dimension list's Parent
column, payroll attribution, approval routing, a flat export — the answer comes from a **primary
flag on the mapping**, set on the Mapping screen: "a Branch's Division is its primary parent".

Declared rather than derived from the mapping order, because deriving it would let a screen's layout
decide payroll attribution.

## D11 The unit head stays a dated appointment (Q-e)
ADR-0012 stands unchanged: a head appointment is its own dated record, carries no allocation, is
counted by no headcount, and the head need not be a member of the unit they head. "Head as a
position" is **revisited in prompt 5**, when approval routing is built and the cost of either
answer is visible. The reserved ADR-0027 is not written until then.

## D12 Figure 9 is a loose mock; one value per mapped parent is the default (Q-f)
FRS figure 9 draws Seef Branch under two Departments, which reads against Rule 3's "one value from
each mapped parent". Treat the figure as loosely drawn.

- **Default cardinality is `ExactlyOne`** — one value per mapped parent dimension, which is Rule 3.
- **`OneOrMore` is available as a per-mapping setting** for the cases that need it (a grade range,
  D8). A group set to `OneOrMore` renders as checkboxes; `ExactlyOne` renders as radios.

## D13 Position ships with the pre-configured set (Q-g)
Figure 2's dimension list omits Position while FRS 4.1.4 shows a Position tree and figure 9 puts
Position above Employee. The omission is treated as an error in the figure. Position ships.

## D14 Position's mapping into the organisation is per tenant (Q-h)
There is no platform answer to "is a Position under a Branch or under a Department" — it depends on
whether the customer has branches. The mapping is the tenant's to declare on the Mapping screen.

Demo recipes declare it explicitly:
- **Zenith** — Position mapped to Department and to Team.
- **Crescent** — Position mapped to Branch and to Department.

**`AtLeastOneAcross` is not added** to `MappingCardinality` unless a slice proves it is needed. A
cardinality that spans several mappings is a rule nobody has asked for yet.

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

## D17 Demo organisations share the standard dimension types
The menu clutter that caused the per-type menu entries to be withdrawn in prompt 4 session D —
*Branchs, Cost centres, Departments, Departments, Departments, Divisions, Divisions, Divisions,
Projects, Regions, Sections, Teams* — came from **three demo organisations each defining their own
Department and Division types in one tenant**, not from the menu mechanism.

So the demos are the fix as much as the authored plurals are: **every demo recipe uses the standard,
shipped dimension types** rather than prefixing its own. A test with the base recipe plus Zenith plus
Crescent all loaded into one tenant asserts that each dimension appears in the menu **exactly once**.

Prefixed *record* codes stay — they are what keeps the three companies' values from colliding — and
only the *type* codes are shared. This lands with the menu in slice **F2** and with the demo rebuild
in **F9**.

## D18 Slice order
Approved as proposed with one change: the enquiry tree moves to directly after the DAG, and the
employee leaves are added to it in F6.

```
F1 -> F2 -> F3 -> F4 -> F8 -> F5 -> F6 -> F7 -> F9
```

| | Slice |
| --- | --- |
| F1 | Alias, Status, and the dimension screen's two views |
| F2 | The menu, one item per dimension (D3, D17) |
| F3 | Mapping, with one parent |
| F4 | Several parents at once — the DAG |
| F8 | The Organisation tree enquiry report, **without employee leaves** |
| F5 | The shipped dimensions and Position |
| F6 | Employees join through a position, **and the enquiry tree gains its employee leaves** |
| F7 | Grades, steps and job titles |
| F9 | Demo recipes, export and Delete |

## Superseded
D5's "'Grade Steps' is one dimension with its own tree" is refined by **D7**: one dimension in the
menu and on screen, two dimension types underneath. D5's intent is unchanged.
