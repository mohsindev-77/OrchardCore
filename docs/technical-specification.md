# WorkMate 2.0 — Technical Specification for Orchard Core

Sep 29, 2026 · @Umair Tariq

The engineering specification: how the platform in the product specification is built on Orchard Core, module by module, with the conventions the whole team follows.

## Purpose and scope

This document tells an engineer how WorkMate 2.0 is built on Orchard Core. The product specification says what the platform is; the dimension engine architecture settles the foundation design; this document covers everything the team writes, module by module, and the conventions every module follows.

**Scope:** the platform foundation, the dimension engine as implemented, records and the form designer, approvals, the five HCM module groups, entitlement and the control plane, cross-cutting concerns, and the quality and delivery process. This is the first business domain end to end; later domains follow the same patterns.

**Not in scope:** the migration of existing customers, the aviation pack's duty-rule capability, and any domain beyond human capital. Each has or will have its own document.

### How to read it

Sections 2 and 3 apply to every module and are read first. Sections 4 to 8 are the build order: each depends on the ones before it. Section 9 is consulted while building anything. Section 10 is the definition of done.

Where a section states a rule, it is binding. Where it states a mechanism, the mechanism is the decision and the exact API is a detail to confirm.

### Verify against the version in use

Orchard Core's APIs move between releases: module names, service interfaces, and the shapes of content definition, workflow and indexing calls have all changed across versions. Before any module is started, the team pins one Orchard Core version in the solution, records it here, and checks every type and interface this document names against it. A mechanism that turns out to work differently in the pinned version is a finding to record in section 11, not a reason to depart from the design silently.

**Pinned version:** \[to be recorded at project start\]

### Companion documents

The product specification, the dimension engine architecture, and the Claude Code prompt library that turns each section of this document into a scoped piece of development work.

## Solution structure and conventions

These conventions exist so that any engineer, or Claude Code, can open any module and know where things are. They are not optional.

### Repository layout

```
/src
  /WorkMate.Web                      Orchard Core host; no business code
  /WorkMate.Core                     shared abstractions, no Orchard dependency
  /WorkMate.Platform                 tenancy, entitlement hooks, base recipe, shared UI
  /WorkMate.Dimensions               the dimension engine (section 4)
  /WorkMate.Records                  employee record, form designer (section 5)
  /WorkMate.Approvals                approval engine (section 6)
  /WorkMate.Hcm.Core                 Core HR module group
  /WorkMate.Hcm.Leave
  /WorkMate.Hcm.Attendance
  /WorkMate.Hcm.Payroll
  /WorkMate.Hcm.SelfService
  /WorkMate.Entitlement              entitlement service and control panel (section 8)
  /WorkMate.Reporting
/tests
  /WorkMate.<Module>.Tests           one test project per module
  /WorkMate.Integration.Tests        multi-tenant, recipe and migration tests
/recipes                             tenant recipes: base, editions, sector packs
/tools                               seed generator, migration tooling, scripts
/docs                                ADRs and module READMEs
```

Every module is an Orchard Core module project with a `Manifest.cs`, its own `Startup.cs`, `Migrations.cs`, and the folders Orchard expects: `Controllers`, `Drivers`, `Handlers`, `Indexes`, `Models`, `Services`, `ViewModels`, `Views`, `Recipes`.

### Naming

| Thing | Convention | Example |
| --- | --- | --- |
| Module project | `WorkMate.<Area>` or `WorkMate.Hcm.<Module>` | `WorkMate.Hcm.Leave` |
| Feature id | matches the project name | `WorkMate.Hcm.Leave` |
| Content part | `<Noun>Part` | `DimensionRecordPart` |
| Content type | PascalCase, singular, no prefix | `Employee`, `LeaveRequest` |
| Index | `<Noun>Index` | `DimensionClosureIndex` |
| Service interface | `I<Noun>Service`, one per aggregate | `IDimensionService` |
| Permission | `<Verb><Noun>` constants in `Permissions.cs` | `ManageDimensions` |
| Recipe step | `<module>-<purpose>` | `dimensions-seed` |
| Migration method | `CreateAsync`, then `UpdateFrom<N>Async` | `UpdateFrom3Async` |

### Rules that apply everywhere

1. **Business logic lives in services, never in controllers, drivers or handlers.** Drivers shape content for display and edit; handlers react to lifecycle events; both delegate.
2. **Every custom table has a YesSql index class and an index provider.** No raw SQL for reads. Raw SQL is permitted only inside a migration or a clearly named maintenance command.
3. **Every write takes an explicit effective date where the model is dated.** No defaulting on writes.
4. **Every service method is tenant-scoped by construction.** Nothing takes a tenant id as a parameter; the shell scope supplies it.
5. **Permissions are declared in the module and checked in the service**, not only in the controller, so the API and background jobs are covered.
6. **Migrations are additive.** A migration never destroys data; a deprecation is a new migration that marks and later removes.
7. **Every user-visible string goes through the localiser.** No literal English in views or services.
8. **Async throughout, cancellation tokens on every public method.**
9. **One aggregate, one service.** A service that needs another aggregate injects that aggregate's service, never its tables.
10. **A module's README states what it owns, what it depends on, and its recipe steps.** Updated in the same pull request as any change to those.

### Coding standards

.NET analyzers enabled at warning-as-error for the solution. Nullable reference types on. File-scoped namespaces. `record` types for value objects and service results. No public setters on domain models outside content parts. Tests follow the module structure and are named for the behaviour they assert.

### Branching and review

Trunk-based with short-lived branches. Every change is reviewed by a person; Claude Code output is reviewed with the same rigour as human output. A change to a migration, a recipe step or a permission requires a second reviewer.

## Platform foundation

The `WorkMate.Platform` module and the host. Everything else assumes this exists.

### Tenancy

One Orchard Core tenant per customer, created through the control plane (section 8), never by hand. Each tenant has its own database or table prefix per the deployment model; the shell scope is the isolation boundary and no code path opens a session outside it. Tenant names follow `<customer-code>-<env>` so that production and configuration tenants for one customer are unmistakable.

### Environments per customer

| Tenant | Purpose | Who configures |
| --- | --- | --- |
| `<code>-config` | Where configuration is made and validated | Delivery team, customer admins |
| `<code>-prod` | Live | Restricted role only, always audited |

Promotion from config to prod is a recipe export, review, and import. There is no other supported path.

### Feature activation

Each module is an Orchard feature. Which features are enabled in a tenant is decided by the entitlement record, applied at provisioning and on every entitlement change. A feature that is not entitled is not enabled: its routes, admin menu items, background tasks and API endpoints do not exist in that tenant. This is the coarse gate; section 8 covers the fine one.

### Site settings

Platform-wide settings live in a `WorkMateSettings` site settings part: default locale and calendar, RTL preference, fiscal year start, currency, working week, and the customer code. Module-specific settings live in their own settings parts and are never added to the platform part.

### Base recipe

`recipes/base.recipe.json` runs on every new tenant before anything customer-specific:

1. Enable the platform features and the entitled module features
2. Create the platform roles: Platform Administrator, Tenant Administrator, HR Administrator, Manager, Employee, Auditor
3. Set default site settings and localisation cultures (`en`, `ar`)
4. Register the system dimension types (section 4) and the primary Organisation structure
5. Create the standard content types that every tenant has (section 5)
6. Install the standard report library

Edition recipes and sector packs layer on top of the base; they never replace it.

### Shared UI

The admin theme is Orchard's, extended with a WorkMate layout that provides the RTL-aware shell, the bilingual field editor pattern, and the approval badge in the header. Employee and manager surfaces are a separate front-end theme in `WorkMate.Hcm.SelfService`. Both consume a shared component set so that a bilingual field, a dimension picker or a date-range control looks and behaves identically everywhere.

### Startup order

`WorkMate.Platform` declares dependencies on the Orchard features it needs. Every WorkMate module declares a dependency on `WorkMate.Platform`; HCM modules also declare `WorkMate.Dimensions`, `WorkMate.Records` and `WorkMate.Approvals` as needed. Nothing depends on an HCM module except another HCM module, and payroll depends on leave and attendance, never the reverse.

## Dimension engine on Orchard Core

The design is settled in the dimension engine architecture document. This section is the implementation contract for `WorkMate.Dimensions`.

### Storage decisions

| Entity | Storage | Implementation |
| --- | --- | --- |
| DimensionType, Structure, StructureLevel | Plain tables via YesSql documents with indexes | `DimensionTypeDocument`, `StructureDocument`; indexes `DimensionTypeIndex`, `StructureIndex` |
| DimensionRecord | Content item; one content type per dimension type, created at runtime | `DimensionRecordPart` attached to each generated type, plus the type's own fields |
| DimensionLink, DimensionClosure, EmployeeAssignment | Custom YesSql index tables with no content item behind them | `DimensionLinkIndex`, `DimensionClosureIndex`, `EmployeeAssignmentIndex`, each with a provider and a migration |

### DimensionRecordPart

The standard part every generated dimension content type carries: `Code`, `NameEn`, `NameAr`, `DimensionTypeId`, `EffectiveFrom`, `EffectiveTo`, `IsActive`, `SortOrder`, `CostCentreCode`, `GlAccountRef`, `HeadEmployeeId`. The part has a driver for edit and display, a handler that validates and maintains indexes, and a `DimensionRecordPartIndex` for code lookups and type filtering.

### Runtime content type creation

When an administrator creates a dimension type, `IDimensionTypeService.CreateAsync` uses the content definition manager to: create a content type named for the dimension type's code; attach `DimensionRecordPart`; attach `TitlePart` bound to `NameEn` for admin listing; add the fields the attribute schema declares; set the type as creatable, listable, securable and not draftable. The reverse on delete is refused if any record exists.

Every content-definition change made this way is recorded in the audit trail with the acting user and a diff of the definition.

### Index maintenance

`DimensionRecordPartHandler` handles created, updated and removed events. It never writes the closure index itself; it calls `IDimensionGraphService`, which owns link and closure maintenance inside the ambient session so that a record write and its index changes commit or roll back together. Move and merge are explicit service operations, not edits to a parent field.

### Services

| Interface | Responsibility |
| --- | --- |
| `IDimensionTypeService` | Create, update, retire dimension types; manage attribute schemas; create the backing content type |
| `IStructureService` | Define structures and their ordered levels and rules |
| `IDimensionService` | Create, update, retire, move, merge records; resolve by id or code; all dated |
| `IDimensionGraphService` | Ancestors, descendants, children, is-under, depth; link and closure maintenance; rebuild and verify |
| `IEmployeeAssignmentService` | Place, end, re-allocate; resolve effective assignment; employees under a node as at a date |
| `IDimensionValidator` | The single validation service; every write path calls it |

### Permissions

`ManageDimensionTypes`, `ManageStructures`, `ManageDimensionRecords`, `MoveDimensionRecords`, `MergeDimensionRecords`, `ViewDimensionHistory`, `AssignEmployees`. Data visibility by node is a separate concern implemented in section 9 and consumed by every module.

### Admin UI

Three screens: the dimension type list and editor including the attribute schema builder; the structure editor with ordered levels and rules; and the org designer, a tree per structure with drag-to-move, inline rename, retire, merge, and an effective-date control on every mutation. The designer calls the same services as the API; it has no privileged path.

### Recipe steps

`dimension-types`, `structures`, `dimension-records` (with parent references by code, resolved at import), and `employee-assignments`. Export produces these in dependency order. Import validates the whole set before writing any of it.

### Caching

Structure and record lookups are cached per tenant via `IMemoryCache` keyed on tenant name plus structure id, invalidated by the graph service on any mutation. The cache key including the tenant is asserted by a test.

### Tests required

Unit: every validator rule, closure maintenance on create, move, delete and merge, effective-dated resolution at boundary dates. Integration: index verification after every mutation in a scripted scenario; recipe round-trip; tenant isolation of cache and queries. Performance: descendant resolution under 200 ms at 5,000 records and 5,000 employees on seeded data.

## Records and the form designer

`WorkMate.Records` owns the employee record and the mechanism by which administrators define forms. Both rest on the same primitive: content type definition at runtime.

### The employee record

`Employee` is a content type with a fixed core and a configurable remainder. The core is `EmployeePart`: employee code, English and Arabic names, date of birth, nationality, gender, join date, employment status, line manager (content picker to `Employee`), and photo. It is not editable by administrators because payroll, leave and attendance depend on these fields by name.

The configurable remainder is a set of named sections, each a `BagPart` of typed items or a set of content fields the administrator adds through the form designer. Job details, documents, bank details, qualifications, dependants and contact information ship as standard sections; a tenant can add its own.

The employee holds no department or cost-centre field. Placement is an assignment row in the dimension engine, and the employee editor shows a Placement section that reads and writes through `IEmployeeAssignmentService`.

### Employee lifecycle

Status transitions — prospective, active, on leave, suspended, exited — are service operations on `IEmployeeService`, each dated and each raising a domain event that leave, attendance and payroll subscribe to. Exit triggers final-settlement calculation in payroll and closes open assignments on the exit date.

### The form designer

An administrator-facing designer that produces content types. A form definition holds: name and code, bilingual labels, sections, fields with type and validation, conditional visibility rules, calculated fields, and the approval definition it uses if it is a request form.

`IFormDefinitionService.PublishAsync` compiles a form definition into a content type: one content type per form, `FormSubmissionPart` attached for request forms, fields mapped to Orchard content fields with WorkMate custom field types where needed (bilingual text, dimension picker, employee picker, money, Hijri-aware date, attachment set). Publishing a change creates a new definition version; existing submissions keep the version they were created against.

### Field types

| WorkMate field | Backed by | Notes |
| --- | --- | --- |
| Bilingual text | Custom field with `En` and `Ar` values | Used everywhere a name appears |
| Dimension picker | Custom field storing a record id, structure-scoped | Honours node visibility |
| Employee picker | Content picker restricted to `Employee` with visibility filtering |  |
| Money | Decimal with currency code | Currency from tenant settings unless overridden |
| Date | Date with dual calendar display | Stored Gregorian |
| Attachment set | Media field with document type and expiry | Feeds document expiry alerts |
| Lookup | Taxonomy field | The one place taxonomies are used: flat lookup lists only |

### Validation

Definition-time: field type constraints, required, ranges, regular expressions, uniqueness within tenant. Cross-field rules are expressed in a small declarative rule language evaluated server-side; scripting is not exposed to administrators. Conditional visibility is evaluated both client- and server-side; the server is authoritative.

### Localisation

Every label, help text and option in a form definition is bilingual at definition time. The editor renders RTL when the current culture is Arabic. Validation messages are localised through the standard localiser.

### Recipe steps

`form-definitions` exports definitions, not the generated content types; import republishes them, which regenerates the types. This keeps recipes stable across Orchard versions.

### Tests required

Publish and republish round-trip; version pinning of submissions; every field type's edit and display driver; visibility rule evaluation; bilingual rendering in both cultures; employee lifecycle events reaching their subscribers.

## Approvals

`WorkMate.Approvals` gives administrators a simple approval designer and runs the result on Orchard's workflow engine. Administrators never see the workflow canvas; engineers keep it for unusual cases.

### The approval definition

An `ApprovalDefinition` document: the form it applies to, an organisation scope (a dimension node, with or without descendants), an ordered list of steps, and a version. Each step has an approver resolution rule, an optional condition, an optional deadline, and an escalation target.

| Approver rule | Resolves to |
| --- | --- |
| Line manager | The requester's line manager, walked up N levels if specified |
| Unit head | The head of the requester's node on a named structure, walked up if vacant |
| Role in scope | Any user holding a role, filtered to the requester's node visibility |
| Named user | A specific user, with a required fallback |
| Requester's choice | From a permitted set, chosen at submission |

Conditions are the same declarative rule language as form validation, evaluated against the submission. A step whose condition is false is skipped and recorded as skipped.

### Compilation to workflows

`IApprovalCompiler.CompileAsync` turns a definition into an Orchard workflow type: a content-created trigger filtered by form type and scope, then one custom activity per step, with the branching that conditions and outcomes require. The generated workflow type is marked as system-managed and hidden from the admin workflow list. Recompiling on definition change produces a new workflow type version; in-flight instances continue on the old one.

### Custom activities

`ResolveApproversActivity`, `AwaitApprovalActivity` (a blocking activity correlated to the submission), `RecordDecisionActivity`, `EscalateActivity`, `NotifyActivity`, `SetSubmissionStatusActivity`, `DelegateCheckActivity`. Each is small, tested in isolation, and expresses one HCM verb. No script activities in generated workflows.

### Runtime model

`ApprovalTask` rows, one per pending approver per step, indexed by assignee, submission, due date and status. The inbox reads these; nothing reads workflow instances directly. A decision writes the task, resumes the blocking activity, and appends to the immutable `ApprovalHistory`.

### Delegation and out-of-office

A user may delegate to another for a date range, optionally per form type. Task resolution checks active delegations at assignment time and again at decision time; a decision made under delegation is recorded with both users. Delegation chains are limited to one hop.

### Deadlines and escalation

A background task scans due tasks on a schedule, escalates per the step's rule, and records the escalation. Escalation never removes the original assignee's ability to act; it adds an assignee.

### Inbox

One screen, the most used in the product: my pending tasks, tasks I delegated, tasks awaiting me by deadline, and history. Bulk approve for tasks of the same form type. Available on web and mobile through the same API.

### Definition versioning

The rule is fixed: a submission completes under the definition version it started on. An administrator may cancel and resubmit under the new version, which is recorded as such. There is no migration of in-flight requests between versions.

### Permissions

`ManageApprovalDefinitions`, `ActOnApprovals`, `DelegateApprovals`, `ViewAllApprovals` (audit role), `OverrideApproval` (restricted, always recorded with a reason).

### Tests required

Every approver rule including vacant-head walk-up; condition skipping; compilation producing a valid, runnable workflow type; version pinning of in-flight requests; delegation at assignment and at decision; escalation adding rather than replacing; inbox queries returning only tasks visible to the user.

## HCM modules

Five module groups, each an Orchard module depending on Dimensions, Records and Approvals. Every module follows one shape: content types for records, custom indexes for anything queried by range or aggregate, a service per aggregate, domain events for cross-module effects, recipe steps for its configuration, and its own tests.

### Core HR — `WorkMate.Hcm.Core`

| Capability | Implementation |
| --- | --- |
| Documents and expiry | `EmployeeDocument` content type with type, issue and expiry dates, attachment; `DocumentExpiryIndex`; a daily background task raising expiry notifications at configured lead times; renewal is a request form |
| Letters | `LetterTemplate` content type with Liquid body and bilingual variants; `ILetterService` renders against employee data and records issue; issue may require approval |
| Assets | `Asset` and `AssetAssignment` with dated assignment rows; return on exit enforced through the lifecycle event |
| Announcements | Content type scoped by dimension node; read receipts indexed per user |
| Air ticket entitlement | `TicketEntitlement` per employee with cycle, eligibility and accrual rule; consumed and encashed through request forms; posts to payroll accruals |
| Government data | Configurable section on the employee record with expiry-bearing fields feeding the same expiry task |

### Leave — `WorkMate.Hcm.Leave`

| Capability | Implementation |
| --- | --- |
| Types and policies | `LeaveType` and `LeavePolicy` documents; a policy binds entitlement, accrual rule, carry-forward, encashment rule and eligibility to a dimension scope and employee attributes, effective-dated |
| Balances | `LeaveBalanceIndex` maintained by `ILeaveBalanceService`; never computed by summing requests at read time; a rebuild command exists and a verification compares the two |
| Opening balances | Import with dry run and reconciliation report; written as a dated adjustment with reason |
| Requests | `LeaveRequest` request form; balance check at submission and again at final approval; calendar and working-week aware |
| Accruals | Period accrual task producing dated balance movements and a liability figure per employee, exposed to payroll |
| TOIL | Earned from approved overtime or duty via a domain event; its own type with expiry |
| Encashment | Request form; amount computed by payroll's formula engine, not by leave |

### Time and attendance — `WorkMate.Hcm.Attendance`

| Capability | Implementation |
| --- | --- |
| Shifts and patterns | `Shift` and `ShiftPattern` documents with breaks, tolerances and overtime rules |
| Rosters | `RosterAssignment` rows per employee per day; publish is an explicit operation raising a notification; swap and cover are request forms |
| Device integration | `IAttendanceDeviceAdapter` per vendor behind a common contract; scheduled and on-demand pull into a raw `Punch` table; matching to employees by device id mapping |
| Daily processing | A background task pairs punches into `AttendanceDay` rows against the roster: late, early, absent, overtime, missing punch |
| Corrections | Request form with reason code; original values retained; approval per definition |
| Payroll feed | `IAttendanceSummaryService` returns per-employee period summaries; payroll pulls, attendance never pushes |

### Payroll — `WorkMate.Hcm.Payroll`

The most consequential module and the one held to the strictest rule: every calculation is reproducible and explainable.

| Capability | Implementation |
| --- | --- |
| Setups and policies | Company and branch cut-offs, pay groups, elements (earnings, deductions, employer contributions), eligibility by scope, all effective-dated |
| Formula engine | A sandboxed expression evaluator over a typed context: employee attributes, element values, attendance summary, leave liability, dimension attributes. No general scripting. Formulas are versioned and every run records which version produced each figure |
| Run lifecycle | `PayrollRun` document: draft → readiness check → calculated → reviewed → approved → posted → closed. Each transition is a service call with permission and audit |
| Readiness | Checklist of blocking and warning conditions: unapproved overtime, pending leave, missing bank details, incomplete assignments |
| Calculation | Per employee, per element, in dependency order, writing `PayrollLine` rows with the inputs that produced them. A run is idempotent: recalculation replaces lines, never appends |
| Cost allocation | Each line split by the employee's effective cost assignments and allocation percentages; a mid-month transfer produces two allocation rows |
| GL posting | Journal generated from allocated lines through the COA mapping on cost dimension records; export format per ERP adapter |
| Statutory and bank output | Country adapters producing GOSI, WPS, LMRA and bank files from posted runs |
| Settlements | Final settlement composed from leave liability, indemnity rule, ticket accrual and unpaid elements, triggered by the exit event |
| Traceability | For any employee in any run: the elements, the formula versions, the inputs, and the allocation, as one readable view |

### Self-service and mobile — `WorkMate.Hcm.SelfService`

| Capability | Implementation |
| --- | --- |
| Employee and manager web | A front-end theme over the same services; every screen reads through visibility filtering |
| Request submission | Any published request form; drafts, attachments, status tracking against approval tasks |
| Approvals inbox | The Approvals module's inbox rendered for the front end |
| Payslips | Rendered from posted runs, access-controlled to the employee and payroll roles |
| Mobile | Native iOS and Android consuming a dedicated, versioned API surface; push via the notification module; bilingual and RTL |

### Cross-module rules

Payroll pulls from leave and attendance; they never write into payroll. Lifecycle events flow from Records outward. No HCM module reads another's tables; it uses the other's service. Every request form in any module routes through Approvals; no module has its own approval logic.

## Entitlement and the control plane

`WorkMate.Entitlement` runs in every tenant; the control panel runs only in the default tenant, which holds no customer data.

### The entitlement record

One signed document per tenant: edition, add-ons, packs, licensed headcount, deployment type, valid from and to, grace days, and a signature over all of it. Written by the control panel into the tenant's settings at provisioning and on every change. Read-only inside the tenant; the signature is verified on load and on a schedule, and a failed verification moves the tenant to read-only with an administrator alert.

### Two levels of gating

**Coarse:** feature activation. Section 3 covers it: an unentitled module's feature is disabled, so its routes and jobs do not exist.

**Fine:** `IEntitlementService.IsAllowedAsync(capability)`. Capabilities are string constants declared per module in a `Capabilities.cs`. Services check them at the point of use; navigation providers check them to build menus; the API returns a typed entitlement error, never a generic failure. A capability check inside a module that is not entitled is unreachable by construction, but the check is written anyway so that fine boundaries within an entitled module hold.

### Headcount

A nightly task counts active employees against the licence. Over by any amount: a soft warning in the admin dashboard. Over beyond the grace allowance for beyond the grace period: creation of new employee records is blocked with a clear message; nothing else changes. The count and the licence are both visible to the tenant administrator so the conversation happens before the block.

### Behaviour on change

| Change | Effect |
| --- | --- |
| Upgrade | Features enabled, configuration steps appear, immediate |
| Downgrade | Features disabled, data and configuration retained read-only, tenant told exactly what changed |
| Expiry | Grace period with full read and restricted write, then read-only; data never deleted |
| Renewal | Restores the previous state without reconfiguration |

### The super control panel

A module enabled only in the default tenant: create and retire tenants, apply base and edition recipes, issue and amend entitlement records, report usage against licence across the estate, and track platform version per tenant. Every action is audited with the acting operator. Access is restricted to a platform operator role with mandatory MFA.

### On-premises

An on-premises deployment carries a signed offline licence file with the same schema, validated on start and daily. A licence nearing expiry warns; an expired one enters grace exactly as the cloud case. Renewal is a file replacement, no connectivity required.

### Tests required

Signature verification and tamper detection; every capability check in every module covered by a test that runs with and without entitlement; headcount warning and block thresholds; downgrade preserving data; the control panel refusing to run outside the default tenant.

## Cross-cutting concerns

Consulted while building anything. Each is implemented once in `WorkMate.Platform` or `WorkMate.Core` and consumed everywhere.

### Data visibility by node

The rule every module obeys: a user sees an employee's data only if the employee's primary organisation node is within the user's visibility scope. `IVisibilityService` resolves a user's scope from role assignments to nodes (with or without descendants) and exposes a predicate that every employee-bearing query applies. Managers get their own subtree by default; HR administrators get scopes assigned explicitly. The predicate is applied in services, so the API and background jobs are covered; it is never applied only in a view.

### Localisation and RTL

Cultures `en` and `ar` on every tenant; content localisation for user-authored text; the bilingual field type for names and labels; a layout that switches direction on culture; date and number formatting through the culture; Hijri display alongside Gregorian where the tenant enables it. No literal strings in code. A pull request that adds a user-visible string without a resource entry fails review.

### Audit

Orchard's audit trail extended with WorkMate event types: structure change, assignment change, policy change, payroll transition, approval decision, permission change, entitlement change, content-definition change. Each event carries the before and after values and the acting user. Retention is per tenant setting, never shorter than the longest regulatory period the tenant declares.

### Security

Authentication through Orchard users with password policy at all editions; OpenID Connect against the customer's provider at Enterprise; MFA enforceable per role. Authorisation by permission and by visibility scope, both checked in services. Secrets in the host's configured secret store, never in recipes or settings. Dependency and container scanning in the pipeline. All file uploads scanned and stored outside the web root through the media module.

### API and GraphQL

Orchard's GraphQL exposes content types automatically; WorkMate adds visibility filtering to every employee-bearing type through a custom filter and disables the default unfiltered access. A versioned REST surface under `/api/v1` serves mobile and integration: structures, employees, requests, approvals, payslips, attendance. Every endpoint checks entitlement, permission and visibility. Webhooks for lifecycle events, approval outcomes and payroll posting, with signed payloads and retry.

### Background jobs

Orchard background tasks, one per concern: document expiry, attendance daily processing, leave accrual, approval escalation, headcount check, entitlement verification, closure index verification. Each is idempotent, logs its run with tenant context, and is disabled automatically when its module's feature is disabled.

### Caching

Per-tenant memory cache for structures, policies and entitlement; distributed cache optional per deployment. Every cache key begins with the tenant name. Invalidation is explicit and owned by the service that owns the data.

### Notifications

Orchard's notification module for in-app; email through the configured provider; push through the mobile API. Templates are bilingual Liquid, selected by the recipient's preferred culture. Every notification is recorded so that "was the manager told" is answerable.

### Observability

Structured logging with tenant, user and correlation id on every line. Health endpoints per tenant. Metrics for request latency, background task duration, approval task age and payroll run duration. Alerts on entitlement verification failure, closure index divergence and background task failure.

## Quality and delivery

The definition of done for every piece of work in this specification.

### Testing strategy

| Level | What it covers | Where it runs |
| --- | --- | --- |
| Unit | Services, validators, formula evaluation, approver resolution, closure maintenance | Every pull request, under a minute |
| Integration | Migrations, recipes round-trip, multi-tenant isolation, background tasks, API contracts | Every pull request against a throwaway database |
| Scenario | End-to-end journeys on seeded tenants: hire, transfer, leave, roster, payroll run, exit | Nightly and before release |
| Performance | Descendant resolution, payroll run, attendance processing at the 5,000-employee seed | Nightly, with thresholds that fail the build |
| Security | Dependency and container scanning, permission matrix tests, visibility predicate tests | Every pull request |

Seed data is generated, reproducible from a fixed random seed, and includes the deliberate anomalies: skipped level, mid-month transfer, split allocation, matrix reporting, renamed and retired units, vacant head, future-dated records.

### CI/CD

One pipeline: restore, build with warnings as errors, unit and integration tests, package, publish artifacts, deploy to a shared development environment. Merge to main deploys to staging automatically. Production deployment is a manual approval on the same artifact that passed staging; no rebuild between environments. Recipes are validated by applying them to a fresh tenant in the pipeline.

### Environments

| Environment | Purpose | Data |
| --- | --- | --- |
| Development | Shared, reset nightly from seed | Synthetic |
| Staging | Release candidate, mirrors production topology | Synthetic plus anonymised copies where agreed |
| Production | Live tenants | Real, isolated |

Customer configuration tenants live in production, not staging: a customer's config tenant is a production tenant that does not yet hold live data.

### Configuration promotion

Configuration moves between a customer's config and prod tenants as a recipe export reviewed in a pull request against the customer's configuration repository, then imported. The import runs in dry-run first and reports differences. Direct production configuration by the restricted role is exported back within the same day so the repository stays the truth.

### Release cadence

Platform releases monthly, with release notes per tenant listing any behavioural change affecting that tenant's configuration. Hotfixes as needed through the same pipeline. A release that changes a migration, a recipe format or a formula engine behaviour carries a compatibility note and an upgrade test against every edition recipe.

### Definition of done

A change is done when: it has tests at the levels above appropriate to its scope; permissions and entitlement checks are in services; every string is localised; the module README is current; the audit trail covers the change if it is material; the migration is additive; a reviewer has approved; and, for migrations, recipes and permissions, a second reviewer has approved.

## Open technical decisions

To settle in the first sprint. Each is recorded as an architecture decision record in `/docs/adr` once taken.

| # | Decision | Default if not decided | Owner |
| --- | --- | --- | --- |
| 1 | Orchard Core version to pin | Latest stable at project start | Head of Technology |
| 2 | Database per tenant or shared database with table prefix | Database per tenant for Enterprise; prefix for shared cloud | Head of Technology |
| 3 | Formula engine: build an expression evaluator or adopt a sandboxed library | Adopt, with a hard whitelist of functions | Software Development Manager |
| 4 | Front-end for the employee theme: server-rendered Razor with progressive enhancement, or a SPA over the API | Server-rendered; the API exists for mobile regardless | Technology team |
| 5 | Mobile: native per platform or a cross-platform framework | Cross-platform, with native modules where push and biometrics require | Technology team |
| 6 | Distributed cache for multi-node cloud | Redis, enabled per deployment | Head of Technology |
| 7 | Search: Lucene or Elasticsearch | Lucene for shared cloud; Elasticsearch offered at Enterprise | Technology team |
| 8 | Attendance device adapters to build first | Named once the first customer's devices are known | Delivery |
| 9 | ERP journal export formats to build first | Generic CSV plus one named ERP adapter | Delivery |
| 10 | Whether the dimension engine ships as a separately versioned package | Yes, from the start | Head of Technology |

### How to raise a new one

Any engineer who finds that a mechanism in this document does not work as described in the pinned Orchard Core version, or that a rule cannot be met, opens an ADR proposing the change. The specification is updated when the ADR is accepted. Departing from the document without an ADR is the one thing this process forbids.
