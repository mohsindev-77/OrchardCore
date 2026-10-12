# Prompt 4, session B — the form designer: design proposal

**Status:** proposal, for review. Nothing is built.
**Date:** 12 October 2026
**Author:** Claude Code session on `prompt-04-records`
**Contract:** specification section 5 ("Records and the form designer"); prompt 4 of
`/docs/prompt-library.md`, session B.

Session A — the employee record, its six standard sections, the lifecycle, placement,
unit heads and the dimension screens — is complete and committed. This proposal covers
what comes next and asks for decisions before any of it is written.

---

## How to read this

Each numbered section opens with **plain language** — what it means for a customer and
for the person configuring their tenant — and then gives the **engineering detail**
under a sub-heading. A reviewer who only reads the plain-language halves will get the
whole shape of the proposal and every question I need answered.

**Questions for you** are collected in section 1.5 and repeated inline where they
arise. They are numbered **Q1** to **Q12**. Seven of them change what gets built.

**Everything stated as a fact about Orchard Core 3.0.1 in this document was verified
against the pinned assemblies** with `tools/api-probe`, not from memory, as CLAUDE.md
requires. Section 12 lists what was checked and what it returned.

---

## 1. Scope

### 1.1 What the form designer is, in plain language

Today a customer's employee record has a fixed shape: the core (name, code, join date,
nationality, and so on) plus six standard sections — job details, documents, bank
details, qualifications, dependants and contacts. Every customer gets the same fields
in those sections.

Real customers do not have the same fields. A bank wants a "regulator registration
number" on job details. A university wants "academic rank" and "tenure status" on
faculty and not on administrative staff. A construction company wants "site safety
card expiry" on documents. Today each of those is a code change.

The form designer is the screen where a customer's administrator adds those fields
themselves, in both languages, with their own help text, in the order they want, marked
required or not, and visible only when they should be. The product specification's
thesis in one line is "a customer's organisation structure, forms, approval flows and
entitlements are data, not code"; forms are the half of that sentence that does not
exist yet.

It also covers **request forms** — a leave request, an overtime claim, a document
renewal — which are forms an employee fills in and which then route through the
approval engine. Those are the subject of prompt 5, and the form designer is what
produces them.

### 1.2 What the specification actually asks for

Quoted from `/docs/technical-specification.md`, section 5, in full, because several
later decisions turn on the exact words.

> **The form designer.** An administrator-facing designer that produces content types.
> A form definition holds: name and code, bilingual labels, sections, fields with type
> and validation, conditional visibility rules, calculated fields, and the approval
> definition it uses if it is a request form.
>
> `IFormDefinitionService.PublishAsync` compiles a form definition into a content type:
> one content type per form, `FormSubmissionPart` attached for request forms, fields
> mapped to Orchard content fields with WorkMate custom field types where needed
> (bilingual text, dimension picker, employee picker, money, Hijri-aware date,
> attachment set). Publishing a change creates a new definition version; existing
> submissions keep the version they were created against.

On the employee record, from the same section:

> The configurable remainder is a set of named sections, each a `BagPart` of typed items
> or a set of content fields the administrator adds through the form designer. Job
> details, documents, bank details, qualifications, dependants and contact information
> ship as standard sections; a tenant can add its own.

On validation:

> **Definition-time:** field type constraints, required, ranges, regular expressions,
> uniqueness within tenant. Cross-field rules are expressed in a small declarative rule
> language evaluated server-side; scripting is not exposed to administrators.
> Conditional visibility is evaluated both client- and server-side; the server is
> authoritative.

On localisation:

> Every label, help text and option in a form definition is bilingual at definition
> time. The editor renders RTL when the current culture is Arabic.

On recipes:

> `form-definitions` exports definitions, not the generated content types; import
> republishes them, which regenerates the types. This keeps recipes stable across
> Orchard versions.

And the hard constraint, from the employee record paragraph:

> It is not editable by administrators because payroll, leave and attendance depend on
> these fields by name.

### 1.3 In this session

By "this session" I mean the whole of session B, delivered as the seven slices in
section 10.3 — each slice is one working session, with its own review.

| | In session B |
| --- | --- |
| Managed lookup lists (job title, grade, nationality, …), tenant-editable, bilingual | ✔ |
| Binding the standard sections' existing free-text fields to those lists, losing nothing | ✔ |
| Adding fields to any of the six standard sections | ✔ |
| Adding whole new sections to the employee record | ✔ |
| Adding record-level fields to the employee record without touching the fixed core | ✔ |
| Field order, required, bilingual label, bilingual help text | ✔ |
| Retiring a field that is no longer wanted, without destroying its data | ✔ |
| Conditional visibility and cross-field validation, in a closed rule language | ✔ |
| Field types: text, number, date, yes/no, choice from a list, employee picker, file | ✔ |
| Request forms: one content type per form, `FormSubmissionPart`, version pinning | ✔ |
| The `form-definitions` and `lookup-lists` recipe steps, and their export | ✔ |
| One new admin menu entry, and everything else reachable from existing screens | ✔ |

### 1.4 Later, and why

| | Why it waits |
| --- | --- |
| **Dimension (unit) picker field** | Needs a new content field with its own drivers, structure scoping and visibility filtering. The *control* exists (`workmate-unit-picker`, built in A3); the *field* does not. Specification section 5 asks for it, so this is deferred, not dropped. Natural home: the slice after B4, or prompt 6 when an HCM form first needs it. |
| **Money field** | Specification asks for "decimal with currency code, currency from tenant settings unless overridden". Nothing in session B needs it; payroll (prompt 8) does. Deferring it avoids guessing at the rounding and multi-currency rules payroll will set. |
| **Dual-calendar (Hijri-aware) date** | Specification section 9: "Hijri display alongside Gregorian where the tenant enables it". That is a display concern over the existing `DateField`, and a platform-wide one — it belongs with the tenant calendar setting, not inside the form designer. |
| **Attachment set with document type and expiry** | This is really the Core HR documents capability (specification section 6/7: `EmployeeDocument`, `DocumentExpiryIndex`, the daily expiry task). Building the field here without the expiry task behind it ships a field that promises alerts nobody sends. Prompt 6. |
| **Calculated fields** | See **Q3**. They need an expression evaluator, and the formula engine is open technical decision #3, explicitly reserved to the Software Development Manager. I am not choosing it. |
| **Uniqueness constraints** ("uniqueness within tenant") | Needs an index per constrained field and a decision about scope — unique across the tenant, or within one employee's rows. See **Q4**. |
| **Per-role field visibility** | Different from conditional visibility: "the salary field is visible to HR and not to a manager" is data visibility, specification section 9's concern, and it has its own mechanism (`IVisibilityService`). Mixing it into the rule language would give us two answers to one question. |
| **An index over stored lookup values** | Wanted for reporting ("headcount by grade"). Not needed to make lists work. Section 4.5 explains the cost and why I am not paying it yet. |

### 1.5 Questions for you

These are the places the specification does not decide, or decides something I think
needs confirming. **Q1, Q2, Q3, Q7, Q9, Q11 and Q12 change what gets built.**

**Q1 — Does an "employee record form" extend existing sections, or only create new
ones?** The specification says the designer "produces content types", and separately
that the employee record's configurable remainder is "a set of content fields the
administrator adds through the form designer". Those two sentences fit together only
if a form definition can *target* something that already exists. My reading: a
definition either targets one of the six standard sections (and adds fields to its
part), or creates a new section (a new type plus a new bag on `Employee`), or targets
the employee record itself (section 2.2). That reading drives most of this document.
*Recommendation: confirm it.*

**Q2 — Where do record-level extra fields go?** The core is fixed and must stay fixed.
But a customer wanting one extra field on the employee *record* — not a list, a single
value, like "staff category" — has nowhere to put it today except a section, which
would render it as a one-row list. I propose a second part, `EmployeeProfilePart`,
owned by the designer and welded onto `Employee`, holding designer-added record-level
fields. The core stays untouchable; the guard still refuses reserved and
placement-shaped names. *This is my addition, not the specification's. Recommendation:
accept it; without it, "add a field to the employee form" has no honest answer.*

**Q3 — Calculated fields: build, or defer?** The specification lists them. Building
them means an expression evaluator over employee data, and open technical decision #3
("formula engine: build an expression evaluator or adopt a sandboxed library") is
undecided and owned by the Software Development Manager. Three options:
(a) defer entirely, revisit when #3 is settled;
(b) build arithmetic into the rule language we are building anyway — a closed grammar,
numbers and dates only, no functions beyond a fixed list — and treat payroll's formula
engine as a separate and much larger thing (it needs a typed context over payroll
elements, attendance summaries and leave liability, and per-run version recording);
(c) wait for #3 and use whatever it chooses.
*Recommendation: (b), as a slice after B5, but only with your explicit agreement that a
form's calculated field and payroll's formula engine are two things and not one. If you
would rather they were one thing, (a).*

**Q4 — Is "uniqueness within tenant" in scope, and at what scope?** The specification
lists it under definition-time validation. Enforcing it needs an index per constrained
field, and "unique" is ambiguous: unique across every employee (a national ID number)
or unique within one employee's rows (one primary bank account). *Recommendation:
defer; it is one small slice later and it is not what customers ask for first.*

**Q5 — What does "version" mean for an employee-record form?** The specification's
versioning rule — "Publishing a change creates a new definition version; existing
submissions keep the version they were created against" — is clearly about request
forms, which have submissions. An employee record is one living record, not a
submission, and is not draftable. My reading: employee-record definitions are versioned
for audit and for export, but nothing pins to a version, because the record always has
the current shape (and the shape is additive, so an old record simply has empty values
in newer fields). *Recommendation: confirm. If you want employee records pinned to a
definition version, that is a materially larger and different design and I should
redo section 5.*

**Q6 — Section-level visibility.** The Khaleej University reference tenant says
"faculty and administrative staff have different field sets". That is more naturally
expressed as *a whole section is visible when the employee is faculty* than as the same
rule copied onto nine fields. *Recommendation: allow a visibility rule on a section as
well as on a field; same grammar, no extra cost.*

**Q7 — Edition boundaries.** The product specification's packaging table says:
"**Forms** | Standard forms, field configuration | Full form designer | Form designer
plus definition versioning" across Essential / Professional / Enterprise. Read
literally that means an Essential tenant can configure fields on standard forms but
cannot create new forms, and version history is Enterprise only. That mapping happens
to line up well with the slices I propose. *Recommendation: confirm the three-way
split, because it determines which capability constants get declared and where the
checks go.* Note also that `WorkMate.Entitlement` is a skeleton — there is no
`IEntitlementService` to ask yet (prompt 8). See **Q12**.

**Q8 — "Employee type" as a concept.** Khaleej has Faculty, Teaching Assistant,
Administrative, Technician, Visiting, and those drive field sets, leave policies and
approval routes. Is that a lookup list, a dimension type, or a first-class concept with
its own model? *I am not deciding this. The design in section 9 keeps all three open
the same way it keeps positions open — nothing in the form designer may hold a hard
reference to it.* Flagging it because it is the same shape of question as positions and
should probably be answered at the same time.

**Q9 — Lookup lists: our documents, or Orchard taxonomies?** Specification section 5's
field table says "Lookup | Taxonomy field | The one place taxonomies are used: flat
lookup lists only", and CLAUDE.md permits taxonomies for exactly this. I want to depart
from it, for reasons in section 4.3 that come down to one verified fact: Orchard's
`TaxonomyField` stores content item **ids**, and every portable thing on this platform
is named by **code**. *Recommendation: our own documents, recorded as ADR-0014. This is
a specification mechanism change and is therefore yours to accept or refuse; if you
refuse, section 4.3 says what changes.*

**Q10 — `form-definitions` on re-import when the tenant's copy differs.** ADR-0008 says
a divergent row fails the step naming the difference, never overwrites. Applied to form
definitions that makes promotion from a config tenant to a production tenant awkward:
the whole point of promotion is that the production tenant's forms change. *My
recommendation is to follow ADR-0008 exactly — fail, name the difference, and let the
operator use the dry-run that specification section 10 already requires — and record
the awkwardness as a known limitation rather than invent a "replace" mode now.* Say if
you would rather I designed the replace mode up front.

**Q11 — The one new menu entry.** I propose `People → Forms`, a second leaf under the
existing `People` group (whose own comment already says "the form designer's definitions
land under it in session B"). No new top-level group. Everything else — lookup lists,
reconciliation, version history — hangs off that one screen or off the employee editor.
*Recommendation: confirm, because section 8's click paths all assume it.*

**Q12 — The entitlement seam.** CLAUDE.md rule 5 requires entitlement checked in the
service. There is nothing to check against: `WorkMate.Entitlement` has no service yet.
`WorkMate.Records` already has this gap. The codebase's own precedent is
`IVisibilityService`, wired now and allowing everything until prompt 9, "wired now
rather than retrofitted later precisely because a picker written without it is a picker
whose call sites nobody can find". *Recommendation: add the same one-question seam for
entitlement in `WorkMate.Platform` during slice B3 and call it from the new services.
This is a change outside `WorkMate.Records`, so it is yours to approve.*

---

## 2. What can be designed

### 2.1 In plain language

An administrator opens a form, sees the fields it has, and can:

- **add a field**, choosing its type, giving it an English and an Arabic label, an
  English and an Arabic line of help text, and saying whether it must be filled in;
- **reorder** the fields;
- **change a label or help text** at any time — these are presentation, not data;
- **retire a field** they no longer want, which takes it off the editor and leaves
  everything already stored in it exactly where it is;
- **add a whole new section** to the employee record;
- **say when a field or a section should appear** — "show 'visa expiry' only when
  nationality is not Saudi".

They can never remove a field that has been published, and never change its type. Both
of those destroy data, and section 5.3 explains why we refuse rather than warn.

### 2.2 What is designable, and whether the specification asks for it

| Capability | Specification? | In session B |
| --- | --- | --- |
| Extra fields on each of the six standard sections | **Yes** — "a set of content fields the administrator adds through the form designer" | ✔ |
| New sections of the tenant's own | **Yes** — "a tenant can add its own" | ✔ |
| Extra fields on the employee record itself (not in a list) | **No** — my addition, see **Q2** | ✔ as `EmployeeProfilePart` |
| Any change to the fixed core | **Explicitly forbidden** — "not editable by administrators" | ✘ and enforced |
| Field order | Implied by "sections, fields" being an ordered definition | ✔ |
| Required | **Yes** — "fields with type and validation" and "required" under definition-time validation | ✔ |
| Bilingual label | **Yes** — "bilingual labels"; "every label… bilingual at definition time" | ✔ |
| Bilingual help text | **Yes** — "every label, help text and option in a form definition is bilingual" | ✔ |
| Conditional visibility | **Yes** — "conditional visibility rules", evaluated both sides, server authoritative | ✔ (slice B5) |
| Cross-field validation | **Yes** — "cross-field rules… a small declarative rule language" | ✔ (slice B5) |
| Ranges and regular expressions on a field | **Yes** — definition-time validation | ✔ |
| Retire a field | **No** — my addition, the safety valve that makes "never remove" liveable | ✔ |
| Calculated fields | **Yes** — but see **Q3** | deferred pending Q3 |
| Uniqueness within tenant | **Yes** — but see **Q4** | deferred pending Q4 |
| Section visibility rule | Implied; see **Q6** | ✔ if Q6 agreed |
| Request forms with an approval definition reference | **Yes** | ✔ (slice B7) |

### 2.3 Engineering detail

A **form definition** targets one of three things:

| Target | What publishing does | Data already there |
| --- | --- | --- |
| `Section` — one of the six standard sections, or one the tenant added earlier | Adds fields to the part named for that section's item type (`EmployeeJobDetail`, `EmployeeDocument`, …) | Yes, possibly a lot |
| `NewSection` | Creates a new item type with its own part, carrying the `EmployeeSection` stereotype, not creatable and not listable, and welds a new `BagPart` onto `Employee` — exactly what `Migrations.SectionTypeAsync` does today for the six | No |
| `Record` | Adds fields to `EmployeeProfilePart` on `Employee`, creating the part on first publish | Possibly |
| `Request` (slice B7) | Creates a content type of its own with `FormSubmissionPart` attached | No |

Changing a **label**, **help text**, **order**, **required flag**, **visibility rule**
or **validation rule** never touches the stored data and is therefore always allowed.
Adding a field is additive. Everything else is refused (section 5.3).

Two details that look small and are not:

- **Labels and help text live in our definition, not in Orchard's settings.**
  `ContentPartFieldSettings` carries `DisplayName` and `Description` as single strings
  (verified). A bilingual product cannot put its label there. So the definition document
  is the source of truth for both halves, our editor renders from it, and the compiled
  content definition gets the **English** in `DisplayName`/`Description` purely as a
  fallback for Orchard's own generic screens. Writing it the other way round would mean
  an Arabic reader seeing English labels on half the product.
- **Field order uses `ContentPartFieldSettings.Position`** (verified), so Orchard's own
  editors honour the order too.

---

## 3. Field types

### 3.1 In plain language

The designer offers a short, fixed menu of field types. It is short on purpose: every
type is something the whole platform — reports, the API, payroll, the approval engine —
has to be able to read and reason about, so "any field type installed on the tenant" is
not an option. The dimension engine took exactly the same decision for its attribute
schema and wrote down why.

| What the administrator picks | What it is for |
| --- | --- |
| Text | A line of text in one language — a reference number, a note |
| Bilingual text | A name or label carried in English and Arabic |
| Number | A quantity |
| Date | A calendar date |
| Yes / No | A tick |
| Choice from a managed list | One value from a list the tenant maintains — grade, document type |
| Employee | A person, chosen from a searching picker |
| File | A document or image |

### 3.2 In scope now, and what backs each

| Designer type | Backed by | In session B | Note |
| --- | --- | --- | --- |
| Text (single line) | Orchard `TextField` | ✔ | |
| Text (multi-line) | Orchard `TextField`, `TextArea` editor | ✔ | `TextFieldSettings.Type` is a `FieldBehaviorType` (verified) |
| Bilingual text | `BilingualTextField` in `WorkMate.Platform` | ✔ | Already exists (ADR-0003) |
| Number | Orchard `NumericField` | ✔ | |
| Date | Orchard `DateField` | ✔ | Dual-calendar display later |
| Yes / No | Orchard `BooleanField` | ✔ | |
| **Choice from a managed list** | Orchard `TextField` **storing a code**, rendered by a WorkMate lookup **editor** | ✔ | Section 4. **No new field type** — this is the decision that makes migration free |
| Employee picker | Orchard `ContentPickerField` restricted to `Employee`, candidates from `EmployeePickerResultProvider`, filtered by `IVisibilityService` | ✔ | Already exists; the designer just declares it |
| File | Orchard `MediaField` | ✔ | Plain attachment only |
| Unit (dimension) picker | New `DimensionPickerField` + drivers, structure-scoped, visibility-aware | ✘ later | Specification asks; control exists, field does not |
| Money | Decimal + currency code | ✘ later | Payroll sets the rules |
| Dual-calendar date | `DateField` + Hijri display | ✘ later | A platform display concern |
| Attachment set with document type and expiry | `MediaField` + type + expiry, feeding `DocumentExpiryIndex` | ✘ later | Needs the expiry background task (prompt 6) |

The palette is **closed**, and — importantly for section 9 — it contains no type that
points at anything which does not already have its own service and its own dated model.

### 3.3 Why "choice" is not a new field type

A new `LookupField` would store its own thing and would mean that turning an existing
free-text field (`GradeCode`, a `TextField`) into a choice is a **type change** — which
section 5.3 forbids, for good reason, and which would need a data migration rewriting
stored values on every employee in the tenant.

Instead, a choice field *is* a `TextField`. It stores the same plain string it always
did. What changes is which **editor** renders it. Orchard 3.0.1 supports exactly this:
`ContentPartFieldSettings.Editor` is a string on the field's definition, there is a
`WithEditor(builder, editor)` extension, `services.AddContentField<T>().ForEditor<…>()`
registers an editor-specific driver, and `ContentFieldDisplayDriverResolver
.GetEditorDrivers(fieldName, editor)` selects it — all verified against the pinned
assemblies.

So "make grade a managed list" is a settings change on the field, not a type change, not
a migration, and reversible. That is ADR-0016 in section 11.

---

## 4. Managed lookup lists

### 4.1 In plain language

Today a grade, an employment type, a document type, a nationality and a relationship are
all typed in by hand on each employee record. Three people will type "Full Time", "Full
time" and "FT" and the tenant will have three employment types where it meant to have
one. Nothing catches it, and every report that groups by employment type is quietly
wrong.

A **lookup list** is a list the tenant maintains once: each entry has a short code, an
English name, an Arabic name, and a flag saying whether it is still in use. Instead of
typing, the person picks. Entries are **retired**, never deleted, because last year's
records still refer to them and a report about last year still has to be able to read
them.

### 4.2 The lists we would ship

Taken directly from the free-text fields the six standard sections define today:

| List code | What it replaces | Where |
| --- | --- | --- |
| `job-title` | `JobTitle` (bilingual text) | Job details — see **Q9a** below |
| `grade` | `GradeCode` | Job details |
| `employment-type` | `EmploymentTypeCode` | Job details |
| `work-location` | `WorkLocation` | Job details |
| `document-type` | `DocumentTypeCode` | Documents |
| `nationality` | `NationalityCode` on the **core**, `DependantNationalityCode` on dependants | Employee record, Dependants |
| `country` | `CountryCode` | Qualifications, Contacts |
| `currency` | `CurrencyCode` | Bank details |
| `relationship` | `RelationshipCode` | Dependants |
| `contact-type` | `ContactTypeCode` | Contacts |

Two things worth noticing:

- **`NationalityCode` is on the fixed core**, which the designer may not touch. It does
  not need to: the core already stores a *code*. Binding a list to it changes how it is
  edited, not what it is. The core's shape is untouched and the guard is not weakened —
  this is the platform improving a core field's editor, not an administrator editing the
  core. *If you would rather the core were left entirely alone even at the editor level,
  say so and I will drop nationality from the first slice.*
- **`job-title` is the awkward one (Q9a).** `JobTitle` is a `BilingualTextField`, not a
  code. Two honest options:
  (a) bind the list to the bilingual field, so choosing an entry writes its English and
  Arabic names into the field. Nothing lost, no type change, no migration — but the
  record stores a *name*, so renaming the list entry does not change historical records.
  For a job title held on a dated job-detail row that is arguably correct.
  (b) retire `JobTitle` and introduce `JobTitleCode` bound to the list, with a one-off
  reconciliation mapping existing names to codes. Reporting by a stable key then works.
  *Recommendation: (a) for now, because (b) pre-empts the positions decision (section 9)
  and a code-keyed job title is really a position by another name.*

### 4.3 Documents, not taxonomies (Q9)

Specification section 5 says a lookup is a taxonomy field. I propose it is not, and this
needs your decision.

**Verified fact:** `OrchardCore.Taxonomies.Fields.TaxonomyField` holds
`TaxonomyContentItemId` (string) and `TermContentItemIds` (string array) — content item
ids. `TaxonomyPart` holds `TermContentType` and a list of term content items.

**Why that is the wrong currency here.** Everything portable on this platform is named
by code, on purpose and at length: ADR-0011 ("references by code, never by id — the rule
every other step follows, and the reason a recipe is portable at all"), ADR-0008's
comparison rules, dimension codes, employee codes. A taxonomy-backed lookup means either
every recipe and every export carries tenant-specific ids, or we write a
code-to-term-id resolution layer on both sides — which is most of the work a document
gives us for nothing.

**Three further costs.** Terms are content items, so "retired" has to be a field on a
term type we define anyway, and bilingual names likewise — we would be building the
model regardless and then storing it awkwardly. Enabling `OrchardCore.Taxonomies` adds
its own admin menu entry, which is precisely the clutter the product owner has ruled
out. And Orchard's generic taxonomy editor would let an administrator **delete** a term
that records still reference, bypassing the retire-not-delete rule entirely.

**Precedent.** ADR-0002 already decided the dimension engine is not taxonomies, for a
related family of reasons.

**What I propose instead.** `LookupListDocument` — code, bilingual name, and an ordered
list of items, each with code, bilingual name, active flag and sort order — stored as a
YesSql document with a `LookupListIndex`, owned by `ILookupListService`, administered by
two screens of our own. This is ADR-0014.

**If you refuse**, the change is contained: the field binding mechanism in section 3.3
is unaffected (a taxonomy term id is still just a string in a `TextField` — although a
tenant-specific one), the screens become Orchard's, the recipe step becomes a term-id
resolution layer, and `OrchardCore.Taxonomies` joins `base.recipe.json` (it is **not**
enabled today — I checked).

### 4.4 Nothing is lost, and that is a property of the mechanism

The brief asks how existing free-text values in the Zenith and Crescent demo data are
handled. The honest answer starts with a finding:

**There is almost nothing to migrate.** The demo employee recipes
(`organisation-designer-zenith-employees.recipe.json` and its Crescent twin) carry only
the employee **core** — code, bilingual name, join date, status, and optionally date of
birth, nationality, gender, line manager and photo. The `employees` recipe step has no
notion of sections at all, so no demo employee has a job detail, a document, a bank
account, a qualification, a dependant or a contact. The only demo field that a lookup
list would touch is `NationalityCode` on the core, and the generator does not currently
emit it.

So **this is the cheapest moment this change will ever have.** That is worth acting on
rather than discovering later.

For real tenants, and for whatever demo data we add, the mechanism is this — four rules,
in order of importance:

1. **An unmatched stored value is never discarded.** The lookup editor renders a value
   that is not in the list as a selected option marked *not in the list*, and keeps it on
   save unless somebody deliberately chooses something else. This is the lesson the
   employee picker already learned the hard way and recorded: the selection is a fact the
   control holds, not something re-derived from the text in the box.
2. **Nothing is ever rewritten automatically.** Not on binding, not on import, not on
   upgrade.
3. **Binding is off by default.** Lists ship with content from the starter recipe;
   binding a list to a field is a deliberate act on the designer screen. A tenant that
   upgrades and does nothing sees no change at all.
4. **Reconciliation is a screen, not a migration.** Section 8's screen 7 lists the
   distinct stored values that are not in the list, with a count of records for each, and
   offers three actions per value: *add it to the list* (code and English name taken from
   the value, Arabic blank per ADR-0003's addendum), *map it to an existing entry* (which
   rewrites those stored values, audited, after showing the count and asking), or *leave
   it*. Leaving it is a supported permanent state.

I would also recommend extending `tools/generate-demo-employees.sh` in a later slice to
emit job details with a deliberately unmatched value, so the reconciliation screen has
something real to demonstrate — the same reasoning behind the deliberate anomalies the
specification already requires in seed data ("skipped level, mid-month transfer, split
allocation, matrix reporting, renamed and retired units, vacant head").

### 4.5 One cost, stated plainly

Finding the unmatched values means reading every stored value of every bound field.
There is no index over them today. Two options:

- Build a `LookupValueIndex` (content item, type, part, field, list, value) maintained by
  a handler. Cost: an index row per bound field per record — at 5,000 employees with six
  bound fields that is 30,000 rows, written on every save, for a screen run rarely.
- Scan on demand behind an explicit **Check stored values** button, with a row cap and a
  clear "showing the first N" message.

*Recommendation: the scan, in the first slice.* The index becomes worth building the
moment Reporting wants "headcount by grade", and at that point it is justified by the
report rather than by an admin screen. Recorded as a known limitation in the README.

---

## 5. Storage and safety

### 5.1 In plain language

A form definition is **our own record of what the customer asked for**. The content type
Orchard ends up with is **the result of applying it** — derived, regenerable, and never
the thing we back up, export or hand-edit. If the two ever disagree, the definition wins
and republishing makes it so.

That split is what makes the specification's recipe rule possible: exporting a tenant's
forms exports the definitions, and importing them republishes, which rebuilds the types.
A recipe written today therefore still applies after an Orchard upgrade that changes how
content types are stored.

A field that already holds data is protected by one rule, stated the same way the
organisation designer already states it: **once a field has been saved it cannot be
removed or have its type changed, because either would destroy data already held in it;
a field can still be added.** What we add on top is **retire** — take it off the editor,
keep the data — because a rule with no escape valve gets worked around.

### 5.2 Engineering detail — storage

| Thing | Storage | Why |
| --- | --- | --- |
| `FormDefinitionDocument` | YesSql document + `FormDefinitionIndex` (code, target, target type, published version, has unpublished changes) | The working copy. Code is the natural key, immutable, as everywhere else on this platform |
| `FormVersionDocument` | One immutable document per published version + `FormVersionIndex` (form code, version number, published on, published by) | A submission pins a version for ever; a version a later publish could edit is not a version |
| `LookupListDocument` | YesSql document + `LookupListIndex` (code, item count, active item count) | Section 4.3 |
| The generated content types and parts | Orchard content definitions | **Derived output.** Never exported, never the source of truth |
| A submission's pinned version | `FormSubmissionPart` on the generated type: form code + version number | Slice B7 |

**ADR-0007 compliance.** No settable property on any of these documents defaults to a
bare `Type.Member` reference. Bilingual names default to `new(string.Empty,
string.Empty)`; collections are `IReadOnlyList<T> { get; set; } = []`. The existing
`SharedStaticDocumentDefaultTests` guard in `WorkMate.Platform.Tests` scans every
module's source and therefore covers this new code the day the files exist, with nothing
to register.

**ADR-0006 compliance, and a trap we must not walk into.** `IFormDefinitionService
.PublishAsync` is a write path that alters content definitions, so every definition read
it makes uses `LoadTypeDefinitionAsync` / `LoadPartDefinitionAsync`, never `Get…`. More
importantly: **a recipe can publish a form and then create employees in the same shell
scope**, and `IContentManager.NewAsync` reads the *cached* definition. The result,
measured in ADR-0006, is a content item with **no parts welded at all** that silently
discards everything set on it. `DimensionRecordHandler.ActivatingAsync` already carries
the repair for dimension records; **`EmployeeHandler` does not, and will need it.** That
is a concrete, silent, high-likelihood failure and it is on the slice B3 task list.

**Per-tenant isolation.** The documents live in the tenant's own store; services are
tenant-scoped by construction (CLAUDE.md rule 4) and take no tenant parameter. Lookup
lists will be cached — they are read on every employee editor render — with cache keys
beginning with the tenant name, invalidated by `ILookupListService` on any mutation, and
a test asserting the key includes the tenant, exactly as `WorkMate.Dimensions` does.

**Migrations (ADR-0009).** `WorkMate.Records.Migrations` is at version 3 (`CreateAsync`
→ 1, `UpdateFrom1Async` → 2, `UpdateFrom2Async` → 3). The new index tables go into a new
`UpdateFrom3Async` (and `UpdateFrom4Async` for the later slice), and `CreateAsync` is
amended **only** so that a brand-new tenant gets the current schema in one pass. No
shipped step's body is touched. Each schema change ships an upgrade test on the
`DimensionsMigrationUpgradeTenantTests` pattern: build the tables as the previous version
left them, migrate forward, assert the result matches a fresh install — and then
exercise the real operation through the service, not merely assert a column exists.

**Audit.** A new audit trail category `Form`, with `FormDefinitionChanged`,
`FormDefinitionPublished`, `LookupListChanged` and `LookupValuesRemapped`, all mandatory,
carrying before-and-after and the acting user. Specification section 9 already names
"content-definition change" as a WorkMate audit event type; publishing is the event that
produces one, and recording the publish is more useful to an auditor than recording the
resulting definition diff alone — so both are written, and the publish event names the
definition version.

### 5.3 Engineering detail — the safety rules

Four rules, enforced in the service (CLAUDE.md rule 1 and rule 5), not only in the UI:

1. **A published field is never removed.** Refused with the same shape of message the
   dimension engine uses: *"The field 'X' cannot be removed from the form 'Y'. Removing a
   field deletes the data held in it. Retire it instead."*
2. **A published field's type never changes.** Same reason, same refusal.
3. **A published field's name never changes.** The name is the content-definition field
   name; renaming it orphans the stored value. Labels change freely — that is what labels
   are for.
4. **A field may be retired.** It disappears from the editor and from display, stays in
   the definition, stays in the content definition, and its data stays where it is.
   Un-retiring is allowed and is the exact inverse.

Two more that are specific to this module:

5. **The fixed core is unreachable.** `EmployeeContentDefinitionGuard.EnsureMayDefine`
   already refuses a field name that collides with the core, or that reintroduces
   placement (`DepartmentCode`, `CostCentre`, …). The compiler calls it for **every**
   field it defines, which closes the one row the Records README currently marks *"A form
   definition naming the `Employee` type → Session B's compiler → **Not yet**"*. A
   definition targeting the `Employee` content type's `EmployeePart`, or any of the
   reserved names, is refused at definition time and again at publish time.
6. **Designer-created parts are not attachable.** Like `EmployeePart`, so they do not
   appear in Orchard's "Add parts" list and cannot be welded onto something else.

**Turning "required" on** is the one interesting case. It does not destroy data but it
can make existing rows invalid. Proposal: allowed, enforced on save from then on, and
the publish-confirmation screen (screen 3) shows the count of existing rows that would
fail — the same dry-run-then-apply shape the exit screen, the move and the merge all
already use.

**Orchard's own content type editor can still reach the generated types.** We cannot
remove that screen. The mitigation is the direction of truth: `PublishAsync` rebuilds
the definition from the document, so a hand edit survives until the next publish and then
loses. That is the right direction, and it is worth saying out loud in the README so that
nobody is surprised by it.

---

## 6. Bilingual

### 6.1 In plain language

Everything an administrator types while designing a form — the form's name, every field
label, every line of help text, every entry in every lookup list, and the message a
validation rule shows when it fails — is captured in English and in Arabic.

English is required. **Arabic is optional**, unless the tenant has turned
`RequireArabicNames` on, because a customer being set up has an English spreadsheet and
no Arabic, and insisting on Arabic at the first field does not get the translation done —
it stops the form being built. A reader is never shown a blank: an Arabic reader looking
at an untranslated label sees the English one, which they can act on.

That is ADR-0003 and its 7 October addendum, unchanged. This proposal adds no new rule;
it applies the existing one to a new set of strings.

### 6.2 Engineering detail

- Every author-written string is a `BilingualText` on the definition document, rendered
  through `BilingualText.Display(en, ar)` — the single implementation of the fallback.
- The policy question "is Arabic required on this tenant" is asked through
  `IBilingualNamePolicy`, the same one-question seam every other write path uses, so the
  form designer and the organisation designer cannot disagree about the answer.
- The compiled content definition receives the **English** half in `DisplayName` and
  `Description`, as a fallback for Orchard's generic screens only (section 2.3).
- Our editors render from the definition, so a culture switch changes labels with no
  republish.
- Bilingual sentences avoid the empty-bracket trap: `BilingualDisplay.NameWithAlternate`
  omits the alternate entirely when there is none.
- `workmate-bilingual` marks the English input required and never the Arabic one; the
  server is the authority either way.
- Validation messages that **we** generate go through the localiser (CLAUDE.md rule 7,
  and `UserVisibleStringTests` enforces it automatically for new files). Validation
  messages an **administrator** writes on a rule are content and are bilingual on the
  definition.
- The designer screens render RTL in Arabic culture, like every other admin screen.
- One guard inherited free: `ViewModelsSurviveNullBindingTests` reflects over every view
  model, nulls every settable string and reads every getter — which is what stops an
  empty Arabic box (which binds to `null`, not `""`) throwing inside the model binder
  before the controller ever runs.

---

## 7. Recipes and export

### 7.1 In plain language

A customer's forms and lookup lists are configuration, so they must travel: from the
configuration tenant to the production tenant, into a new tenant built from an industry
pack, and out again when a customer asks for their data.

Two new recipe steps carry them. Both export what they import — the same file format,
read and written by the same field names — because two descriptions of one thing drift,
and the drift is silent (ADR-0011). Applying a file twice is safe (ADR-0008).

### 7.2 Engineering detail

**Two steps, in `WorkMate.Records/Recipes/`:**

| Step | Carries |
| --- | --- |
| `lookup-lists` | Each list by code: bilingual name, and each item's code, bilingual name, active flag and sort order |
| `form-definitions` | Each definition by code: bilingual name, target and target code, the published version number, and each field's name, type, bilingual label, bilingual help text, required flag, order, retired flag, validation and visibility rules, and list binding where it has one |

**Never exported:** the generated content types and parts. The import republishes, which
regenerates them. This is the specification's own rule and the reason recipes stay stable
across Orchard versions.

**Ordering.** `lookup-lists` before `form-definitions` (a field binds a list by code),
and both before `employees` (a stored value has to resolve). Ordering is the one thing an
operator can get wrong, so — exactly as the A3 export does for its six steps — both step
screens say it in words.

**Re-run behaviour (ADR-0008).** Per-step idempotency, never cross-step atomicity.
Everything validates across the whole step before anything is written.

| | `lookup-lists` | `form-definitions` |
| --- | --- | --- |
| Row's code not in the tenant | create | create and publish |
| Content identical | skip, no write, no error | skip |
| Content differs | fail, naming the list and the item that differs | fail, naming the form and the field that differs — **see Q10** |
| Tenant holds an item the row does not mention | **not a difference** | **not a difference** |

That last row needs justifying, because it widens nothing but could be read as doing so.
ADR-0008's rule is that comparison is scoped to what a row actually states: "a row with
no `attributes` key says *nothing* about attributes rather than claiming there are none".
A list the tenant has added entries to since the file was written is the same case — the
file is not claiming those entries do not exist. A **stated** item that differs is still
a failure, which is the half that matters.

Comparison goes through the same normalisation names already use (`RecipeNameComparison`:
null and empty are one value, surrounding whitespace is not part of one), for the reason
ADR-0003's addendum records — a recipe carrying `null` and a tenant holding `""` describe
the same absent value, and reporting them as different refuses a re-run of a correct file.

**Export (ADR-0011).** `RecordsDeploymentStep` — which exists, under *Tools →
Deployments*, with its one `IncludeEmployees` switch — gains two more:
`IncludeFormDefinitions` and `IncludeLookupLists`. Both **on by default**, unlike
`IncludeEmployees`: these are configuration, which travels between environments as a
matter of course, whereas employees are operational data about real people. That is the
same distinction the A3 amendment drew for `IncludeAssignments` and `IncludeHeads`.

**One risk worth naming.** An operator can also add Orchard's own *Content Definition*
deployment step to the same plan, which would carry the **generated** types — so an
import could apply a stale generated type and then republish over it. We cannot stop
them adding it. Two mitigations: the step screen says plainly that form definitions carry
their own types and that the content-definition step is not needed for them; and
`PublishAsync` is idempotent, so republishing over an imported stale type converges on
the right answer rather than compounding the error.

---

## 8. Screens

### 8.1 The one new menu entry

**`People → Forms`.** One new leaf, under the group that already exists, next to
`Employees`. No new top-level group, and no new Orchard feature that would bring its own
menu entry — which is a second reason not to enable `OrchardCore.Taxonomies` (section
4.3).

The `People` group was built in session A2 in the shape it would keep, and its own code
comment says so: *"The group will grow — the form designer's definitions land under it in
session B — so it is created in the shape it will keep rather than as a single link to be
rearranged later."* This is that.

```
Organisation
  ├ Organisation designer
  ├ Dimension types
  └ Structures
People
  ├ Employees
  └ Forms            ← the one new entry
```

Positioned second, after Employees, because Employees is what somebody comes to this
group to do and Forms is how it is configured — the same ordering reasoning the
Organisation group uses (the chart first, the configuration screens after).

### 8.2 The screens, and exactly how a user reaches each one

Nothing below is reachable only by typing a URL. `EmployeeLifecycleClickBrowserTests`
exists because session A2 shipped five transitions with a control for one of them and the
browser tests did not notice, "which proves a screen works and says nothing about whether
anybody can get to it". Every path here is a click path and will be tested as one.

---

**Screen 1 — Forms** *(the list)*
**Reached by:** admin menu → **People → Forms**.

```
┌────────────────────────────────────────────────────────────────────────┐
│ Forms                                      [ Lookup lists ] [ + New ]  │
│                                                                        │
│  ○ Employee record    ○ Request forms                                  │
│                                                                        │
│  Name                  Code          Extends        Fields  Status     │
│  ───────────────────────────────────────────────────────────────────   │
│  Job details           job-details   Job details      7+2   Published   │
│  الوظيفة                                                   v3 · 2 Oct  │
│  Documents             documents     Documents        6     Published   │
│  Bank details          bank-details  Bank details     7     Published   │
│  Qualifications        …                                               │
│  Dependants            …                                               │
│  Contacts              …                                               │
│  Profile fields        profile       Employee record  0     Not yet     │
│  Safety cards          safety-cards  New section      4     Draft ●     │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

Two tabs, because an employee-record form and a request form are different enough that a
single list would need a column nobody reads. "7+2" means seven fields that shipped with
the product and two the tenant added. A draft marker (●) means there are unpublished
changes. **Lookup lists** is a link in the page header, not a menu entry — that is how
screen 5 is reached.

---

**Screen 2 — Form designer** *(the editor)*
**Reached by:** Forms list → click a form's name. Also from **screen 8**, the employee
editor's *Design this section* link.

```
┌────────────────────────────────────────────────────────────────────────┐
│ ‹ Forms      Job details          code: job-details   Published v3     │
│                                                       [ History ]      │
│ Name (English) [Job details          ]                                 │
│ Name (Arabic)  [تفاصيل الوظيفة        ]                                 │
│ ───────────────────────────────────────────────────────────────────────│
│  Fields                        │  JobTitle                             │
│  ──────────────────────────────│  ─────────────────────────────────────│
│  ⠿ Job title        standard ▸ │  Field name   JobTitle    (fixed)     │
│  ⠿ Grade            standard ▸ │  Type         Bilingual text (fixed)  │
│  ⠿ Employment type  standard ▸ │  Label (En)   [Job title          ]   │
│  ⠿ Work location    standard ▸ │  Label (Ar)   [المسمى الوظيفي      ]   │
│  ⠿ Effective from   standard ▸ │  Help (En)    [As printed on the …]   │
│  ⠿ Effective to     standard ▸ │  Help (Ar)    [                   ]   │
│  ⠿ Probation ends   standard ▸ │  ☐ Required                           │
│  ⠿ Cost code        added    ▸ │  Choices from  [ job-title      ▾ ]   │
│  ⠿ Safety card no.  retired  ▸ │                [ Manage this list ]   │
│                                │  Show this field                      │
│  [ + Add a field ]             │   ◉ always  ○ when…                   │
│                                │                                       │
│                                │  [ Retire this field ]                │
│ ───────────────────────────────────────────────────────────────────────│
│ [ Save draft ]   [ Publish… ]                        Draft changes ●   │
└────────────────────────────────────────────────────────────────────────┘
```

Field name and type are shown read-only once published, with the reason beside them —
the same pattern the dimension type editor already uses for a code ("The code cannot be
changed once the dimension type exists"). Reordering is up/down buttons and a round trip,
not a drag-and-drop library: the placement split screen established that adding and
removing a row are round trips "like every other form here", and the constraint in prompt
4's brief is that no JavaScript framework beyond what the admin theme ships is introduced
without approval.

---

**Screen 3 — Publish** *(the confirmation, its own page)*
**Reached by:** designer → **Publish…**

```
┌────────────────────────────────────────────────────────────────────────┐
│ Publish Job details — version 4                                        │
│                                                                        │
│ This will:                                                             │
│   + add the field “Cost code” (text)                                   │
│   + change the English label of “Grade” to “Pay grade”                 │
│   + bind “Grade” to the lookup list “grade”                            │
│   ! make “Employment type” required                                    │
│       1,284 existing job detail rows have no value. They are not        │
│       changed; the next person to save one will have to fill it in.    │
│   − retire the field “Safety card no.”                                 │
│       Its data is kept and is still exported and reported on.          │
│                                                                        │
│ Refused:                                                               │
│   ✕ “Grade” cannot change from text to number — that would destroy     │
│      the values already stored in it. Add a new field instead.         │
│                                                                        │
│                                        [ Cancel ]  [ Publish ]         │
└────────────────────────────────────────────────────────────────────────┘
```

Its own page, not a modal, because — in the words the module already uses for the exit
screen — these "commit dated decisions with consequences", and the dry-run-then-apply
shape is what the move, the merge, the retirement and the exit all already do.

---

**Screen 4 — Version history**
**Reached by:** Forms list row → **History**, or designer header → **History**.
A table of published versions: version, published on, by whom, and a diff against the
previous version. Read-only. (Enterprise-only if **Q7**'s edition mapping is confirmed.)

---

**Screen 5 — Lookup lists**
**Reached by:** Forms list header → **Lookup lists**.
A table: code, bilingual name, total entries, active entries, and *used by* (which forms
and fields bind it). Row click → screen 6.

---

**Screen 6 — Lookup list editor**
**Reached by:** Lookup lists → click a list. Also from screen 2's field panel →
**Manage this list**, which returns to the designer afterwards.

```
┌────────────────────────────────────────────────────────────────────────┐
│ ‹ Lookup lists     Grade          code: grade                          │
│                                                                        │
│  Code    Name (English)      Name (Arabic)       Active  Order         │
│  ────────────────────────────────────────────────────────────────────  │
│  G1      [Grade 1        ]   [الدرجة ١       ]      ☑     [10]  ▸     │
│  G2      [Grade 2        ]   [الدرجة ٢       ]      ☑     [20]  ▸     │
│  G9      [Grade 9 (old)  ]   [               ]      ☐     [90]  ▸     │
│  [ + Add an entry ]                                                    │
│                                                                        │
│  Used by: Job details → Grade                                          │
│                                                                        │
│  [ Check stored values ]                            [ Save ]           │
└────────────────────────────────────────────────────────────────────────┘
```

An entry's **code is fixed once anything references it**. Unticking *Active* retires it:
it stops being offered to new records and stays readable on old ones, which is exactly
what "Grade 9 (old)" above is. Deleting an entry is offered only when the list has never
been bound and no stored value references it.

---

**Screen 7 — Values not in the list** *(reconciliation)*
**Reached by:** lookup list editor → **Check stored values**.

```
┌────────────────────────────────────────────────────────────────────────┐
│ Grade — values stored on records that are not in this list             │
│                                                                        │
│  Value          Records   What to do                                   │
│  ────────────────────────────────────────────────────────────────────  │
│  “Grade One”      114     ○ leave  ○ add to the list  ◉ map to [G1 ▾]  │
│  “g1”              17     ○ leave  ○ add to the list  ◉ map to [G1 ▾]  │
│  “Trainee”          3     ○ leave  ◉ add to the list  ○ map to [  ▾]  │
│                                                                        │
│  Mapping rewrites the value on those records. It is recorded in the    │
│  audit trail and cannot be undone automatically.                       │
│                                                                        │
│                                         [ Cancel ]  [ Apply ]          │
└────────────────────────────────────────────────────────────────────────┘
```

Default for every row is **leave**. Nothing happens unless somebody chooses it.

---

**Screen 8 — The employee editor** *(changed, not new)*
**Reached by:** People → Employees → an employee.

Designer-added fields appear in their section alongside the standard ones, in the order
and with the labels and help text the definition gives them. A new **Profile** block
appears below the core if the tenant has added record-level fields (**Q2**). Each section
header gains a small **Design this section** link, visible only with
`ManageFormDefinitions`, which opens screen 2 for that section — so an administrator
looking at a record who notices a missing field gets to the designer in one click instead
of navigating back out to the menu. That is the same pattern as the organisation
designer's card menu carrying *Place an employee here…*.

---

**That is the whole surface: one new menu entry, seven new screens behind it, and one
changed screen.**

---

## 9. Positions

### 9.1 In plain language

How a customer models "positions" is undecided. There are three plausible futures:

1. **Free text** — a job title typed on the job details row. What exists today.
2. **A managed list** — job titles maintained as a lookup list, picked rather than typed.
3. **Full position management** — a position is a thing in its own right, with a
   headcount, a reporting line, a grade and a budget, which a person *holds* for a period
   and which continues to exist when they leave.

The design must not force any of these, and must not make any of them expensive later.

### 9.2 How this design keeps all three open

**The field never becomes a reference.** A choice field stores a plain string — a code —
in a `TextField`, exactly as the free-text field does today (section 3.3). So:

| Future | What changes | Cost |
| --- | --- | --- |
| Free text | nothing | already true |
| Managed list | bind a list to the field | a settings change; no type change, no migration, reversible |
| Full position management | a `Position` becomes an aggregate with a service, and the employee's link to it becomes a **dated assignment row**, not a field | the field survives as a historical label and is neither migrated nor deleted |

**The link to a position would be an assignment, not a field — and that is already the
platform's answer to this shape of question.** The employee record holds no department
and no cost centre precisely because a field has no dates and a mid-month transfer would
overwrite where somebody sat rather than record that they moved. Every one of those
arguments applies to a position: people are promoted, posts are re-graded, a post is held
by nobody for two months. So when positions become real, they become either a dimension
type with a structure of their own, or an aggregate with its own dated assignment table —
and in **neither** case is the right answer a field on a form.

**Which means the rule to write down now is a rule about the palette:** the form designer
must never be the place a cross-record relationship is invented. Every field type that
points at another record — the employee picker, and the unit picker when it arrives —
points at an aggregate that **already has a service and a dated model**. Positions do
not, so there is no position picker in the palette and there will not be one until there
is something for it to point at. One rule, and it is what keeps all three futures open.

**And the strongest form of not blocking:** the dimension engine can already express
Position as a dimension type, with its own structure and its own attribute schema, with
no new code at all. The Khaleej University reference tenant in prompt 11 already assumes
exactly that — its dimension types are listed as "Business Unit, Campus, College,
Administrative Directorate, Department, Section, Academic Programme (custom), Cost Centre,
**Grade, Position**". A customer can pilot full position management on the platform as it
stands, and whatever we learn from that pilot informs the decision rather than being
constrained by it.

**What I am deliberately not doing:** not naming the lookup list `position`, not making
`job-title` mandatory, and not adding a `PositionCode` field to any section. Each would
be a quiet vote for one of the three futures. Section 4.2's **Q9a** is the one place this
bites, and its recommendation — keep `JobTitle` as a bilingual name rather than
introducing a code-keyed `JobTitleCode` — is made for exactly this reason: a code-keyed
job title is a position by another name, and inventing one as a side-effect of a lookup
list would decide this question by accident.

---

## 10. Testing, risks, and slices

### 10.1 Testing plan

Specification section 5 requires: *"Publish and republish round-trip; version pinning of
submissions; every field type's edit and display driver; visibility rule evaluation;
bilingual rendering in both cultures; employee lifecycle events reaching their
subscribers."* The last is already done (session A).

**Unit** — no shell, under the one-minute budget:

- The **rule evaluator**, against a table of cases — the brief asks for exactly this.
  One table, run against **both** the server evaluator and the client evaluator, so the
  two cannot disagree (section 10.2, risk 4).
- The **publish planner** as a pure function: given a stored definition and a proposed
  one, what is added, relabelled, reordered, retired, and what is refused. No session, no
  localiser, no clock — the shape `PlacementSplit` established, and for the same reason:
  every case becomes a unit test and the message can name the field that is wrong.
- Lookup list validation: duplicate codes, immutable code once referenced, retire versus
  delete, bilingual name policy.
- `EmployeeContentDefinitionGuard` extended to the compiler: every reserved name and
  every placement-shaped name refused, as a table.

**Integration, against a real tenant:**

- Publish → read the definition → republish unchanged → assert nothing changed.
- Publish, create a record holding the new field, republish with a relabel, assert the
  stored value is intact.
- Removal refused; type change refused; rename refused; retire keeps the data.
- A definition naming the `Employee` type or a core field name is refused — the row the
  Records README currently marks "Not yet".
- **The ADR-0006 case explicitly:** a recipe that publishes a form **and then** creates
  employees in the same scope, asserting every part is welded. This is the test that
  catches the silent failure in section 10.2 risk 2.
- Recipe steps: ADR-0008's four outcomes for each step; apply twice, nothing changes;
  leftovers from a broken run do not block a fixed re-run.
- Export → import into a fresh tenant → compare definitions and lists **by code**, the
  ADR-0011 round-trip pattern. Likely a new `FormsExportRoundTripTenantTests` rather than
  an extension of an existing one, for the reason `PeopleExportRoundTripTenantTests` was
  separated: the thing most likely to be wrong is the join between modules.
- Migration upgrade tests, one per schema change (ADR-0009): build the previous version's
  tables, migrate, assert equality with a fresh install, then perform the real operation
  through the service.
- Bilingual rendering in both cultures, including a label with no Arabic falling back to
  English rather than rendering blank.
- Tenant isolation of the lookup list cache: the key contains the tenant name.

**Browser:** one typed URL and clicks for everything after it — menu → Forms → a form →
add a field → publish → Employees → an employee → the field is there and saves. Plus the
lookup list path: Forms → Lookup lists → a list → add an entry → back to the designer →
bind it → the employee editor offers it.

**Inherited free, nothing to register:** `SharedStaticDocumentDefaultTests` (ADR-0007),
`UserVisibleStringTests` (localisation), `ViewModelsSurviveNullBindingTests`.

### 10.2 Risks

| | Risk | Mitigation |
| --- | --- | --- |
| 1 | **Runtime content-definition churn is governed by an unanswered question.** Dimension engine architecture open question 3 — "how much runtime definition churn is permitted, and what guard rails apply — naming rules, a review step for destructive changes, a limit on types per tenant" — is owned by Technology and Delivery and is open. This feature is precisely what it is about. | Additive-only plus retire (section 5.3) is a conservative answer that cannot be wrong in the destructive direction. **I am escalating the question rather than answering it**; ADR-0015 records the Records-specific half and says the general answer is still theirs |
| 2 | **ADR-0006's same-scope blindness, silently.** A recipe publishing a form and then creating employees in the same scope gets content items with no parts welded, which discard everything set on them and fail validation complaining about something else. Three sessions were lost to this once already. | `EmployeeHandler.ActivatingAsync` gains the repair `DimensionRecordHandler` already has; the integration test above is written first |
| 3 | **The rule language becomes a scripting language by increments.** Each individual "just one more function" is reasonable. | The grammar is closed and is listed in the ADR. The brief's own constraint governs: "If a rule cannot be expressed, that is a finding for me, not a reason to expose scripting" |
| 4 | **The client and server rule evaluators disagree** — and they disagree on exactly the case the rule exists for. This is ADR-0010's lesson ("two readings of one rule, implemented separately, disagree on exactly the case the rule exists for"), which this module already applied once to the unit picker. | One grammar, one parser definition, one shared test table run against both evaluators, and the server always re-evaluates and overrides — which specification section 5 requires anyway |
| 5 | **Binding a list changes an editor under live data.** | Unmatched values preserved, never rewritten, binding off by default, reconciliation explicit (section 4.4) |
| 6 | **Editor render cost.** An employee editor with ten bound fields resolves ten lists on every render. | Per-tenant `IMemoryCache`, key prefixed with the tenant name, invalidated by the owning service — specification section 9's rule, and the key asserted by a test |
| 7 | **Edition and entitlement boundaries are unconfirmed and unenforceable.** **Q7** and **Q12**. | Declare the capability constants in slice B3; wire the check when a seam exists; record it in the README's known limitations until then |
| 8 | **Orchard's generic content-type editor can reach generated types.** | Designer-created parts are non-attachable; `PublishAsync` rebuilds from the document, so hand edits lose at the next publish; stated in the README |
| 9 | **Slice B3 is the largest and least visible.** A model slice with no UI is hard to review. | It is exercised by the recipe step and by tests; if it proves too large it splits into B3a (model, service, safety rules) and B3b (compiler, `EmployeeProfilePart`) |

### 10.3 Proposed slices, in the order I recommend

Each is one session, model-first then services then UI then tests, stopping for review
after each — the working pattern CLAUDE.md sets and the build order the dimension engine
architecture already argued for ("the UI comes last deliberately: it is the part most
likely to change after the first customer sees it, and the least useful to build against
an unsettled model").

| | Slice | What lands | Why here |
| --- | --- | --- | --- |
| **B1** | **Lookup lists** | `LookupListDocument`, `LookupListIndex`, `ILookupListService`, migration, screens 5 and 6, permission, audit, `lookup-lists` recipe step, export switch, tests | Self-contained, depends on nothing in this session, and is what makes the designer's Choice type possible. Also the first slice that could ship to a customer on its own |
| **B2** | **Binding, and the standard fields** | The `WorkMateLookup` editor for `TextField` and `BilingualTextField`, the binding settings, unmatched-value preservation, screen 7, the starter list content | Delivers the visible customer benefit — grade, employment type, document type and nationality stop being free text — **before** any designer exists. If session B stopped here it would still have been worth doing |
| **B3** | **The definition model and the compiler** | `FormDefinitionDocument`, `FormVersionDocument`, their indexes, `IFormDefinitionService.PublishAsync`, the four safety rules, the guard extension, `EmployeeProfilePart`, the `EmployeeHandler` ADR-0006 repair, migration, audit, capability constants. **No UI** | The part most expensive to get wrong and least useful to build against an unsettled screen. Exercised by tests and by B6's recipe step |
| **B4** | **The designer screens** | Screens 1, 2, 3, 4 and the change to screen 8; the one menu entry | The model is settled by now, so the screen can be built against something real |
| **B5** | **The rule language** | Grammar, server evaluator, client evaluator, the shared case table, conditional visibility on fields and sections, cross-field validation | Needs a screen to put a rule on; a rule with nowhere to be entered cannot be reviewed |
| **B6** | **Recipe step and export** | `form-definitions` both ways, the ordering warning on both step screens, the round-trip test | Needs the model (B3) and benefits from having real definitions (B4, B5) to round-trip |
| **B7** | **Request forms** | `FormSubmissionPart`, one type per form, submission version pinning, the approval definition reference as a code | Nothing before it needs request forms; prompt 5 is what consumes them, so this is the handover |

**After session B, in rough priority order:** the dimension picker field; calculated
fields (if **Q3** says build); uniqueness constraints (if **Q4** says build); money and
dual-calendar date with payroll; the attachment set with Core HR's expiry task; a
`LookupValueIndex` when Reporting wants it.

**If you want fewer slices**, B1+B2 merge cleanly and B5 can fold into B4. I would not
merge B3 into anything.

---

## 11. ADRs I would write

Each would be written at the start of the slice that implements it, not retrospectively.

**ADR-0014 — Lookup lists are WorkMate documents, not taxonomies.**
Specification section 5's field table says a lookup is an Orchard taxonomy field, and
CLAUDE.md permits taxonomies for exactly this case. This departs from it. The decisive
fact, verified against the pinned assemblies: `TaxonomyField` stores
`TaxonomyContentItemId` and `TermContentItemIds` — content item ids — while every
portable thing on this platform is named by code, which ADR-0011 records as "the rule
every other step follows, and the reason a recipe is portable at all". A taxonomy-backed
lookup would make every recipe and every export either tenant-specific or dependent on a
code-to-id resolution layer that is most of the work a document does for free. Three
secondary costs: a term type would have to carry code, Arabic name and an active flag
anyway, so the model gets built either way and then stored awkwardly; enabling
`OrchardCore.Taxonomies` (not enabled today) adds an admin menu entry the product owner
has ruled out; and Orchard's generic taxonomy editor would let an administrator delete a
term that live records reference, bypassing retire-not-delete. ADR-0002 is the precedent:
the dimension engine is not taxonomies either, for a related family of reasons.
**Needs your decision (Q9).**

**ADR-0015 — A designed field is additive and retirable, never removed and never
retyped.**
Extends to the form designer the rule `DimensionTypeService` already enforces for
attribute schemas — a field cannot be removed, and its kind cannot change, once records
exist — and adds the thing the dimension engine lacks: **retire**, which takes a field
off the editor and leaves its data intact. The addition matters because a prohibition
with no escape valve gets worked around, usually by someone creating a parallel field and
leaving the old one visible and confusing. The ADR also records what is deliberately
*not* decided here: dimension engine architecture open question 3 — how much runtime
definition churn is permitted across the platform, and what guard rails apply — remains
open and owned by Technology and Delivery. This ADR answers only the Records case, in the
conservative direction, which cannot be wrong destructively.

**ADR-0016 — Binding a lookup list changes the editor, never the stored shape.**
A field bound to a managed list remains the field type it already was — a `TextField`
holding a code, or a `BilingualTextField` holding two names — and the binding is
expressed as `ContentPartFieldSettings.Editor` plus our own settings naming the list.
Verified mechanism: `WithEditor`, `AddContentField<T>().ForEditor<…>()`, and
`ContentFieldDisplayDriverResolver.GetEditorDrivers(fieldName, editor)`. The consequences
are the point: turning an existing free-text field into a managed choice needs no type
change, no data migration and no downtime, and is reversible; a stored value that is not
in the list is preserved and surfaced for explicit reconciliation rather than rewritten.
"No data lost" becomes a property of the mechanism rather than of a migration script
somebody has to get right once.

**ADR-0017 — The rule language is closed, and one grammar serves both evaluators.**
Conditional visibility and cross-field validation are expressed in a small declarative
grammar: field references, literals, comparison operators, `and`/`or`/`not`, membership
against a lookup list, and a fixed, enumerated function set. No scripting is exposed to
administrators, now or later; a rule that cannot be expressed is a finding for the
product owner, not a reason to open the grammar. One parser definition produces both the
server evaluator and the client evaluator, and one shared table of cases tests both — the
ADR-0010 lesson that two independent readings of one rule disagree on exactly the case the
rule exists for, which this module has already paid for once. The server always
re-evaluates and overrides the client, as specification section 5 requires.

**An addendum to ADR-0008, not a new ADR** — covering how `form-definitions` and
`lookup-lists` compare on re-run, in particular the rule that an entry the tenant holds
and the file does not mention is not a difference, which follows the existing
"comparison is scoped to what the row actually states" principle rather than widening it.
Depends on **Q10**.

**Possibly ADR-0018 — Calculated fields and payroll's formula engine are two things** —
only if **Q3** is answered (b). It would record that a form's calculated field is
arithmetic in the closed rule language over the submission's own fields, and that
payroll's formula engine is a separate, larger, versioned evaluator over a typed context
of elements, attendance summaries and leave liability; and that open technical decision
#3 governs the second and not the first. I will not write it unless you agree the two are
separate.

---

## 12. What was verified against Orchard Core 3.0.1

Checked with `tools/api-probe` against the pinned assemblies, not from memory, per
CLAUDE.md. Everything this proposal relies on:

| Checked | Result |
| --- | --- |
| `ContentPartFieldSettings` | `DisplayName`, `Description`, **`Editor`**, `DisplayMode`, `Position` — all `string`. Confirms per-field editor selection and field ordering, and confirms labels/help text are **not** bilingual there |
| `ContentPartFieldSettingsExtensions.WithEditor(builder, editor)` | present |
| `ContentFieldServiceCollectionExtensions.ForEditor(…)` | four overloads, including a predicate form |
| `ContentFieldDisplayDriverResolver.GetEditorDrivers(fieldName, editor)` | present |
| `ContentFieldDisplayDriver<T>.GetEditorShapeType(ContentPartFieldDefinition)` | present (four overloads) — the shape-alternate mechanism for a named editor |
| `OrchardCore.Taxonomies.Fields.TaxonomyField` | `TaxonomyContentItemId` (string), `TermContentItemIds` (string[]) — **ids, not codes**. The basis of ADR-0014 |
| `OrchardCore.Taxonomies.Models.TaxonomyPart` | `TermContentType`, `List<ContentItem> Terms` |
| `OrchardCore.Taxonomies` in `recipes/base.recipe.json` | **not enabled** — so choosing taxonomies would mean enabling a feature and accepting its admin menu entry |
| `ContentFields.Settings.TextFieldSettings` | `DefaultValue`, `Type` (`FieldBehaviorType`), `Pattern`, `Placeholder` |
| `TextFieldPredefinedListEditorSettings` | `ListValueOption[] Options`, `EditorOption Editor` (Radio / Dropdown), `DefaultValue`. Exists, and is **not** what we want: options are static in the type definition and `ListValueOption.Name` is a single string, so a bilingual, tenant-managed list would mean re-stamping the content definition on every list edit and picking one culture at stamp time |
| `ContentFields.Fields.ContentPickerField` + `ContentPickerFieldSettings` | `ContentItemIds`; settings carry `Multiple`, `DisplayedContentTypes`, `DisplayedStereotypes`, `TitlePattern` — enough for the employee picker the designer declares, and `DisplayedStereotypes` is how a picker could ask for "an employee section" |
| `OrchardCore.Media.Fields.MediaField` | `Paths`, `MediaTexts` |
| `WorkMate.Records.Migrations` current version | 3 (`CreateAsync`→1, `UpdateFrom1Async`→2, `UpdateFrom2Async`→3), so the next step is `UpdateFrom3Async` |
| The `employees` recipe step's shape | core only — code, names, join date, status, status date, date of birth, nationality code, gender, line manager code, photo path. **No section data**, which is the basis of section 4.4's finding |

**Not verified, and to be verified in the slice that needs it:** the exact shape-alternate
naming a named field editor produces (`GetEditorShapeType` exists; what it returns for a
custom editor name needs a running tenant, which `tools/api-probe` cannot give — it loads
metadata only and never executes Orchard code). This does not change any decision in this
document; it is a detail of slice B2.

---

## 13. What I need from you

1. Answers to **Q1, Q2, Q3, Q7, Q9, Q10, Q11 and Q12** — these change what gets built.
2. A view on **Q4, Q5, Q6 and Q8**, which I can proceed without but would rather not.
3. Approval of the slice order in section 10.3, or a different one.
4. Approval to write **ADR-0014** before slice B1 begins, since it is the decision B1
   rests on.

Nothing is built and nothing is committed. On your word I will start with slice B1 and
stop after it for review.
