# CLAUDE.md — standing instructions for every Claude Code session

## What this is
WorkMate 2.0 is a multi-tenant SaaS platform built on Orchard Core. Human capital
management is the first business domain; the foundation (dimension engine, form
designer, approval engine, entitlement) is domain-neutral by design so later
domains are additions, not new products.

Each customer is an Orchard Core tenant. Everything that differs between
customers — organisation structure, forms, approval routes, entitlements — is
configuration held in the tenant, never code.

## Read before proposing anything
- /docs/technical-specification.md      the engineering contract, section by section
- /docs/dimension-engine-architecture.md the foundation design (binding)
- /docs/product-specification.md         what the product is and how it is packaged
- /docs/prompt-library.md                the sequenced briefs; find the one for this slice
- /docs/adr/                              decisions already taken; do not reopen them silently

## The working pattern (mandatory)
1. Read the relevant spec section and the module README if it exists.
2. Verify every Orchard Core API you intend to use against the version pinned in
   Directory.Packages.props. Never rely on memory of the API surface; it changes
   between releases. Report any divergence from the specification.
3. Propose: the class list, the files you will touch, the tests you will write.
   Then STOP and wait for approval.
4. Build in small, reviewable steps. After each step, summarise what changed and
   what you need from the reviewer. Stop again.
5. End the session by summarising what changed, what you assumed, and what you
   left for a human. That summary goes into the pull request description.

If a requirement is ambiguous, ask. Do not guess, and do not silently adapt the
specification to make something easier. A mechanism that cannot work as
specified in the pinned version is an ADR, not a workaround.

## Repository layout
```
/src/WorkMate.Web              Orchard Core host; no business code
/src/WorkMate.Core             shared abstractions; no Orchard dependency
/src/WorkMate.Platform         tenancy, settings, roles, base recipe, shared UI
/src/WorkMate.Dimensions       the dimension engine
/src/WorkMate.Records          employee record and form designer
/src/WorkMate.Approvals        approval engine over Orchard workflows
/src/WorkMate.Hcm.*            Core, Leave, Attendance, Payroll, SelfService
/src/WorkMate.Entitlement      entitlement service and control panel
/src/WorkMate.Reporting
/tests/WorkMate.<Module>.Tests one test project per module
/tests/WorkMate.Integration.Tests
/recipes                       base, editions, starters, packs, reference tenants
/tools                         seed generator, migration tooling
/docs                          specifications, ADRs, module READMEs
```
Every module is an Orchard Core module project with Manifest.cs, Startup.cs,
Migrations.cs and the folders Orchard expects: Controllers, Drivers, Handlers,
Indexes, Models, Services, ViewModels, Views, Recipes.

## Naming
| Thing             | Convention                                  | Example                  |
|-------------------|---------------------------------------------|--------------------------|
| Module project    | WorkMate.<Area> or WorkMate.Hcm.<Module>    | WorkMate.Hcm.Leave       |
| Feature id        | matches the project name                    | WorkMate.Hcm.Leave       |
| Content part      | <Noun>Part                                  | DimensionRecordPart      |
| Content type      | PascalCase, singular, no prefix             | Employee, LeaveRequest   |
| Index             | <Noun>Index                                 | DimensionClosureIndex    |
| Service interface | I<Noun>Service, one per aggregate           | IDimensionService        |
| Permission        | <Verb><Noun> constants in Permissions.cs    | ManageDimensions         |
| Recipe step       | <module>-<purpose>                          | dimensions-seed          |
| Migration method  | CreateAsync, then UpdateFrom<N>Async        | UpdateFrom3Async         |

## Rules that apply everywhere
1. Business logic lives in services, never in controllers, drivers or handlers.
2. Every custom table has a YesSql index class and an index provider. No raw SQL
   for reads; raw SQL only inside a migration or a named maintenance command.
3. Every write takes an explicit effective date where the model is dated. No
   defaulting on writes. Reads default to today.
4. Every service method is tenant-scoped by construction. Nothing takes a tenant
   id as a parameter; the shell scope supplies it.
5. Permissions AND entitlement are declared in the module and checked in the
   service, not only in the controller.
6. Migrations are additive and append-only. A migration never destroys data,
   and the body of `CreateAsync` or any shipped `UpdateFromNAsync` is never
   edited once it has shipped — a tenant that already ran it is recorded at
   that version and will never run it again, so an edit to it is invisible to
   every tenant that upgraded before the edit landed. A schema change,
   including one that only adds a column or an index, always goes in a new
   `UpdateFromNAsync` step; `CreateAsync` is updated only to keep producing
   the current schema for a brand-new tenant, never to carry the migration
   for an existing one. Every schema change needs an upgrade test: create the
   tables as the earlier version left them, run the migration, and assert the
   result matches a fresh install. See
   `src/WorkMate.Dimensions/Migrations.cs`'s `UpdateFrom3Async` and
   `tests/WorkMate.Integration.Tests/DimensionsMigrationUpgradeTenantTests.cs`
   for the pattern and the defect it was written to stop recurring.
7. Every user-visible string goes through the localiser. No literal English in
   views or services. Every name field is bilingual (en/ar).
8. Async throughout; cancellation tokens on every public method.
9. One aggregate, one service. A service that needs another aggregate injects
   that aggregate's service, never its tables.
10. A module's README states what it owns, what it depends on, and its recipe
    steps. Update it in the same pull request as any change to those.

## Hard boundaries
- No taxonomies for organisation structure. Taxonomies are permitted only for
  flat lookup lists in WorkMate.Records.
- Nothing reads the dimension link, closure or assignment tables directly.
  IDimensionGraphService and IEmployeeAssignmentService are the only entry.
- Workflows route work and record decisions. No calculation in an activity.
  Payroll, accrual and entitlement calculations live in their own engines.
- Administrators never see the Orchard workflow canvas. The approval designer
  compiles to hidden, system-managed workflow types.
- Payroll pulls from Leave and Attendance; they never write into Payroll.
- The employee record holds no department or cost-centre field. Placement is an
  assignment row.
- No secrets in recipes, settings or pipeline files.

## Coding standards
Warnings as errors. Nullable enabled. File-scoped namespaces. record types for
value objects and service results. No public setters on domain models outside
content parts. Tests named for the behaviour they assert.

## Build and test
    dotnet restore
    dotnet build -warnaserror
    dotnet test
The pipeline additionally applies every recipe under /recipes to a fresh tenant;
a recipe that fails to apply fails the build.

## Definition of done
Tests at the levels the spec requires; permissions and entitlement checked in
services; every string localised; module README current; audit trail covers
material changes; migration additive; one reviewer approved; for migrations,
recipes and permissions, a second reviewer approved.

## Conventions still to decide
See /docs/adr for open decisions (formula engine choice, front-end approach,
mobile framework, distributed cache, search provider). Do not choose these
yourself; propose and stop.
