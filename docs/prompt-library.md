# WorkMate 2.0 — Claude Code Prompt Library

Sep 29, 2026 · @Umair Tariq

Scoped, sequenced prompts for building WorkMate 2.0 on Orchard Core with Claude Code in Visual Studio. Each prompt turns one section of the technical specification into a reviewable piece of work.

## How to use this library

Each prompt is a scoped brief for one slice of the technical specification, written to be pasted into Claude Code as the opening message of a session. They are sequenced: each assumes the previous ones have landed and been reviewed.

### Setup

Run Claude Code from the solution root, in the terminal integrated with Visual Studio or alongside it. Prompt 0 creates the `CLAUDE.md` that every later prompt relies on; do not skip it. Keep the technical specification and the dimension engine architecture in the repository under `/docs` so Claude Code can read them rather than being told about them.

### The working pattern

Every prompt follows the same structure, and the pattern matters more than any single prompt:

1. **Context** — what exists, what this slice adds, where it sits
2. **Before writing code** — read, verify against the pinned Orchard Core version, propose, stop
3. **What to build** — the concrete deliverables from the specification
4. **Constraints** — the rules that apply, restated so they are in the session
5. **How to work** — small steps, summarise, stop for review

The stop after step 2 is deliberate. Claude Code explores the codebase quickly and proposes confidently; the proposal is where a reviewer catches a wrong assumption cheaply. Approve the proposal, then let it build.

### Review gates

Claude Code output is reviewed exactly as human output: a pull request, a reviewer, the definition of done from the specification. Migrations, recipe steps and permissions need a second reviewer. Nothing merges on the strength of Claude Code's own summary.

### Rules that apply to every prompt

These are restated inside each prompt, but they are also the standing instructions in `CLAUDE.md`, so a session that drifts can be reminded with one line: *follow CLAUDE.md*.

- Read the relevant section of `/docs/technical-specification.md` before proposing anything
- Verify Orchard Core APIs against the pinned version in the solution, not from memory
- Match existing conventions in the repository; do not introduce a new style
- Business logic in services; permissions and entitlement checked in services
- Every dated write takes an explicit effective date
- Every user-visible string localised
- Tests at the levels the specification requires, in the same pull request
- Ask when a requirement is ambiguous; do not guess and do not silently adapt the specification

### Prompts that are elsewhere

The seed-data generator prompt was written earlier and is referenced from prompt 2. It runs after the dimension engine model exists.

## Prompt 0 — Repository bootstrap and CLAUDE.md

Run once, in an empty repository. Everything later depends on the `CLAUDE.md` this creates.

```
# Task: Bootstrap the WorkMate 2.0 repository and write CLAUDE.md

## Context
WorkMate 2.0 is a multi-tenant SaaS HCM platform (first domain of a wider
business platform) built on Orchard Core. The technical specification is at
/docs/technical-specification.md and the dimension engine architecture at
/docs/dimension-engine-architecture.md. Read both before doing anything.

## What to do
1. Create the repository layout exactly as section 2 of the specification
   describes: /src, /tests, /recipes, /tools, /docs. Empty projects are fine;
   this prompt is about structure and standing instructions, not features.
2. Create a .NET solution with an Orchard Core host project (WorkMate.Web) and
   a shared abstractions project (WorkMate.Core). Pin the Orchard Core version
   to the latest stable release and record it in /docs/technical-specification.md
   under "Pinned version" and in a Directory.Packages.props.
3. Enable analyzers with warnings as errors, nullable reference types, and
   file-scoped namespaces across the solution via Directory.Build.props.
4. Write CLAUDE.md at the root. It is the standing instruction set for every
   later session, so it must contain, concisely:
   - The project's purpose in two sentences
   - The repository layout and the naming conventions from spec section 2
   - The ten rules from "Rules that apply everywhere" in spec section 2
   - The instruction to read the relevant spec section before proposing
   - The instruction to verify every Orchard Core API against the pinned
     version in the solution, never from memory
   - The working pattern: read, verify, propose, stop for approval, then
     build in small reviewable steps
   - How to run the build and tests
   - The definition of done from spec section 10
5. Add an ADR template under /docs/adr and a first ADR recording the pinned
   Orchard Core version and the reason for choosing it.
6. Add a README at the root pointing to the specification, the architecture
   document, CLAUDE.md and the ADRs.

## Constraints
- Do not add any feature code. If you find yourself creating a content type,
  stop.
- Do not invent conventions the specification does not state. Where the
  specification is silent, note it in a section of CLAUDE.md called
  "Conventions still to decide" rather than choosing.

## How to work
Show me the proposed layout and the draft CLAUDE.md before creating anything.
After I approve, create the files, confirm the solution builds, and stop.
```

### After it runs

Read `CLAUDE.md` yourself, line by line. It will be read at the start of every later session, so a wrong statement in it becomes a wrong assumption everywhere. Commit it with a second reviewer.

## Prompt 1 — Solution skeleton and platform foundation

Builds `WorkMate.Platform`, the base recipe, roles and the shared UI shell. Spec section 3.

```
# Task: Platform foundation — WorkMate.Platform, base recipe, roles, shell

## Context
The repository is bootstrapped and CLAUDE.md exists; follow it. This slice
builds the platform layer every other module depends on. Read section 3 of
/docs/technical-specification.md and the cross-cutting section 9 before
proposing.

## Before writing code
1. Confirm the pinned Orchard Core version and verify against it: how a module
   declares features and dependencies, how site settings parts are registered,
   how roles and permissions are declared, how recipes and recipe steps are
   defined, how content localisation and cultures are configured.
2. Tell me what you found and where the version differs from what the
   specification assumes.
3. Propose the module layout for WorkMate.Platform and the recipe structure,
   then stop for approval.

## What to build
- WorkMate.Platform as an Orchard module with Manifest, Startup, Migrations
  and the standard folders from spec section 2.
- WorkMateSettings site settings part: default locale and calendar, RTL
  preference, fiscal year start, currency, working week, customer code. With
  its driver, view and permission.
- The platform roles from spec section 3 with their base permissions:
  Platform Administrator, Tenant Administrator, HR Administrator, Manager,
  Employee, Auditor.
- Cultures en and ar configured; a bilingual text field type in WorkMate.Core
  with edit and display drivers, used by every later module.
- The RTL-aware admin layout extension and the shared component set: bilingual
  field, date-range control, placeholders for the dimension picker and
  employee picker (implemented later).
- recipes/base.recipe.json performing the six steps in spec section 3, with the
  module-specific steps stubbed where the module does not exist yet, and a
  clear comment marking each stub.
- A localisation resource pattern and a failing test that catches any literal
  user-visible string without a resource entry.
- Structured logging with tenant, user and correlation id on every log line.

## Constraints
- No business logic. This module knows about tenants, settings, roles, cultures
  and layout; it knows nothing about employees.
- Tenant creation is not built here; it belongs to the control plane. Do not
  add a tenant-creation UI.
- Every string localised. Every setting has a sensible default.

## How to work
Build the module and settings first, show me it running in a fresh tenant,
then the roles and recipe, then the shell. Stop after each for review.
```

## Prompt 2 — The dimension engine

The foundation everything else sits on. Spec section 4 and the whole architecture document. Split into two sessions if it runs long: model and indexes first, services and validation second.

```
# Task: Dimension engine — WorkMate.Dimensions: model, indexes, services

## Context
WorkMate.Platform exists. This slice builds the dimension engine described in
/docs/dimension-engine-architecture.md and implemented per section 4 of
/docs/technical-specification.md. Read both in full before proposing. Follow
CLAUDE.md.

## Before writing code
1. Verify against the pinned Orchard Core version: YesSql index classes and
   providers, how a content part is defined and attached, how the content
   definition manager creates and alters content types at runtime, how a
   content handler participates in the ambient session, how IMemoryCache is
   scoped per tenant.
2. Report what you found and any divergence from the specification.
3. Propose the class list — documents, indexes, providers, part, handler,
   services, validator — with one line each, and stop for approval.

## What to build

### Configuration layer
- DimensionTypeDocument and StructureDocument with their indexes and providers.
  A Structure holds ordered levels, allowSkipLevel, strict flag, isPrimaryOrg.
- IDimensionTypeService and IStructureService.
- IDimensionTypeService.CreateAsync creates the backing content type through
  the content definition manager exactly as spec section 4 describes: named
  for the code, DimensionRecordPart attached, TitlePart bound to NameEn,
  attribute-schema fields added, creatable/listable/securable/not draftable.
  Every such change written to the audit trail with a diff.

### Record layer
- DimensionRecordPart with the standard fields, its driver, its handler, and
  DimensionRecordPartIndex.
- The handler delegates all index work to IDimensionGraphService inside the
  ambient session so record and index commit together.

### Graph layer
- DimensionLinkIndex, DimensionClosureIndex, EmployeeAssignmentIndex as custom
  tables with providers and migrations.
- IDimensionGraphService: ancestors, descendants, children, isUnder, depth;
  link and closure maintenance on create, move, delete, merge; rebuild;
  verify with a divergence report.
- IDimensionService: create, update, retire, move, merge; resolve by id and
  code; every operation dated.
- IEmployeeAssignmentService: place, end, reallocate; effective assignment on
  a date; employees under a node as at a date, paged.

### Validation
- IDimensionValidator as the single validation service with every rule in
  spec section 4 and architecture section 6. Called by every write path.
  Typed errors naming the rule and the offending node.

### Permissions and caching
- The permissions in spec section 4.
- Per-tenant cache keyed on tenant name plus structure id, invalidated by the
  graph service. A test asserts the key contains the tenant.

### Tests
- Unit: every validator rule; closure maintenance for create, move, delete,
  merge; effective-dated resolution at the day before and after each change.
- Integration: closure verified against links after every mutation in a
  scripted scenario; tenant isolation of queries and cache.
- Performance: descendant resolution under 200 ms at 5,000 records and 5,000
  employees using the seed generator (run the seed-data prompt after the model
  exists).

## Constraints
- No taxonomies anywhere in this module.
- No module may read link, closure or assignment tables directly; the service
  is the only entry. Put the tables in an internal namespace.
- Every read takes an effective date defaulting to today; every write takes
  one explicitly with no default.
- No admin UI in this prompt. That is prompt 3.

## How to work
Configuration layer first, then records, then graph, then validation and
caching, then tests. Stop after each layer with a summary. If the pinned
Orchard version makes any mechanism impossible as specified, stop and tell
me; that is an ADR, not a workaround.
```

## Prompt 3 — Organisation designer and recipe steps

The admin surfaces over the engine, plus the recipe steps that make a configured structure portable. Spec section 4, admin UI and recipe steps.

```
# Task: Organisation designer admin UI and dimension recipe steps

## Context
WorkMate.Dimensions has its model, services and validation. This slice adds
the three admin screens and the recipe steps from spec section 4. Follow
CLAUDE.md; read section 4 and the architecture document's sections 6 and 7.

## Before writing code
1. Verify against the pinned Orchard Core version: admin controllers and
   routing, admin menu navigation providers, how the admin theme's shapes and
   templates are extended, how a recipe step is implemented and how export
   produces steps, and what client-side tooling the admin theme ships.
2. Read the existing shared component set in WorkMate.Platform and reuse it.
3. Propose the screen structure and the client-side approach for the tree
   (server-rendered with progressive enhancement unless you can show why not),
   then stop for approval.

## What to build

### Screens
- Dimension types: list; editor with the attribute schema builder (field name,
  type, required, unique, bilingual label). Creating a type calls
  IDimensionTypeService and shows the generated content type's name.
- Structures: editor for ordered levels drawn from dimension types, with
  allowSkipLevel, strict and isPrimaryOrg.
- Organisation designer: one tree per structure. Expand and collapse, search,
  inline rename (asking corrective or substantive per architecture section 5),
  drag to move, retire, merge with dry-run preview, and an effective-date
  control on every mutation. Violations from IDimensionValidator shown
  inline before commit; the service re-checks on write.

### Behaviour
- Every screen calls the same services the API uses. No controller contains
  business logic; no screen has a privileged path.
- The designer renders RTL under the Arabic culture and shows bilingual names.
- Move and merge show what will change before it happens, including the count
  of employees affected, and require confirmation.

### Recipe steps
- dimension-types, structures, dimension-records (parents referenced by code,
  resolved at import), employee-assignments.
- Export in dependency order. Import validates the entire set before writing
  anything; a failure writes nothing and reports every problem.
- A round-trip integration test: export a seeded tenant, import into a fresh
  one, verify closure and assert equality.

### Permissions
- Each screen and each action guarded by the permissions from spec section 4.
  Tests that a user without MoveDimensionRecords cannot move, in UI and API.

## Constraints
- Use the existing bilingual field and date-range components; do not build
  new ones.
- No JavaScript framework beyond what the admin theme already ships, unless
  you propose it in step 3 with a reason and I approve.
- Every string localised. Every action audited.

## How to work
Dimension types screen first, then structures, then the designer, then recipe
steps. Show me each running before moving on.
```

## Prompt 4 — Employee record and the form designer

`WorkMate.Records`. Spec section 5. Two sessions: the employee record first, the form designer second.

```
# Task: WorkMate.Records — the employee record and the form designer

## Context
Dimensions is complete with its admin UI. This slice builds the employee
record and the mechanism administrators use to define forms, both on runtime
content-type definition. Read spec section 5 in full. Follow CLAUDE.md.

## Before writing code
1. Verify against the pinned Orchard Core version: BagPart and how typed
   items are declared, content picker fields and how their candidate set is
   filtered, media fields, taxonomy fields, custom content field
   implementation with drivers, content item versioning, and how a content
   handler raises and subscribes to events across modules.
2. Propose the EmployeePart field list, the standard sections, the
   FormDefinition model and the compile step, then stop for approval.

## What to build

### Session A — the employee record
- Employee content type with EmployeePart holding the fixed core from spec
  section 5. The core is not editable through the form designer; make that
  impossible, not merely discouraged.
- Standard sections as BagPart-based or field-based sections: job details,
  documents, bank details, qualifications, dependants, contacts.
- A Placement section that reads and writes through
  IEmployeeAssignmentService. The employee holds no department or cost
  centre field; a test asserts none exists.
- IEmployeeService with the lifecycle transitions from spec section 5, each
  dated, each raising a domain event. Exit closes open assignments on the
  exit date.
- Employee picker field type: content picker restricted to Employee with
  visibility filtering (stub the visibility predicate against
  IVisibilityService; the service is completed later).

### Session B — the form designer
- FormDefinition document: code, bilingual name, sections, fields with type
  and validation, conditional visibility rules, calculated fields, and the
  approval definition reference for request forms. Versioned.
- IFormDefinitionService.PublishAsync compiling a definition into a content
  type: one type per form, FormSubmissionPart for request forms, fields
  mapped to Orchard fields and the WorkMate field types in spec section 5.
  Publishing a change creates a new version; submissions keep theirs.
- The WorkMate field types not yet built: dimension picker (structure-scoped,
  visibility-aware), money, dual-calendar date, attachment set with document
  type and expiry. The bilingual text field already exists in WorkMate.Core.
- The declarative rule language for cross-field validation and conditional
  visibility: a small grammar, a server-side evaluator, and a client-side
  evaluator that the server always overrides. No scripting exposed to
  administrators.
- The form designer admin screen, reusing the shared component set.
- Recipe step form-definitions exporting definitions, not generated types;
  import republishes.

### Tests
- Publish and republish round-trip; version pinning of submissions; each
  field type's drivers; rule evaluation with a table of cases; bilingual
  rendering in both cultures; lifecycle events reaching a test subscriber.

## Constraints
- Taxonomies only for flat lookup lists, nowhere else.
- The rule language is closed. If a rule cannot be expressed, that is a
  finding for me, not a reason to expose scripting.
- Every dated write explicit. Every string localised.

## How to work
Session A to completion and review before session B begins. Within each,
model first, then services, then UI, then tests, stopping after each.
```

## Prompt 5 — The approval engine

`WorkMate.Approvals`. Spec section 6. The slice most likely to hit version-specific workflow API differences, so the verification step matters more than usual.

```
# Task: WorkMate.Approvals — definitions, compiler, activities, inbox

## Context
Records is complete: request forms exist as content types with
FormSubmissionPart. This slice builds the approval engine that every request
form routes through, on top of Orchard's workflow module, per spec section 6.
Follow CLAUDE.md.

## Before writing code
1. Verify against the pinned Orchard Core version, carefully: how a workflow
   type is defined programmatically rather than in the designer; how custom
   activities and blocking activities are implemented and registered; how an
   instance is correlated to a content item and resumed; how workflow type
   versions behave for in-flight instances; how background tasks are
   scheduled. Report exactly what the version supports.
2. If the version cannot pin in-flight instances to an old definition
   version natively, propose how we achieve it (for example, one generated
   workflow type per definition version, never mutated) and stop.
3. Propose the ApprovalDefinition model, the activity list and the compiler
   design, then stop for approval.

## What to build

### Definitions
- ApprovalDefinition document: form type, organisation scope (node, with or
  without descendants), ordered steps, version. Each step: approver rule from
  spec section 6, optional condition in the rule language from Records,
  optional deadline, escalation target.
- Approver resolution service implementing every rule, including vacant-head
  walk-up and named-user fallback.

### Compiler
- IApprovalCompiler producing a system-managed, hidden workflow type per
  definition version: content-created trigger filtered by form type and
  scope, one activity per step, branching for conditions and outcomes.
  Recompile on change produces a new type; in-flight instances continue on
  the old one.

### Activities
- ResolveApprovers, AwaitApproval (blocking, correlated to the submission),
  RecordDecision, Escalate, Notify, SetSubmissionStatus, DelegateCheck. Each
  small, each unit-tested. No script activities in generated workflows.

### Runtime
- ApprovalTask index rows per pending approver per step, indexed by assignee,
  submission, due date, status. ApprovalHistory as an immutable append log.
- IApprovalService: decide, delegate, escalate, cancel and resubmit under a
  new version, override with mandatory reason.
- Delegation with date range and optional form-type filter; checked at
  assignment and again at decision; recorded with both users; one hop only.
- Escalation background task that adds an assignee and never removes one.

### Inbox
- The admin inbox: my pending, delegated to me, by deadline, history; bulk
  approve for one form type. A front-end rendering comes with Self-service.

### Permissions and tests
- Permissions from spec section 6.
- Tests: every approver rule; condition skipping; the compiler producing a
  runnable type; in-flight version pinning; delegation at both points;
  escalation adding not replacing; inbox visibility.

## Constraints
- Administrators never see the workflow canvas. Generated types are hidden
  from the workflow admin list.
- No calculation of any kind in an activity. Workflows route and record.
- Nothing reads workflow instances directly; the inbox reads ApprovalTask.

## How to work
Definitions and resolver first, then the compiler with one trivial
definition end to end, then activities, then runtime and inbox. Stop after
each. The version pinning question in step 2 is the risk; resolve it before
anything else is built.
```

## Prompts 6 to 8 — The HCM modules

Three prompts sharing one preamble. Spec section 7. Run in this order: leave, attendance, payroll — payroll pulls from the other two.

### Shared preamble

Every HCM module prompt opens with this block, then the module-specific section below it.

```
# Task: WorkMate.Hcm.<Module>

## Context
Dimensions, Records and Approvals are complete. This slice builds one HCM
module per spec section 7. Follow CLAUDE.md. Read section 7 for this module
and the cross-module rules at its end.

## Before writing code
1. Read the three foundation modules' service interfaces. This module uses
   them; it never touches their tables.
2. Verify against the pinned Orchard Core version anything new this module
   needs, and report.
3. Propose the content types, indexes, services, events and recipe steps,
   then stop for approval.

## Constraints that apply to every HCM module
- Business logic in services. Permissions and entitlement checked in services.
- Every request form routes through Approvals. No approval logic here.
- Every dated write explicit. Every string localised. Every material change
  audited.
- Cross-module effects through domain events or the other module's service,
  never its tables.
- Recipe steps for this module's configuration, exportable and importable.
- Tests at unit and integration level in the same pull request, using the
  seed generator's tenants.

## How to work
Model first, then services, then request forms and their approval wiring,
then background tasks, then admin UI, then tests. Stop after each.
```

### Prompt 6 — Leave

```
## What to build — WorkMate.Hcm.Leave
- LeaveType and LeavePolicy documents; a policy binds entitlement, accrual
  rule, carry-forward, encashment rule and eligibility to a dimension scope
  and employee attributes, effective-dated.
- LeaveBalanceIndex maintained by ILeaveBalanceService as dated movements.
  Never computed by summing requests at read time. A rebuild command and a
  verification that compares the two.
- Opening balance import: dry run, reconciliation report, written as dated
  adjustments with reason.
- LeaveRequest as a request form: balance checked at submission and at final
  approval; working-week and holiday calendar aware; partial days per policy.
- Period accrual background task producing movements and a per-employee
  liability figure exposed through ILeaveLiabilityService for payroll.
- TOIL as its own type, earned from an approved-overtime domain event, with
  expiry.
- Encashment request: the amount is requested from payroll's formula engine
  through its service; leave does not compute money.
- Reports: balance, liability, utilisation, forecast, honouring visibility.
```

### Prompt 7 — Time and attendance

```
## What to build — WorkMate.Hcm.Attendance
- Shift and ShiftPattern documents with breaks, tolerances, overtime rules.
- RosterAssignment rows per employee per day; publish as an explicit
  operation raising a notification; swap and cover as request forms.
- IAttendanceDeviceAdapter contract with one reference adapter that reads a
  CSV drop, so the pipeline can be tested before any vendor device exists.
  Punches land in a raw Punch table matched to employees by device-id mapping;
  unmatched punches are queued for resolution.
- Daily processing background task pairing punches against the roster into
  AttendanceDay rows: late, early, absent, overtime, missing punch.
  Idempotent per employee per day.
- Correction request form with reason code; original values retained.
- IAttendanceSummaryService returning per-employee period summaries for
  payroll. Attendance never pushes into payroll.
- Approved overtime raises the domain event that Leave consumes for TOIL.
- Reports: raw logs, exceptions, late and absence analysis, timesheet export.
```

### Prompt 8 — Payroll and the formula engine

```
## What to build — WorkMate.Hcm.Payroll
The strictest module: every figure reproducible and explainable. Read the
Payroll table in spec section 7 line by line before proposing.

### Setups
- Company and branch cut-offs, pay groups, elements (earnings, deductions,
  employer contributions), eligibility by scope, all effective-dated, all
  exportable as recipe steps.

### Formula engine
- Decision 3 in spec section 11 governs: adopt a sandboxed expression
  evaluator with a hard whitelist, or build one. Propose with reasons and
  stop before implementing.
- A typed evaluation context: employee attributes, element values, attendance
  summary, leave liability, dimension attributes. No general scripting, no
  reflection, no I/O.
- Formulas versioned. Every run records which formula version produced each
  figure.

### Run lifecycle
- PayrollRun document with the transitions in spec section 7, each a service
  call with permission and audit.
- Readiness checklist with blocking and warning conditions.
- Calculation per employee per element in dependency order, writing
  PayrollLine rows with their inputs. Idempotent: recalculation replaces.
- Cost allocation splitting each line by effective cost assignments and
  allocation percentages; a mid-month transfer yields two allocation rows.
- GL journal from allocated lines through COA mapping on cost records; a
  generic CSV export and one adapter interface for named ERPs.
- Statutory and bank output behind a country-adapter interface; implement
  Bahrain first.
- Final settlement composed from leave liability, indemnity rule, ticket
  accrual and unpaid elements, triggered by the exit event.
- Traceability view: for one employee in one run, every element, formula
  version, input and allocation, readable by a payroll officer.

### Tests
- A golden-file test suite: seeded employees with known inputs and expected
  net pay to the cent, run on every pull request. Any change to a formula or
  the engine that alters a golden figure fails the build until the golden
  file is deliberately updated with a second reviewer.
```

## Prompt 9 — Entitlement and the control plane

`WorkMate.Entitlement` and the super control panel. Spec section 8. Also completes `IVisibilityService` from section 9, which every earlier module stubbed.

```
# Task: WorkMate.Entitlement, the control plane, and visibility by node

## Context
All HCM modules exist and each declares its capabilities in Capabilities.cs
and calls IEntitlementService.IsAllowedAsync at points of use (a stub so
far). This slice makes entitlement real, builds the control plane in the
default tenant, and completes IVisibilityService. Read spec sections 8 and 9.
Follow CLAUDE.md.

## Before writing code
1. Verify against the pinned Orchard Core version: tenant creation and shell
   settings programmatically, enabling and disabling features per tenant at
   runtime, running a recipe against another tenant, site settings storage,
   and how a module can be restricted to the default tenant.
2. Propose the entitlement record schema, the signing approach, the control
   panel's screens and the visibility model, then stop for approval.

## What to build

### Entitlement
- Entitlement record: edition, add-ons, packs, licensed headcount, deployment
  type, valid from and to, grace days, signature. Stored in tenant settings,
  read-only in the tenant, signature verified on load and on a schedule.
  Failed verification moves the tenant to read-only with an admin alert.
- IEntitlementService with IsAllowedAsync(capability) backed by the record,
  cached per tenant, invalidated on change.
- Feature activation from the record: entitled modules enabled, others
  disabled, applied at provisioning and on every change.
- Nightly headcount task with the warning and block thresholds from spec
  section 8; both visible to the tenant administrator.
- Downgrade, expiry and renewal behaviour exactly as spec section 8's table:
  data never deleted, configuration retained read-only.
- Offline licence file for on-premises with the same schema, validated on
  start and daily.

### Control plane
- A module that refuses to run outside the default tenant, with a test.
- Screens: create and retire tenants; apply base and edition recipes; issue
  and amend entitlement records; estate view of usage against licence;
  platform version per tenant. Every action audited with the operator.
- Platform operator role with MFA required.

### Visibility by node
- IVisibilityService: a user's scope from role assignments to nodes, with or
  without descendants; managers get their own subtree by default; HR
  administrators get explicit scopes.
- A query predicate every employee-bearing query applies, in services.
- Replace every visibility stub left by earlier prompts; grep for them and
  list what you replaced.
- A permission matrix test: for each role and each module, what is visible
  and what is not, on a seeded tenant.

### Tests
- Signature tamper detection; every capability check in every module run
  with and without entitlement; headcount thresholds; downgrade preserving
  data; control plane refusing non-default tenants; the visibility matrix.

## Constraints
- The tenant never edits its own entitlement. Make that impossible.
- No customer data in the default tenant, ever.
- Signing keys in the host's secret store, never in configuration or recipes.

## How to work
Entitlement record and service first, then feature activation, then the
control plane, then visibility. Stop after each. The visibility stub
replacement is a sweep across every module; do it last and list every file
touched.
```

## Prompt 10 — Recipes, CI/CD and release; and follow-up patterns

The last build prompt, then the short prompts used every day once the code exists.

### Prompt 10

```
# Task: Edition recipes, sector packs, CI/CD and the release process

## Context
Every module exists with its recipe steps. This slice assembles them into
edition recipes and sector starting points, and builds the pipeline that
spec section 10 describes. Follow CLAUDE.md; read section 10.

## Before writing code
1. List every recipe step every module exposes, with its dependencies.
2. Propose the recipe composition (base, three editions, one generic starter,
   one sector pack skeleton) and the pipeline stages, then stop.

## What to build
- recipes/editions/essential, professional, enterprise: each layers on base,
  enables the entitled features, and seeds edition-appropriate defaults.
- recipes/starters/generic: a working default organisation with sample
  structures, forms, policies and approval routes, so a new tenant never
  starts empty.
- recipes/packs/<sector>: a skeleton showing how a sector pack is composed,
  with a README describing what a pack may and may not contain.
- Pipeline: restore, build with warnings as errors, unit and integration
  tests, recipe validation by applying each recipe to a fresh tenant,
  package, publish artifacts, deploy to development. Merge to main deploys
  to staging. Production is a manual approval on the same artifact.
- Nightly: scenario tests on seeded tenants and the performance suite with
  thresholds that fail the run.
- Release notes generation per tenant from a conventional commit log, listing
  behavioural changes that affect that tenant's enabled features.
- Configuration promotion tooling: export a tenant's configuration to a
  repository, diff, dry-run import, import.

## Constraints
- No secrets in recipes or pipeline files.
- A recipe that fails to apply to a fresh tenant fails the build.
- Do not build deployment infrastructure beyond what the pipeline needs;
  infrastructure is a separate concern.
```

### Follow-up patterns

Once the code exists, most sessions are small. These openers keep them disciplined.

**Review a pull request**

```
Review the changes on this branch against CLAUDE.md and the definition of
done in /docs/technical-specification.md section 10. For each finding give
the file, the rule broken, and a concrete fix. Do not change any code.
```

**Debug a failure**

```
The test <name> fails with <message>. Reproduce it, explain the cause in
plain terms, propose the smallest fix that respects CLAUDE.md, and stop
before changing anything. If the cause is a divergence between the
specification and the pinned Orchard Core version, say so; that is an ADR.
```

**Add a capability to an existing module**

```
Add <capability> to WorkMate.Hcm.<Module> per spec section 7. Read the
module's README and existing services first, match their conventions, route
any new request form through Approvals, declare a capability constant and
check it in the service, add tests, update the README. Propose before
building.
```

**Verify an Orchard Core assumption**

```
The specification assumes <mechanism>. Check whether the pinned Orchard Core
version supports it as described. Show me the relevant types and any
difference. Do not change code; this is a finding.
```

**Refactor safely**

```
Refactor <target> for <reason>. Keep every public interface and test
unchanged. Show me the plan and the list of files first. After the change,
run the full test suite and show the result.
```

### One habit

End every session by asking Claude Code to summarise what changed, what it assumed, and what it left for a human. Paste that summary into the pull request description. It is the fastest way a reviewer finds the assumption that should not have been made.

## Prompt 11 — Two reference tenants

Two fictional customers with deliberately different shapes, built as recipes so they can be applied to a fresh tenant in one step. They are the standing demonstration that the platform is configured rather than coded: the same product, two organisations that share almost nothing structurally. They also become the scenario-test tenants in the pipeline.

### Tenant A — Khaleej University

A public university. Deep, mixed academic and administrative, several axes, and a custom dimension. About 1,200 employees.

**Dimension types:** Business Unit, Campus, College, Administrative Directorate, Department, Section, Academic Programme (custom), Cost Centre, Grade, Position.

| Structure | Levels | Rules |
| --- | --- | --- |
| Organisation (primary) | Business Unit → College or Administrative Directorate → Department → Section | Two dimension types permitted at level 2; Section optional |
| Location | Country → Campus → Building | Strict |
| Cost | Cost Centre, flat | Cost centres do not follow Organisation |
| Academic | College → Academic Programme | Academic units only |

**Organisation sample:**

| Level 2 | Departments | Sections |
| --- | --- | --- |
| College of Engineering | Civil, Electrical, Computer | Labs and Research Group per department |
| College of Business | Accounting, Management | None; staff attach at department |
| College of Law | Commercial Law, Public Law | None |
| Directorate — Registrar | Admissions, Records, Examinations | Undergraduate, Postgraduate under Examinations |
| Directorate — Corporate Services | Finance, HR, IT, Facilities | Networks, Development, Support under IT |

**Deliberately awkward:** faculty and administrative staff have different field sets and leave policies; a professor holds a joint appointment across two departments at 60/40; Finance's cost is recharged to colleges by headcount; IT's cost centre differs from its parent's; Examinations was renamed mid-year; one department has a vacant head; a visiting lecturer is fixed-term with no leave accrual.

**Employee types:** Faculty, Teaching Assistant, Administrative, Technician, Visiting. Faculty carry academic rank, tenure status, programme assignments and a research allowance; Administrative carry grade and overtime eligibility.

**Leave:** annual by employee type with faculty tied to the academic calendar, sick, Hajj, maternity, study leave for faculty only, compensatory for technicians.

**Approvals:** leave — line manager then unit head; faculty study leave — head, dean, then a Vice-President role; recruitment — head, HR, Finance, then Vice-President; overtime above a threshold — head then Finance.

**Visibility:** deans see their college subtree; directorate heads their directorate; HR sees all; Finance sees cost data across all units but not personal records.

### Tenant B — Gulf Trading Co

A private distributor. Flat, commercial, cross-border, small. About 140 employees.

**Dimension types:** Company, Department, Team, Branch, Cost Centre, Grade.

| Structure | Levels | Rules |
| --- | --- | --- |
| Organisation (primary) | Company → Department → Team | Team optional; no divisions exist |
| Location | Country → Branch | Strict; three countries |
| Cost | Cost Centre, one per Branch | Cost follows Location, not Organisation |

**Organisation sample:** Sales with Retail and Wholesale teams; Operations with Warehouse and Logistics teams; Finance, HR and IT with no teams, staff attached at department.

**Locations:** Bahrain — Manama head office and Sitra warehouse; Saudi Arabia — Riyadh; UAE — Dubai.

**Deliberately awkward:** cost is by branch while approvals are by department, so the two axes genuinely diverge; a Riyadh salesperson reports to the Sales manager in Manama; warehouse staff are on shifts with biometric attendance while office staff are not; one employee transferred from Dubai to Manama on the sixteenth of a month; three countries mean three statutory payroll adapters and three working weeks.

**Employee types:** Office, Warehouse, Sales (commission-eligible), Driver.

**Leave:** annual by country, sick, public holidays per country, no study leave.

**Approvals:** leave — line manager only; purchase-related forms — manager then Finance; overtime — warehouse supervisor then Operations manager; anything above a value threshold — Managing Director role.

**Visibility:** managers see their department across all branches; branch supervisors see their branch across all departments; HR and Finance see all.

### Why these two

Between them they exercise every case the dimension engine architecture lists: level skipping, two types at one level, a custom dimension, cost diverging from organisation, multi-country location, split allocation, mid-month transfer, rename with history, vacant head, matrix reporting, and employee-type-specific policies. A platform change that breaks either tenant's scenario tests has broken something real.

### The prompt

```
# Task: Build the two reference tenants as recipes and scenario fixtures

## Context
Every module exists with its recipe steps, and edition recipes are in place
(prompt 10). This slice builds two complete fictional customers as recipes
under /recipes/reference/, applied on top of the Professional edition, and
wires them into the nightly scenario tests. Read the "Two reference tenants"
section of /docs/prompt-library.md and the seed-data README. Follow CLAUDE.md.

## Before writing code
1. List every recipe step available across modules and confirm each
   configuration item below maps to one. Anything that does not map is a gap
   in a module's recipe coverage: report it, do not work around it.
2. Propose the recipe file structure per tenant and the order of steps, then
   stop for approval.

## What to build

### For each tenant, in dependency order
- Dimension types, including Khaleej's custom Academic Programme type with
  its attribute schema
- Structures with levels and rules exactly as specified, including two
  permitted types at level 2 for Khaleej and the optional Team for Gulf
- Dimension records for every structure, parents by code, with the
  deliberate anomalies: renamed unit with history, vacant head, cost centre
  differing from parent
- Employee types and their field-set configurations through the form
  designer
- Leave types and policies scoped to employee type and, for Gulf, country
- Approval definitions per the routes specified, including role-based and
  threshold-conditioned steps
- Roles and visibility scopes per the specification
- Shift patterns and a roster fortnight for Gulf's warehouse; none for office
- Payroll setups: pay groups, elements and eligibility; three country
  adapters for Gulf, one for Khaleej
- Employees generated by the seed tool at the stated headcounts, bilingual
  names, with the specified anomalies: the 60/40 joint appointment, the
  cross-border reporting line, the mid-month transfer, the visiting
  lecturer with no accrual

### Scenario tests, one file per tenant
- Hire, place, transfer mid-month, run payroll, verify the cost split
- Submit each request form type and walk its approval route to completion,
  asserting the resolved approvers at every step
- Resolve the organisation as at a date before and after the rename
- Assert every visibility rule with one positive and one negative case
- Gulf only: process a fortnight of punches, verify overtime, verify TOIL
- Khaleej only: study leave route reaching the Vice-President role;
  Finance recharge appearing in cost allocation

### Verification
- Apply each recipe to a fresh Professional tenant in the pipeline; failure
  fails the build
- Export the applied tenant and diff against the recipe; the diff must be
  empty
- Run the closure and leave-balance verification commands; both clean

## Constraints
- All data synthetic and clearly so; no real people, ids or bank details
- Names in real Arabic script, correctly encoded
- Recipes contain no secrets and no environment-specific values
- Do not change any module to make a recipe easier. A gap is a finding.

## How to work
Gulf Trading first, the smaller and shallower, end to end including its
scenario tests, reviewed; then Khaleej University. Stop after each tenant's
recipes apply cleanly, before writing its scenario tests.
```

### Keeping them current

When a module gains a capability, one or both reference tenants gain configuration for it in the same pull request. A capability neither tenant exercises has no scenario test, and a capability with no scenario test is one the pipeline cannot protect.
