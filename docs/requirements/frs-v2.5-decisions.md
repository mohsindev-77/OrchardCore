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

## Still to confirm with the product owner
- Grade range on a position (D5).
- Which parent is "primary" for a value with two parents, if any report needs exactly one.
