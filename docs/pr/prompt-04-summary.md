# Prompt 4 — Employee record, unit heads, recipe steps and export

Branch `prompt-04-records`. Spec section 5; architecture section 6. **Session A only** —
sessions B and C (the form designer) are not in this branch.

---

## What this adds

### Screens

| Screen | What it does |
| --- | --- |
| **Employees** | List with search across code and both halves of the name, a status filter, and a **Status** menu on each row offering exactly the transitions valid from that row's status. Create and edit forms for the fixed core. |
| **Employment status** | The five dated transitions, each its own small form. **Exit is a dry run first**: a `GET` computes every placement it would close and every unit it would leave without a head, and only the `POST` commits. |
| **Placement** | Where somebody sits, per axis, with the dated history. Place, and end. |
| **Unit head** | Appoint and clear, from the designer card's own ⋯ menu, with the unit's term history on the page. |
| **Organisation designer** | Cards now carry the unit's **head** ("Head: Vacant" when the post is empty) and its **employee count**, both read through `IEmployeeAssignmentService` — the same source prompt 5's approval routing will read, so the chart and the routing cannot disagree. |

### The employee record

- **A fixed core plus sections.** Code, both halves of the name, join date, status and its effective date, date of birth, nationality, gender, line manager, photo. Everything else is a section, which is what the form designer will extend in sessions B and C.
- **The code is the natural key** — `^[A-Za-z0-9][A-Za-z0-9._/-]{0,31}$`, compared case-insensitively, immutable once set. Every recipe step and every export names a person by it.
- **The employee record holds no department field.** Placement is an assignment row, per the hard boundary.
- **`StatusEffectiveFrom` is always the first day of the status**, including for an exit — which stores last-day-plus-one and converts back through `ExitedOn`. One off-by-one, in one place.
- **Not creatable through Orchard's generic content screen.** `Employee` is `Creatable = false`, guarded by `EmployeeContentDefinitionGuard` and by a migration for tenants that already had it: a record created generically would skip every invariant the service enforces.

### The unit head (ADR-0012)

A head appointment is **its own dated record**, not a flag on an assignment. The design it replaced could not express the case the demo organisations are built around: `ValidateAssignmentAsync` refuses an allocation of zero, so a head who is not a member of the unit had nowhere to live. One person may head several units; a head is counted by no headcount and charged to no cost centre; an exit closes every headship on every axis, and the dry run lists them.

Four rules now have tests of their own (`UnitHeadRulesTenantTests`), because each is a claim the decision was argued from and none was pinned: one person may head several units, a head is counted by no unit they head and once by the unit they work in, an acting head from another department is allowed and named, and an exit lists and ends **every** headship on **every** axis.

### Recipe steps and export (ADR-0011)

| Step | Module | What it carries |
| --- | --- | --- |
| `employees` | `WorkMate.Records` | People by code. The status is **reached**, not written — the step walks each new employee to it through the same dated transitions a person uses, so no record exists that a real sequence of decisions could not have produced. |
| `employee-assignments` | `WorkMate.Dimensions` | One entry per employee per axis, carrying dated **changes**; each change is the whole set of concurrent placements from that date. A split allocation is one change with several rows; a transfer is a later change. `endedOn` is coming off an axis altogether, which is not the same statement as transferring. |
| `unit-heads` | `WorkMate.Dimensions` | One entry per unit per axis, carrying every term. A term with an end that nothing follows is a vacancy, and vacancy is a fact rather than missing data. |

Exports: `RecordsDeploymentStep` (`IncludeEmployees`) and two new switches on `DimensionsDeploymentStep` (`IncludeAssignments`, `IncludeHeads`), **both off by default** — the three configuration switches beside them travel between environments as a matter of course, these are operational data about real people. Two switches rather than one because a head is not a placement. A plan applies the records step **first**: an assignment names its employee by a code something has to have created.

`PeopleExportRoundTripTenantTests` is the proof, and a second round trip rather than more cases in the first because the join between the two modules — a code resolved at import — is what neither module's own tests can see. It seeds a mid-month transfer, a split allocation, a matrix, a head who is not a member, one person heading three units, a post left vacant and somebody who came off an axis; exports both modules into one recipe; imports into a fresh tenant; and compares the answers on dates either side of every change. A second test applies the same export twice and asserts nothing changed.

### Demo employees

`organisation-designer-zenith-employees` (158 people) and `organisation-designer-crescent-employees` (69), at the headcounts in the prompt library's 7 October note as amended by the 10a ruling. **Separate recipes**, applied after their structure recipes and loaded by nothing that does not name them — a test about the shape of an organisation should not have to load 227 people, and the browser suite builds a tenant per fixture.

Zenith is where the matrix is: an engineer deployed to a site team holds a primary at their home department and a secondary at the team, so cost rolls up by project and HR rolls up by department over the same rows; foremen, labour and technicians are project-hired and hold one primary at their team only. Between them the two recipes seed all three head shapes — one person over two units, an acting head who is not a member, a post vacant since a term ended.

Generated by `tools/generate-demo-employees.sh`, because a hand-written file of 158 people is a file nobody re-reads. `DemoEmployeeRecipesTenantTests` asserts every figure in the table, the matrix on one person, each head shape, and a clean re-run.

---

## The four UI gaps found in A2 browser testing, and what they cost

### 1. Lifecycle buttons existed for one transition out of five

A2 shipped all five transitions with a control for Exit alone. Activate — the move every new record needs — was reachable only by typing `/Admin/Employees/Lifecycle/Activate/{id}`.

Fixed from one table: `EmployeeLifecycle.ActionsFrom(status)` returns the valid moves, `Shared/_LifecycleActions` renders them, and nothing renders at all without `ChangeEmploymentStatus`. A newly created employee lands on their record with "Activate" offered.

### 2. "Appoint a head…" was missing from every lazily loaded card

A2 added it to `_DesignerNode`'s markup and to neither the URL map nor the script — and the script *removes* any action it has no URL for, so the roots had it and every card below them silently did not.

Fixed from one list: `DesignerCardActions.All` drives the markup, the URL map and the script's wiring together.

### 3. "1 employees"

The script built the label from a format string, which gives every count one form. Moved to the server as `EmployeeCountLabel`, where `IStringLocalizer.Plural` can choose between the forms a language has — two in English, six in Arabic. `LocalisationResourceTests` learnt to see `S.Plural(...)`, which it previously could not.

### 4. Four head rules made explicit

`UnitHeadRulesTenantTests`, above.

### Why the A2 browser tests missed 1 and 2, and what changed

**They navigated by URL.** `page.GotoAsync("/Admin/Employees/Create")` proves a screen works and says nothing about whether anybody can reach it. A screen with no link to it passes every test that starts by going there.

**They only ever looked at server-rendered cards** — the structure card and the roots, which are precisely the ones in the first response. Fetched cards, built by cloning a template, were never compared against them, and a test that looks at one of two code paths cannot find a difference between them.

Three browser tests were added that work purely by clicking:

| Test | What it clicks |
| --- | --- |
| `EmployeeLifecycleClickBrowserTests` | One typed path, then everything by button: create → Activate → Put on leave → Activate → Suspend → Activate → Exit (with its dry run) → Reinstate. Plus: a prospective employee is offered only the valid moves, and a reader without the permission gets no control at all. |
| `DesignerCardMenuParityBrowserTests` | The full ⋯ menu of a server-rendered card compared against fetched cards at depth 2 and depth 3, for each permission set — read off the DOM rather than against a list of names, because a list would have to be kept in step by hand, which is the mistake being tested for. |
| `…AHeadCanBeAppointedFromADepthTwoCard…` | Zenith → Engineering Division → Civil, appointed from that card's own ⋯ menu, then "Head: Adeel Mahmood" on the card — including on a card rebuilt from scratch after reopening the branch. |

### A fifth defect, found by the third of those tests

**Every fetched card reported every post vacant, however many were filled.** `buildNode` filled each line of a cloned card by hand and the head line was simply never written, so the card kept the template's own text — which read `Head: Vacant`. A blank placeholder would have been visibly unfinished; a plausible one was not.

Fixed in the same shape as the menu: the script's `cardText` lists every line and the payload property it comes from, and the words now come from the model (`DesignerNodeViewModel.HeadLabel`) rather than being composed in the view, so the template renders **blank** and a line nobody filled in shows as nothing rather than as a claim.

### A sixth, found by running the browser suite under load

**A node's children could be fetched and appended twice.** `data-loaded` was set when the response arrived, so every call between asking and being answered looked like the first — two expands close together issued two requests and the tree ended up holding each child twice with the same record id on both. The in-flight promise is now remembered, not just its result.

### And a seventh, found by reviewing A3's own code

**The `employees` step refused every row asking for on leave or suspended**, with a message saying a new employee could not be put into that status — by a step that could do it. The validation pass asked whether a prospective employee could move *directly* to the stated status; the walk reached both in two moves. The demo recipes hire everybody and activate them, so nothing exercised it.

Fixed the same way as the other two: one `RouteFromNew` table, read by the validation and by the walk. `EmployeesRecipeStepTenantTests` now covers all five statuses, plus a forward line-manager reference, a duplicate code, a missing manager, and ADR-0008's skip-or-fail.

This is three defects in one session whose shape was *two places that had to agree by hand*. Each is now one list or one table with two readers.

---

## TIFF mitigation (ADR-0013 updated)

Orchard 3.0.1's default `MediaOptions.AllowedFileExtensions` was measured and found to include `.tif`/`.tiff`. WorkMate tenants now configure an explicit list in `src/WorkMate.Web/appsettings.json` — common image types for photos, `pdf`/`docx`/`xlsx` for documents, and no TIFF. `MediaUploadPolicyTenantTests` posts a real `.tif` through the admin media endpoint with a valid antiforgery token and asserts it is refused, and posts a `.png` and asserts it is accepted.

Four of the five ImageSharp advisories are now out of reach. **One remains** — see the open issues below.

---

## ADRs

| ADR | Decision |
| --- | --- |
| **0012** | The unit head is a dated appointment of its own, not a flag on an assignment. Records why the assignment-flag design cannot express a head who is not a member. |
| **0013** | Five ImageSharp advisories suppressed by id, with each advisory's severity, our exposure, why it cannot be fixed now, the media allow-list mitigation, and a review trigger. **Still blocking before production.** |

---

## Migrations

`WorkMate.Dimensions.Migrations` is at **version 7**.

| Step | Version | What |
| --- | --- | --- |
| `UpdateFrom5Async` | 6 | ADR-0012: `UnitHeadIndex` with its two covering indexes. |
| `UpdateFrom6Async` | **7** | Repairs `ContentItem.DisplayText` on dimension records written before the handler set it. Through `IContentManager`, not a raw session save. |

`WorkMate.Records.Migrations` is at **version 3**.

| Step | Version | What |
| --- | --- | --- |
| `CreateAsync` | 1 | `EmployeePart`, the standard sections, the `Employee` type (`Creatable = false`) and `EmployeeIndex`. |
| `UpdateFrom1Async` | 2 | Repairs `DisplayText` on employees created before the handler set it. |
| `UpdateFrom2Async` | **3** | Makes `Employee` non-creatable on tenants that already had it. |

Both repair migrations go through `IContentManager` rather than `ISession.SaveAsync`: `ContentItem` has no `Version` property, so a direct save cannot be concurrency-checked, and `ConcurrencyCheckedSaveTests` was **not** weakened to let one through.

The export and the two new recipe steps needed **no migration** — they read and write through services that already existed.

---

## Open issues

| | |
| --- | --- |
| 🔴 **BLOCKING before any production deployment** | **One ImageSharp advisory is still reachable.** Four of the five are now out of reach behind the media allow-list; the ICC-profile one is not, because it is reached by decoding a file type we still accept. There is no fixed 3.1.x, and 4.x is untested against `OrchardCore.Media` 3.0.1. ADR-0013 carries the detail and the review trigger. This must be resolved before production. |
| 🟡 **Second reviewer** | `OrchardCore.Flows` added to `recipes/base.recipe.json` (A1 — needed for `BagPart`, which the employee sections are built on). |
| 🟡 **Second reviewer** | Three new recipe steps and two new deployment steps, per the definition of done's rule for recipes. |
| 🟡 **Second reviewer** | Two new migration steps in each of the two modules. |
| 🟡 **Second reviewer** | Four new permissions in `WorkMate.Records`. |
| 🟡 **Design, not yet decided** | **`RecordResult<T>` / `IRecordsAuthorisation` duplicate their `WorkMate.Dimensions` equivalents.** Left as is at your direction; the reasoning and the options are the prompt library's 8 October backlog note. |
| 🟡 **Carried forward** | **The `employees` export carries a status, not a status history.** An employee's past statuses are in the audit trail rather than a dated table, so a round trip reproduces where somebody is and not the route they took. That is a limit of the model, not of the export — making it answerable is a model change. |
| 🟡 **Carried forward** | **Two simultaneous creates can share an employee code.** YesSql 5.4.7 has no unique index API; the service checks then writes. Recorded in the module README. |
| 🟡 **Carried forward** | **Audit display of an empty bilingual half** still has nowhere to appear — no WorkMate-authored audit display driver yet. From prompt 3. |
| 🟡 **Known gap, pre-existing and widened** | **The deployment step *drivers* have no tests.** Both round-trip tests drive the `IDeploymentSource` directly, which is what a plan ends up calling but not how a person configures one. The combination rules live in the driver — records need types and structures, assignments and heads need records — and none of them, including prompt 3's, is covered. A wrong rule there produces a file that fails on arrival rather than a refusal on the screen. Testing a `DisplayDriver`'s `UpdateAsync` needs an `UpdateEditorContext`, which is why it was skipped rather than done badly. |

---

## Test counts

Full `dotnet build -warnaserror` with **NuGet audit ON**: 0 warnings, 0 errors.

| Assembly | Passed | Skipped |
| --- | ---: | ---: |
| `WorkMate.Dimensions.Tests` | 181 | 0 |
| `WorkMate.Platform.Tests` | 183 | 0 |
| `WorkMate.Records.Tests` | 75 | 0 |
| `WorkMate.Integration.Tests` | 290 | 0 |
| `WorkMate.Browser.Tests` | 78 | 3 |
| **Total** | **807** | **3** |

The three skips are `RealDataFact` tests, opt-in by `WORKMATE_REAL_APPDATA` and skipped with a reason when nobody has offered any — unchanged from prompt 3.
