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
| ✅ **Closed** | ~~The deployment step drivers have no tests.~~ `DeploymentStepDriverTenantTests`. Finding the screen did not open at all is what the gap was hiding. |
| ✅ **Closed** | ~~The "unsuppressed advisory still fails the build" proof is not in the repository.~~ `NuGetAuditPolicyTests` every build, `tools/verify-nuget-audit.sh` on demand. |
| 🟡 **Second reviewer** | `OrchardCore.AdminMenu` added to `WorkMate.Platform`'s manifest and to `base.recipe.json` — needed because `OrchardCore.Contents` contributes a data-localization provider that depends on it. Without it the deployment plan screen throws. |
| 🟡 **Second reviewer** | Six features added to `WorkMate.Records`' manifest, one to `WorkMate.Dimensions`', two to `WorkMate.Platform`'s. These change which features a tenant enables, including existing ones. |
| 🟡 **Known limit** | **Without JavaScript the employee picker is a `<select>` of the first page.** The control is still correct — it posts, binds and validates identically — but reaching somebody past the first page needs the search, and the search needs the script. Everything else in the admin already requires script to expand a chart, so this is consistent rather than new; it is written down because "never a hard cap that hides people" is true with script and degraded without it. |

---

## Session A follow-up: picker, feature dependencies, unit employee list, closed BELIEVED items

### The employee picker now searches the tenant

It was a `<select>` of the first hundred employees of any status. On the Zenith demo organisation that is a hundred of a hundred and fifty-eight — **fifty-eight people could not be chosen as a head, a line manager or anything else, from any screen**, and the control said nothing about it. It also offered leavers to the one form that cannot accept them.

Now: server-side search (`EmployeePickerAdminController.Search`), an ARIA combobox the script builds over the existing `<select>` so the posted value and validation are unchanged without it, options carrying **name, code and where they sit**, and per-form rules — appointing a head offers everybody regardless of placement (ADR-0012) and excludes leavers, saying why. `IVisibilityService` still filters.

### `OrchardCore.Flows` — and five more — are manifest dependencies now

Your tenant's employee sections did not work because Flows was added to `base.recipe.json` and nowhere else. **A recipe is applied once, at setup; editing it reaches tenants created afterwards and no others, ever.** A manifest dependency is resolved every time a shell is built.

The audit found six more of the same: `WorkMate.Records` declared none of Flows, Title, ContentFields, AuditTrail, Deployment or Media; `WorkMate.Dimensions` had no Deployment; `WorkMate.Platform` had no Contents or ContentTypes.

| Guard | What it holds |
| --- | --- |
| `ModuleFeatureDependencyTests` | Every Orchard package a module references implies a feature its manifest must declare, through a table that **fails on any package it does not classify** — so adding a reference forces somebody to say whether it brings a feature. Verified to bite: removing the Flows line fails with "Add to src/WorkMate.Records/Manifest.cs: 'OrchardCore.Flows'". |
| `ModuleFeatureDependencyTenantTests` | A tenant set up with all eight features **struck out of the base recipe** still has them, and an employee's `BagPart` sections still resolve. The source guard proves the manifest says the right thing; this proves saying it has an effect. |

### A unit's employees, from the designer

The card's count is a link and the ⋯ menu gains **View employees**; both open the employee list filtered to that unit, axis and date. Header, an **Include sub-units** checkbox (off by default — the card's number counts the node itself, and opening on a different number from the one just clicked answers a question nobody asked), and columns for **Role here** (primary, or secondary with the home unit named), **Allocation %** and a ★ head marker. The head who is not a member is listed, marked, and **not** counted. The figure comes from the same service call that draws the card.

Both ways in are hidden — not disabled — without `ViewEmployees`, asked of the module that owns it via `IEmployeeLookup.CanViewEmployeeListAsync`.

### Both BELIEVED items from A3 are closed

| Was | Now |
| --- | --- |
| "the deployment plan screens work" | `DeploymentStepDriverTenantTests`. **The screen did not open at all** — see below. The combination rules moved onto the step as `Problems()`, so they are testable without an HTTP context, while the sentences stayed on the driver where the localisation guard can see them. A plan executed through `IDeploymentManager` writes the same six steps in the same order as the direct source path. |
| "an unsuppressed advisory still fails the build" | `NuGetAuditPolicyTests` on every build: the audit is on, set to `all` (the ImageSharp advisories are transitive and `direct` would hide them), at `low`, suppressing only advisories by id and only ones an ADR names. Plus `tools/verify-nuget-audit.sh` for the live end-to-end probe — **run, and it passes in both directions**: the unsuppressed advisory fails the restore, and suppressing it by id lets the same restore through. |

### Two defects found while doing the above

**The deployment plan screen threw on every request.** `OrchardCore.Contents` registers a data-localization provider depending on `OrchardCore.AdminMenu`; the base recipe enables `OrchardCore.DataLocalization` so content type names can be translated; AdminMenu was enabled by nothing. Resolving the providers therefore failed with a DI exception naming three features and no cause. The export was proved end to end while the only way for a person to reach it was broken — the same shape as the lifecycle buttons and the dropped menu entry. `WorkMate.Platform` now declares AdminMenu, which is also what reaches existing tenants.

**The employee list ran one query per row** to resolve each person's unit, and resolved it only on the primary organisation axis — which the base recipe ships empty, so the column read "Not placed" against people plainly placed. Now one batched query, on the axis being viewed when there is one.

---

## Two picker defects found in the browser

### The label read "LocalizedHtmlString"

`label="@T["Head of this unit"]"` hands a tag helper a `LocalizedHtmlString`; the `Label` property was a `string`; Razor binds an expression to a `string` by calling `ToString()`, which that type does not override. It compiled, it rendered, and the label was the name of a class.

Four views had it right with `.Value` and three had it wrong without — **that split is the defect**, not the three views. The `Label` properties on all three tag helpers now take `object?` and go through `TagHelperLabel`, which tells `IHtmlContent` (already encoded — encoding again turns an apostrophe into `&amp;#39;`), `LocalizedString` and plain `string` (encoded, and the half that matters, since a dimension attribute's label is tenant data) apart. Every call site is normalised to one idiom.

`RenderedLabelTenantTests` sweeps fourteen admin screens for either type name, including in attributes a browser test would never look at. **Verified to bite**: forcing the old behaviour fails it with "/Admin/Employees/Create renders 'LocalizedHtmlString'".

### Selecting with the mouse announced "No match"

**The selection was never recorded as a fact** — it was inferred from the text in the box. The box holds a chosen person's *full label* ("Adeel Bhatti (zenith-labour-b-12) — Site Team B"), the focus handler re-read that as a search term, found nobody, and said so at a reader who had just chosen successfully.

The posted value survived, which made it worse rather than better: the control claimed the selection had failed while quietly holding a correct one — so **clicking Appoint did work**, which is why nothing downstream complained. Its mirror image had the same cause and is the dangerous one: typing over a selection left the old id in the `<select>`, so the form would post somebody the reader had visibly moved away from.

Now `chosen` is explicit state. Choosing records it; editing the text abandons it; focus searches for the query rather than the label; submitting text that names nobody is refused on the field. The error is a `.invalid-feedback` element of its own, because the status line is a running commentary on the search and a request already in flight overwrites anything written there.

**Why the first picker tests missed it.** They selected an option and asserted the page that came back — and that page was right. They never looked at the control's state in between: the hidden `<select>` that is what gets posted, and the status line. Nor did they do the second thing a person does, which is look back at the box. `EmployeePickerSelectionBrowserTests` does both, by mouse and by keyboard, on Appoint a head, line manager and placement.

Fixing it **unmasked a race** in an older test: `TheKeyboardAloneChoosesSomebody` waited for "the first option", which focus now makes visible immediately, so the keys landed on whoever sorts first alphabetically. It could not fail before because the bug left the list empty. It now waits for the option it means.

### And one inconsistency between two screens

The picker fell back across axes to answer "where they sit"; the employee list did not, and the base recipe ships an empty primary organisation — so the same person read "Mechanical" in the picker and "Not placed" in the list. Both now go through `EmployeeUnitNames`.

---

## Session C: placing more than one row, and the words on the screens

Placement worked for exactly one row and could be reached from exactly one direction. This session
closes both, and renames the concept the screens had been calling an *axis*.

### Change placement — the split, as editable rows

`EmployeePlacementAdminController.Change`, reachable from the employee list, from the placement
overview and from the Place screen. One structure at a time, because the rules — allocations total
100, exactly one row is main — are statements about one employee on one structure. Rows are added
and removed by round trip rather than by script, like every other form in this module, and the
round trip also redraws each row's unit picker against the current structure and date.

**One write path.** Save is one call to `IEmployeeAssignmentService.ReallocateAsync`, with the same
changes/split shape the `employee-assignments` recipe step builds. `PlaceAsync` is already a
one-row reallocation, so there is no second mechanism and nothing new in the engine. Old rows end
the day before the effective date; nothing is deleted.

**`PlacementSplit`** holds what the screen checks first, as a pure function with no session, no
localiser and no clock — so every case is a unit test. It is not a second copy of the engine's
rules: `ValidateAssignmentAsync` remains the authority and runs again on the write. It adds the two
things a validator working on a resolved split cannot do (say *which row* is wrong, and catch an
empty row before it reaches a service that would call it an unknown record id) and one rule of its
own.

**That rule is the backdating refusal, and it is an ADR-0005 question answered differently.**
`ReallocateAsync` states the whole picture from its effective date onwards, so a split dated behind
a change already recorded would replace that change rather than slot in behind it — the "overwrite"
behaviour ADR-0005's addendum examined and rejected for moves, reached through a different door.
ADR-0005 chose *split* for a move because a link carries one parent at a time and the arithmetic is
unambiguous; a set of rows that must total 100 on every date has no honest equivalent, so this
screen takes ADR-0005's third option — refuse — and names the date it will accept. No ADR change:
the addendum's reasoning is about moves, and this records why the same answer does not carry.

### The unit picker

`workmate-unit-picker` in `WorkMate.Dimensions`, with `UnitPickerAdminController` behind it, on
Place and on every row of Change. Every option carries its full path —
`Crescent › Retail Banking › Credit` — because the Crescent reference tenant has two departments
called Credit and two called Operations on one structure, and a list of bare names offers identical
rows with no way to choose correctly. An optional **Within** filter narrows to a container's subtree
through the closure index.

The employee picker's combobox became `workmate-picker.js` in `WorkMate.Platform` and both pickers
now drive it, rather than the second picker being a second three hundred lines of the same keyboard
handling, race guard and submit refusal — four of which were defects fixed once in the original.

### "Place an employee here…" on the designer card

Declared in `DesignerCardActions.All` immediately after `sethead` and gated on the same
`AssignEmployees`, because placing somebody and appointing a head are the same act. It is the first
card action whose availability depends on the **unit** as well as on the viewer, which a cloned
template cannot branch on in Razor — so the link declares what it needs
(`data-designer-action-requires="holdsEmployees"`), the node carries the answer, and the script
removes it on a container. A fetched container's menu is therefore identical to a server-rendered
container's, which is the property `PermittedActions` exists to protect.

### "Axis" is "Structure" everywhere a user can see it

Views, labels, help text, validation messages, PO files and test assertions. Internal C# names are
unchanged: the type is still `AxisPlacementViewModel`, the concept on screen is a structure.

### The employee list

**Role here → Placement, primary/secondary → Main/Additional.** A role is a job title, which that
column never held; "Secondary" reads as a judgement about the person rather than a statement about
the unit's share of them.

**The main placement, with the rest behind "+N more"** — full path and share for each. Somebody
split 60/40 has two true answers to "where do they sit", and the column used to show whichever row
the engine returned first.

**The ★ tooltip names every unit that person heads**, not "this unit". ADR-0012 lets one person head
several, and the Zenith demo deliberately has one over two departments and a site team.

### The Ahmed Khan row

Reported as `Civil · Secondary · 40% · home: Civil`. The `home: {0}` caption under the role rendered
`PrimaryUnitName`, and on a unit-filtered list `PrimaryUnitName` is resolved on *the structure the
list is filtered to* — which is where the main placement is. So the caption was, by construction,
the cell immediately to its left; the only way the two could ever have differed is if the main
placement had been read off a different structure, which would have made the pair meaningless
rather than merely redundant. It is removed, not reworded: where else somebody sits is now one
question answered in one place, the "+N more" disclosure.

### A race in the picker, found by running the browser suite under load

A reader focuses the box (which searches at once), types (which searches again), picks from the
second list and moves on — and the first response lands afterwards and re-opens a dropdown over
whatever they were about to click. The sequence guard stopped a slower earlier response overwriting
a faster later one; it could not stop one overwriting a *closed* list, because closing was not
something any response knew about. `close()` now abandons what is in flight.

---

## Session D: a list and a New button per dimension type, reached from the Dimension types list

A Department could only be created or edited inside the organisation designer. The designer is a
chart — it answers "what sits under what", it is navigated by expanding branches, and finding one
department by a half-remembered code means opening the branch it is in first. HR asked for the shape
every other list in the product has.

### The way in: "View units" on the Dimension types list

This shipped first as a menu entry per dimension type, built from the tenant's own configuration,
and the product owner withdrew it on sight: on a real tenant the Organisation menu read *Branchs,
Cost centres, Departments, Departments, Departments, Divisions, Divisions, Divisions, Projects,
Regions, Sections, Teams*. Several structures legitimately use types of the same name, and no rule
pluralises an English noun correctly (Branch, Company, Analysis) while Arabic pluralises by changing
the word (قسم → أقسام). A navigation menu is a short list somebody scans; it is the wrong place to
enumerate configuration.

The Organisation menu is three fixed entries — **Organisation designer, Dimension types,
Structures** — and a type's units are reached by the **View units** link on each row of the
Dimension types list, which satisfies CLAUDE.md's "every screen reachable by clicking" one click
further in, where somebody is already looking at the list of types. The "In the menu" block is off
the dimension type editor.

Three things survived the withdrawal and are deliberate:

- **`ShowInMenu`, `PluralName` and `MenuOrder` are still on `DimensionTypeDocument`, and nothing
  reads them.** `Migrations.UpdateFrom7Async` has shipped and sets them, and ADR-0009 makes a
  shipped step uneditable: tenants are recorded past it and will never run it again. Deleting the
  properties would stop it compiling, and a step to clear them would be a migration whose whole
  purpose is to destroy data, running on every tenant including those that never had the feature.
  They stay inert, and the frozen step's own comment now says so. `DimensionTypeMenu` is trimmed to
  the one method that step calls.
- **The three fixed entries keep explicit numeric positions.** They used `PrefixPosition()`, which
  orders by the caption in whatever language the reader is in, so an Arabic reader got a different
  order from an English one. That is a real defect found while building the per-type entries and is
  worth keeping fixed.
- **The per-type screens name a type by its singular** — "Department units", not "Departments". The
  stored plural was maintained by the editor that went with the menu, so reading it now would be
  reading a field a customer can no longer correct.

### The list, and creating from it

Code, bilingual name, parent as a full path per structure, status with its dates, headcount linking
into the employee list, and the type's attributes as optional columns. Search, structure, `Within`
(the unit picker, narrowing through the closure index) and active/retired, paged.

**Creating is one operation.** "Place under" is the unit picker in a new parent-choosing mode:
options come from `SearchPermittedParentsAsync`, the containment map read forwards from the same
`Permits` the validator reads backwards, so the picker never offers a parent the save would refuse.
A root type may be left with no parent, and that is an option with its own words rather than an empty
box. Saving calls `IDimensionService.AddUnitAsync`, which writes the record and its dated
`DimensionLink` together — no create-then-move, no second set of validation messages. Moving and
retiring stay the designer's dated screens, linked from the list and the editor.

### Orchard's generic screens are not a second way in

A Department created through Content → New is a content item with no `DimensionLink`: on no
structure, invisible to the closure index and every roll-up. The content type is now
`Creatable = false` (`UpdateFrom8Async` for existing tenants), `Listable` stays on, and
`DimensionRecordAdminRedirectFilter` sends the generic create and edit URLs to our screens with a
note saying why — keyed on the controller's type name, so this module still takes no reference to
`OrchardCore.Contents`.

### The demo recipes declare which types hold employees

Zenith attaches people to Departments and Teams, Crescent to Branches and Departments, so "Place an
employee here…" is gone from the containers above them. Stating that on a structure a tenant already
has raised an ADR-0008 question, answered in that ADR's **11 October addendum** and summarised in the
Dimensions README: for this field alone, empty in the tenant is the *absence* of an answer rather
than a different one, so a row that states a set against a structure declaring none fills it in; a
tenant holding a real, different set is still refused, writing nothing. The addendum is marked
pending review, and names the alternative (a new recipe file) that a reviewer may prefer.

### A defect found and deliberately not fixed

`IDimensionGraphService.RemoveAsync` throws YesSql's "The object to update was not found in identity
map" on a tenant with several structures. It has one caller — `AddUnitAsync`, undoing a record whose
placement was refused after it had been created — and nothing exercised it until this screen made a
refusable parent reachable. The reproduction, the stack and the reasoning are on the skipped
`DimensionRecordsAdminTenantTests.AForbiddenParentIsRefusedWithTheValidatorsOwnWords`, and the likely
repair is to make `AddUnitAsync` validate the placement before creating the record — which needs a
`IDimensionValidator` entry point for a record that does not exist yet, and belongs in its own slice.

---

## Test counts

Full `dotnet build -warnaserror` with **NuGet audit ON**: 0 warnings, 0 errors.

| Assembly | Passed | Skipped | Added in session D |
| --- | ---: | ---: | ---: |
| `WorkMate.Platform.Tests` | 199 | 0 | 0 |
| `WorkMate.Dimensions.Tests` | 185 | 0 | +4 |
| `WorkMate.Records.Tests` | 101 | 0 | 0 |
| `WorkMate.Integration.Tests` | 373 | 1 | +25 |
| `WorkMate.Browser.Tests` | 106 | 3 | +6 |
| **Total** | **964** | **4** | **+35** |

The counts are after the menu withdrawal. Ten tests that only proved per-type menu entries were
removed — `DimensionTypeMenuTests`, `DimensionTypeMenuTenantTests`, `DimensionTypeMenuBrowserTests`
and the `UpdateFrom7Async` backfill test, whose subject is now three fields nothing reads (the
migration chain's catch-up is still covered by the version-2 rollback test). Six replace them:
`OrganisationMenuTenantTests` asserts the group has exactly those three entries, in order, on a
tenant carrying base, Zenith and Crescent; `OrganisationMenuBrowserTests` asserts the same in
English and Arabic in a real browser, where a culture actually switches; and
`DimensionTypeUnitsBrowserTests` clicks Organisation → Dimension types → View units → + New.

Three skips are `RealDataFact` tests, opt-in by `WORKMATE_REAL_APPDATA` — unchanged from prompt 3.
The fourth is the recorded `RemoveAsync` defect above.
