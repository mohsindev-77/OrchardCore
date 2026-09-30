# WorkMate 2.0 — Product Specification

Sep 29, 2026 · @Umair Tariq

## Purpose and scope

WorkMate 2.0 is a multi-tenant SaaS HCM platform built on Orchard Core, where each customer is a configured tenant rather than a code branch. This document specifies what the product is, how it is packaged for sale, and how a customer is brought onto it.

Bahrain Royal Flights is the first reference tenant. Its Phase 1 scope is used throughout to test whether the specification is concrete enough to build against, but nothing here is BRF-specific unless section 9 says so.

**In scope:** the platform architecture, the functional module set, the commercial packaging and its technical enforcement, tenant lifecycle, and non-functional requirements.

**Out of scope:** sprint-level estimates, UI design, the migration plan for existing QuickHCM customers, and pricing. Each needs its own document once this one is agreed.

**Audience:** the technology and delivery leadership who will build and sell this — Head of Technology, Software Development Manager, Head of Project Delivery — plus the commercial owners who decide what sits in which package.

The product thesis in one sentence: a customer's organisation structure, forms, approval flows and entitlements are data, not code, so onboarding a new customer is a configuration exercise that the delivery team — and eventually the customer — can complete without engineering.

## Lineage: what carries forward

This is the third expression of one idea, and the parts worth keeping are already proven.

| Stage | What it contributed | Status |
| --- | --- | --- |
| PECT ERP | The dimension concept: structure expressed as dimension types and the relations between them, rather than fixed tables per customer | Inspiration, not code |
| QuickHCM | GCC payroll and compliance depth, a live Bahrain customer base, the module footprint an HCM buyer expects | In market; continues to be sold and maintained |
| WorkMate (current) | The SaaS shape, a UI-based form builder, an org designer, and dimensions already at the core of the data model | In market; WorkMate 2.0 is its next generation, specified here |

**What carries forward:** the domain knowledge (GCC payroll rules, leave and indemnity policy, Arabic localisation), the dimension model as the structural primitive, the module set, and the customer relationships that let us validate against real requirements.

**What does not:** a fixed organisation hierarchy, per-customer code branches, manual deployment and folder-copy releases, and approval chains wired in at build time. Each of these is a named target for removal, not an incidental change.

## Product principles

These are binding on every module. A design that breaks one needs an explicit, recorded exception.

1. **Configuration over code.** Anything that differs between customers is data held in the tenant, not a branch, a flag in source, or a build-time switch.
2. **Dimensions over fixed hierarchy.** Organisation structure is one instance of a general dimension model. Location, cost, project and any customer-defined axis use the same machinery.
3. **Effective-dated by default.** Structures, assignments, policies and rates carry effective dates. Any question of the form "what was true on this date" must be answerable without restoring a backup.
4. **Tenant isolation is absolute.** No query, cache, index, background job or export may cross a tenant boundary. Every such boundary is enforced in one place and tested.
5. **Bilingual and RTL from the start.** English and Arabic are both first-class. No screen assumes left-to-right, and no field assumes a single name.
6. **Configuration is exportable.** Whatever an administrator configures can be captured as a recipe, reviewed, versioned and promoted between environments.
7. **Workflows route; engines compute.** The workflow layer moves work between people and records decisions. Payroll, accrual and entitlement calculations live in their own engines and are never expressed as workflow steps.
8. **Entitlement is enforced in one place.** Package and module licensing is checked by a single service, not scattered through feature code.
9. **Self-service is a primary surface.** ESS, MSS and mobile are specified alongside the admin experience, not retrofitted after it.
10. **Everything material is audited.** Who changed what, when, and what the value was before — for structure, policy, pay and approvals.

## Platform architecture

Four layers. Each is built on the one below it, and the boundary between what Orchard Core provides and what Aramis builds is drawn deliberately.

&#91;embedded content: platform architecture · four layers\]

The dimension engine is highlighted because everything above it depends on it: forms scope to structures, approvals route by them, payroll attributes cost through them, and reports aggregate along them.

### Tenancy model

One Orchard Core tenant per customer. Tenants share the application but not data, definitions, indexes, caches or background jobs. Deployment is shared cloud by default; dedicated cloud and on-premises run the same code base with the same configuration mechanism.

### What we take from the platform

Content definition at runtime is the mechanism behind both the dimension model and the form designer: a definition created by an administrator becomes a real content type, with the editor, validation, versioning, permissions and API that come with it. Recipes and deployment plans make a configured customer an artifact that can be reviewed and promoted. Tenancy, localisation, security, search and audit are platform concerns we do not rewrite.

### What we wrap rather than expose

The workflow designer is a developer tool. Administrators get a purpose-built approval designer that compiles down to workflow definitions underneath; engineers keep the full canvas for unusual cases. The forms module is too thin for HR forms, so the form designer is built on content-type definition instead.

### The dimension engine

Organisation structure is one instance of a general model, not a feature. Dimension types and structures are configuration; dimension records are content items; parent-child links are scoped to a structure so one record can sit on several axes; a closure index keeps descendant queries fast; and employee assignments are effective-dated and splittable by percentage.

Taxonomies are explicitly not the primitive here. They store a term tree as nested data in a single item, declare one term type per taxonomy, and carry no effective dating. They cannot support per-level attributes, mid-month transfers or fast descendant queries at enterprise volume.

### One boundary that must hold

Workflows route and record decisions. Payroll, accrual and entitlement calculations live in their own engines. Expressing a calculation as a workflow step is a trap that is very difficult to reverse later.

## Functional modules

Seven module groups. The Package column names the lowest edition that includes the capability; section 6 defines the editions.

### Core HR

| Capability | What it does | Package |
| --- | --- | --- |
| Organisation designer | Build and maintain dimension structures visually; move, rename, activate and retire units with the rules enforced as you work | Essential |
| Employee records | Profile, job details, documents, bank details, qualifications, experience, dependants; configurable field set per tenant | Essential |
| Employee documents | Upload, categorise, set expiry, alert before lapse; renewal workflow | Essential |
| Letter management | Templated letters and certificates generated from employee data, with approval and issue log | Professional |
| Asset tracking | Assign, transfer and recover company assets against employees and units | Professional |
| Announcements | Targeted communications by dimension scope, with read tracking | Essential |
| Air ticket management | Entitlement cycles, eligibility, booking record and encashment, wired to payroll accruals | Professional |
| Public relations data | Visa, residency, permit and government-document tracking with expiry alerts | Professional |

### Leave management

| Capability | What it does | Package |
| --- | --- | --- |
| Types and policies | Leave types with eligibility, entitlement and carry-forward rules defined per policy, assigned by dimension scope | Essential |
| Opening balances | Load and reconcile balances at go-live, with an audit of the loaded position | Essential |
| Leave entry and approval | Request, approve and record; balance checked at request time and on approval | Essential |
| Accruals | Period accrual of entitlement and liability, effective-dated and feeding payroll | Professional |
| TOIL | Compensatory leave earned against approved overtime or duty, with its own expiry rules | Professional |
| Encashment | Rules-driven encashment calculated from the payroll engine | Professional |
| Leave reports | Balance, liability, utilisation and forecast | Essential |

### Time and attendance

| Capability | What it does | Package |
| --- | --- | --- |
| Shift management | Shift definitions, patterns, breaks, tolerance and overtime rules | Essential |
| Rosters and scheduling | Build and publish rosters by unit; coverage view; swap and cover requests | Professional |
| Biometric integration | Device and vendor adapters, scheduled and on-demand pull, reconciliation of unmatched punches | Professional |
| Attendance modification | Correction with reason codes, approval and full audit of the original value | Essential |
| Logs and reports | Raw logs, exceptions, late and absence analysis, timesheet export to payroll | Essential |
| Duty-period rules (aviation) | Duty-day, rest and limit tracking against a configured scheme; see section 9 | Industry pack |

### Payroll

| Capability | What it does | Package |
| --- | --- | --- |
| Payroll setups and policies | One policy-driven station: cut-offs, allowances, deductions, eligibility | Professional |
| Formula engine | Configurable calculation rules at the core of every payroll element | Professional |
| Processing checklist | Pre-run readiness: unapproved overtime, missing data, open leave | Professional |
| Salary process workflow | Review, adjust, approve and post a run digitally | Professional |
| Accruals and settlements | Leave, indemnity and air passage accrual; final settlement on exit | Professional |
| Cost allocation and GL | Cost-centre attribution including split and mid-period transfer; COA mapping and journal export | Enterprise |
| Statutory and bank output | GOSI, WPS, LMRA and bank file generation per country | Professional |
| Payroll traceability | Per-employee explanation of how every figure in a run was reached | Enterprise |

### ESS, MSS and mobile

| Capability | What it does | Package |
| --- | --- | --- |
| E-applications | Any configured form submitted as a request by an employee | Essential |
| Manager and HR approvals | Approval inbox with delegation, escalation and full history | Essential |
| Status tracking | Where a request sits and who holds it | Essential |
| Online payslips | Secure payslip access and history | Professional |
| Overtime requests | Request, approve and feed to attendance and payroll | Professional |
| Native mobile apps | iOS and Android, bilingual, with push notification | Professional |

### Workflow and forms

| Capability | What it does | Package |
| --- | --- | --- |
| Form designer | Build forms as first-class types: fields, sections, conditional visibility, cross-field validation, calculated fields | Professional |
| Approval designer | Pick the form, scope it to the organisation, sequence approvers by user, role or resolved manager, add conditions | Essential |
| Delegation and out-of-office | Cover arrangements with a date range and an audit of who acted under delegation | Professional |
| SLA and escalation | Deadlines per step with automatic escalation | Enterprise |
| Definition versioning | In-flight requests complete under the definition they started on | Enterprise |

### Reporting, administration and platform

| Capability | What it does | Package |
| --- | --- | --- |
| Standard report library | Operational, statutory and management reports per module | Essential |
| Report builder | Customer-built reports over permitted data, respecting dimension visibility | Professional |
| Dashboards | Role-based home screens driven by dimension scope | Professional |
| Analytics and export | Scheduled export and BI feed | Enterprise |
| Users, roles and permissions | Role definitions with data visibility scoped by dimension | Essential |
| Single sign-on | Standards-based SSO against the customer's identity provider | Enterprise |
| Audit trail | Change history across structure, policy, pay and approvals | Essential |
| Integration and API | Documented API, webhooks and connector framework | Enterprise |

## Packaging and editions

Three editions plus add-ons. The editions are cumulative: Professional includes everything in Essential, Enterprise includes everything in Professional. Add-ons and industry packs are sold on top of any edition unless stated.

### The three editions

|  | Essential | Professional | Enterprise |
| --- | --- | --- | --- |
| **Who it is for** | Small and mid-size employers who need core HR running properly | Established employers running payroll, rosters and self-service at scale | Regulated, multi-entity or multi-country employers with integration and audit demands |
| **Indicative size** | Up to 250 employees | 250 to 1,500 employees | 1,500+, or any size with regulatory obligations |
| **Organisation model** | Single organisation structure, standard levels | Multiple dimension axes, customer-defined dimensions | Multi-entity, multi-country, cross-entity reporting |
| **Forms** | Standard forms, field configuration | Full form designer | Form designer plus definition versioning |
| **Workflow** | Approval designer, sequential approval | Delegation, conditional routing | SLA, escalation, in-flight version control |
| **Payroll** | Not included | Full payroll, statutory output, bank files | Adds cost allocation, GL posting, payroll traceability |
| **Mobile** | Web ESS only | Native iOS and Android | Native apps with SSO |
| **Reporting** | Standard library | Report builder and dashboards | Scheduled export, BI feed, API |
| **Identity** | Local accounts | Local accounts | SSO against the customer's provider |
| **Deployment** | Shared cloud | Shared cloud | Shared cloud, dedicated cloud or on-premises |
| **Support** | Standard | Priority | Named contact with defined response times |

### Add-on modules

Sold with any edition that meets the stated dependency.

| Add-on | Contents | Depends on |
| --- | --- | --- |
| Payroll | The full payroll module set | Essential or above |
| Time and attendance | Shifts, rosters, biometric integration, logs | Essential or above |
| Recruitment and onboarding | Requisitions, job portal, applicant tracking, checklist onboarding | Essential or above |
| Performance and development | Appraisal cycles, goals, training and development records | Professional or above |
| AI assistant and document intelligence | Conversational self-service, OCR document capture, payroll anomaly detection | Professional or above |
| Advanced analytics | BI feed, scheduled export, custom data models | Professional or above |

### Industry packs

Industry packs are pre-configured dimension structures, policies, forms, workflows and reports for a sector, plus any genuinely sector-specific capability. They are how vertical depth is sold without forking the product.

| Pack | What it adds | First customer |
| --- | --- | --- |
| Aviation | Crew duty-period and rest-limit tracking, crew rostering patterns, flight-crew allowance structures, aviation letter and document templates | Bahrain Royal Flights |
| Banking and financial services | Segregation-of-duty controls, regulator-ready audit reporting, grade and incentive structures | QuickHCM banking base |
| Education | Academic versus administrative unit types, academic programme dimension, faculty contract types, semester-based scheduling | University of Bahrain |
| Retail and distribution | High-volume rostering, branch-level cost attribution, seasonal and part-time contract handling | Prospective |

### Rules that keep packaging honest

- An edition boundary must be defensible as a difference in customer need, not an arbitrary line drawn to force an upgrade.
- No capability appears in two editions with different behaviour. Features are present or absent, never quietly degraded.
- Industry packs are configuration and clearly-bounded extensions. A pack that requires changes to core code is a product gap, and is recorded as one.
- Every edition ships the same platform. Isolation, audit and localisation are never an upgrade.

## Entitlement and licensing

A single entitlement service answers one question for the whole platform: is this capability available to this tenant right now. Every feature asks it; no feature decides for itself.

### The entitlement record

Each tenant holds one entitlement record: edition, active add-ons, active industry packs, licensed employee count, deployment type, valid-from and valid-to dates, and a grace period. It is signed and written by the control plane, never editable inside the tenant.

### How enforcement works

| Layer | What it does |
| --- | --- |
| Module activation | Orchard features are enabled or disabled per tenant from the entitlement record, so an unlicensed module's routes, jobs and admin screens do not exist in that tenant |
| Capability checks | Fine-grained capabilities within an enabled module are checked through one service at the point of use |
| Navigation and UI | Menus and actions are built from resolved entitlements, so a user is never shown a route that will refuse them |
| Licensed headcount | Active employee count is measured against the licence; breach raises a soft warning first and blocks new records only after a defined grace period |
| API surface | Endpoints for unlicensed capabilities return a clear entitlement error, never a generic failure |

### Behaviour on change

**Upgrade** takes effect immediately; newly licensed modules activate and their configuration steps appear.

**Downgrade** never destroys data. Capability is withdrawn, configuration and history are retained read-only, and the tenant is told exactly what became unavailable. If they upgrade again the configuration is still there.

**Expiry** moves the tenant to a defined grace period with full read access and restricted write, then to read-only. Data is never deleted as a licensing consequence.

### The super control panel

A central, secured administration surface outside every tenant. It provisions tenants, issues and amends entitlement records, reports usage against licence across the estate, and tracks version and upgrade status per tenant. On-premises customers hold a signed offline licence with a defined validation and renewal path.

### Open point

Whether a customer's own administrators can self-serve an upgrade, or whether every change goes through Aramis, is unresolved. It changes the control panel scope materially and is listed in section 11.

## Tenant lifecycle

The measure of this product is how quickly a signed customer reaches production without engineering involvement. The target is that a standard customer is configured and loaded inside four weeks.

1. **Provision.** The control panel creates the tenant, applies the entitlement record, and runs the base recipe for the chosen edition. Nothing customer-specific yet.
2. **Apply the starting point.** An industry pack or a generic starter recipe seeds dimension types, a default structure, standard forms, approval templates, leave types and the report library. The customer starts from something working, never from an empty screen.
3. **Configure.** The delivery team, with the customer, builds the organisation structure in the designer, adjusts forms and fields, sets policies and approval routes, and defines roles and visibility. This is the step that used to be development.
4. **Load data.** Employees, opening leave balances, historical assignments where needed, and payroll year-to-date if mid-year. Every load runs in dry-run first and produces a reconciliation report the customer signs.
5. **Validate.** Parallel run for payroll, sample approval journeys end to end, permission and visibility checks per role, and performance validation at the customer's real headcount.
6. **Promote and go live.** The configuration is exported as a recipe, reviewed, and applied to production. Cutover, opening balances confirmed, users enabled.
7. **Operate.** Monthly release notes, scheduled platform upgrades with the tenant's configuration preserved, usage reported against licence, support per the edition.
8. **Change.** Structure changes, new forms, new approval routes and package upgrades are configuration, performed in a non-production tenant and promoted the same way as step 6.
9. **Offboard.** On exit the customer receives a full structured export of their data within an agreed period, and the tenant is retired on a defined schedule.

### Environment model

Every customer has at least a production tenant and a configuration tenant. Configuration changes are made and validated in the configuration tenant and promoted as reviewed recipes. Direct configuration in production is available only to a restricted role and is always audited.

### Upgrade rule

Platform upgrades must never silently alter a tenant's configuration. Where a new version changes a configured behaviour, the upgrade reports what will change before it is applied, per tenant.

## BRF as reference tenant

BRF is the first customer on the new platform and the test of whether the packaging holds. Its Phase 1 plan covers 29 items across five modules, dated October to December, with development estimates on only three of them.

### Edition and packs

BRF maps to **Professional plus the Aviation pack**, with two Enterprise capabilities likely needed: SSO, and cost allocation if payroll comes into scope. That combination should be confirmed commercially, because it is the first real test of whether the edition boundaries are drawn in the right place.

### Phase 1 scope against the product

| BRF module | Product coverage | Gap to close |
| --- | --- | --- |
| Core HR — org structure, employee records, documents | Standard Essential capability | None; this is the foundation work |
| Announcements, asset tracking, letter management | Standard | None |
| Air ticket management | Standard Professional capability | Accrual treatment needs confirming without payroll in scope |
| Public relations data | Standard Professional capability | Field set and expiry rules to be specified |
| Leave management, all eight items | Standard | Opening balance load has no plan line; see below |
| Time and attendance — shifts, rosters, modification, biometric, logs | Standard | Biometric vendor and device model not yet named |
| Crew duty-day | **Aviation pack — not yet built** | Needs a written regulatory specification before it can be estimated |
| ESS/MSS and mobile, all six items | Standard Professional capability | Online payslips depend on payroll; see below |
| Reports and dashboards | Standard | Currently one plan line covering an entire workstream |

### What the plan does not yet carry

These are not criticisms of the plan so much as the things that will decide whether December is met.

- **Payroll is absent from Phase 1, but online payslips is a December deliverable.** Either payslips come from an existing system, or payroll is an unstated dependency. This needs an answer before October.
- **Crew duty-day is one line.** It is a regulatory capability — duty periods, rest requirements, cumulative limits — and it cannot be built from the generic shift module. Which scheme applies, and whose interpretation governs, must be written down.
- **Opening balances imply a migration** from whatever BRF runs today, but there is no data extraction, cleansing or reconciliation line in the plan.
- **No UAT, training, cutover or go-live lines**, and no QC line despite quality being a named category in the transformation roadmap.
- **December carries 13 of 29 items**, including biometric integration with its hardware lead time and native mobile apps with app-store review cycles.
- **26 of 29 items are unestimated.** The three that exist total ten working days.
- **Employee profile fields are explicitly open** — "to be finalised through discussion" — inside a three-day estimate.

### One technical note

The Phase 1 setup line names taxonomy as the mechanism for company hierarchy. Taxonomies will not carry per-level attributes, effective dating, or fast descendant queries at scale. If BRF is built on taxonomies now and the platform moves to the dimension engine later, that migration is paid for twice. The recommendation is that BRF is the first tenant on the dimension engine, and that the five-day estimate is revisited on that basis.

## Non-functional requirements

| Area | Requirement |
| --- | --- |
| Scale per tenant | 5,000 employees without degradation; payroll run, attendance fetch and hierarchy queries validated at that volume as an acceptance gate |
| Response time | Interactive screens under 2 seconds at the 95th percentile at target volume; hierarchy resolution under 200 milliseconds |
| Payroll run | A 1,000-employee run completes within an agreed window, with the figure set from measurement rather than assumed |
| Availability | 99.5% for shared cloud, measured monthly, excluding announced maintenance |
| Tenant isolation | No shared query path, cache key, index or background job may span tenants; isolation is covered by automated tests, not review alone |
| Data residency | Customer data resides in the region agreed with the customer; GCC residency available |
| Security | Encryption in transit and at rest, role-based access with dimension-scoped visibility, secrets never in configuration, dependency scanning in the pipeline |
| Authentication | Local accounts with policy controls at all editions; SSO and MFA at Enterprise |
| Audit | Immutable change history for structure, policy, pay, approvals and permissions, retained per the customer's regulatory period |
| Localisation | English and Arabic throughout, RTL layout, Hijri and Gregorian calendar support, locale-correct number and date handling |
| Accessibility | Keyboard navigation and screen-reader support on employee-facing surfaces |
| Backup and recovery | Defined recovery point and recovery time objectives per deployment type, with restore tested on a schedule |
| Upgrade | Zero configuration loss on platform upgrade; a tenant-by-tenant report of any behavioural change before it is applied |
| Workflow volume | Instance retention and pruning policy defined before go-live, not after |
| Observability | Per-tenant logging, metrics and alerting, with tenant context on every log line |
| Deployment | Shared cloud, dedicated cloud and on-premises from one code base; on-premises via a guided secure installer |

The figures above are targets, not measurements. Each needs confirming against a real workload before it is quoted to a customer.

## Open decisions, assumptions and risks

### Decisions needed before build

| # | Decision | Why it cannot wait |
| --- | --- | --- |
| 1 | Is the dimension engine a shared module used by WorkMate and QuickHCM, or WorkMate-internal? | Deciding later is a rewrite; deciding now is a naming choice |
| 2 | Who configures a customer — the delivery team only, or the customer's own administrators? | Determines how much is invested in guard rails, review steps and designer quality |
| 3 | Can customers self-serve a package upgrade, or does every change go through Aramis? | Changes the control panel scope materially |
| 4 | Do existing QuickHCM customers migrate to this platform, or do the two run in parallel? | Decides whether migration tooling is a product requirement or a one-off project |
| 5 | Is payroll in BRF Phase 1 or not? | Online payslips is a December deliverable that depends on it |
| 6 | Which duty-period scheme governs the aviation pack, and whose interpretation is authoritative? | Nothing can be estimated until this is written down |
| 7 | Are the edition boundaries in section 6 commercially right? | BRF is the first test; changing them after contracts are signed is expensive |

### Assumptions made in this document

- Three editions rather than two or four. Three is the common shape and maps cleanly onto the customer sizes we see, but it is a choice, not a finding.
- Payroll is an add-on rather than an edition feature, on the basis that some customers run payroll elsewhere.
- Indicative employee counts per edition are illustrative and carry no pricing implication.
- The BRF plan dates are treated as fixed constraints rather than estimates, because the file is titled "dates locked".

### Risks

| Risk | Effect | Mitigation |
| --- | --- | --- |
| Foundation work is underestimated | Every module slips behind it | Size the dimension engine properly before committing Phase 1 dates |
| Aviation pack scope is unknown | A regulatory capability sits unestimated in a locked December | Write the duty-period specification in October, with BRF sign-off |
| Runtime configuration churn | Tenant definitions drift, promotion breaks | Naming rules, a review step for destructive changes, a tested promotion path |
| Workflow instance volume | Database growth degrades performance in the first year | Retention and pruning policy defined before go-live |
| Platform version drift | Modules and APIs differ from what was assumed at design time | Verify module availability and API shapes against the exact Orchard Core version before committing to a design |
| Package erosion in sales | Enterprise features given away to win deals, undermining the model | A single approval route for package exceptions, recorded centrally |

### Next documents

This specification is the parent. Four documents follow from it: the dimension engine architecture, the aviation pack specification, the QuickHCM migration assessment, and the commercial pricing model.
