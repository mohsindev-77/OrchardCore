# Dimension Engine — Technical Architecture

Sep 29, 2026 · @Umair Tariq

The foundation layer of WorkMate 2.0: how organisation structure and every other dimension axis is modelled, queried and maintained.

## Purpose and decisions settled

This document specifies the dimension engine — the foundation layer of WorkMate 2.0, on which forms, approvals, payroll attribution and reporting all depend. It is the first thing built and the hardest thing to change later.

It settles six decisions so that build can start without reopening them.

| # | Decision | Rationale |
| --- | --- | --- |
| 1 | Dimensions are a general model; organisation structure is one instance | A customer's location, cost, project and custom axes use identical machinery, so no axis needs special-case code |
| 2 | Taxonomies are not used | A taxonomy stores its term tree as nested data in one item, declares a single term type, and carries no effective dating — none of which survives enterprise volume or mid-period transfers |
| 3 | Definitions are configuration; records are content; the graph is custom tables | Each layer gets the storage that suits it, rather than forcing all three into one mechanism |
| 4 | Links are scoped to a structure | One record can sit on several axes at once, which a single parent field makes impossible |
| 5 | A closure index is maintained, not derived on read | Descendant queries are the hottest path in payroll and reporting; walking a tree per request does not hold at scale |
| 6 | Assignments are effective-dated and allocatable | Mid-month transfers, split cost centres and historical reproduction all follow from this one choice |

### What this document does not cover

The admin user interface for the org designer, the form designer built on the same content-definition mechanism, and the approval layer that consumes dimension scope. Each is specified separately once this is agreed.

### Verify before building

Orchard Core's APIs move between releases. Every type and interface named here must be checked against the exact version the solution references before it is relied on. Where this document names a mechanism, treat the mechanism as the decision and the exact API as a detail to confirm.

## The data model

Six entities. The split between them is the whole design: definitions are configuration, records are content, and the graph is custom tables built for query speed.

&#91;embedded content: dimension engine entities, configuration through graph\]

### Entity definitions

**DimensionType** — a kind of unit: Business Unit, Division, Department, Section, Branch, Cost Centre, Project, or anything a customer invents. Carries its code, bilingual name, whether it is system-defined or customer-created, whether it may nest inside itself, and the attribute schema its records add beyond the standard fields.

**Structure** — a named axis with an ordered list of dimension types as its levels. Organisation, Location and Cost are each a Structure. Carries whether a level may be skipped, whether the chain is strict, and whether it is the primary organisation axis used as the default scope for approvals and visibility.

**DimensionRecord** — an individual unit, stored as a content item. Standard fields on every record: code unique within the tenant, English and Arabic name, dimension type, effective dates, active flag, sort order, cost centre code, GL account reference and head employee. Its type's attribute schema adds the rest.

**DimensionLink** — one parent-child edge, scoped to a structure and effective-dated. Scoping to a structure is what allows a record to participate in several axes; without it a record has one parent and the axes collapse into a single tree.

**DimensionClosure** — the derived ancestor-descendant index: structure, ancestor, descendant, depth. Maintained on write, never computed on read.

**EmployeeAssignment** — an employee placed on a structure at a record, with effective dates, an allocation percentage and a primary flag. All three matter: dates give mid-month transfers, percentage gives split cost centres, and the primary flag resolves matrix cases without ambiguity.

### One rule that follows from the model

The employee record holds no department field. Every placement is an assignment row. Line manager is stored separately, because the reporting line and the organisation placement are different facts and customers routinely have them differ.

## Mapping onto Orchard Core

Each layer gets the storage that suits it rather than forcing all three into one mechanism.

| Layer | Storage | Why |
| --- | --- | --- |
| DimensionType, Structure | Plain tables with a tenant-scoped admin UI | These are configuration, not content. They are read on almost every request, change rarely, and need no drafts, versions or workflow |
| DimensionRecord | Content items, one content type per dimension type, created at runtime through the content-definition manager | Brings the editor stack, validation, versioning, permissions, search and API for free, and lets each dimension type carry its own fields |
| DimensionLink, DimensionClosure, EmployeeAssignment | Custom tables with their own indexes | These are queried by shape and range, not by identity. Content storage would be the wrong tool and would not perform |

### The runtime content type decision

When an administrator creates a dimension type, the engine creates a matching content type and adds the standard part plus the fields its attribute schema declares. This is the mechanism that makes the product configurable in the way PECT is, and it is the same mechanism the form designer uses.

The alternative — one generic content type discriminated by a type id — is simpler to query but gives up the per-type editor and validation, and would mean rebuilding the field editor ourselves. The runtime approach is chosen, with the churn it creates managed by the guard rails in section 6.

### Tenant scoping

Every custom table carries no tenant column, because Orchard's per-tenant database or table prefix already separates them. That is the isolation boundary and it must not be worked around. Any code path that opens a session outside the current tenant's scope is a defect, and the test suite asserts this rather than leaving it to review.

### Packaging as recipes

Dimension types, structures and a starting set of records must be expressible as a recipe step, so that an industry pack or a customer's configuration can be exported, reviewed, versioned and promoted between environments. This is a build requirement, not an afterthought: the recipe step is written alongside the model, not after it.

## Hierarchy queries and the closure index

The hot query is "every employee under this node, including all descendants". Payroll, reporting, approvals and data visibility all issue it, often several times per request. It must be a single indexed read.

### The index

DimensionClosure holds one row per ancestor-descendant pair per structure, including the self-pair at depth zero. A node five levels deep contributes six rows. For a 400-node organisation the table is in the low thousands of rows — small, and it makes every one of these queries trivial:

| Question | Query |
| --- | --- |
| All descendants of a node | Closure rows where ancestorId is the node |
| All ancestors of a node | Closure rows where descendantId is the node |
| Is A under B | One row lookup |
| Depth of a node | The depth on its root pair |
| Employees under a node | Assignment joined to closure on descendantId, filtered by date |

### Maintenance

The index is rebuilt inside the same transaction as the change that caused it.

**Create** — insert the self-pair, then one row for each ancestor of the new parent.

**Move** — delete every pair joining the moved subtree to its old ancestors, then insert pairs joining that subtree to the new ancestors. Only the subtree is touched, not the whole structure.

**Delete** — remove every pair where the node is ancestor or descendant, after the referential checks in section 6 have passed.

**Rename** — no index change. Names live on the record.

### Rebuild and verification

A rebuild command reconstructs the whole index for a structure from the link table, and a verification command compares the two and reports any divergence without changing anything. Both are needed: the rebuild for recovery, the verification as a scheduled check and as a test assertion. An index that has silently drifted from the links is the worst failure mode in this design, because every downstream number stays plausible while being wrong.

### Performance gate

Descendant resolution under 200 milliseconds at 5,000 records and 5,000 employees in a tenant. This is an acceptance criterion measured against seeded data, not a design aspiration.

## Effective dating and history

Every question of the form "what was true on this date" must be answerable from the live data, without restoring a backup. Three things carry dates: records, links and assignments.

### The resolution rule

A query always resolves as at a date. If no date is supplied the service uses today. There is no un-dated read path, because an un-dated read path is how historical questions quietly start returning present-day answers.

### Worked example: a mid-month transfer

An employee moves from Department A to Department B on 16 March. Two assignment rows exist: A from their join date to 15 March, B from 16 March with no end date.

| Question asked | What the engine returns |
| --- | --- |
| Where do they work today | Department B |
| Where did they work on 10 March | Department A |
| Whose cost is their March salary | Split by the day counts, A then B, through the cost structure |
| Who approves their April leave | B's approver chain |
| Who approved their February leave | Recorded on the request; not re-resolved |

That last row is a rule, not a detail. A completed approval records who acted. It is never recomputed from the current structure, because the structure has moved on and the audit answer must not change.

### Reparenting

Moving a unit is a dated link change, not a destructive edit. The old link is closed with an end date and a new one opened. The closure index is rebuilt for the subtree as section 4 describes. A report for a prior period resolves the links effective then and returns the old shape.

### Renaming

A rename can be corrective or substantive, and these need different answers. A typo fix should apply retrospectively; a genuine renaming should leave last year's reports showing the old name. The engine supports both through dated name records, and the designer asks the user which they mean rather than guessing.

### What history does not cover

Structure history answers where someone sat and what a unit was called. It does not replace the audit trail, which records who made the change and when. Both are kept; they answer different questions.

## Validation and referential safety

Validation runs in one service, called by the designer, the API and every import path. The designer shows a violation before the user commits, but the service is the authority and re-checks on write.

### Rules enforced

| Rule | Behaviour |
| --- | --- |
| Parent type must be a permitted level | Blocked. A Section may sit under a Department; under a Business Unit only if the structure allows skipping |
| Level skipping | Blocked or allowed per structure setting, not per node |
| No cycles | Blocked. A node may never become its own ancestor |
| Self-nesting | Allowed only when the dimension type declares it |
| Code uniqueness | Blocked within a tenant, including against retired records |
| Effective ranges must not overlap | Blocked for an employee on one structure |
| Assignment allocations must total 100 percent | Blocked on save for a given date |
| Exactly one primary assignment | Blocked otherwise, so matrix cases stay unambiguous |
| Parent must be effective when the child is | Warned, not blocked, because customers legitimately pre-build future structures |

### Deletion

A record is never hard-deleted once anything references it. The engine checks in order: current assignments, historical assignments, posted payroll, open workflow instances, and references from other configuration such as approval scopes.

**Clean** — no references at all. Hard delete is permitted.

**Referenced by history only** — retirement. The record is closed with an end date, disappears from pickers, and continues to resolve for historical queries.

**Referenced by live data** — blocked, with the specific blockers listed so the user can act. Never a generic failure message.

### Merge

Merging two units is a supported operation, not a manual workaround. It reassigns children and employees to the target from a stated effective date, retires the source, and leaves prior-period reporting resolving to the source. It runs in dry-run first and produces a report of exactly what will change.

### Import paths

Every bulk import validates row by row and reports all failures rather than stopping at the first. An import that would leave the structure invalid is rejected whole; partial application of a structural import is not permitted.

## Service interface

One service is the only way the rest of the product touches the structure. No module reads the link or closure tables directly. That rule is what makes the storage replaceable later and the tenant boundary testable in one place.

### Operations

| Group | Operations |
| --- | --- |
| Resolve | Get a record; get its ancestors; get its descendants; get its children; test whether one node is under another; get the depth |
| Query | Find records by type, by structure, by code, by attribute; resolve a record from an external code during import |
| Mutate | Create, update, retire, move, merge a record; every one dated and validated |
| Assign | Place an employee on a structure; end an assignment; change an allocation; resolve the assignment effective on a date |
| Reverse lookup | All employees under a node as at a date, with or without descendants; the head of a node; all nodes an employee touches across every structure |
| Maintain | Rebuild the closure index; verify it; report divergence |

### Rules on the interface

Every read takes an effective date, defaulting to today. Every write takes an effective date explicitly, with no default, because a silent default on a write is how dating errors enter the data.

Descendant queries are paged and never return an unbounded set. A structure with a thousand records under one node must not load a thousand content items to answer a count.

Every operation raises typed, specific errors. A blocked move says which rule blocked it and which node is at fault.

### Caching

The structure changes rarely and is read constantly, so it is cached per tenant and invalidated on any mutation. The cache key must include the tenant, and that is one of the isolation boundaries the test suite asserts directly.

### External API

The same operations are exposed as a documented API for integration: read structures and records, resolve employees under a node, and create or move records for customers who master their structure in an ERP. Write operations through the API run the same validation service; there is no privileged path that bypasses it.

## Migration, seeding and test gates

### Migrating a flat structure

Existing customers hold a flat list with the mess real data has: duplicate names differing by case or spacing, departments sitting beside sections, missing or inconsistent codes, and entries nobody uses. The migration runs in four passes.

1. **Profile.** Read the flat list and report what is there: counts, duplicates, orphans, entries with no employees, code gaps.
2. **Propose.** Infer a structure — which entries are departments, which are sections, what the levels should be — and present it for correction. The tool proposes; a human decides.
3. **Dry run.** Apply the approved mapping to a copy and produce a full report of what would change, including every employee whose placement moves.
4. **Apply.** Execute inside one transaction, with the closure index built and verified before commit.

No migration ever runs without a signed dry-run report. That is a process rule as much as a technical one.

### Seed data

The engine ships with generated seed data for development and testing, covering the cases that break naive implementations: a skipped level, a mid-month transfer, a split cost allocation, a matrix assignment, a renamed unit, a retired unit with history, a four-level and a two-level path in one tenant, and a unit head who is not a member of the unit. A scaled variant produces a 1,200-employee tenant for performance work.

### Test gates

| Gate | Criterion |
| --- | --- |
| Correctness | Ancestor and descendant resolution, cycle prevention, level rules, reparenting and merge all covered by unit tests |
| Index integrity | After every mutation in the test suite, the closure index is verified against the links |
| Effective dating | Historical resolution tested at several dates, including the day before and after each change |
| Isolation | Automated tests assert no query, cache key or job crosses a tenant |
| Performance | Descendant resolution under 200 milliseconds at 5,000 records and 5,000 employees; move operation on a 500-node subtree within an agreed window |
| Migration | Dry-run output matches applied output exactly, asserted on the messy legacy fixture |

### Build order

Model and migrations, then the validation service, then the closure index and its maintenance, then the read service, then assignments and effective dating, then the recipe steps, then the admin UI. The UI comes last deliberately: it is the part most likely to change after the first customer sees it, and the least useful to build against an unsettled model.

## Open questions

These need answers from the technology team before or during the first sprint. None blocks starting; each blocks finishing.

| # | Question | Who decides |
| --- | --- | --- |
| 1 | Is this a shared module consumed by WorkMate 2.0 and QuickHCM, or WorkMate-only? Decides the namespace, the package boundary and whether the API is internal or public | Head of Technology |
| 2 | Dynamic content types per dimension type, or one generic type with a discriminator? This document chooses dynamic; confirm against the Orchard version in use | Technology team |
| 3 | How much runtime definition churn is permitted, and what guard rails apply — naming rules, a review step for destructive changes, a limit on types per tenant | Technology and Delivery |
| 4 | Does a customer's structure ever sync from an external master such as an ERP, and if so in which direction? Changes whether the API needs write conflict handling | Delivery, from customer requirements |
| 5 | Where does allocation-based cost recharging live — in this engine, or in payroll consuming it? This document assumes payroll consumes it | Software Development Manager |
| 6 | What is the retention policy for closure rows and assignment history on a long-lived tenant | Technology team |
| 7 | Is BRF built on this engine, or on the taxonomy approach in the current Phase 1 plan? The recommendation is this engine; the decision has a date implication | Leadership |

### Related documents

This is one of four documents following the WorkMate 2.0 product specification. The others cover the aviation industry pack, the QuickHCM migration assessment, and the commercial pricing model.
