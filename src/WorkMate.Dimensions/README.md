# WorkMate.Dimensions

The dimension engine: the foundation layer every other WorkMate module resolves
structure against. Designed in `/docs/dimension-engine-architecture.md` and
contracted in section 4 of `/docs/technical-specification.md`.

## Owns

### Configuration layer — built
- **`DimensionTypeDocument`** — a kind of unit (Business Unit, Department,
  Cost Centre, anything a customer invents): code, bilingual name,
  system-defined flag, self-nesting rule, attribute schema, and the name of the
  content type generated for it. Indexed by `DimensionTypeIndex`.
- **`StructureDocument`** — a named axis with ordered levels: `AllowSkipLevel`,
  `IsStrict`, `IsPrimaryOrganisation`. Indexed by `StructureIndex`.
- **`IDimensionTypeService`** — creates, updates and retires types, and owns the
  backing content type. `CreateAsync` generates a content type named for the
  code with `DimensionRecordPart` attached, `TitlePart` generating its title
  from the English name, the attribute schema's fields on a part named for the
  type, and the type creatable, listable, securable and not draftable.
- **`IStructureService`** — defines axes, their ordered levels and their rules.
- **`IDimensionTypeLookup` / `IStructureLookup` / `IDimensionRecordLookup`** —
  the read side of each aggregate: does this reference exist, and what does it
  look like. See **"The lookup pattern"** below.
- **`EffectiveDates`** — the one place a calendar date crosses into an index
  column and back. See ADR-0005.
- **`Permissions`** — the seven in specification section 4, checked in the
  services rather than only in a controller.
- **`IDimensionAuthorisation`** — the one place this module decides whether a
  caller may act, and what "today" is. A caller with no authenticated user is
  refused unless it has entered `WorkMate.Platform`'s `ISystemOperation` scope,
  which recipe steps and background tasks do deliberately.
- **`ISession.SaveCheckedAsync`** — the only way this module writes a document
  under optimistic concurrency control. `ConcurrencyCheckedSaveTests` fails the
  build if any other code path saves one of those types directly.

### Record layer — built
- **`DimensionRecordPart`** — the eleven standard fields every generated
  dimension content type carries. It has **no parent and no structure field**,
  deliberately: a record sits on several axes at once, and a single parent
  field would collapse them into one tree. `DimensionRecordPartTests` asserts
  that no such field appears.
- **`DimensionRecordPartIndex`** — code lookup, type filtering and dated
  resolution without loading the content item.
- **`DimensionRecordPartHandler` / `DimensionRecordHandler`** — the chokepoint.
  The generated types are creatable, so the admin screens, the API, GraphQL, a
  recipe and an import can all produce a record; these two are the only point
  all five share. They stamp the dimension type on from the content type, and
  reject a record with a missing or malformed code, a name in only one
  language, a missing effective date, or an end before its start.

  The split between them is not arbitrary and is explained in full on
  `DimensionRecordHandler`: a *part* handler's validation context carries its
  own result object that the caller never sees, so a rule written there
  compiles, runs and silently rejects nothing. The rules live on the part
  handler; the item handler supplies the context that works, and repairs the
  parts Orchard could not weld (ADR-0006).
- **`DimensionRecordPartDisplayDriver`** and its views — edit and display. The
  editor offers no parent picker, for the reason above; placement is an
  explicit dated operation needing `MoveDimensionRecords`.
- **`DimensionNameDocument` / `DimensionNameIndex` / `DimensionNameHistory`** —
  the dated name history behind architecture section 5's two renames. The
  storage and the rules are here; the operations that drive them land on
  `IDimensionService` with the graph layer.

A record created outside the designer is **unplaced**: it exists with no parent
on any structure. That is a legitimate state, not an error, and it carries no
flag — unplaced is derivable as *no link on a structure whose levels include
its type, and its type is not at ordinal zero*. The organisation designer lists
those in prompt 3.

### Graph layer — built
The three tables live in the internal namespace `Internal.Graph`, so nothing
outside this module can reach them: the services are the only entry, which is
what makes the storage replaceable and the tenant boundary testable in one
place. Storage is decided in ADR-0005.

- **`DimensionLinkDocument` / `DimensionLinkIndex`** — one dated parent edge per
  row, one document per structure and record.
- **`DimensionClosureDocument` / `DimensionClosureIndex`** — one dated
  ancestor-descendant pair per row, including the self pair at depth zero. A
  row's effective range is the **intersection** of the ranges of every link
  along the path, which is what makes a historical descendant query one indexed
  read rather than a walk of the link table.
- **`EmployeeAssignmentDocument` / `EmployeeAssignmentIndex`** — dated
  placements with allocation and primary flag, one document per employee and
  structure, which is exactly the boundary the assignment rules are about.
- **`IDimensionGraphService`** — ancestors, descendants, children, is-under,
  depth; link and closure maintenance; rebuild; verify with a divergence
  report. Cycles are refused across **every** date, not only today.
- **`IDimensionService`** — create, update, retire, move, merge, cancel a move;
  the two renames; resolve by id and by code. Merge and cancelling a move both
  compute their plan once and `apply` only decides whether the writes follow,
  so the dry run cannot drift from what happens.
- **`IEmployeeAssignmentService`** — place, end, reallocate; effective
  assignment on a date; employees under a node, paged.

Two things worth knowing before changing any of it:

- **Self pairs follow the structure, not only the record.** They are written
  when a record is created, when a structure is created whose levels already
  have records, and when a level is added. Removing a level **closes** the rows
  rather than deleting them, so prior-period reporting still resolves.
- **The subtree query uses a correlated sub-select**, not a list of descendant
  ids. At the 5,000-record scale the acceptance criterion names, an id list
  exceeds SQL Server's 2,100-parameter limit — it would pass on SQLite and
  throw on a customer's database. `EmployeeAssignmentTenantTests` holds that
  with more than 2,100 employees.

**Cancelling a move is not a delete.** A move is identified by the structure,
the record and the date it was effective from — `DimensionLinkDocument.Parents`
holds exactly one `ParentLink` starting then. Cancelling removes that link and
extends whatever link it had displaced back to the date it used to end at,
which `InsertLink` recorded on the displaced link's replacement the moment the
move was made — so the previous placement needs no history kept elsewhere to
be restored exactly. A move with nothing before it (the record's first-ever
placement on that axis) restores to no parent at all: a root, exactly as it
was. The restored placement is checked against **today's** rules through
`IDimensionValidator.ValidatePlacementAsync`, not the rules as they stood on
the original date, because a dimension type's or a structure's configuration
carries no history of its own past state — the type may have stopped allowing
self-nesting since, or the structure's levels may have changed. A blocking
violation refuses the cancellation and leaves the timeline untouched.
`IDimensionService.CancelMoveAsync` requires a reason and records it, with the
acting user, against `MoveCancelled` in the audit trail — this operation is
never a silent delete — and `PlanCancelMoveAsync` previews it exactly, the same
dry-run-then-apply shape as merge.

### Validation — built
**`IDimensionValidator` is the single validation service.** Every write path
calls it, so the designer, the API and every import path get the same answer
from the same code. Architecture section 6: the designer may show a violation
before the user commits, but the service is the authority and re-checks on
write.

Every rule in architecture section 6 is now enforced. Five were not enforced
anywhere before this session: **record code uniqueness** (including against
retired records), **permitted level**, **level skipping**, **self-nesting**,
and the advisory **parent not yet effective** — advisory because pre-building
next year's structure is legitimate and refusing it would make the engine
unusable for a case it was designed for.

**`BeginBatch()` is what makes an import correct.** Two rows of one file
sharing a code are each individually fine and together are not, and nothing in
the database can see the clash because neither row is committed. The batch
remembers what it has already seen. Architecture section 6 requires an import
to validate row by row and report every failure rather than stopping at the
first; this is the same bookkeeping.

**Deletion** returns one of three outcomes with its blockers named —
`Clean`, `RetireOnly`, `Blocked` — never a generic failure. Today it can see
employee assignments (live and historical) and child records. Payroll,
workflows and approval scopes are modules that do not exist yet; each will
implement **`IDimensionDeletionBlockerProvider`** and register it, rather than
this check quietly claiming to cover more than it does.

### The lookup pattern

Three services are, deliberately, nothing but reads: **`IDimensionTypeLookup`**,
**`IStructureLookup`** and **`IDimensionRecordLookup`**. Each answers only "does
this reference exist, and what does it look like" for its aggregate, each is
implemented by a small internal class in `Internal/Lookups` whose only
dependency is `ISession`, and each full write service (`DimensionTypeService`,
`StructureService`, `DimensionService`) delegates its own read methods to the
matching lookup rather than querying the session a second time.

This is what keeps the module free of dependency cycles as it grows, and it is
worth following in later modules rather than reaching for `Lazy<T>` again:

- **The problem the pattern solves.** `IDimensionValidator` has to check that a
  reference is real — a dimension type exists, a structure exists, a record
  exists — so every write service depends on the validator to validate a write.
  If the validator depended on those same write services to check references,
  resolving any one of them would mean resolving the validator, which would
  mean resolving it again: a cycle. The same shape appeared between
  `StructureService` (which tells the graph when an axis's levels change) and
  `DimensionGraphService` (which used to read structure configuration through
  `IStructureService`, the very thing that depends on it).
- **Why `Lazy<T>` was tried first, and why it was replaced.** Wrapping one side
  of a cycle in `Lazy<T>` defers resolution past the moment the circular
  constructor chain would fail, and it worked — but it is a workaround for the
  cycle, not a removal of it, and every one of the four `Lazy<T>` injections
  this module briefly carried existed only because something depended on a
  full write service for reads it never needed.
- **The fix.** Each aggregate's write service keeps its lookup interface
  separate from its own full interface, and the implementation of that lookup
  has no dependency on anything that could depend on it — not the validator,
  not another aggregate's service, not even its own write service. A consumer
  that only needs to ask "does this exist" takes the lookup; a consumer that
  needs to create, change or retire takes the full service. `IDimensionValidator`
  and `IDimensionGraphService` both take lookups exclusively. No `Lazy<T>`
  remains anywhere in this module.
- **When to reach for this.** If a new service needs to read another
  aggregate's data and that aggregate's write service depends — even
  indirectly — on something the new service will itself be depended on by,
  give it a lookup rather than the full service. If you are not sure whether
  that applies, resolving every service from a real DI container (as the
  integration suite already does for this module) settles it immediately:
  .NET's container throws on a genuine cycle rather than silently accepting
  one.

### Caching — built

**Dimension types and structures are cached; the graph and records are not.** Both are read
constantly — every placement validated, every move, every screen that lists levels — and written
rarely: a customer defines a handful of dimension types and structures once and barely touches
them again. Dated graph queries (ancestors, descendants, employees under a node) are the opposite
shape — one of the acceptance criteria is 5,000 records deep — and are deliberately left uncached
until a performance test run against the seed generator shows a specific query needs it. Caching
them speculatively now would be guessing at a cost nobody has measured yet, against a correctness
risk (an under-invalidated dated query returning yesterday's shape) that is real and worth avoiding
until there is a number to justify taking it on.

- **`CachedDimensionTypeLookup`** and **`CachedStructureLookup`**, in `Internal/Lookups`, are the
  only cached implementations — decorators in front of the plain, uncached `DimensionTypeLookup`
  and `StructureLookup`, registered as `IDimensionTypeLookup` / `IStructureLookup` in their place.
  Everything that resolves the interface — the validator, the graph service, both write services'
  own read methods — gets caching for free, with no change at any of those call sites.
- **Keys carry the tenant.** `IMemoryCache` in this host is a singleton shared across every tenant
  in the process — Orchard Core does not re-register it per shell — so every key
  `DimensionCacheKeys` builds is `WorkMate.Dimensions:{tenant}:...`. Two tenants whose dimension
  types happen to share a code must never see each other's cached document.
  `DimensionCacheKeysTests` pins the shape directly, with no shell involved.
- **Invalidation is coarse and goes through `ISignal`, never `IMemoryCache.Remove`.** Every cached
  entry for a tenant's dimension types is tagged with one change token,
  `DimensionCacheKeys.TypesSignal(tenant)`; every entry for that tenant's structures is tagged with
  another. `DimensionTypeService` calls `ISignal.SignalTokenAsync` on the types signal after every
  write — create, update, retire — and `StructureService` does the same on the structures signal
  after create and update, which is also what covers a structure's levels changing. One signal per
  aggregate per tenant rather than one per id, because types and structures change rarely enough
  that evicting all of a tenant's cached types on any one write to them costs nothing worth
  avoiding, and it is far simpler to get right than tracking which cached entries one write could
  have affected.
- **This is also what specification section 11's open decision 6 — the distributed cache — reaches
  this module for free, once it is taken.** `ISignal` is Orchard's own abstraction; verified
  against the pinned version with the API probe, it resolves to `Signal` (local,
  single-process, change-token based) today, and would resolve to
  `OrchardCore.Caching.Distributed.DistributedSignal` instead the moment a distributed cache and
  `IMessageBus` are configured for the host — `DistributedSignal` publishes the same
  `SignalTokenAsync` call over the bus, so every server's cache invalidates together. Nothing in
  this module would need to change for that to happen; it is a known limitation only in the sense
  that, until decision 6 is taken, invalidation is single-process, which is a limitation of the
  host's configuration, not of this module's code.
- **Record and graph mutations do not touch either cache**, by design — nothing about a move,
  merge, cancellation or rename changes a `DimensionTypeDocument` or a `StructureDocument`, and
  wiring them to invalidate anyway would needlessly defeat caching the one kind of data that
  actually benefits from it. `DimensionCachingTenantTests.RecordAndGraphMutationsDoNotDisturbTheCachedTypeOrStructureReads`
  pins that explicitly, alongside one test per mutation that does invalidate something.
- **A write path must never load the document it is about to mutate through the cached lookup.**
  `DimensionTypeService.UpdateAsync` / `RetireAsync` and `StructureService.UpdateAsync` load
  through the plain, uncached `DimensionTypeLookup` / `StructureLookup` instead, injected
  alongside the cached interface for exactly this purpose. The reason is specific to YesSql, not a
  general caching concern: a document already tracked by the current session's identity map must
  be mutated through the exact object instance the session is tracking, and `ISession.SaveAsync`
  throws "an object with the same identity is already part of this transaction" if a write hands it
  a different instance carrying the same id — which a cached read can easily do, since the cache's
  whole purpose is to hand the same instance to everyone who asks. This was found, not anticipated:
  an early version cloned the cached document before returning it to avoid a different hazard (two
  concurrent requests mutating the one shared cached instance before either saved) and broke
  exactly this way in the integration suite. The fix keeps the cache's returned instance shared and
  therefore read-only in practice — nothing that resolves it through `IDimensionTypeLookup` /
  `IStructureLookup` ever mutates the result — and gives the two write services their own uncached
  path for the one case that must.

## Admin UI

Three screens, each calling the same services the API and the recipe steps
use — no screen holds a privileged path:

- **`DimensionTypesAdminController`** — list and editor for dimension types.
- **`StructuresAdminController`** — list and editor for structures and their
  ordered levels. Editing the levels of a structure that already has records
  placed on it previews the impact first (`IStructureService.PlanLevelChangeAsync`)
  and requires confirmation for a level removal, or refuses outright, naming
  the record at fault, for a reorder that would leave an existing placement
  invalid — the same dry-run-then-apply shape as move and merge.
- **`OrganisationDesignerAdminController`** — the read-only tree: a
  structure's roots (`IDimensionGraphService.GetRootsAsync`), lazily loaded
  children (`GetChildrenAsync`, fetched from the browser as each node is
  expanded rather than all at once), search with expand-to-match
  (`SearchAsync`, each hit carrying the ancestor chain the tree expands to
  reveal it), and the unplaced-records panel (`GetUnplacedAsync`). Gated by
  `ManageDimensionRecords` rather than a new permission — viewing the tree is
  the same day-to-day capability that already lets a caller create and change
  records, not a reason for an eighth permission. Move, merge, retire, rename
  and "add unit" are a later slice; this controller does not write anything.

### The designer's two views

One tree, rendered once, shown two ways: a top-down **chart** of cards and an
indented **list**. Both come from the same partial (`_DesignerNode.cshtml`) and
differ only by a class on the viewport, so switching between them keeps every
branch that is already expanded and there is no second rendering path to drift.
The choice is remembered in a cookie the server writes, so it survives a browser
with no JavaScript — which is the browser that most needs the list.

The chart starts at the structure's own card, with the structure's roots beneath
it. Every card carries one and the same expand control: a count of the unit's
children, computed on the server (`IDimensionGraphService.CountChildrenAsync`)
before anything is fetched, and no control at all when the count is zero. Nothing
the browser does afterwards changes which control a card has — a card's control
never becomes a different, disabled one because of what has already been clicked.

**The whole card toggles, not only the control on it.** Reaching for the unit's
name is what people do first, and a card that ignores it reads as a broken
screen. The control stays because it is what is reachable from the keyboard and
what says, in its label and count, that there is something to open. Dragging the
chart to pan does not toggle the card it was grabbed by: the pan handler marks a
pointer gesture that travelled more than a few pixels and the click that follows
it is ignored.

### Assets go through the resource manager

`WorkMateDimensionsResourceManifest` registers this module's stylesheet and three
scripts, and the views ask for them by name — `<style asp-name="…">`,
`<script asp-name="…" at="Foot">` — never by path.

This is not tidiness. Static files are served with
`cache-control: public, max-age=2592000`. Referenced by a bare path, that is
thirty days during which a browser that has loaded the screen once will not fetch
the file again, or even ask whether it changed; rebuilding, restarting and
deploying all leave it running the old script against freshly rendered HTML.
That failure looks exactly like a screen whose server code is correct and whose
every control is dead, and it cost a full review cycle to find. Through the
resource manager Orchard appends a content hash to the URL, so changing a file
changes its URL. `DesignerAssetsAndCultureTenantTests` fails if any WorkMate
asset goes back to a bare path.

### Dates on the wire

`Internal/IsoDate.cs` is the one place the designer parses and formats the dates
it exchanges with the browser, always ISO-8601 and always invariant. A date
control submits `yyyy-MM-dd` but *displays* in the browser's locale, so the same
day reads as 05/10/2026 to one user and 10/05/2026 to another; a culture-sensitive
parse anywhere in that round trip resolves a different day for an Arabic user than
for an English one, and under a culture whose default calendar is not Gregorian
(ar-SA uses Umm al-Qura) a different year. Pinned by `IsoDateTests` across en,
en-US, en-GB, ar and ar-SA.

Zoom in, zoom out and fit-to-screen are rendered by the server in chart view and
hidden by script in list view, so a script that fails to run leaves the controls
present rather than silently removing them. The chart pans by dragging. Every
rule that positions anything uses CSS logical properties, so the whole chart
mirrors under `dir="rtl"` without a rule written twice.

The behaviour that only exists once the browser runs the page's script —
expanding, collapsing, switching view, zooming, search-to-branch — is covered by
`tests/WorkMate.Browser.Tests`, which drives a real Chromium against a real
tenant seeded with the demo recipe. See that project's README; it needs browsers
installed before it can run.

### Waiting on the employee record: the unit head

**Backlog note, 5 October 2026.** Each unit has an effective-dated head: an
employee assignment flagged as head of that unit, not a field on the dimension
record — the same reason placement is an assignment row and not a department
field on the employee. The designer's chart card shows the current head's name,
and "Head: Vacant" when the unit has none.

Neither is possible yet, because no employee can exist until
`WorkMate.Records` ships, so the card shows `Head: —` today. The dash means
"not built yet", not "nobody": a card that said "Vacant" while the feature is
missing would be making a claim about the organisation that nothing has
checked. The line is rendered rather than omitted so the card's height does not
change when heads start arriving.

Prompt 5's approval routing — route to the unit head, and the vacant-head
rule — must read the same source, so that what the chart shows and what an
approval routes to can never disagree. The same note is on prompt 4 in
`/docs/prompt-library.md`.

## Known limitation: simultaneous creation of the same code

Even once `IDimensionValidator` enforces code uniqueness, two requests creating
the same code at the same instant can both pass validation and both commit.
Validation is a read followed by a write, and nothing in between stops a second
writer.

A unique database index would close it, and **there is no way to declare one**:
YesSql 5.4.7's schema builder offers only `CreateIndex(name, columns)` with no
unique variant, and no unique-constraint API anywhere. Raw DDL in a migration
is permitted by rule 2, but the obvious target is wrong — `DimensionRecordPartIndex`
has one row per content item *version*, which is why it carries `Latest` and
`Published`, so a unique index on `Code` would reject the second version of
every record. A unique constraint cannot express "unique among latest versions
only".

**The future option, if this ever needs closing:** a dedicated code reservation
table — one document per code, naturally unique — with a unique index created
by raw DDL in a migration. That is a schema decision with its own trade-offs
and needs its own proposal; it was reviewed and deliberately not taken on
3 October 2026, because the window is narrow and the consequence is a duplicate
code rather than lost or corrupted data.

## Depends on
- WorkMate.Platform — the bilingual field, the platform roles, the settings
- WorkMate.Core — `BilingualText`, `EffectiveRange`, `Page<T>`
- OrchardCore.Contents, OrchardCore.ContentTypes — content items and the
  content definition manager
- OrchardCore.Title — `TitlePartSettings`, for binding a record's title to its
  English name
- OrchardCore.ContentFields — the field types an attribute schema may declare
- OrchardCore.AuditTrail — where every structure and content-definition change
  is recorded, which section 4 requires rather than suggests
- `OrchardCore.Environment.Cache.ISignal` and `Microsoft.Extensions.Caching.Memory.IMemoryCache` —
  the dimension-type and structure cache. Both resolve from packages already pulled in
  transitively; neither needed adding to the `.csproj`.

## What it records in the audit trail
Category `Dimension`, four mandatory events: `DimensionTypeChanged`,
`StructureChanged`, and `ContentDefinitionChanged` — the last carrying a diff of
the generated content type, as section 4 requires — and `MoveCancelled`, the
only one of the four that carries a free-text reason, because cancelling a
move is the one record-level operation this module requires one for.

## Recipe steps
`dimension-types`, `structures` and `dimension-records` are built, in
`Recipes/`. `employee-assignments` has not landed yet — it arrives with the
employee assignment admin UI.

Each step validates every row of its own JSON array against one
`IDimensionValidator.BeginBatch()` before creating any of them, and throws
`RecipeExecutionException` naming every problem at once if any row fails, so
a bad row never leaves a partial write behind from its own step. `structures`
resolves its level types by code against what `dimension-types` already
created; `dimension-records` resolves its type and structure the same way,
and resolves a placement's parent by code too, once the record it names has
actually been created — which is why a recipe must list a parent before its
children, the same ordering constraint the record layer already has between
types, structures and records themselves. See the remarks on
`DimensionRecordsRecipeStep` for exactly where that two-phase shape stops
being able to guarantee nothing-written and why that is still sound in
practice. `Recipes/organisation-designer-demo.recipe.json`, in this module, is
a worked example: a self-contained three-level structure with a deliberately
unplaced record, clearly marked as test data, never referenced by
`base.recipe.json`, and discoverable on `/Admin/Recipes` precisely because it
sits in the module's own `Recipes/` folder rather than at the repository
root — see `recipes/demo/README.md` for why that distinction matters.

**Re-running a recipe is safe — ADR-0008.** A row naming a code that already
exists is compared field by field against the tenant's data: an exact match
is skipped (no write, no error), and anything else fails the step, naming
the code and what differs, rather than silently overwriting it. This is a
per-step guarantee, not a whole-recipe one — cross-step atomicity was
considered and rejected; the ADR explains why — so a failure partway through
one run can leave earlier steps' writes behind, and a later, corrected run
is expected to recognise them as already correct rather than collide with
them. `DimensionsRecipeStepsTenantTests` covers running the demo recipe
twice, a fixed re-run after a deliberately broken one, and a same-code row
with different content.

### For whoever writes `employee-assignments`

- **Open a validation batch** with `IDimensionValidator.BeginBatch()` and pass
  it to every create in the step, or duplicates within one import file go
  undetected. Every `CreateAsync` takes one. See the three built steps for
  the shape: validate the whole array first, write nothing until every row
  across the step has passed, write for real, revalidating.
- **Export uses a different mechanism from import, and that decision is
  recorded as ADR-0010** (ADR-0007 is the fix for a shared static document
  default, found while building the dimension types screen; ADR-0008 is
  recipe re-run behaviour, found while building these three steps; ADR-0009
  is migrations being append-only, found from a real tenant's missing
  `NameAr` column; the export-mechanism ADR this slice still owes has moved
  to the next free number three times now because of it):
  `IRecipeStepHandler`/`NamedRecipeStepHandler` for import,
  `IDeploymentSource`/`DeploymentStep`/`DeploymentStepDriver` from
  `OrchardCore.Deployment.Abstractions` for export, with one composite
  `DimensionsDeploymentStep` emitting all four recipe steps in dependency
  order. Write ADR-0010 when the export side lands, not before.
- **The open question in ADR-0006 — whether Orchard 3.0.1's recipe executor
  runs each step in its own shell scope — is answered there**:
  `DimensionsRecipeStepsTenantTests` runs `dimension-types` and
  `dimension-records` in one execution with `RequireNewScope = false` and it
  works, which is the harder case this module is built to survive either way.
- **This one is for the designer screen, not the recipe steps: warn, don't
  silently accept, when a backdated move meets a later one.** The engine
  records it correctly either way — `DimensionGraphService.InsertLink` splits
  the period rather than overwriting the later move, per the addendum to
  ADR-0005 — but a human entering a date months in the past, on a node moved
  again since, is usually trying to correct history, not to partially replace
  a decision they may not know exists. Show what the move will actually do
  (the computed split) before it is committed, the same way merge and
  cancelling a move already show a dry run; a designer that applies it with
  no warning will look correct in every test and still surprise someone in
  production.

## Decisions recorded
- **ADR-0002** — the dimension engine rather than taxonomies.
- **ADR-0005** — graph storage: documents behind the index tables, dated
  closure rows, and why every calendar date is stored as a midnight `DateTime`.
  Its addendum settles what a backdated move does to the moves after it.
- **ADR-0006** — write paths read content definitions with `Load…`, never
  `Get…`, and why a record created in the same scope as its type needs its
  parts welded by hand. **Read this before writing prompt 4's form designer**,
  which creates content types at runtime the same way.
- **ADR-0007** — a document's settable property must never default to a
  shared static instance. `DimensionTypeDocument.Name` and
  `StructureDocument.Name` both defaulted to `BilingualText.Empty`, so once a
  tenant held a second dimension type every type's name resolved to
  whichever had most recently been created. Fixed to a fresh instance each;
  `SharedStaticDocumentDefaultTests` in the platform test suite guards every
  module against the same mistake.
- **ADR-0008** — recipe re-run behaviour: each of the three recipe steps
  treats an already-existing code as a merge candidate (skip if it matches
  exactly, fail naming the difference if it does not), never silently
  overwritten. Cross-step atomicity — one transaction for the whole recipe —
  was considered and rejected in favour of this.
- **ADR-0009** — migrations are append-only. `DimensionRecordPartIndex.NameAr`
  was added to an already-shipped migration step instead of a new one, so a
  tenant that had already run that step never got the column and failed the
  moment `dimension-records` tried to create a record. Fixed with
  `Migrations.UpdateFrom3Async`, which checks whether the column already
  exists (a brand-new tenant already has it, from the step that was edited)
  before adding it, rather than reverting that edit. The check could not use
  `DbConnection.GetSchema("Columns", …)` — `Microsoft.Data.Sqlite` does not
  implement that collection at all, confirmed directly, and calling it broke
  every tenant's setup until this was found — so it queries SQLite's
  `PRAGMA table_info` and SQL Server's `INFORMATION_SCHEMA.COLUMNS` instead.
  `DimensionsMigrationUpgradeTenantTests` both reproduces the exact historical
  defect and checks, for every index table this module defines, that a fresh
  tenant's live columns match the C# class exactly.

## Open questions this module is waiting on

### Architecture open question 3 — runtime definition churn
How much runtime content-definition churn is permitted, and what guard rails
apply to destructive changes: naming rules, a review step, a limit on types per
tenant.

**Pending that answer, `IDimensionTypeService.UpdateAsync` refuses two things**,
each with a `DimensionRule.ImmutableOnceInUse` error naming the attribute:

- **removing an attribute** from a type's schema, because removing the field
  from the content definition deletes the data held in it;
- **changing an attribute's kind**, because it rewrites the field type under
  data already stored in it — the same destruction by another route.

A customer who no longer wants an attribute can stop populating it; a customer
who needs it gone permanently needs question 3 answered first. Reviewed and
accepted on 2 October 2026 as the conservative position until then. When the
guard rails are agreed, the two checks in `UpdateAsync` are where the new rule
goes, and `DimensionTypeServiceTests` is where its tests go.

### Architecture open question 6 — retention
The retention policy for closure rows and assignment history on a long-lived
tenant. Dated closure rows (ADR-0005) make this a question that has to be
answered rather than merely noted, because the closure table now grows with
history instead of staying proportional to the live structure.

## Specification
Section 4 of `/docs/technical-specification.md`, and the whole of
`/docs/dimension-engine-architecture.md`.
