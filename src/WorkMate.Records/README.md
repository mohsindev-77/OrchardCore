# WorkMate.Records

The employee record and the form designer, both built on runtime content-type
definition. Contracted in section 5 of `/docs/technical-specification.md`.

**Status: sessions A1 and A2 of prompt 4.** The model, the migration, the
services and the screens are built. The recipe steps and the export are A3; the
form designer is session B and nothing of it exists yet.

## Owns

### The employee record — built

- **`EmployeePart`** — the fixed core specification section 5 names, and nothing
  else: employee code, English and Arabic names, date of birth, nationality,
  gender, join date, employment status and the date it took effect, line manager
  and photo.

  **It holds no organisational placement**, deliberately. No department, no cost
  centre, no structure, no parent. Every placement is a dated
  `EmployeeAssignment` row in `WorkMate.Dimensions` — architecture section 2's
  one rule that follows from the model — because an employee sits on several axes
  at once and a single department field would collapse them into one, and because
  a field has no dates, so a mid-month transfer would overwrite where somebody sat
  rather than record that they moved. `EmployeePartTests` reflects over the type
  and fails on any property that looks like one; it is written as a reflection
  test rather than as three named assertions because the way this decision gets
  undone is not somebody adding `Department` — nobody would — it is somebody
  adding `PrimaryUnitId` in good faith to make one screen simpler.

  **Line manager is not placement either**, and is here precisely because the two
  are different facts customers routinely have differ.

- **`EmployeeIndex`** — code lookup, bilingual name search, status filtering and
  "everyone employed on this date", without loading a content item.

  `CodeUpper` is a second column rather than a case-insensitive comparison in the
  query, because collation is the database's property and not ours: SQLite's
  default is case-sensitive and SQL Server's usual one is not, so the same
  uniqueness query would admit `EMP-1` alongside `emp-1` on one customer's
  database and refuse it on another. `Code` keeps the customer's own
  capitalisation, because that is what they recognise.

- **`EmployeeHandler`** — the chokepoint. The `Employee` type is creatable,
  listable and securable, so the admin screens, the API, GraphQL, a recipe and an
  import can all produce one; this is the only point all five share. An
  `IContentHandler`, not a `ContentPartHandler`, for the reason
  `DimensionRecordHandler` sets out at length: a part handler's validation context
  carries its own result object the caller never sees, so a rule written there
  compiles, runs and silently rejects nothing.

- **`IEmployeeService`** — create, update, correct the join date, the five
  lifecycle transitions, and the reads. Every dated write states its date; nothing
  is defaulted. Permissions are checked here, not only in a controller.

- **`EmployeeCodes`** — the natural key's format and comparison rules.

- **`IEmployeeLifecycleHandler`** — the domain events leave, attendance and
  payroll subscribe to in prompts 6 to 8.

- **`EmployeeLookup`** — this module's implementation of
  `WorkMate.Dimensions.Services.IEmployeeLookup`.

### The employee code

**The natural key: required, unique within the tenant including against
employees who have left, and immutable once the record exists.** It is what a
recipe, an export and every integration name an employee by, because a generated
content item id is tenant-specific and a file carrying one is portable nowhere.

Uniqueness includes leavers because payroll history, past approvals and last
year's headcount all resolve through an exited employee; reusing their code makes
every one of those ambiguous about which person it meant, and nothing reports it —
the figures are simply for two people added together.

**The format is deliberately looser than a dimension code's.**
`DimensionCodes.IsValidCode` requires a leading letter because a dimension code
becomes a *content type name* and a type name cannot start with a digit. An
employee code becomes nothing: it is stored, compared and printed. Carrying the
stricter rule across would refuse `00412` and `1001`, which is what most HR
departments and every payroll file already use.

So: one to thirty-two characters of letters, digits, hyphen, underscore, dot or
slash, no whitespace. Compared ordinally and case-insensitively, so an import that
shouts a code finds the same person rather than creating a second record for them.

**Not auto-generated.** A tenant setting for a code format is a sensible later
feature; inventing one now would make every tenant's numbering a product decision
taken by accident.

### The lifecycle

The five states in specification section 5 — prospective, active, on leave,
suspended, exited — with the permitted moves declared once in `EmployeeLifecycle`
so the screen that decides which buttons to offer and the service that decides
whether to accept a post read the same table. Two copies would disagree the first
time a state was added, and the disagreement would show up as a button that does
nothing.

Every transition is dated, audited and raises `EmployeeStatusChanged`.

**Everybody starts prospective, whatever their join date.** Activating somebody is
a decision a person makes and dates, never inferred from a date having passed:
"we hired them and they never turned up" is a real outcome, and a status that
activated itself could not represent it.

**A move to the state already held is refused.** Accepting it would overwrite the
date the current state began — which is the date leave accrual, probation and
suspension are counted from — on a request that said nothing about changing it.

**The exit is the transition worth reading.** It closes every placement *and*
every headship the person holds, on every axis, on the last day of service
inclusive. Closing the headships is the half that is easiest to forget and worst
to get wrong: a leaver who stays recorded as a head is somebody prompt 5's routing
still routes approvals to, and nothing about that failure is visible, because the
chart and the routing agree with each other and both are wrong. `PlanExitAsync` is
the dry run and names the units that would be left vacant before anybody commits —
the same dry-run-then-apply shape as a move, a merge or a retirement.

**Reinstating a leaver reopens nothing.** Where somebody sat before they left is
not where they sit now, and a rehire that silently restored last year's placements
could put them in a unit that has since been retired.

**The last day and the first day of ex-employment are consecutive days, not the
same day**, and both numbers get used: placements close on the last day inclusive,
while "had they left by this date" is asked of the day after. Rather than let one
field mean a last day in one state and a first day in every other — which is how
an off-by-one survives review — `ExitAsync` takes the last day, `StatusEffectiveFrom`
stores the day after it, and `ExitedOn` converts back. `StatusEffectiveFrom`
therefore means exactly what it is called, always.

### The fixed core is not editable, and that is enforced

Specification section 5: the core "is not editable by administrators because
payroll, leave and attendance depend on these fields by name". Several mechanisms,
because there are several routes in, all reading one policy —
`EmployeeContentDefinitionGuard` over `EmployeeFieldNames`:

| Route | Closed by | Closed? |
| --- | --- | --- |
| Orchard's content-type editor attaching or detaching the part | `EmployeePart` is declared **not attachable** in `Migrations`, so it is not offered in "Add parts" | **Yes** |
| A section this module defines colliding with a core name | Every section field goes through `EnsureMayDefine` as the migration declares it, so it fails at tenant setup | **Yes** |
| A section reintroducing placement as a field (`DepartmentCode`, `CostCentre`, …) | The same guard, `PlacementBelongsInTheDimensionEngine` | **Yes** |
| A form definition naming the `Employee` type | Session B's compiler, which is a write path of ours and can refuse | **Not yet** |

`EmployeeFieldNames.Reserved` is **declared** and then checked against the type by
reflection, rather than generated from it. Generating it would make any field
somebody adds automatically "reserved and therefore fine", which is the opposite
of a guard; declaring it makes adding a core field a deliberate act in two places,
and `EmployeePartTests` makes the second place compulsory.

**`IContentDefinitionEventHandler` was considered as a runtime backstop and
rejected.** Verified against the pinned version: all seventeen of its members are
`void` and past-tense — `ContentFieldAttached`, `ContentPartUpdated` — so it is a
notification that the change has happened, not a veto on it happening. A guard
built on it could report a violation and could not prevent one.

### The standard sections

Six, as specification section 5 names them: job details, documents, bank details,
qualifications, dependants and contacts. Each is a `BagPart` on the `Employee`
type, restricted by `BagPartSettings.ContainedContentTypes` to one item type of
its own.

**A bag of typed items rather than fields on the record**, because every one of
them is a list: an employee has several documents, several qualifications, more
than one dependant, two bank accounts when their salary is split, and a job
history rather than a job. Documents are the clearest case — the expiry alerts the
product promises are a query over those rows, and a flattened section could hold
one passport and no visa.

No section item type is creatable or listable on its own. A bank account with no
employee is not a thing, and a listable one would appear in the content picker of
every form in the tenant. They carry the `EmployeeSection` stereotype instead, so
a picker or session B's designer can ask for "an employee section" without a
hard-coded list of six names that would be wrong the moment a tenant adds a
seventh.

Restricting each bag matters: without it a bag offers every creatable type in the
tenant, so a "Bank details" section would invite somebody to add a blog post to it.

## Admin UI

Four screens, each calling the same services a recipe step or an API would — no
screen holds a privileged path.

- **`EmployeesAdminController`** — the list and the editor. The list searches a
  code and both halves of a name, filters by status, and shows where each person
  sits today on the primary organisation axis. That last column comes from the
  dimension engine and is not on the record at all, which is why this module
  ships a list rather than relying on Orchard's generic one.

  **The list resolves placement in two queries per page, not two per row.** A
  screen is the easiest place in a product to grow an N+1, and this one would
  have grown it on the column that matters most.

- **`EmployeeLifecycleAdminController`** — the five dated transitions, each its
  own small form rather than one screen with a dropdown, for the reason the
  designer gives every action its own page: these commit dated decisions with
  consequences.

  **The exit is a dry run first.** `GET` computes what ending employment would
  close — every placement on every axis, and every unit the person heads — and
  only `POST` commits. The units losing their head are the half worth reading:
  a department losing its approver on Friday is something somebody has to act
  on, and finding out from a stuck approval the following week is too late.

- **`EmployeePlacementAdminController`** — where one person sits on every axis on
  a date, what they lead, and the dated operations that change it.

  **A screen rather than a driver on the employee editor.** A display driver's
  `UpdateAsync` has nowhere to put a required effective date: it is handed a
  posted model and asked to write it. Every write here is dated and nothing is
  defaulted, so the editor shows placement read-only and links here.

  The unit picker offers only units the axis says may hold employees, from
  `IDimensionGraphService.GetEmployeeAttachableUnitsAsync` — the same rule
  `ValidateAssignmentAsync` enforces, read forwards. That is ADR-0010's lesson
  applied to a second rule: two readings of one rule, implemented separately,
  disagree on exactly the case the rule exists for.

- **Appointing a unit head** is on the designer's own card, in
  `WorkMate.Dimensions` — the question a person is asking is "who runs this
  department", not "what does this person run". The placement screen lists what
  somebody leads and says where to change it.

### The employee picker, which is two things

| | What it is | Where |
| --- | --- | --- |
| **The picker field** | An Orchard `ContentPickerField` restricted to `Employee`, with its candidate set served by `EmployeePickerResultProvider` and filtered through `IVisibilityService`. Searches rather than lists, so it scales. What session B's form designer generates. | `EmployeePickerResultProvider` |
| **The picker control** | `<workmate-employee-picker>`, a server-rendered `<select>` for the plain admin forms that are not content items — setting a unit's head, naming a line manager. Works with no JavaScript. Says out loud when the list is capped, because a control showing the first hundred of four hundred in silence is one somebody will use to pick the wrong person. | `EmployeeDirectory`, behind `WorkMate.Platform`'s tag helper |

Both go through **`IVisibilityService`**, which allows everything until prompt 9
builds visibility by node. It is wired now rather than retrofitted later
precisely because a picker written without it is a picker whose call sites nobody
can find.

## Depends on

- **WorkMate.Core** — `BilingualText`, `EffectiveRange`, `Page<T>`
- **WorkMate.Platform** — the bilingual field, the platform roles,
  `IBilingualNamePolicy`, `ISystemOperation`
- **WorkMate.Dimensions** — `IEmployeeAssignmentService` (placements and head
  appointments), `IDimensionService` and `IStructureService` (naming the units an
  exit vacates), and `EffectiveDates`, which is the one place a calendar date
  crosses into an index column and back. This module reuses it rather than keeping
  a second copy with a second open-ended sentinel.
- **OrchardCore.Flows** — `BagPart` and `BagPartSettings`, for the six standard
  sections. **The feature has to be enabled on the tenant**, and
  `recipes/base.recipe.json` now names it; without it the `BagPart` definition
  does not exist and the migration cannot attach a section.
- **OrchardCore.Title** — the generated title bound to the English name
- **OrchardCore.AuditTrail** — where this module's three events are recorded

## What it records in the audit trail

Category `Employee`, three mandatory events: `EmployeeChanged`,
`EmploymentStatusChanged` and `JoinDateCorrected`. The last two are separate from
the first because they are the kinds of edit that change what *other* modules do —
an exit closes placements and headships and starts a final settlement; a corrected
join date moves gratuity, accrual and probation — and an auditor reconciling one
of those should not have to read every phone-number change to find it.

All three are mandatory rather than merely enabled by default: an event an
administrator can quietly switch off is not an audit trail, least of all the one
that records somebody leaving.

## Permissions

| | Who gets it by default |
| --- | --- |
| `ManageEmployees` — create and change records | Platform/Tenant admin, HR admin |
| `ViewEmployees` — see the list and a record | Platform/Tenant admin, HR admin, Auditor |
| `ChangeEmploymentStatus` — the lifecycle, and correcting a join date. Security-critical | Platform/Tenant admin, HR admin |
| `ViewEmployeeSensitiveData` — bank details, documents, date of birth. Security-critical | Platform/Tenant admin, HR admin |

**Placement is deliberately not one of them.** Placing an employee on a structure
is a dimension-engine write and already has
`WorkMate.Dimensions.Permissions.AssignEmployees`; a second permission here would
split one capability across two modules and leave a customer granting both to
achieve one thing. Appointing a unit head is the same write and takes the same
permission.

**Managers and employees get nothing here.** What a manager may see of their own
team is data visibility by node, which is specification section 9's concern and a
different mechanism entirely. Granting `ViewEmployees` to managers now would give
every one of them the whole tenant's staff list, which is exactly what that
mechanism exists to prevent.

## Recipe steps

_None yet._ `employees` and its export land in session A3, alongside
`employee-assignments` and `unit-heads` in `WorkMate.Dimensions`.

## Decisions recorded

- **ADR-0003 and its addendum** — the bilingual field lives in
  `WorkMate.Platform`; English is required and Arabic is optional unless the
  tenant turns `RequireArabicNames` on. Asked here through `IBilingualNamePolicy`,
  the same one-question seam every write path on the platform uses, so the
  employee record and the organisation chart cannot disagree about the answer.
- **ADR-0006** — write paths read content definitions with `Load…`, never `Get…`.
  Read it before writing session B's form designer, which creates content types at
  runtime the same way this module's migration does.
- **ADR-0009** — migrations are append-only.
- **ADR-0012** — the unit head is a dated appointment of its own, not a flag on an
  assignment row and not a field on the dimension record. The storage and the
  service surface are in `WorkMate.Dimensions`; what this module owns is closing
  an employee's headships when they leave.

## The generated title, and why a handler sets it

Both `Employee` and every generated dimension type bind `TitlePart` to the
English name with `TitlePartOptions.GeneratedDisabled` and a Liquid pattern,
which is what specification section 4 asks for — and on a real tenant it produces
nothing. `TitlePartHandler` renders the pattern through `ILiquidTemplateManager`,
and **that throws outside an HTTP request**, measured directly against a tenant in
a shell scope.

So pattern-generated titles never worked for anything created by a service, a
recipe import or a background job — which is every record on a tenant seeded from
a recipe. The symptom is silent: the record is correct in every other respect and
simply has no name on Orchard's content list, in a content picker, or anywhere
else reading `DisplayText`.

`EmployeeHandler` therefore sets `DisplayText` from the English name on the
present-tense hooks, and `DimensionRecordHandler` does the same for units.
`Migrations.UpdateFrom1Async` here and
`WorkMate.Dimensions.Migrations.UpdateFrom6Async` repair what is already stored,
filling only what is absent so a hand-typed title survives. The pattern is kept:
the two cannot disagree — both are "the English name" — and a path where Liquid
does work gives the same answer rather than a different one.

## Known limitations

### Two simultaneous creates can share a code

Even with uniqueness enforced in both the service and the handler, two requests
creating the same code at the same instant can both pass validation and both
commit: validation is a read followed by a write, and nothing in between stops a
second writer. The same limitation `WorkMate.Dimensions` records for record codes,
with the same cause — YesSql 5.4.7's schema builder offers no unique index — and
the same consequence, which is a duplicate code rather than lost or corrupted data.

### One way to create an employee, and why

`Employee` is **not creatable** through Orchard's generic "new content item"
screen. It is still listable and still securable; it simply has no New button.

That path does not call `IEmployeeService.CreateAsync`, so it would never raise
`EmployeeCreated` — and leave, attendance and payroll, which specification
section 5 requires to subscribe to it, would simply never be told about the
person. The failure is permanent and silent: there is no later moment at which
anybody notices the event did not fire. `Migrations.UpdateFrom2Async` applies the
setting to a tenant created before it.

Editing is unaffected, which is what the standard sections are edited through.

### Two things duplicated from `WorkMate.Dimensions`, on purpose

`RecordResult<T>` / `RecordError` / `RecordRule` mirror `DimensionResult<T>` /
`DimensionError` / `DimensionRule`, and `IRecordsAuthorisation` mirrors
`IDimensionAuthorisation`. In both cases the right end state is one shared
abstraction in `WorkMate.Core` or `WorkMate.Platform` that every module takes.
Hoisting either means editing every call site in `WorkMate.Dimensions` for no
behaviour change, which is its own reviewable piece of work with its own risk;
doing it inside this slice would bury it. Recorded here so it is a decision
somebody took rather than a thing nobody noticed.

## Specification

Section 5 of `/docs/technical-specification.md`.
