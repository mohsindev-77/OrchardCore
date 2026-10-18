# WorkMate.Records

The employee record and the form designer, both built on runtime content-type
definition. Contracted in section 5 of `/docs/technical-specification.md`.

**Status: prompt 4 session A complete, and session B slices B1 and B2.** The
employee record, its screens, its recipe step and its export are session A. The
managed lookup lists are B1; binding fields to them, the standard lists and the
reconciliation screen are B2. The form designer itself — field definitions, the
compiler, the rule language, request forms — is slices B3 to B7 and nothing of it
exists yet; `Views/FormsAdmin/Index.cshtml` says so on the screen rather than
leaving a reader to wonder.

## Owns

### Binding a field to a list — built in B2

- **`LookupFieldSettings` and `LookupFieldEditor.Name`** — what a binding is: the
  field's `Editor` setting becomes `WorkMateLookup` and its settings name a list
  by code.

  **Binding changes the editor, never the stored shape.** A bound field is still
  the Orchard `TextField` it always was, holding the same plain string. That is
  what makes binding free — no type change, no data migration, no downtime, and
  reversible by clearing two settings — and it is asserted rather than asserted by
  comment: `LookupBindingTenantTests` checks the live definition still says
  `TextField`.

- **`Views/TextField-WorkMateLookup.Edit.cshtml`** — the alternate editor Orchard
  renders for a bound field.

  **No field driver is registered, deliberately.** Orchard's own
  `TextFieldDisplayDriver` asks for the shape `GetEditorShapeType` names, which
  for this editor is `TextField_Edit__WorkMateLookup` — this file. It is the same
  mechanism Orchard's own `PredefinedList` text editor uses. A second driver
  registered for the editor name would not replace Orchard's: 
  `ContentFieldDisplayDriverResolver.GetEditorDrivers` returns *every* driver whose
  predicate matches, so the field would render twice, and the only way to stop it
  would be to reach into another module's registration and narrow its predicate in
  an order neither module controls.

- **`<workmate-lookup>`** — one control, used by the alternate editor view *and*
  by this module's own employee screens for the core nationality. The employee
  picker is "two things" for a good reason; this is one thing used twice, which is
  better where it is possible.

  **A plain `<select>`, with no script at all.** Unlike the employee picker there
  is nothing to search: a list is tens of entries and the browser's own control
  beats anything this could add.

- **`ILookupChoiceProvider`** — what a bound field offers, and the two rules that
  make binding safe:

  **It falls back to free text when the list is missing or has nothing to offer.**
  That is what lets the binding ship to every tenant, including ones created long
  before the lists existed: nothing changes on their screens until they apply the
  lists. An empty dropdown would have taken away the ability to type a value — a
  regression dressed as a feature.

  **It never drops the value the record holds.** A value matching no entry is
  rendered as its own option, selected and marked *not in the list*; a retired
  entry the record holds is offered and marked *retired*. Without that the
  `<select>` could not represent what is stored, and the first save after binding
  would silently replace somebody's grade.

- **`ILookupValueReconciliation`** — finding the values records hold that no entry
  matches, and mapping them. The deliberate half of "no data lost": the control
  keeps the value, and a person decides what it should be.

  **Behind a button, because it reads every employee record in the tenant.** A
  bag's rows are serialised inside their employee rather than stored as content
  items, so there is no index over their values; one would be a row per bound
  field per employee, written on every save, to serve a screen opened a handful of
  times. Capped at 2,000 records, and the screen says when it stopped early.

  **The core nationality is written through `IEmployeeService`, section rows
  through `IContentManager`.** The core has a service that owns it; a section row
  is only ever edited through Orchard's content editor and has none.

### The standard lists, and what is bound to them — B2

`StandardLookupLists` declares them once and three things read it: the migration
that binds the fields, the recipe that ships the entries, and the reconciliation
screen that has to know where a list's values live.

| List | Bound to |
| --- | --- |
| `grade` | Job details → `GradeCode` |
| `employment-type` | Job details → `EmploymentTypeCode` |
| `work-location` | Job details → `WorkLocation` |
| `document-type` | Documents → `DocumentTypeCode` |
| `currency` | Bank details → `CurrencyCode` |
| `country` | Qualifications and Contacts → `CountryCode` |
| `relationship` | Dependants → `RelationshipCode` |
| `nationality` | The employee **core**, and Dependants → `DependantNationalityCode` |
| `contact-type` | Contacts → `ContactTypeCode` |
| `job-title` | **nothing** — see known limitations |

**The binding is a migration; the entries are a recipe.** A binding is schema: the
same on every tenant, and it has to reach tenants that already exist, which a
recipe cannot do because a recipe is applied once at setup. The entries are
content, so they ship as `standard-lookup-lists.recipe.json` — named by
`base.recipe.json`'s `recipes` step for a new tenant, and applied by hand from
Configuration → Recipes for one created earlier. One file either way, because a
tenant set up today and one upgraded today must end up with the same lists.

**Binding the core's nationality does not make the core editable.** The core
already stores a two-letter code; what the binding changes is which control edits
it. `EmployeeFieldNames.Reserved` is untouched and the guard still refuses any
attempt to define a content field with a core name — `LookupBindingTenantTests`
asserts both halves.

**So is binding "off by default" for an existing tenant?** In the only sense that
matters to a user, yes. The migration binds the fields on every tenant, but a
tenant that has not applied the standard lists has nothing behind the binding and
sees exactly the free-text boxes it saw yesterday. The screens change when the
lists are loaded, which is a deliberate act.

### Managed lookup lists — built in B1

- **`LookupListDocument`** — a list the tenant maintains: a code, a bilingual
  name, and an ordered set of entries each with a code, a bilingual name, an
  active flag and a sort order.

  **Documents of ours rather than Orchard taxonomies, per ADR-0014.**
  Specification section 5's field table names a taxonomy field; verified against
  the pinned version, `TaxonomyField` stores content item **ids**, and everything
  portable on this platform is named by **code** — ADR-0011's "references by code,
  never by id, the rule every other step follows, and the reason a recipe is
  portable at all". Read ADR-0014 before changing any of this; it also records
  what is replaced if the decision is overturned, and that `ILookupListService` is
  the seam that makes the blast radius six files.

- **`ILookupListService`** — create, update, delete, and the two reads. The
  permission and the entitlement capability are both checked here, not only in a
  controller, so the recipe step and any future API are covered by the same check.

  **Two rules make a list safe to bind a field to, and both live here.** An entry
  that the tenant already holds cannot be dropped out of the set, and an entry's
  code cannot change. Either would leave records naming something nothing can
  resolve — and nothing would report it, because a report grouping by a missing
  code simply comes out short. Retiring is the supported way to stop offering an
  entry: it disappears from what a reader is given and stays readable on every
  record that has it. This is the same rule `DimensionTypeService.UpdateAsync`
  applies to an attribute schema, reached independently and for the same reason.

  **A list may be deleted only while it is empty**, which is the one state in
  which nothing can be relying on it. That makes a mistyped code recoverable
  without opening a hole. A system-defined list — one that arrived in a recipe —
  is never deleted at all.

- **`LookupListIndex`** — code lookup, the uniqueness check, and the entry counts
  the list screen shows. `CodeUpper` is a second column rather than a
  case-insensitive comparison in the query, for the reason `EmployeeIndex` gives
  at length: collation is the database's property and not ours.

- **`LookupCodes`** — two code rules, not one. A list code is an identifier we
  author that ends up in a URL segment, so it is a leading letter then letters,
  digits, hyphens and underscores. An entry code is whatever the customer already
  calls the thing — `G1`, `001`, `FT`, `ID.CARD`, `HR/0042` — so it is as loose as
  an employee code. Holding both to one rule would either refuse data customers
  arrive with or let a slash into a route value.

- **`RecordsDocuments`** — the one way this module writes a concurrency-checked
  document, with `ConcurrencyCheckedSaveTests` enforcing it over the source. The
  same helper and the same guard `WorkMate.Dimensions` keeps, and the same reason:
  YesSql's check is opt-in per call, so one bare `SaveAsync` turns it off for that
  path with no error and no trace.

- **`Capabilities`** — the three entitlement capability constants specification
  section 8 asks each module to declare. `FormFieldConfiguration` is what the
  lookup lists are gated on. Nothing is refused yet; see known limitations.

### The employee record — built

- **`EmployeePart`** — the fixed core specification section 5 names, and nothing
  else: employee code, English and Arabic names, date of birth, nationality,
  gender, join date, employment status and the date it took effect, line manager
  and photo.

  **It holds no organisational placement**, deliberately. No department, no cost
  centre, no structure, no parent. Every placement is a dated
  `EmployeeAssignment` row in `WorkMate.Dimensions` — architecture section 2's
  one rule that follows from the model — because an employee sits on several structures
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

**Offered where the record is, by `EmployeeLifecycle.ActionsFrom`.** The moves
valid from the current status — and only those — appear as buttons beside the
status on the editor and behind a "Status" menu on each list row, both rendered
by `Shared/_LifecycleActions`, and neither appears at all without
`ChangeEmploymentStatus`. A newly added employee lands on their own record with
"Activate" offered, because that is the next thing anybody does.

A2 shipped all five transitions with a control for one of them: the list offered
"End employment" and nothing else, so activating somebody — the move every new
record needs — could be reached only by typing its URL. The browser tests of the
day did not notice because they *started* by typing URLs, which proves a screen
works and says nothing about whether anybody can get to it.
`EmployeeLifecycleClickBrowserTests` therefore types one path and clicks for
everything after it.

**Everybody starts prospective, whatever their join date.** Activating somebody is
a decision a person makes and dates, never inferred from a date having passed:
"we hired them and they never turned up" is a real outcome, and a status that
activated itself could not represent it.

**A move to the state already held is refused.** Accepting it would overwrite the
date the current state began — which is the date leave accrual, probation and
suspension are counted from — on a request that said nothing about changing it.

**The exit is the transition worth reading.** It closes every placement *and*
every headship the person holds, on every structure, on the last day of service
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
| A section reintroducing what slice F6 **derives** (`Position`, `Grade`, `ReportsTo`, …) | The same guard. Added in F6b: an employee's position, grade and reporting line are read from the dated assignment and from where that position sits (D30) and are on the record nowhere, so a designed field of that name would be an undated shadow of all three | **Yes** |
| A form definition naming the `Employee` type | `IFormDefinitionService` calls `EmployeeContentDefinitionGuard.CheckTarget` on the target and `Check` on every field it compiles | **Yes** — slice B3, D33 |

**`JobTitle` and `GradeCode` are deliberately *not* in that set yet.** The shipped
`JobDetails` section defines both, so forbidding them would make `CreateAsync`
throw on a brand-new tenant. D22 retires the two — keeping the data and the stored
field, removing them from the editor — and adding them here belongs with that, in
slice F6c.

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

### The employee page's shape: fixed core first, then a loop — F6b

The employee page renders in three bands, and the order is the decision:

```
[ fixed core: identity     ]   markup, written out, first
[ fixed core: organisation ]   derived from the position held (D30) — read-only
[ for each section …       ]   a LOOP over what this tenant has
```

**The section list is never hard-coded.** `IEmployeeSectionCatalogue` reads the
tenant's own content definition — every `BagPart` on `Employee` whose contained
item type carries the `EmployeeSection` stereotype — rather than
`EmployeeSections.All`, which is only what the product ships. A seventh section
published by the form designer in B3 appears on the page with no view edited, and
a bag of something that is *not* an employee section is left alone, because the
stereotype is the test and never the name.

**The organisation block is part of the fixed core and is not a section.** It is
rendered above the loop by the page's own markup and holds nothing: Position,
Grade, the organisation chain and *Reports to* are all computed from the dated
assignment and from where that position sits. Writing it as a seventh section
would be the mistake B3 then has to undo. It is collapsible and first so that B4's
screen work does not have to restructure the page once designed sections start
arriving beneath it.

**The chain is one row per ancestor, labelled with the tenant's own dimension
name** — *Division*, *Department*, *Region*, *Branch* — never four hard-coded
words. A tenant whose chart has no Department level simply has no Department row,
which is correct rather than a gap.

## Admin UI

Eight screens, each calling the same services a recipe step or an API would — no
screen holds a privileged path.

### Forms, and the one new menu entry

**`People → Forms` is the only admin menu entry the form designer gets.** A
product-owner constraint rather than a preference: the designer brings a lookup
list screen, a list editor, and later a reconciliation screen, a form list and a
version history, and every one of them is individually reasonable to put in a
menu. A menu is how a product becomes unusable one reasonable decision at a time.
`PeopleMenuTenantTests` and `LookupListClickBrowserTests` are what stop it
eroding, in both languages.

- **`FormsAdminController`** — the Forms screen. In B1 it is the way in to the
  lookup lists and an honest statement of what is not built yet, in the manner
  `PlaceholderPickerTagHelper` already sets for this product. It exists now rather
  than in B4 because a menu entry is the thing customers learn and link to, and
  moving one later is a change every customer notices for no benefit they can see.

  **It carries the Load standard lists button**, shown only while a list in
  `StandardLookupLists.All` is missing — which on a tenant created after B2 is
  never, because the base recipe applies the same file at setup. A tenant upgraded
  into B2 has the bindings and none of the entries, so its bound fields render as
  free-text boxes until somebody loads them; before this existed the only way to
  do that was Configuration → Recipes, an operator's screen listing every recipe
  in the installation, three clicks from anything an HR administrator should be
  touching.

  **`IStandardLookupListsLoader` runs the module's own recipe rather than holding
  a second copy of the entries.** A hundred and sixty codes and twice that many
  names in C# beside the same thing in JSON is a pair that drifts, and the first
  time it did, a tenant seeded by the button and one seeded by the recipe would
  hold different lists under the same codes with nothing to report it. Running the
  file also inherits what the recipe step already guarantees: safe to press twice,
  fills in entries a later version adds, leaves the customer's own entries alone
  (ADR-0008), and writes through `ILookupListService` so every list is validated
  and audited like a hand-typed one. The permission and the entitlement are checked
  in the loader, before the step enters system authority — out there the caller is
  still a person, and inside it the platform is acting.

- **`LookupListsAdminController`** — the list of lists, the editor, the narrow
  deletion, and the **Check stored values** screen. Reached from Forms, never from
  the menu; the reconciliation screen hangs off the editor, which is where
  somebody who has just bound a list already is.

  **The reconciliation screen defaults every row to "leave".** Pressing Apply
  without choosing anything changes nothing, which is the behaviour a screen that
  rewrites employee records had better have. Adding a value to the list touches no
  record; mapping one rewrites it everywhere and is said to be irreversible before
  the button rather than after it.

  **It says where it looked.** A reader who sees nothing needs to tell "every
  value matches" from "nothing is bound to this list" — which is the honest answer
  for `job-title`.

  **Entry rows are added and removed by round trip, not by script**, like the
  placement split screen and for the reason it gives: these forms commit
  configuration, and one that only worked once a script had loaded would silently
  do nothing when it had not. The round trip is also what redraws a row's controls
  against what the server knows — which is what keeps a saved entry's code
  read-only and keeps Remove off it.

  **The affordances match the rules.** A stored entry has no Remove button and a
  read-only code, because it can never be removed or recoded; a reader who could
  press Remove and then be refused would reasonably conclude the product was
  broken rather than that the rule exists. The service refuses it either way —
  `LookupListsAdminTenantTests` posts a hand-made request that drops one, and gets
  the message on the form rather than a 500.

### The employee screens

- **`EmployeesAdminController`** — the list and the editor. The list searches a
  code and both halves of a name, filters by status, and shows where each person
  sits today on the primary organisation structure. That last column comes from
  the dimension engine and is not on the record at all, which is why this module
  ships a list rather than relying on Orchard's generic one.

  **It shows the main placement, and the rest behind a disclosure.** Somebody
  split 60/40 has two true answers to "where do they sit", and the column used
  to show whichever row the engine returned first. Now it names the main one and
  offers **+N more**, which lists the others by their full path and share — the
  full path because two of the units a person is split across routinely have the
  same name.

  **The list resolves placement in two queries per page, not two per row.** A
  screen is the easiest place in a product to grow an N+1, and this one would
  have grown it on the column that matters most.

  **Add-employee asks for a position; the editor shows one and does not ask —
  F6b.** The Create form carries the fixed *Job and organisation* block: a
  position drop-down of the posts active on the join date (each naming the post,
  the unit it sits in and filled/approved), a grade limited to that position's own
  and disabled until one is chosen, the derived organisation chain, *Reports to*
  in all four of D28 A's cases, and the access-roles notice (D26 I). It writes
  through `IEmployeeAssignmentService.PlaceAsync` — the same write path the
  Placement screen uses, not a second one.

  **Why that form may ask and the editor may not.** Every write in this slice is
  dated and nothing is defaulted (CLAUDE.md rule 3). The Create form already asks
  for a join date, and that is the effective date the placement needs; the editor
  has none to offer, so it renders the block read-only and links to the dated
  forms — *Change position* and *Change grade* — which stay on
  `EmployeePlacementAdminController`. A display driver's `UpdateAsync` has nowhere
  to put a required effective date, which is the same reason placement has been a
  screen rather than a driver since prompt 4.

  **The position is optional, deliberately.** Leaving it blank creates an unplaced
  employee exactly as before. Required would break every tenant that hires before
  HR has built its position list, and would make ADR-0029's mixed state
  unliveable. A tenant with no positions at all sees none of the block and keeps
  today's Add and Edit forms unchanged, which is the whole of D30's scope.

- **`EmployeeSectionsAdminController`** — the sections of one employee, at
  `/Admin/Employees/Sections/Edit/{id}`, reached from the employee page and from
  nowhere in the menu.

  **It replaces a link to Orchard's generic content editor**, which worked and was
  addressed to somebody else: it introduced the job details section as "JobDetails
  — Provides a collection behavior for your content item where you can place other
  content items", put a disabled "Title — auto-generated using the pattern" box
  above it, and lit up Content → Content Items in the menu while the reader was in
  the middle of an employee record.

  **The fields inside a section are still Orchard's**, built through
  `IContentItemDisplayManager` by the display drivers that own each field type —
  including this module's lookup editor on the bound ones. Only the frame is ours.
  Reimplementing the editor would mean doing it again for every field type the
  form designer adds in B3.

  **Three extension points, no change to Orchard**, as the hard boundary requires:
  a controller of our own; the shape alternate
  `Views/ContentPart-Employee-BagPart.Edit.cshtml`, which is
  `ContentPart_Edit__Employee__BagPart` and so matches every bag section on the
  employee type and nothing else in the tenant; and `placement.json`, which
  withholds `TitlePart_Edit`.

  **The alternate is generic over the sections.** The six WorkMate ships are named
  in it because they are ours to word; a section the designer adds is headed with
  the display name the tenant gave it. Nothing here needs changing when the set
  changes — and it will: the FRS work on positions moves grade off the job details
  section.

  **Withholding the title editor does not withhold the title.** `TitlePartHandler`
  generates it from the pattern on save, which is what `GeneratedDisabled` means;
  the editor was only ever showing the answer. `EmployeeSectionsTenantTests` saves
  a section and asserts the employee's name and generated title are untouched.
  `placement.json` lists the seven content types by name because Orchard 3.0.1
  ships placement filters for content type and content part only — there is no
  stereotype filter — so a section type the designer adds in B3 will need its own
  line, or a shape table provider that can read the stereotype. That file carries
  no comments because Orchard deserialises every top-level key in it as a
  placement node, `$comment` included.

  **The write goes through `IEmployeeService.UpdateSectionsAsync`**, which owns the
  permission, the publish and the audit entry. `GetContentItemAsync` is the only
  place this module hands a `ContentItem` out, and the pair is why that is not a
  leak: a caller holding the item cannot save it without stepping outside the
  aggregate.

- **`EmployeeLifecycleAdminController`** — the five dated transitions, each its
  own small form rather than one screen with a dropdown, for the reason the
  designer gives every action its own page: these commit dated decisions with
  consequences.

  **The exit is a dry run first.** `GET` computes what ending employment would
  close — every placement on every structure, and every unit the person heads — and
  only `POST` commits. The units losing their head are the half worth reading:
  a department losing its approver on Friday is something somebody has to act
  on, and finding out from a stuck approval the following week is too late.

- **`EmployeePlacementAdminController`** — where one person sits on every
  structure on a date, what they lead, and the dated operations that change it.

  **A screen rather than a driver on the employee editor.** A display driver's
  `UpdateAsync` has nowhere to put a required effective date: it is handed a
  posted model and asked to write it. Every write here is dated and nothing is
  defaulted, so the editor shows placement read-only and links here.

  Three operations, one write path each. **Place** adds one placement,
  **Change placement** states the whole split, **End here** closes everything on
  a structure. The first two both go through
  `IEmployeeAssignmentService.ReallocateAsync` — `PlaceAsync` is a one-row
  reallocation, not a second mechanism — with the same changes/split shape the
  `employee-assignments` recipe step builds. A second write path is how a screen
  and an import come to disagree about what a split means.

  The unit picker offers only units the structure says may hold employees, from
  `IDimensionGraphService` — the same rule `ValidateAssignmentAsync` enforces,
  read forwards. That is ADR-0010's lesson applied to a second rule: two
  readings of one rule, implemented separately, disagree on exactly the case the
  rule exists for.

### Change placement: the split, as rows

One structure at a time, because the rules — the allocations total 100, exactly one row is main —
are statements about one employee on one structure. A screen showing every structure at once would
be several independent forms wearing one Save button.

Each row is a unit, a share and a radio. **A radio group, not a checkbox per row**: "exactly one"
is what a radio group means, and a set of checkboxes is a set of independent answers that lets a
reader build a split the engine must then refuse. Adding and removing a row are round trips rather
than script, like every other form here, and the round trip also redraws each row's unit picker
against the current structure and date — which a cloned row could not do.

`PlacementSplit` holds the rules the screen applies before anything is written, as a pure function
with no session, no localiser and no clock in it, so every case is a unit test. It is not a second
copy of the engine's rules: `ValidateAssignmentAsync` remains the authority and runs again on the
write. What it adds is the two things a validator working on a resolved split cannot do — say
*which row* is wrong, so the message lands under the control, and catch an empty row before it
reaches a service that would report it as an unknown record id.

**It refuses a date earlier than a change already on record, and that is its own rule.**
`ReallocateAsync` states the whole picture from its effective date onwards, so a split backdated
behind a later change would replace that change rather than slot in behind it — the "overwrite"
behaviour ADR-0005's addendum examined and rejected for moves, reached through a different door.
ADR-0005 chose *split* for a move because a link carries one parent at a time and the arithmetic is
unambiguous; a set of rows that must total 100 on every date has no honest equivalent, so this
screen takes ADR-0005's third option and refuses, naming the date it will accept.

### The unit picker

`workmate-unit-picker`, in `WorkMate.Dimensions` beside the structures it reads, used by **Place**
and by every row of **Change placement**. It is the employee picker's combobox —
`workmate-picker.js` in `WorkMate.Platform`, shared by both — with this module's two questions on
it: where it searches, and what an option reads as.

**Every option carries its full path**, "Crescent › Retail Banking › Credit". The Crescent reference
tenant has two departments called Credit and two called Operations on one structure; a list of bare
names offers a reader identical rows and no way to choose correctly, and choosing wrong puts
somebody in the wrong branch of the bank — a mistake nothing downstream would flag, because both
answers are valid units.

An optional **Within** filter narrows the search to a container's subtree through the closure index.
It is rendered hidden and unhidden by the script, because without the script the `<select>` already
holds its units and there is no round trip behind it: a control that visibly does nothing is worse
than one that is not there.

- **Placing somebody from a unit's card.** The designer's ⋯ menu carries **Place an employee
  here…** beside **Appoint a head…**, declared in the same `DesignerCardActions` entry list and
  gated on the same `AssignEmployees`. It opens this screen with the structure and unit already
  chosen and asks *who* instead. It appears only on a unit the structure places employees on — which
  is a property of the unit rather than of the viewer, so the node carries the answer and the link
  declares that it needs it (`data-designer-action-requires`), which is what keeps a card the
  browser builds from offering what a card the server renders does not.

- **Appointing a unit head** is on the designer's own card, in
  `WorkMate.Dimensions` — the question a person is asking is "who runs this
  department", not "what does this person run". The placement screen lists what
  somebody leads and says where to change it.

### The employee list, filtered to a unit

The same screen, with `structureId`, `recordId` and `asAt` on the query string — which is what a
designer card's employee count and its ⋯ menu's **View employees** link to. One action rather than
a second screen, because the search, the status filter, the paging and the row actions are all
wanted either way and a second copy would drift.

What the filter adds: a header naming the unit, the structure and the date; an **Include sub-units**
checkbox, **off by default** because the card is the way in and the card's number counts the node
itself — opening on a different number from the one just clicked would answer a question nobody
asked; and three columns, **Placement** (*Main* or *Additional*), **Allocation %**, and a ★ against
the head, whose tooltip names every unit that person heads rather than saying "this unit".

**Nothing on a row says "home: …", and nothing should.** It used to, under the role, and it
repeated the unit the row already named: the placement column beside it is resolved on *the
structure the list is filtered to*, which is where the main placement is, so the caption was by
construction the cell one to its left. Ahmed Khan's row on Site Team A read
"Civil · Secondary · 40% · home: Civil". Where else somebody sits is now one question answered in
one place — the **+N more** disclosure in the placement column, with full paths and shares.

**The head is listed and not counted.** ADR-0012: a head need not work in the unit they head, so an
acting head borrowed from another department appears, marked *Head (not a member)* and carrying no
allocation — and is excluded from the figure, because the unit they do work in is already counting
them. The screen says so rather than leaving a reader to work out why the list is one longer than
the number above it.

**The figure is the card's figure**, from the same `IEmployeeAssignmentService` call, never from the
length of the list — which is a page and is narrowed by any search. Two numbers from two sources is
how a chart and a list come to disagree about the size of a department.

Who is in the unit comes from the dimension engine and narrows the list through
`IEmployeeService.ListAsync`'s `restrictedTo`: membership is an assignment row and deliberately not
a column here, so the engine answers *who* and this module answers *which of them match*.

### The employee picker, which is two things

| | What it is | Where |
| --- | --- | --- |
| **The picker field** | An Orchard `ContentPickerField` restricted to `Employee`, with its candidate set served by `EmployeePickerResultProvider` and filtered through `IVisibilityService`. Searches rather than lists, so it scales. What session B's form designer generates. | `EmployeePickerResultProvider` |
| **The picker control** | `<workmate-employee-picker>`, for the plain admin forms that are not content items — setting a unit's head, naming a line manager. A server-rendered `<select>` that the browser upgrades in place into a searching combobox. | `EmployeeDirectory`, behind `WorkMate.Platform`'s tag helper |

**The control searches the tenant, and that is the whole of it.** It was a `<select>` of the first
hundred employees of any status. On the Zenith demo organisation that is a hundred of a hundred and
fifty-eight: fifty-eight people could not be named as a head, as a line manager or as anything
else, from any screen, and nothing said so. Filtering a list the browser already holds has the same
hole — it can only narrow what was sent. So the filtering happens on the server, through
`EmployeePickerAdminController.Search`, and the browser holds one page at a time.

The `<select>` stays and is still the form control: the script hides it and sets its value, so the
posted value, the model binding and the validation are identical whether or not the script ran, and
what is lost without it is only the ability to reach somebody past the first page. The combobox
follows the WAI-ARIA pattern — arrow keys, Enter, Escape, `aria-activedescendant` — because a
picker that needs a mouse is a picker some people cannot use.

Each option carries **name, code and where they sit**, because two of the three are routinely not
enough to tell two people apart. Where they sit comes from `EmployeeUnitNames`, which is also what
fills the list's "Where they sit" column — one answer to one question, after the two disagreed and
the same person read "Mechanical" on the picker and "Not placed" on the list.

**The selection is a fact the control holds, not something inferred from the text in the box.** That
distinction is the whole of a defect worth remembering: the box shows a chosen person's *full label*
— name, code and unit — and the code that re-read it as a search term found nobody and announced
"No match" at a reader who had just chosen successfully. The posted value survived, which made it
worse rather than better: the control said the selection had failed while quietly holding a correct
one. Its mirror image had the same cause and is the dangerous one — typing over a selection left the
old id in the `<select>`, so the form would post somebody the reader had visibly moved away from.

So: choosing records the selection, editing the text abandons it, re-focusing searches for nothing
rather than for the label, and submitting text that names nobody is refused on the field with a
message of its own. The message is a `.invalid-feedback` element rather than the status line,
because the status line is a running commentary on the search and a result already in flight will
overwrite anything written there.

Each form says which people it wants. Appointing a head asks for everybody in the tenant regardless
of placement — ADR-0012, a head need not be a member — and excludes leavers, because the service
refuses an appointment starting after somebody's last day and offering a choice that cannot be made
is worse than not offering it. The form says so, since a picker that silently withholds a person
reads as a missing record rather than as a rule.

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

Two categories.

Category `Forms`, two mandatory events. `LookupListChanged` carries the whole list
before and after and the operation that changed it. `LookupValuesRemapped` is the
one operation here that changes **records** rather than configuration, and cannot
be undone by repeating it: one entry for the whole remap, naming the list, the old
value, the new code, how many rows changed and the first few employees — because
four hundred `EmployeeChanged` entries saying the same thing would bury both the
decision and everything around it.

**A second category rather than more events in the `Employee` one**, because
changing a lookup list changes what every record in the tenant may say, while
changing an employee changes one —
and an auditor asking "who retired the grade every report groups by" should not
have to read a thousand employee events to find it. Slice B3's form definitions
file here too, which is why the category is named for what will live in it rather
than for what is in it today.

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
| `ManageLookupLists` — create and change the lists a form field offers | Platform/Tenant admin, HR admin |

**Lookup lists are configuration, not data, which is why they are not part of
`ManageEmployees`.** Changing a list changes what every record in the tenant may
say; changing an employee changes one. A customer routinely wants HR officers who
keep records current without being able to redefine the grades those records are
classified by. It is not security-critical: retiring a grade is disruptive and
recoverable, because the entry and every record naming it survive.

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

Two, in `Recipes/`, and the order between them matters: `lookup-lists` is applied
before anything that names a value from one, and `employees` before the dimension
engine's `employee-assignments` and `unit-heads`.

### `lookup-lists`

Lists by code, with their entries. Re-running is safe per ADR-0008 and its
**lookup-list addendum**, which gives five outcomes rather than three:

| Tenant holds | Recipe states | Outcome |
| --- | --- | --- |
| nothing | the list | create |
| the same entry, same names, active state and order | the entry | skip |
| **no such entry** | the entry | **add it**, through the service, audited |
| an entry whose names, active state or order differ | the entry | **fail**, naming the difference |
| entries the recipe does not name | — | left alone |

Both asymmetries are worth knowing because neither is obvious. **An entry the
tenant holds and the file does not mention is not a difference** — comparison is
scoped to what a row states, and a list a customer has extended is exactly that
case. **An entry the file names and the tenant lacks is an absence rather than a
divergence**, because ADR-0014 guarantees an entry is only ever retired, so one
that is not there has never been there: there is no decision to overwrite. That
is what makes a revised pack appliable to a tenant holding the old one.

A fill goes through `ILookupListService.UpdateAsync`, so it is validated and
audited exactly as an administrator's edit would be, and is reported in the run
log — a recipe that changes a tenant without saying so is one an operator cannot
review.

`isActive` and `sortOrder` are nullable in the step model so that "absent" and
"stated" are different things. An absent `isActive` means offered; an absent
`sortOrder` means the order the file lists them in, which is the ruling ADR-0008's
sort-order addendum already made for dimension records.

### `employees`

People by code, which is what
`WorkMate.Dimensions`' `employee-assignments` and `unit-heads` resolve their
references against — so a recipe lists this step before either of them.

**The status is reached, not written.** `CreateAsync` always produces a
prospective employee whatever the join date, and the step then walks each one to
the status the row states through the same dated transitions a person uses. An
imported active employee is two operations because becoming active is an event;
a step that assigned the status directly would create records no sequence of real
decisions could have produced, and the lifecycle handlers leave and attendance
subscribe to would never fire. On leave and suspended are three, because both
describe an employment that is already running.

Re-running is safe per ADR-0008: a row naming a code that exists is compared
against the tenant on the fields the row actually states, and skipped if they all
match. Everything is validated across the whole step before anything is written,
so a file with a typo in its last row leaves the tenant as it found it. The one
exception is a line manager, who may be named before they are listed — managers
are attached in a second pass once every employee exists, which also means a file
may list people in any order.

### Export (ADR-0011)

`Deployment/` holds the other half: `RecordsDeploymentStep`, under
**Tools → Deployments**, with two switches writing the same steps the importers
read. Leavers are exported — somebody who left in March is part of what this
tenant is, and payroll and gratuity still answer questions about them.

**`IncludeLookupLists` is on by default and `IncludeEmployees` is too, which is a
different judgement from the dimension engine's.** A lookup list is
configuration: the kind of thing that travels from a configuration tenant to a
production one as a matter of course. That is the distinction the A3 amendment
drew when it turned `IncludeAssignments` and `IncludeHeads` off by default
because they are operational data about real people. A grade list is not about
anybody.

The export **states every field on every entry**, including `isActive` and
`sortOrder` when they hold the value an importer would have defaulted to, because
an export reproduces a tenant rather than expressing an intent — ADR-0008's
sort-order addendum settled the same question for dimension records. Arabic is
the exception and goes the other way: it is optional since ADR-0003's addendum,
so an absent half is absent from the file rather than present and empty.
`LookupListsExportRoundTripTenantTests` applies its own export twice, which is
what catches a field written in a shape the comparison would read as different.

What it cannot carry is a status *history*. An employee's past statuses are in
the audit trail rather than in a dated table, so what the record holds, and
therefore all this can carry, is where somebody is now and the date they reached
it. That is a difference between the two models rather than between the two
exports: `WorkMate.Dimensions` carries full dated histories because its data is
dated. `PeopleExportRoundTripTenantTests` covers both modules' halves together,
because the join between them is a code resolved at import.

## Decisions recorded

- **ADR-0014** — a managed lookup list is a document of ours, not an Orchard
  taxonomy. Departs from specification section 5's field table, so it is accepted
  **pending second review**; read it before touching anything in the lookup list
  layer.
- **ADR-0008's lookup-list addendum** — a missing entry is a gap the recipe fills,
  not a difference it refuses. Also **pending second review**, because it widens
  the original decision a second time.
- **ADR-0003 and its addendum** — the bilingual field lives in
  `WorkMate.Platform`; English is required and Arabic is optional unless the
  tenant turns `RequireArabicNames` on. Asked here through `IBilingualNamePolicy`,
  the same one-question seam every write path on the platform uses, so the
  employee record and the organisation chart cannot disagree about the answer.
- **ADR-0006** — write paths read content definitions with `Load…`, never `Get…`.
  Read it before writing session B's form designer, which creates content types at
  runtime the same way this module's migration does.
- **ADR-0009** — migrations are append-only.
- **ADR-0030 (D30)** — an employee joins the organisation only through a position.
  On an axis that has a Position level a unit is no longer somewhere a person
  goes, and their department, division and company are *derived* from where their
  position sits rather than chosen. This module reads that one way, through
  `IEmployeeOrganisationService`, so the Create form, the employee page, the
  Placement screen and the Change-position form cannot compose it four slightly
  different ways. An axis with no positions is untouched.
- **ADR-0029** — the F6 migration moves nobody. Every placement already written at
  a unit stays valid and keeps counting; the rule is on the write. A tenant
  therefore holds both shapes of row at once, on purpose, and the employee page
  says in a sentence which state a person is in rather than showing a blank block.
- **D31** — the line manager follows the position. It lives inside the organisation block, after
  Position, Grade and *Reports to*, and is derived from the holders of the post above: one holder
  is shown, several are offered and only they are, a vacant or top post has none. It narrows D28 A,
  which left the field free and optional, and keeps D28 A's refusal to pick between several
  holders. A labelled override — *"Differs from the position's reporting line"* — covers a dotted
  line or a matrix. Storage is unchanged: `LineManagerEmployeeId`, which is **undated**, so an
  override has no history. Dating it is a model change and an ADR, not something this slice
  invented.
- **D31 addendum** — a vacant post above is not "answers to nobody". *Reports to* and *Line
  manager* are ONE row carrying the post, its state and whoever answers for it; a vacant post falls
  back to the nearest held post up the line, labelled **acting**, and only a chain of empty posts
  all the way to the top leaves nobody ("No one above holds a post yet"). Derived on every render,
  stored nowhere new.
- **D31 addendum 2** — a derived line manager is never written down. The two addenda above derived
  the answer and then saved it, so when the post above changed hands, or a vacant post above was
  filled, the record kept the old person for ever. Now only a human's answer is stored — a pick
  among several holders, a pick among several *acting* holders, or an override — and one holder or
  one acting holder stores nothing at all. Every screen reads
  `IEmployeeOrganisationService.GetLineManagerAsync`, which returns who, on what footing
  (`Derived`, `Acting`, `Chosen`, `ChosenActing`, `Override`, `NeedsChoice`, `Stored`, `None`) and
  why. A choice whose person has left the post reads **"Needs a new line manager — &lt;name&gt; no
  longer holds &lt;position&gt;"** and asks again rather than falling back silently; an acting choice
  is dropped once the post above is filled; an override stands through all of it. The one new
  stored value is `EmployeePart.LineManagerKind` — `None`, `Chosen`, `ChosenActing` or `Override` —
  which costs no migration because the part is JSON inside the content item, and is deliberately
  not on `EmployeeIndex`. It exists because all three stored kinds read as *not a holder of the
  post above* once the chart moves, and the product must treat them three different ways. An
  employee who holds
  no position is untouched and carries no label. `Migrations.UpdateFrom5Async`, over
  `ILineManagerBackfill`, clears what the old code wrote — see **Migrations** below.
- **D31 addendum 2, polish 3** — the *Reports to* row names the person **once**. With one holder of
  the post above the row already reads *"Director Technology — Ali Tahir (line manager)"*: the post,
  and the person who answers for it, which is the whole fact. `_LineManagerChoice` printed the name
  again underneath, so one person looked like two and the screen read as though it had derived the
  same answer twice. The one-holder case now renders no visible text — only the hidden field, which
  was always the actual control. The cases that name somebody the row does **not** name still speak:
  an **acting** manager ("— acting (Director Technology)") and an **override**, which gained its own
  `LineManagerMode` instead of borrowing `Derived` so the partial can print one and stay silent
  about the other. `WithOneHolderThePersonIsNamedExactlyOnceInTheReportsToRow` counts the name on
  all four screens.
- **ADR-0026** — the employee's grade is a column on the assignment row, not a
  second assignment on a `grades` axis, because an assignment there would roll
  employees up under grades in the closure index — which D26 C says must not
  happen. The cost it accepts is that an increment closes and reopens the position
  row, which is why *Change grade* is its own dated screen.
- **ADR-0019** — a cache invalidation is signalled again after the write commits.
  This module's lookup list cache is what found it: `ISignal.SignalTokenAsync` on
  its own fires while the write is still staged on the session, so a read that
  overlaps the write caches the pre-write state under a token nothing will signal
  again — and the lists loaded by the standard-lists recipe went on rendering as
  free-text boxes until the application was restarted. Writes here now go through
  `CacheSignals.SignalNowAndAfterCommitAsync`, and
  `CacheInvalidationSignalTests` in this module's test project fails the build if
  anything calls `SignalTokenAsync` directly. The same ADR records the second line
  taken here and nowhere else: `CachedLookupListLookup` never caches an absence,
  because a stale "there is no such list" looks exactly like the correct behaviour
  for a tenant that has not loaded them.
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

## Migrations

Append-only, per CLAUDE.md rule 6 and ADR-0009. The body of a shipped step is never edited.

| Step | What it does |
| --- | --- |
| `CreateAsync` | `EmployeeIndex`, `EmployeePart`, the six section types, the `Employee` type, the standard lookup bindings |
| `UpdateFrom1Async` | fills `DisplayText` on employees created before `EmployeeHandler` set it |
| `UpdateFrom2Async` | takes `Employee` off Orchard's generic "new content item" list |
| `UpdateFrom3Async` | creates `LookupListIndex` for a tenant from before B1 |
| `UpdateFrom4Async` | binds the standard section fields to the lookup lists |
| `UpdateFrom5Async` | **the line manager backfill** — D31 addendum 2 |
| `UpdateFrom6Async` | **D22's retirement** — points `JobTitle` and `GradeCode` at the retired editor and marks the `job-title` and `grade` lists superseded |
| `UpdateFrom7Async` | **the form designer's two tables** — `FormDefinitionIndex` and `FormVersionIndex`. Slice B3, D33. Alters no content definition: publishing is the only thing that may |

### The line manager backfill — D31 addendum 2

`UpdateFrom5Async` resolves `ILineManagerBackfill` and logs what it did. **It moves nobody**
(ADR-0029) and touches exactly one field, `LineManagerEmployeeId`: no placement, no assignment, no
date, no other property.

For each employee who has a stored line manager at all — the index answers that without loading a
content item, so a tenant where nobody has one does no work:

| Outcome | Rule |
| --- | --- |
| **cleared** | holds a position, and the stored person is the **sole** holder of the post above (or of the acting post above a vacant one). A written-down derivation. The answer shown does not change; it starts being recomputed |
| **kept as chosen** | holds a position, and the stored person is **one of several** holders |
| **kept as override** | holds a position, and the stored person is **not among** the holders — including a post at the top of the line, where there is no list — or whose `LineManagerKind` already says `Override` |
| **untouched** | holds **no position**. Every pre-F6 record and every no-positions tenant |

The four counts go to the log at `EventId` 3100. The step is safe to re-enter: it writes nothing
where the comparison does not match.

**It deliberately does not use `GetLineManagerAsync` to decide what to clear.** That read looks at
the stored value first, so the sole holder of the post above comes back as `Chosen` rather than
`Derived` — from the outside a frozen derivation and a deliberate pick of the only candidate are
the same two bytes. Asking it what to clear would clear nothing, which is exactly the population
the step exists for. `LineManagerBackfill` reads the holders straight off the position instead.

**Ordering is safe**: it reads the dimension graph, and `WorkMate.Records` declares
`WorkMate.Dimensions` in its manifest dependencies, so that feature's migrations have run and its
tables exist before this step is entered.

### D22's retirement — slice F6c

`JobTitle` and `GradeCode` are **retired, not removed**: same type, same stored value, same display
name, and on `GradeCode` the same binding to the `grade` list — which is how a code typed last year
still resolves to a name. What changed is the editor. `ContentPartFieldSettings.Editor` becomes
`RetiredFieldEditor.Name`, Orchard resolves the shape alternate, and this module supplies
`TextField-WorkMateRetired.Edit.cshtml` and `BilingualTextField-WorkMateRetired.Edit.cshtml`. **No
field driver is registered**, for the reason `LookupFieldEditor` records at length.

**The hidden input in those views is load-bearing.** Orchard's field drivers bind their editor's
model and write the result straight back, so an editor that rendered nothing would post nothing, the
driver would store the empty string, and the second save of a Job details row would erase the
history the first one displayed.

**A tenant with no positions still types them** — D22 step 3. The retired editor asks the tenant's
own axes and renders an ordinary box when there are none, so there is no second definition and no
setting anybody can get wrong.

The two lists are retired **as they are created** (`ILookupListService.CreateAsync` consults
`StandardLookupLists.Superseded`), because the `lookup-lists` recipe step runs after this module's
migrations — a migration alone would catch only tenants that already had them.
`EmployeeFieldNames.RetiredOnSections` then reserves both names for **new** definitions, with the
shipped `EmployeeJobDetail` part exempt; `EmployeeContentDefinitionGuard` asks the part as well as
the name.

### The employees list — D32, slice F6c

Code, Name, Position, Grade, Reports to, Organisation, Status. The four new columns are derived on
read through `IEmployeeOrganisationService.GetLinesAsync`, which resolves a page in a number of
queries bounded by the **distinct positions** on it, never by the rows. It shares one body with
`GetLineManagerAsync` for the staleness rules, so the list and the employee's own page cannot
disagree about who manages somebody.

Filters by position and by unit, intersected. Sorts by name (index-backed), and by position or grade
over the matching set up to a cap — see D32 for why no column on `EmployeeIndex` can do it and why
the list says so rather than sorting a subset. A tenant with no positions sees none of it.

### The form designer's model and compiler — D33, slice B3

**No UI in this slice**, deliberately: the model is the part most expensive to get wrong and least
useful to build against an unsettled screen. B4 builds the screens; B6 builds the recipe step.

**The definition is the source of truth and the content type is the output.** `PublishAsync`
rebuilds the content definition from `FormDefinitionDocument` every time, so **a hand edit made on
Orchard's own content-type editor survives until the next publish and then loses.** That is the
right direction and it is said here so nobody is surprised by it — Orchard's type editor cannot be
taken away, and the mitigation is the direction of truth rather than a lock.

A definition targets a **standard section**, a **new section** (a new item type plus a bag on
`Employee`, shaped exactly like the six shipped ones), or the **record itself** through
`EmployeeProfilePart`. Every field goes through `EmployeeContentDefinitionGuard`, and the target
goes through its new `CheckTarget`.

**Publishing is additive, enforced by there being no code that deletes.** No field is taken off a
part, no part off `Employee`, no value touched. A field removed from a definition stops being
rendered and keeps its data. `RemovingAFieldFromADefinitionKeepsTheFieldAndItsValue` pins it.

**A version is written per real publish.** Publishing an unchanged definition is a success that
writes nothing — ADR-0008, so a recipe applied twice does not fail on the second pass.

**ADR-0006 needed a second half here**, and D33 records it: the ADR's null check catches a type
*created* in this scope, and a type *altered* in this scope fails more quietly — the cached read
returns the old definition, `NewAsync` welds the old parts, and values for the new section vanish
with nothing thrown. `ContentDefinitionChangesInThisScope` is the signal the handler repairs on.

## Known limitations

### Which edition each form capability belongs to is still undecided

`Capabilities.FormDesigner` and `FormDefinitionVersioning` are declared and asked through
`IEntitlementService` on every write, and `AllowAllEntitlementService` answers yes to everything
until prompt 8/9. **Nothing is hidden by edition.** D33 (Q7) leaves the mapping open on purpose:
guessing it here would turn a commercial decision into a code one taken by accident.

### Ordering by position or grade needs a filter on a very large tenant

Above 2,000 matching employees the list keeps its name order and says so. `EmployeeIndex` cannot
carry a position or grade column: both change without the employee's content item being touched, so
the column would be a cache nothing invalidates. D32 records the argument; the honest fix is a
reporting read model, which is a later slice.

### A pre-addendum override reads as "needs a new line manager"

Data written before D31 addendum 2 carries no `LineManagerKind`, because the property did not
exist, so it deserialises as `None`. The backfill **keeps** such a value — it is never cleared —
but the screen afterwards reads
*"Needs a new line manager — &lt;name&gt; no longer holds &lt;position&gt;"* rather than *"Differs from
the position's reporting line."*

The migration does not set the bit, because it cannot know: from off-the-line data alone a dotted
line and a stale pick are indistinguishable. Asking a human is the only answer that is not a guess,
and is the same answer D28 A gives everywhere else. Confirming the person through *Change…* records
the override properly and the message goes.

### Which edition each capability belongs to is not decided

`Capabilities.cs` declares `FormFieldConfiguration`, `FormDesigner` and
`FormDefinitionVersioning`, and `LookupListService` asks
`IEntitlementService.IsAllowedAsync` before every write. **Nothing is refused**,
on two counts. There is nothing to ask — `WorkMate.Entitlement` is a skeleton and
the signed entitlement record arrives in prompt 8, so the platform's
`AllowAllEntitlementService` answers yes. And the product specification's
packaging table ("Forms | Standard forms, field configuration | Full form
designer | Form designer plus definition versioning") has not been confirmed as
the mapping, so guessing it here would turn a commercial decision into a code one
taken by accident.

The constants and the call sites are in place so that settling the question is a
change to one implementation rather than a hunt through every service.

### `JobTitle` is not bound to the `job-title` list

The list ships; nothing offers it. `JobTitle` on the job details section is a
`BilingualTextField`, not a text field, so a control binding it would have to
post **two** values — English and Arabic — from one choice. That cannot be done
without script, and the no-script alternative is to post a code and resolve it
back to a pair of names in a display driver of our own, which means registering a
driver for `BilingualTextField` and narrowing `WorkMate.Platform`'s existing one
so the two do not both render the field. That is a real option, and it is a
different decision from the one this slice took.

It is also bound up with the undecided positions model: a code-keyed job title is
a position by another name, and introducing one as a side-effect of a lookup list
would settle that question by accident. So the list ships — a tenant wants it
either way, and a later slice can bind it once the shape of a position is known.

### The demo tenants have no section data to reconcile

Worth recording because it is the reason binding was cheap: the `employees`
recipe step carries only the employee **core**, so no demo employee on Zenith or
Crescent has a job detail, a document, a bank account, a qualification, a
dependant or a contact. The only shipped field with values to reconcile is the
core nationality, and the demo generator does not emit one.

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
