# WorkMate.Dimensions

The dimension engine: the foundation layer every other WorkMate module resolves
structure against. Designed in `/docs/dimension-engine-architecture.md` and
contracted in section 4 of `/docs/technical-specification.md`.

## Owns

### Configuration layer — built
- **`DimensionTypeDocument`** — a kind of unit (Business Unit, Department,
  Cost Centre, anything a customer invents): code, bilingual name,
  system-defined flag, attribute schema, and the name of the
  content type generated for it. Indexed by `DimensionTypeIndex`.
- **`StructureDocument`** — a named axis. `Levels` is its vocabulary in reading
  order; `RootDimensionTypeIds` and `Containment` are its rules, per ADR-0010;
  `IsStrict` and `IsPrimaryOrganisation` are flags. `AllowSkipLevel` survives as
  the record of what the map was derived from and is no longer read by any rule.
  Indexed by `StructureIndex`.
- **`StructureContainment`** — one permitted `(parent type → child type)` pair.
- **`IDimensionTypeService`** — creates, updates and retires types, and owns the
  backing content type. `CreateAsync` generates a content type named for the
  code with `DimensionRecordPart` attached, `TitlePart` generating its title
  from the English name, the attribute schema's fields on a part named for the
  type, and the type creatable, listable, securable and not draftable.
- **`IStructureService`** — defines axes, their vocabulary and their containment
  rules. A caller that already knows the pairings passes a `StructureShape`; one
  that still describes an axis as a chain passes nothing and has the map derived
  by `StructureContainmentDerivation`, the same function the migration uses.
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
- **`DimensionRecordPart`** — the standard fields every generated dimension
  content type carries: the eleven specification section 4 names, plus the
  bilingual **alias** FRS v2.5 §4.1 adds. It has **no parent and no structure
  field**, deliberately: a record sits on several axes at once, and a single
  parent field would collapse them into one tree. `DimensionRecordPartTests`
  asserts that no such field appears, and that the alias gained no date of its own.
- **`DimensionRecordPartIndex`** — code lookup, type filtering, the alias column
  the list draws, and dated resolution without loading the content item.
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
flag — unplaced is derivable as *no link on a structure whose types include its
own, and its type is not one of that structure's root types*. The organisation
designer lists those in prompt 3.

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
- **`HeadAppointmentDocument` / `UnitHeadIndex`** — who has led a unit on an axis,
  over time. One document per structure and record, which is the boundary "one
  head per unit per date" is about. Not an assignment and carrying no allocation;
  see **The unit head** below and ADR-0012.
- **`IDimensionGraphService`** — ancestors, descendants, children, is-under,
  depth; link and closure maintenance; rebuild; verify with a divergence
  report. Cycles are refused across **every** date, not only today.
- **`IDimensionService`** — create, update, retire, move, merge, cancel a move;
  the two renames; resolve by id and by code. Merge and cancelling a move both
  compute their plan once and `apply` only decides whether the writes follow,
  so the dry run cannot drift from what happens.
- **`IEmployeeAssignmentService`** — place, end, reallocate; effective
  assignment on a date; employees under a node, paged; and the unit head's
  appointment, ended and read.

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
the original date, because a structure's configuration carries no history of its
own past state — it may have unticked that pairing since, or its levels may have
changed. A blocking
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
retired records), **containment** (the diagonal included), and the advisory **parent
not yet effective** — advisory because pre-building next year's structure is
legitimate and refusing it would make the engine unusable for a case it was
designed for.

**A placement can be judged before the unit exists.**
`ValidatePlacementAsync` needs a record id, so a caller creating a unit and
placing it in one go could only ask after it had created something — which is
what `AddUnitAsync` did, deleting the record again when the placement came back
refused. A compensating delete on the failure path of the one method whose job
is to avoid half-finished units is the wrong shape, and it was also the thing
that crashed: on a tenant with more than one structure it threw YesSql's "The
object to update was not found in identity map", so typing a parent the
structure forbids produced a 500 rather than a sentence.
`ValidateNewPlacementAsync` asks the question first, taking the child's
dimension type and code in place of an id. The parent-side rules — the parent
exists, containment, parent retired, the not-yet-effective advisory — are one
implementation shared with `ValidatePlacementAsync`, so a create and a move are
refused in the same words; `DimensionsRecipeStepsTenantTests` compares the two
sentences directly. Cycles are the one rule that differs, and only because a
unit with no id is nobody's ancestor.

`AddUnitAsync` therefore writes nothing at all when a placement is refused, and
has no undo. `PlaceAsync` still re-checks on write, as section 6 requires; if
that re-check ever fails it means the world changed underneath the request, and
what it leaves is a unit on no tree — a state the engine already models and the
unplaced panel already reports, which is better than a second write racing the
same change.

### Containment: what may sit under what (ADR-0010)

A structure's `Containment` is a set of permitted `(parent type → child type)`
pairs and `RootDimensionTypeIds` is the set of types that may sit at its top.
Together they replace the arithmetic on level ordinals that used to decide
containment, because a chain can only say that one type is above another and
real organisations branch: under a Division either a Department or a Project;
under one Division Departments and under its sibling Regions. The only way a
chain could express either was to switch on level skipping, which permitted
everything below anything above.

`ContainmentRules.Decide` is the only implementation of the rule, and both the
validator and every picker call it. That is deliberate: before ADR-0010 the rule
was written twice — backwards in `DimensionValidator`, forwards in
`GetPermittedChildTypeIdsAsync` — and the forwards copy ignored `IsStrict`, so
on the one kind of axis that flag exists for the picker and the validator
disagreed. `DimensionContainmentTenantTests` asserts they agree over every
ordered pair of a structure's types, strict and non-strict.

Two things interlock:

| | |
| --- | --- |
| `Levels` | Vocabulary and reading order. Gives the editor's grid its rows and columns and the pickers their order. Carries no rule. |
| `IsStrict` | Whether an undeclared pairing is refused (`ParentTypeNotPermitted`) or merely reported (`ParentTypeNotDeclared`, which is advisory). |

**Self-nesting is a cell of the grid like any other** (ADR-0010 addendum). `X → X`
on a structure is the whole of it: tick it and that kind of unit nests inside
itself on that structure, untick it and it does not, and the same type can do
both on two different axes. The dimension type used to hold a veto no structure
could grant past, which greyed out the diagonal on the one screen whose job is to
answer that question and sent the reader looking for a checkbox on a screen about
something else. *"May a Department contain a Department"* is an axis-specific
fact for the same reason *"may a Division contain a Department"* is.

`DimensionTypeDocument.AllowsSelfNesting` is still stored and still accepted by
the `dimension-types` recipe step, but nothing reads it when deciding a
placement and no screen sets it. It keeps exactly one job: a `structures` row
written as a plain chain (`levelTypeCodes` + `allowSkipLevel`) has nothing else
to derive its diagonal from, so dropping the property would leave an old recipe
applying cleanly while quietly meaning something narrower. The dimension type
editor carries the stored value through a save unchanged rather than posting a
cleared checkbox over it.

`DimensionRule.LevelSkipping` and `DimensionRule.SelfNesting` are retained in the
enum and no longer emitted: a skipped level and an undeclared diagonal are both
simply pairings the structure does not declare. Audit entries written before
those changes name them, and a persisted enum member is a contract.

Having no parent is **not** constrained by containment. A record with no parent
is either a root of the chart or a unit in the unplaced panel, and
`RootDimensionTypeIds` decides which — it does not forbid a non-root type from
being parentless, because moving a unit off the tree is a supported operation.

`Migrations.UpdateFrom4Async` derives the map for every existing structure
through `StructureContainmentDerivation.FromChain`: adjacent pairs always, the
transitive pairs only where skipping was on, and a self-pair for every type whose
old `AllowsSelfNesting` was set. That is exactly the set the old arithmetic
permitted, and `DimensionsMigrationUpgradeTenantTests` proves it pairwise rather
than by inspection — for every ordered pair of level types, the derived map's
answer equals the v4 arithmetic's answer, for both values of the flag.

That derivation is also why the ADR-0010 addendum changed no tenant's permitted
placements. The migration had already written the `X → X` pairs the type flag
used to veto past, so removing the veto leaves the same set: the upgrade test's
"after" reading now consults the map alone and still matches v4 pairwise.

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
- **Keys carry the tenant**, so every key `DimensionCacheKeys` builds is
  `WorkMate.Dimensions:{tenant}:...`. This bullet used to say the tenant was in the key because
  `IMemoryCache` is a process-wide singleton shared by every tenant. **That is wrong**, and it was
  checked rather than assumed while tracing the invalidation defect behind ADR-0019: Orchard
  registers `IMemoryCache` in the tenant container, and a released tenant gets a new one — measured
  on a live tenant, the instance changes when a recipe releases the shell. The tenant stays in the
  key anyway, because specification section 9 requires it and because a key that is correct only
  while a registration in another codebase stays as it is would be a poor thing to rely on.
  `DimensionCacheKeysTests` pins the shape directly, with no shell involved.
- **Invalidation is coarse and goes through `ISignal`, never `IMemoryCache.Remove`.** Every cached
  entry for a tenant's dimension types is tagged with one change token,
  `DimensionCacheKeys.TypesSignal(tenant)`; every entry for that tenant's structures is tagged with
  another. `DimensionTypeService` signals the types signal after every write — create, update,
  retire — and `StructureService` does the same on the structures signal after create and update,
  which is also what covers a structure's levels changing. One signal per aggregate per tenant
  rather than one per id, because types and structures change rarely enough that evicting all of a
  tenant's cached types on any one write to them costs nothing worth avoiding, and it is far
  simpler to get right than tracking which cached entries one write could have affected.
- **The Organisation menu is the third signal, and the only cached *dated* read (ADR-0024, D23 D).**
  `MenuSignal(tenant)` tags one entry per tenant per day, `Menu(tenant, isoDate)`. It needs its own
  signal because it is built from three things — types, structures and **values** — and values have
  none of their own: nothing else here caches a record. Cancelled by `DimensionTypeService`,
  `StructureService` and `DimensionRecordHandler`, the last on the **content hooks** so that
  creating, renaming, correcting and retiring a value are covered by one place rather than by
  eleven success paths that each have to remember. Moving and cancelling a move touch only links,
  so `DimensionService` signals for those two itself.
- **The signal is sent again once the write has committed — ADR-0019, and this module had the
  defect too.** Both services used to call `ISignal.SignalTokenAsync` directly, which fires while
  the write is still staged on the session: a read arriving between that and the commit refills the
  cache from the state the write is about to replace, under a token that has already been spent,
  and the entry is then wrong until the process restarts. It was found in `WorkMate.Records`, where
  it was visible on a screen; here it would have shown as a rename that never reaches the
  organisation chart. Both now call `CacheSignals.SignalNowAndAfterCommitAsync`, which signals now,
  again after the commit, and again if the scope fails instead.

  **The "now" half is not redundant, and this module is the reason it survives.** Recipe steps,
  designer actions and the scenario builders all write and then read in one scope, through the
  cached lookups; deferring the only signal to the commit fails some thirty tests here.
  `DimensionCachingTenantTests` covers both halves, and
  `CacheInvalidationSignalTests` in this module's test project fails the build if anything calls
  `SignalTokenAsync` directly again.
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

Four screens, each calling the same services the API and the recipe steps
use — no screen holds a privileged path:

- **`DimensionTypesAdminController`** — list and editor for dimension types. The
  editor carries the **In the Organisation menu** block since ADR-0024: whether
  the dimension gets its own entry, the authored bilingual plural the entry reads,
  and where it sits. Reached from **Organisation → Settings → Dimension types**.
- **`DimensionRecordsAdminController`** — one list and one editor, serving every
  type the tenant has, reached from the Dimension types list. See *A list and a
  New button, per type* below.
- **`StructuresAdminController`** — list and editor for structures. The editor
  is four blocks: the types this structure uses in reading order; which of them
  may sit at the top; a grid of what may sit under what; and a **build from a
  simple chain** button that fills the last two from the first, so the common
  one-chain case stays a single action. The grid posts one entry per ticked cell
  under `ContainmentPairs` — an unticked checkbox posts nothing, which is exactly
  what a grid wants and what indexed binding would need a hidden companion per
  cell to fake. Changing a structure that already has records placed on it
  previews the impact first (`IStructureService.PlanLevelChangeAsync`) and
  requires confirmation for a type removal, or refuses outright, naming the
  record at fault, if a rule live placements rely on is being untick­ed — the same
  dry-run-then-apply shape as move and merge.

  The grid is server-rendered first and kept in step by `structures.js`, so the
  screen works with no script at all. A post carrying no ticks at all — a Create
  form, or a client with no script — is read as "describe this as a chain" and
  the map is derived; tick one root and the grid becomes the authority, including
  about what is *not* ticked.
- **`OrganisationDesignerAdminController`** — the read-only tree: a
  structure's roots (`IDimensionGraphService.GetRootsAsync`), lazily loaded
  children (`GetChildrenAsync`, fetched from the browser as each node is
  expanded rather than all at once), search with expand-to-match
  (`SearchAsync`, each hit carrying the ancestor chain the tree expands to
  reveal it), and the unplaced-records panel (`GetUnplacedAsync`). Gated by
  `ManageDimensionRecords` rather than a new permission — viewing the tree is
  the same day-to-day capability that already lets a caller create and change
  records, not a reason for an eighth permission — see "Who may do what" below.
  Move, merge and cancel-move are a later slice.

### A card is drawn twice, from one description of it

Cards reach the screen two ways: Razor renders the ones in the first response,
and the browser clones a template for every card it fetches when a branch opens.
Anything described once per path drifts, and the drift is silent — the two cards
sit side by side looking alike.

So each path reads one list. `DesignerCardActions.All` gives the menu, driving
the markup, the URL map on the surface and the script's wiring together; the
script's `cardText` gives the lines of text, each naming the payload property it
comes from. Adding either is one entry in one place.

An action whose controller is in another module names its `Area`. An Orchard
module is an MVC area, so a link to `EmployeesAdmin` from here resolves to
nothing without it — and resolves to nothing *silently*: `Url.Action` returns
null, the anchor renders with no href, and the result looks exactly like a
permission the viewer does not have.

Both lists were written for a defect of exactly this kind. "Appoint a head…" was
added to the markup alone, and because the script removes any action it has no
URL for, every fetched card quietly lost it. The head line was then filled by the
server and never by the script, so fetched cards kept the template's own words —
which read "Head: Vacant", and so reported every post in the organisation empty
however many were filled. The words now come from the model
(`DesignerNodeViewModel.HeadLabel`, `EmployeeCountLabel`), computed where the
localiser is, and the template renders blank: a line nobody filled in shows as
nothing rather than as a plausible claim. `DesignerCardMenuParityBrowserTests`
compares a fetched card against a server-rendered one at depth 2 and 3, for each
permission set.

### The designer's actions: add a unit, rename, retire

Every card carries an action menu, in both views, rendered by the server as a
`<details>` element: it opens, closes and takes focus with no JavaScript, and
each item is an ordinary link to an ordinary page with an ordinary form. Each
form has an effective-date control defaulting to today, and nothing is written
without one.

- **Add unit** — `IDimensionService.AddUnitAsync`. One operation, not
  create-then-place: the record and its placement are validated together and
  written together, so a unit can never exist having failed to reach its parent.
  The form offers only the dimension types that may sit under that parent
  (`IDimensionGraphService.GetPermittedChildTypeIdsAsync`, the same rules
  `ValidatePlacementAsync` enforces read forwards), and renders that type's
  custom attributes from its schema — text, number, boolean, date and bilingual
  alike. The structure's own card carries this action with no parent, which is
  how a top-level unit is added.
- **Rename** — the screen asks which of the two renames is meant, because
  architecture section 6 says it must: a **substantive** rename
  (`RenameAsync`) opens a new name period and leaves earlier reports reading as
  they did; a **correction** (`CorrectNameAsync`) rewrites the name in effect on
  the date given, so last year's report stops showing the typo. There is no
  default that is right often enough to pick silently.
- **Retire** — dry run, then confirm, the same shape as a level change, a move
  and a merge. `PlanRetireAsync` reports what is still attached on the day the
  unit would close: people placed at it, and whatever the deletion assessment
  found referring to it. Those are warnings, not refusals — closing a branch
  from the top is legitimate, and retirement is precisely what the architecture
  prescribes for a record history still refers to. **Units underneath it are not
  a warning**; see below. `RetireAsync` is the write.

#### Retiring a unit that still has units under it

Nothing about the links changes when a parent retires. The closure intersects
every ancestor row with the ancestor's own effective range, so when the parent's
range is capped its children simply stop resolving under it — still active, with
nowhere on the tree to be, and nothing anywhere saying why. That is a legitimate
end state and it used to happen silently, which is the defect this section exists
for.

`RetireAsync` now refuses (`DimensionRule.ChildrenNeedDisposition`) unless the
caller says which of three things should happen, and the screen will not commit
until somebody has chosen:

| | What it does |
| --- | --- |
| **Move them** | Each direct child moves to one new parent on the same date, keeping its own subtree. No day passes with them out of the tree. Needs `MoveDimensionRecords` — reparenting is reparenting, whatever prompted it — so the option is not offered to someone who could not make the move directly. The picker lists only units whose level permits every child's type, from `GetPermittedChildTypeIdsAsync`, minus the branch that is closing. |
| **Retire them too** | The whole subtree closes on the same date, shallowest first so each unit closes after the one above it. The preview shows every unit in the branch, not only the direct children. Each one audits with `CascadedFromRecordId` set, so a row of units closing on one day can be traced to one decision. |
| **Leave them unplaced** | Nothing extra happens to the links — this is what the engine did anyway — but each child is marked with the parent and date on its `DimensionLinkDocument`, so the unplaced panel can say so. Cleared the moment the unit is placed or moved again. |

The plan is built as at **the day the unit closes**, not the day before it: the
question is which units are left with nowhere to sit once it has gone, and a unit
opened on the same morning is invisible to a plan that looks at yesterday.

**The question is asked of every structure, not the one on screen.** Retirement
closes the record, so it lands on every structure the record sits on at once; a
unit that is a department on the organisation chart and a cost centre on the
finance one strands children on both. `PlanRetireAsync` returns one
`StructureChildren` per structure it is a parent on, `RetireAsync` takes the
answers keyed by structure, and a missing one is refused by name — "there are
children somewhere" is not something a person can act on.

The unplaced panel distinguishes the two reasons a unit has no parent — "never
placed in this structure" for one that arrived from an import, and a **Parent
retired** badge naming the parent and the date for one that lost it. The former
parent's name is carried on `OrphanedByParentRetirement` rather than looked up by
the caller, because a retired record does not resolve on any date the caller
would naturally ask for, and one retired on the day it opened does not resolve on
any date at all.

**Three reasons a unit has no parent, and the panel names which.** Never placed, parent retired,
or deliberately taken off the tree. The third was indistinguishable from the first until stage D2
gave leaving the tree a dated entry of its own — see **Leaving the tree is a decision** below.

**The panel's cards are the tree's cards.** It renders `_DesignerNode`, the same
partial, so an unplaced unit has the same action menu, the same drag behaviour
and the same markup as one on the chart — the menu's move entry just reads
"Place under…" instead of "Move to…", because a unit that is not anywhere is
placed rather than moved. It was a bespoke `list-group` before, and that is
exactly why it quietly had no menu and could not be dragged: every browser test
locates a card as `.designer-node`, and those rows were not one, so nothing in
the suite ever asked the panel the question. `EveryCardOnTheScreenHasAnAction
MenuForAUserWhoMayEdit` now asks it of every `.designer-node` on the page rather
than of a list of the ones somebody remembered.

### Leaving the tree is a decision (stage D2)

`ParentLink.ParentRecordId` is nullable, and moving a unit to "(top of the
structure)" writes an entry with no parent rather than removing the entry that
covered that date. History always resolved correctly either way — the displaced
entry was truncated to the day before — but the *decision* left no trace, and
three things followed from that: `GetRecordedMovesAsync` had nothing to offer, so
cancel move could not undo the one operation that takes a whole branch off the
chart; the unplaced panel could not tell this from a unit that had never been
placed, and said "never placed in this structure" to a unit somebody had moved
off it last week; and nothing on the record said anybody had decided anything.

Three readers treat a null parent as "nothing above here, from this date":
`DimensionLinkDocument.ParentOn`, which already returned null for the absence of
an entry; `ComputeAncestorsAsync`, which skips these entries rather than trying
to resolve them; and `GetRemovedFromTreeAsync`, which exists to find exactly
them. `EntryOn` is the reader for the case where the difference matters.

**No schema change and no migration.** `DimensionLinkIndex.ParentId` has always
been a string that is sometimes empty — the index provider already wrote the
`NoParent` sentinel for the axis-membership row — so a dated "no parent" entry
maps onto the column as it stands, and the queries that look for children by
parent id have never matched an empty one.

One case writes nothing: a record that has never been placed and is being placed
nowhere. Recording "no parent from today" against it would invent a decision
nobody made and put every untouched import row into the panel's "removed from the
tree" state.

All three record an audit entry under `DimensionRecordChanged`, naming the
operation, the unit, the date it takes effect, and the name on either side of
the change.

### Move, merge and cancel move

- **Move** — `PlanMoveAsync` previews it: the path it sits on now, the path it
  would sit on, how many units and people travel with it, and anything the
  validator objects to. Reached two ways and both land on that same preview — the
  card menu's "Move to…", and dragging a card onto another. A drag never
  commits: it is the easiest action here to do by accident and so the last one
  that should act on having happened.
- **Merge** — the existing `PlanMergeAsync`/`MergeAsync` pair behind a dry run
  that names what moves across and what happens to the source. Gated by
  `MergeDimensionRecords`.
- **Cancel move** — pick one of the placements on record
  (`GetRecordedMovesAsync`), see what undoing it would restore, and give a
  reason. The reason is mandatory in the markup and in the service, which throws
  rather than record an anonymous cancellation.

**Backdated moves.** ADR-0005's addendum requires the designer to warn when a
move is dated inside a period a later move already claims. `MovePlan` reports the
range the new link would actually claim — `InsertLink`'s own arithmetic, asked
rather than duplicated — and the screen says the move will apply only up to the
day before the one already on record, names the parent that takes over, and
points at cancelling as the way to remove that later move if it was the mistake.

**The action menu dismisses.** It is a `<details>`, which is what makes it work
and stay keyboard-reachable with no script. What `<details>` does not do is close
when you click elsewhere, so the script adds exactly that and nothing else: a
capture-phase `pointerdown` on the document closes any open menu, a capture-phase
`toggle` listener closes the others when one opens, and Escape closes the open
one and returns focus to the summary that opened it. A click that dismissed a
menu is one gesture, not two — it is suppressed from also toggling the card it
landed on (`suppressNextClick`) and from beginning a drag of it
(`dismissedMenu`), which is the difference between "click away to close" and
"click away to close and start reorganising the company".

**Drag against pan.** One pointer, two gestures, told apart by where it goes
down: empty canvas pans, a card begins a move. A card's drag only starts after
8px of travel, because every click on a card starts with a pointerdown on a card.
The grabbed card fades, the card under the pointer is outlined as the drop
target, and Escape abandons the whole thing. A drag that travelled is also
suppressed from becoming a click, so letting go never also toggles the branch.

#### Who may do what

| | Permission |
| --- | --- |
| See the designer | any of `ManageDimensionRecords`, `ViewDimensionHistory`, `MoveDimensionRecords`, `MergeDimensionRecords` |
| Add, rename, retire | `ManageDimensionRecords` |
| Move, cancel move, drag a card | `MoveDimensionRecords` |
| Merge | `MergeDimensionRecords` |
| Appoint or clear a unit's head | `AssignEmployees` |
| Resolve a past date | `ViewDimensionHistory` |

Viewing was gated on `ManageDimensionRecords` while the screen was read-only for
everyone. It is not now: a reader has to be able to see a chart with no action
menus on it, and that state is unreachable if seeing the tree needs the same
permission as changing it. The auditor role, which holds only
`ViewDimensionHistory`, is exactly that reader. Still seven permissions, not
eight.

**A first placement is not a move.** `AddUnitAsync` checks
`ManageDimensionRecords`, and so does `IDimensionGraphService.PlaceAsync`, which
refuses outright if the record already has a parent on that structure.
`MoveDimensionRecords` exists because reparenting silently changes what every
historical report resolves to; a brand-new unit has no history to change. If a
first placement needed the move permission, the stock HR administrator role —
which has `ManageDimensionRecords` without `MoveDimensionRecords` — could create
a department and never put it anywhere.

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

**Nothing on a card is cut off.** A long name wraps; a code is never abbreviated,
because a code is an identifier people type, search for and read out to each
other and half of one is worse than none. Rows stay level anyway: the script
measures each depth and grows every card in it to the tallest, which CSS cannot
do because the cards in a row are not siblings — each belongs to its own parent's
list. Without the script the stylesheet's `min-block-size` keeps rows close to
level and nothing is hidden either way. An earlier version gave every card one
fixed height and an ellipsis to enforce it, which lined the rows up beautifully
and turned "WorkMate Demo Organisation" into "WorkMate De…".

**Every card carries its unit's head and its headcount**, both read as at the date
being shown. The head line says `Head: Vacant` when the post is empty — the dash
that used to mean "not built yet" is gone, because once a unit can have a head a
dash would be a claim about the organisation that nothing had checked. The two
numbers are different facts and come from different places: the count is
placements at the unit, the head is an appointment that carries no allocation, so
a head who is not a member of the unit is named on the card and counted by
nothing. ADR-0012.

**The headcount is a link**, for anybody holding `ViewEmployees`, and the ⋯ menu
carries **View employees** to the same place: `WorkMate.Records`' employee list,
filtered to that unit, on that axis, as at that date. A number somebody reads is
a number they immediately want the names behind, and whoever looks for actions in
menus finds it there instead. Both are absent — not disabled — for a viewer who
may not open the list, because a link that lands on a refusal is a worse answer
than no link; whether they may is asked of the module that owns the permission,
through `IEmployeeLookup.CanViewEmployeeListAsync`. Clicking the count does not
also expand the card: the card's click handler has always ignored anchors.

**A card shows one name, and it is the English one, in every UI language.** The
Arabic name is stored, edited on *Add unit* and *Rename*, searchable in either
language and used in the sentences on the action screens — it simply has no line
of its own on a card. A chart is a shape before it is a sentence: a second line
doubles every card's height and halves how much of the tree fits on screen, and
it buys a reader of either language nothing they cannot get from the card they
are already looking at. This is a decision about the chart, not about the data.
The chart still mirrors under `dir="rtl"`.

**Nothing renders outside a card.** A code that does not fit wraps at its
hyphens at a slightly smaller size, rather than being kept on one line and
printed out through the card's edge over whatever is beside it — which is what
`white-space: nowrap` did to `zenith-dept-mechanical`. The one deliberate
exception is the action menu, which is positioned out of the flow precisely so
that opening it cannot change a card's height. `DesignerCardBrowserTests` walks
every element of every card on the two fully expanded demo companies and fails on
any rectangle outside its card's.

**A chart wider than its viewport scrolls to both edges.** The canvas used to
centre itself with `margin-inline: auto`, which works while the tree fits and
silently breaks when it does not: an auto margin on an overflowing box puts half
the overflow off the *start* edge, where there is no scroll range to reach it.
The leftmost branch read "…ate" and no amount of panning brought it back. The
viewport centres it instead, with `justify-content: safe center` — centre while
it fits, align to the start once it does not. *Fit to screen* has a much lower
zoom floor than the zoom-out button for the same reason: the button's floor stops
someone stepping down into an unreadable chart, while fit is the answer to "show
me the whole thing" and a fit that leaves a branch off screen has answered a
different question.

**Siblings appear in sort order, then by English name.** Every record is given a
sort order when it is created — one past the highest in the tenant — so units
come out in the order they were created, which for an import is the order the
recipe lists them in. Nothing used to set it, so every record sat at zero, the
name was the only clause that ever applied and every tree came out alphabetical:
Zenith's recipe says *Engineering, Projects, Corporate* and the chart said
*Corporate, Engineering, Projects*. A recipe may state `sortOrder`
explicitly, and an export always does. Both views honour it, because both read
`GetRootsAsync` and `GetChildrenAsync`. Existing tenants are untouched: records
already written sit at zero and keep the alphabetical order they have today,
which is why this is not a migration — there is no correct order to invent for
records whose creation order was never recorded.

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

### Names: English required, Arabic optional

ADR-0003's addendum. `WorkMateSettings.RequireArabicNames` is off by default, and
the question is asked through `IBilingualNamePolicy` so that every write path —
`DimensionValidator` for types, structures, records and attribute labels,
`DimensionRecordPartHandler` for anything created outside the designer, the
recipe steps through the validator, and Platform's own `BilingualTextField` —
reads one answer. A caller that forgets to ask gets the permissive behaviour,
which is the right way round for a rule being relaxed.

**Empty is not null, and null is not a crash.** `BilingualText` normalises a null half to an empty
string in its constructor — records deserialise through it, so stored data is normalised on the way
out of the database too, which is why making Arabic optional needed no repair migration. The
bilingual halves on a view model are declared `string?`, for two reasons that are easy to conflate:
an empty text box binds to `null` (`ConvertEmptyStringToNull` is true by default) *and* a
non-nullable reference type property gets an implicit `required` in model state, which would refuse
an empty Arabic name before the controller ran. `ViewModelsSurviveNullBindingTests` guards the
first; declaring the property nullable is what fixes the second.

**A reader is never shown a blank name.** `BilingualText.Display(en, ar)` is the
one implementation of the fallback: the reader's language, or the other one when
theirs is empty. `BilingualDisplay.Name` delegates to it and
`BilingualTextField.ForCulture` has always done the same. Three places had the
"both halves are always present" assumption baked in and no longer do:
`NameWithAlternate` drops the brackets when there is nothing to bracket, the
card and list views render the Arabic line only when there is Arabic, and
`workmate-bilingual` marks the English input `required` and never the Arabic one.

### Dates and names on the screen

`ViewModels/BilingualDisplay.cs` is the other half of that rule, and the opposite
of it: on the wire a date is ISO-8601 because there it must mean the same day to
everyone; on screen it is whatever the reader's culture says a date looks like,
and a name is whichever half of the bilingual pair the reader can read.

Anywhere a name or a date is interpolated into a localised sentence it goes
through that class. A card may show an English line and an Arabic line, and that
is bilingual display done right — both names belong to the record and a reader of
either finds theirs. A *sentence* is a different thing: "Support closed on
2026-10-06. الدعم" is an English sentence with an ISO date and an Arabic word
stranded after the full stop, which reads as a bug in either language. The
sentence is translated whole in the PO file, the name arrives in the reader's
language (`Name`), and the date in the reader's format (`Date`, `DateFromIso`).
Where the point is to identify a record rather than read a sentence — an action
screen's heading, an entry in a picker — `NameWithAlternate` puts the other
language in brackets after it: "Support (الدعم)".

Formatting uses `CurrentCulture` and the choice of language `CurrentUICulture`.
Orchard's localisation middleware sets both from the request so in practice they
agree, but they answer two different questions and CA1305 is right to insist they
be asked separately.

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

### The unit head (ADR-0012)

**A head appointment is its own dated record** — structure, record, employee,
effective range — stored beside the link, closure and assignment tables as
`HeadAppointmentDocument` / `UnitHeadIndex`, one document per structure and
record. It is **not** an assignment and **not** a field on the dimension record.

The backlog note of 5 October proposed a flag on an assignment row, and prompt 4
session A1 found that it does not work: `ValidateAssignmentAsync` refuses an
allocation of zero, so a flag on an assignment cannot express a head who is **not
a member** of the unit they head. Architecture section 8's seed data requires
exactly that case, and acting heads borrowed from another department make it
ordinary rather than exotic. The only way to carry the flag would be to invent a
real allocation for somebody who has none there — and that allocation is then
counted by the unit's headcount and charged to its cost centre. ADR-0012 has the
full argument and the alternatives.

So an appointment carries no allocation, is counted by no headcount, is charged to
no cost centre, and the head need not be assigned to the unit at all.

| Rule | Behaviour |
| --- | --- |
| One head per unit per date | Blocked (`SingleHeadPerUnit`), naming the incumbent and when their term began |
| The employee exists and had not already left when the term starts | Blocked (`HeadNotEligible`) |
| A head who leaves mid-term | **Not a rule.** `IEmployeeService.ExitAsync` ends it on the exit date; recording a past leader's term is exactly the history this engine keeps |
| The head is assigned to the unit | **Deliberately not a rule.** This is the point |

**A handover is not a clash, and that distinction is the whole of the rule.**
Appointing a successor from 1 April while the sitting head's term runs open-ended
from 2024 overlaps on paper and is the most ordinary thing that happens to a unit:
`SetHeadAsync` closes the outgoing term on 31 March, the same displacement a new
placement performs on the one it replaces. What *is* refused is a term already on
record that starts on or after the new date — a future appointment this one would
delete rather than supersede — and a **closed** term by somebody else covering days
the new one claims, which is backdating over a completed tenure. The engine splits
rather than overwrites when a *placement* is backdated (ADR-0005's addendum), but a
head is not a placement: truncating a completed term changes who an approval from
that period resolves to, which is the one thing dating the appointment exists to
keep stable.

Re-appointing the head already in post from the same date changes nothing and
succeeds, which is what makes the `unit-heads` recipe step safe to re-run.

**`IEmployeeAssignmentService` carries the surface** — `SetHeadAsync`,
`ClearHeadAsync`, `GetHeadAsync`, `GetHeadsAsync` (batched for a row of cards),
`GetHeadshipsOfAsync`, `EndHeadshipsOfAsync`, `GetHeadHistoryAsync` — rather than a
service of its own, because there must be exactly one source for "who leads this
unit": the designer's card and prompt 5's approval routing both read
`GetHeadAsync`, so what the chart shows and what an approval routes to cannot
disagree. All of it is gated by `AssignEmployees`.

**`DimensionRecordPart.HeadEmployeeId` is retired, not deleted.** Nothing reads it
when resolving a head and the record editor no longer offers it, but the property
and every stored value stay where they are: ADR-0009's rule is that a migration
never destroys data, and the old field is undated, so there is no term to derive
from it and inventing one would assert that every head had led their unit since
the beginning of time. The same shape as `AllowsSelfNesting` after ADR-0010's
addendum.

**`IEmployeeLookup`** is how this module asks whether a head is a real employee who
has not left. Declared here, implemented in `WorkMate.Records`, resolved as a
collection — the same shape as `IDimensionDeletionBlockerProvider`, and for the
same reason. A tenant with `WorkMate.Records` disabled gets a named refusal rather
than every employee reported as missing.

**`UnitHeadAdminController`** is the screen, reached from the unit's own card:
*Appoint a head…* when the post is vacant, *Change the head…* when it is not.
Gated by `AssignEmployees` — appointing a head and placing somebody are the same
kind of act, and a separate permission would split one capability in two. Both
screens list every term on record, because "who led this unit last March" is the
question dating an appointment exists to answer and the person about to change it
is the one most likely to want it answered. Clearing a head warns, before it
happens, that approvals routing to that unit will have nowhere to go.

**The card reads it in two batched queries for a whole row**, never two per card:
the appointments in one (`GetHeadsAsync`) and the names in one more
(`IEmployeeLookup.GetManyAsync`) — the same reasoning that batched
`CountEmployeesAtAsync`. A head whose name cannot be resolved reads as "Vacant" on
a card, because a content item id tells a reader nothing; on the head screen it
reads as the id, because there the id is the only honest answer and "Vacant" would
hide a problem on the one screen whose job is to fix it.

### Where employees may attach

Specification section 5's backlog note: employees attach to leaf-capable units,
never to a Division, a Region or a Project, "because an employee attached to one
would be counted by every roll-up beneath it". That rule is **declared**, not
derived, for the reason ADR-0010 made containment declared — and it genuinely
cannot be derived:

- **Not from the containment map.** Crescent's `Branch → Department` edge makes
  Branch a container, and yet a branch with no departments holds its own staff.
- **Not from today's tree.** That would let an empty Division take staff and would
  invalidate a staffed Department the moment somebody put a section under it.

So `StructureDocument.EmployeeAttachableDimensionTypeIds` names the types that may
hold employees on that axis, and **empty means unconstrained** — which is every
structure written before this rule existed, the same way ADR-0008 treats a recipe
row that states nothing. `StructureContainmentDerivation.FromChain` deliberately
derives nothing for it: a chain says which types an axis uses and in what order,
not which of them hold people, and guessing "the last level" would impose a
constraint on every structure that has ever been described as a chain.

Two rules, enforced in `ValidateAssignmentAsync` so the service, the recipe step
and the employee editor all get the same answer from the same code:

| | Rule | Blocking? |
| --- | --- | --- |
| `UnitDoesNotHoldEmployees` | The unit's type is not one this axis declared | **Yes**, when the axis declared any |
| `EmployeeAtContainerUnit` | The unit has units under it on that date | **No — advisory** |

The second is advisory on purpose, and both halves of that matter. Blocking would
refuse a department that has three sections and a departmental secretary, which is
an ordinary shape. Saying nothing would hide the thing the rule is about: that
person is counted once by the department and again by every roll-up beneath it,
and the two numbers stay plausible while disagreeing.

Both are asked of **every** unit in a split, not only the primary one. A secondary
placement is counted by exactly the same roll-ups — the Zenith matrix is the worked
example, and its whole point is that cost rolls up by project over the secondary
rows.

An update that says nothing about employee attachment leaves it alone.
`StructureShape.EmployeeAttachableDimensionTypeIds` is nullable where its two
siblings are not, and the difference is load-bearing: null means "this caller is
not describing it", empty means "this axis constrains nothing". A caller that could
only say the latter would silently clear a tenant's rule every time a screen that
predates the setting posted a structure.

**The rule is read forwards in three places, all from the same
`PermitsEmployeesAt`**, which is ADR-0010's lesson applied again — one rule, two
directions, one implementation:

- `GetEmployeeAttachableUnitsAsync` — the whole set, for a control with no search.
- `SearchEmployeeAttachableUnitsAsync` — a page of it, matching typed text and
  optionally scoped to a container's subtree through the closure index. What the
  unit picker asks on every keystroke.
- `DesignerNodeViewModel.HoldsEmployees` — per card, so the designer's **Place an
  employee here…** appears only where the write would be accepted.

### The unit picker

`workmate-unit-picker` (`Components/UnitPickerTagHelper.cs`) and its search endpoint
`UnitPickerAdminController`, used by `WorkMate.Records`' placement screens. They
live here rather than there because the units are this module's, and the module
that owns employees must not read the structures itself.

It is the employee picker's combobox — `workmate-picker.js` in `WorkMate.Platform`,
which both pickers now drive — with two questions of its own: where it searches,
and what an option reads as.

**Every option carries its full path**, `Crescent › Retail Banking › Credit`,
built from `GetAncestorPathsAsync` in two queries for the whole page rather than
one per option. The Crescent reference tenant has two departments called Credit
and two called Operations on one structure; a list of bare names offers a reader
identical rows and no way to choose correctly.

The optional **Within** filter narrows to a container's subtree. Which units count
as containers is `GetEmployeeScopeUnitsAsync`: the types this structure does *not*
place employees on, falling back to its root types on a structure that constrains
nothing — because an unconstrained structure still has a top.

The `<select>` remains the form control and is still posted, bound and validated
without the script; the script hides it and sets its value. The **Within** control
is rendered hidden and unhidden by the script, because without the script there is
no round trip behind it and a control that visibly does nothing is worse than one
that is not there.

### The Organisation menu: one expandable entry per dimension name (ADR-0024, decision D23, slice F2)

```
Organisation
    Organisation designer      ← unchanged, and first, exactly where it was before slice F2
    (Organisation tree)        ← position reserved; slice F8 builds the read-only enquiry report
    Departments                ┐
        All Departments        │  the list screen, over every dimension type in the group
        Civil (Zenith …)       │  the dimension's own active values, at most fifteen,
        Credit (Crescent …)    │  alphabetical in the reader's own language
        …                      │
        Show all (17)          ┘  only when there are more
    Divisions
        …
    Settings
        Dimension types
        Structures
        (Mapping)              ← position reserved; slice F3
```

**This shipped once, was withdrawn by the product owner on sight, and is back in a different
shape.** On a real tenant the withdrawn version read *Branchs, Cost centres, Departments,
Departments, Departments, Divisions, Divisions, Divisions, Projects, Regions, Sections, Teams*.
FRS v2.5 §1.2 and decision D3 ask for it again; decision D17 diagnosed what went wrong and decision
**D23** settled the remedy after the first one was built and reverted.

| What went wrong | What it is now |
| --- | --- |
| **The plural was guessed.** English gained an "s"; Arabic gained nothing, because قسم pluralises to أقسام by changing the word | **Authored**, in both languages, on the dimension type editor. A group with no authored plural falls back to the **singular** — wrong-looking, never wrong. No rule invents a word |
| **The tenant really did hold three Department types**, one per demo organisation | **The menu groups them.** Types sharing an English singular name, compared case-insensitively, are one entry (`DimensionMenuComposition`). Where the entry's values span several charts, each value names its own — *Civil (Zenith Engineering & Construction)* |
| **Nobody could shorten it** | `ShowInMenu` is the switch D3 asks for. A hidden dimension's screens stay reachable from the Dimension types list's **View units** link, which is why the list screen resolves a group from the *types* rather than from the menu |

**D17's remedy was built and reverted, and that is worth knowing before proposing it again.** It
asked the demo recipes to share the standard dimension types so the tenant would hold one Department.
A dimension type is a **level of every structure that declares it**, and `CreateAsync` writes a self
closure pair on all of them — so a shared `division` type put every Crescent division on the Zenith
axis as a parentless root and the Zenith chart drew them. `DemoCompanyRecipesTenantTests` caught it.
That is ADR-0005 working as designed; the demo tenant, holding two unrelated companies, is the odd
case. A second attempt gave each recipe a company-qualified plural — *Zenith Departments* — which
worked and was also wrong, because a tenant should not read its own company name on every entry.

**Each entry expands (D23 C).** *All &lt;plural&gt;* opens the ordinary list screen scoped to the
group rather than to one type — which is what gives it an **Organisation column and filter** when
more than one type is grouped — then the dimension's own active values, then *Show all (N)* when
there are more than `DimensionMenuGroup.MaxValues`. Clicking a value opens
`DimensionRecordsAdminController.Details`: that value's code, names, alias, parent, status, head,
headcount and attributes, with **Edit**, **Move** and **Retire** linking to the screens that already
own those rules.

**The three properties were revived, not re-invented.** `ShowInMenu`, `PluralName` and `MenuOrder`
have been on `DimensionTypeDocument` since `UpdateFrom7Async`, read by nothing, because ADR-0009
made that step unremovable. They are now read by `Navigation.AdminMenu` and written by the editor.

**`Migrations.UpdateFrom10Async` un-guesses what `UpdateFrom7Async` guessed**, and it is a repair
rather than a destruction. It asks the frozen `DimensionTypeMenu.DefaultFor` what it *would* have
produced for each type's name: a stored plural that matches exactly is machine output and is
replaced by the singular; one that differs is something a person typed on session D's editor and is
**left alone**. Using the frozen function as the detector for its own output is the only rule that
cannot drift from what the frozen step actually did — which is the one job that function now has,
and the reason it is still here rather than merely unremovable.

**Positions are explicit numbers, never `LocalizedString.PrefixPosition()`.** That orders by the
caption in whatever language the reader is in, so an Arabic reader got a different order from an
English one. The same reasoning makes `MenuOrder` a number a customer sets rather than a sort by
name, and makes the ordering tiebreak the **English name** rather than the caption.
`OrganisationMenuTenantTests` builds the menu under both cultures and compares the two sequences *by
code*; `OrganisationMenuBrowserTests` does the same against a real rendered page, in both languages,
on a tenant with both demo companies loaded — which is the exact configuration that produced the
original complaint.

**Read through a cached service, not a query.** The menu is built on every admin request, so
`AdminMenu` takes `IDimensionMenuService` — `CachedDimensionMenuService` over `DimensionMenuService`,
the same decorator pattern as the two lookups — rather than querying a dimension's values while a
page waits to render. Two reasons beyond speed: it applies no permission check, which is right
because Orchard filters each entry by the `Permission` it declares; and it needs no authorisation
scope, which a navigation provider has no business entering. That is also why `AdminMenu` is
`internal`: the service is.

**This is the one dated read in the module that is cached**, and the rule above — *dated graph
queries are not cached* — still holds. It is the exception that proves it: the menu is only ever
asked about **today**, and it is read on every admin request. The date is part of the cache key
(`DimensionCacheKeys.Menu(tenant, isoDate)`) rather than a sliding expiry, because a menu built on
Monday is a correct answer about Monday and Tuesday should ask a different question.

**Invalidated on every write that shapes it (D23 D).** One signal, `MenuSignal`, cancelled through
`CacheSignals.SignalNowAndAfterCommitAsync` (ADR-0019) by `DimensionTypeService` (a type's name,
plural or menu settings), `StructureService` (which charts a type is a level of, which decides the
organisation suffix) and `DimensionRecordHandler` (a value created, renamed, corrected or retired).
The last of those is on the **content hooks** rather than on each of `DimensionService`'s eleven
success paths, so one place that cannot be forgotten covers them all; moving and cancelling a move
touch only links and signal for themselves.

**A null `menu` on `IDimensionTypeService.UpdateAsync` means "I am not describing the menu"**, not
"reset it". A recipe row or an API call that says nothing must not clear an authored plural — the
same rule `allowsSelfNesting` is carried through a save under, and the same rule
`StructureShape.EmployeeAttachableDimensionTypeIds` states for its own nullable. The editor always
posts a complete statement, so its `ToMenu()` is never null.

### A list and a New button, per type — reached from the Dimension types list

Until this, a Department could only be created or edited inside the organisation
designer. The designer is a chart: it answers "what sits under what", it is
navigated by expanding branches, and finding one department by a half-remembered
code means opening the branch it is in first. HR administrators asked for the
shape every other list in the product has, and that is what this is.

**There are two ways in, and both are deliberate.** The dimension's own **menu
entry** (ADR-0024, above) is the short one; the **`View units` link** on each row
of the Dimension types list is the other, one click further in and in the place
where somebody configuring a type already is. A dimension hidden from the menu is
still reachable by the second, which is what makes hiding safe rather than
destructive.

The per-type menu was briefly withdrawn between those two slices, and that history
is worth keeping because it is why the entry looks the way it does — see **The
Organisation menu** above for the three defects and their fixes.

Two things from that period survive and are worth knowing about:

- **The fixed entries keep their explicit numeric positions.** They used
  `PrefixPosition()`, which orders by the caption in whatever language the
  reader is in, so an Arabic reader got a different order from an English one.
  That was found while the per-type entries were being built, and it is the same
  reason `MenuOrder` is a number rather than an alphabetisation.
- **The screens still name a type by its singular** — "Department units", not
  "Departments". The heading identifies *this* type's list; the plural is what the
  menu shows, and a heading that read "Departments units" or that silently
  switched between the two would be a second reading of a field with one job.

The list shows code, bilingual name, **alias**, parent (the full path, per
structure), status with its dates, the headcount as a link into the employee
list, and the type's attributes as **optional** columns — off by default, because
a type with eight attributes makes an unreadable table and which of them matter is
the reader's question. It searches code and both names, filters by structure, by
`Within` (the unit picker again, narrowing to a subtree through the closure
index) and by active/inactive, and it pages.

**Creating is one operation.** The form's "Place under" is the unit picker in its
parent-choosing mode: the options come from `SearchPermittedParentsAsync`, which
is the structure's containment map read forwards from the same `Permits` the
validator reads backwards — so the picker never offers a parent the save would
refuse. A type the structure allows at the top may be left with no parent, and
that is an option with its own words rather than an empty box. Saving calls
`IDimensionService.AddUnitAsync`, which writes the record and its dated
`DimensionLink` together; there is no create-then-move and no second set of
validation messages.

**Editing covers names, the alias and attributes, and nothing else.** A rename is
dated and comes in two kinds — substantive or corrective — and the form asks
which, because the two answer "what was this called last March" differently and
nothing downstream can infer it. The alias sits below that whole block and asks
nothing, because it has no periods to choose between. Moving and retiring are
links to the designer's own dated screens: a move rewrites what every historical
report under the unit resolves to, which is why it has its own permission and its
own dry run, and a second way to do it is the last thing it needs.

### Two views, five controls, and an alias — FRS v2.5 §4.1 (slice F1)

The FRS gives every dimension its own screen with a **Tree** view and a **List**
view, the columns Code / Name / Alias / Status, and Filter, Add, Edit, Delete and
Refresh in the top-right corner. This screen is where that lands.

**The alias is a plain field, not a second name.** `DimensionRecordPart.AliasEn` /
`AliasAr`, indexed so the list can draw fifty rows without loading fifty content
items, written by `IDimensionService.SetAliasAsync`. It is **undated**, and that is
a decision rather than an omission: the bilingual name carries a dated history
because "what was this called last March" changes what a historical report prints
and because a rename is two different operations the engine has to be told apart.
None of that is true of a nickname — nothing resolves it as at a date and no report
groups by it — so dating it would mean a second parallel history, with its own
storage and its own two kinds of rename, to answer a question nobody asks.
`DimensionRecordPartTests.TheAliasCarriesNoDateOfItsOwn` is what stops that being
undone by somebody adding `AliasEffectiveFrom` in good faith while making one
screen consistent with the name field beside it.

`SetAliasAsync` is its own method rather than a parameter on `UpdateAsync` for a
reason worth knowing: `UpdateAsync` states every unmanaged field at once — cost
centre, GL account, sort order, the active flag and the retired `HeadEmployeeId` —
so a screen wanting to change only the alias would have to read the rest and post
them back. A form that round-trips fields it does not show silently rewrites
whatever changed underneath it between the read and the write, and
`HeadEmployeeId` is precisely the field nothing may rewrite (ADR-0012 retired it
without deleting it). It also writes **nothing at all** when the value is
unchanged, because the editor posts every field it shows whether or not anybody
touched it.

Bilingual where the FRS's mock draws one box: CLAUDE.md rule 7, and an alias is a
name whatever else it is. The Arabic half is optional like every other one since
ADR-0003's addendum, so a tenant that never fills it in sees exactly the single box
the mock draws.

**The tree is the dimension's own, and only its own.** Rule 2: *"a tree view only
shows the values that belong to that one dimension."* A Department sits under a
Division on the organisation chart and under *HR* on the Department screen, so a
value's parent here is the **nearest ancestor of the same dimension type**, and a
value whose chain has none is a root however deep it sits on the chart. That comes
from `GetAncestorPathsAsync`, which already answers it for the whole page in two
queries and already returns each chain root-first with depths — which is why the
nearest same-type ancestor is the *last* match rather than the first. Asking for
children per node would be a query per row, on the screen most likely to have many.

Three things the tree says out loud rather than leaving a reader to infer:

| | |
| --- | --- |
| **A dimension on several structures** | has several trees, because parentage is scoped to a structure (ADR-0005). The screen refuses to pick one, exactly as it already refuses to pick one for the Parent column and the headcount, and says so. |
| **A capped tree** | tree view loads up to `TreeCap` (500) values at once and never pages, because a value whose parent is on another page would be drawn as a root — a wrong shape rather than a partial one. Over the cap it says so and points at the list. |
| **A filtered tree** | same hazard, same answer: a filter that excludes a parent lifts its children to the top, and the screen says a filter is applied. |

**The five controls, and the one that is not finished.** Filter is a `<details>`
disclosure, Add is the existing New, Refresh re-issues the query — which is not
cosmetic on a screen somebody leaves open while a recipe runs. Edit and Delete act
on the row the reader ticked: one radio per row inside a GET form, with the buttons
outside it joined by the HTML `form` attribute, so a selection works with no script
and the chosen row is visible on the page rather than held somewhere only the
browser knows. Pressing either with nothing ticked says so rather than 404ing on an
empty id.

**Delete is the control the FRS asks for and not yet the behaviour D4 describes.**
ADR-0025 gives it two outcomes — a value nothing has ever used is really deleted,
one that is or was used is made inactive with the reason on screen — and the second
half already exists in full as the dated retirement screen, which previews what is
still attached and will not commit until somebody has said what happens to the
units underneath. The first half does not: a real delete is the one operation in
this module that destroys a record, and it needs its own audit event, its own
confirmation and its own second review. **That lands in slice F9.** Until then the
button opens the retirement screen and says so on the way, rather than being held
back for four slices or labelled with a word the product owner has already answered
the other way.

**What needs the browser, and what does not.** `dimension-records.js` does two
things: collapses a branch, and opens the filter panel. Everything else — adding,
editing, deleting, filtering, paging and switching between tree and list — is a
link or a form the server rendered. Without the script every row is visible and the
filter panel is open, which is a longer screen and not a broken one. The script
never recomputes the tree: each row carries its own id, its parent's, its depth and
its child count from the server, and the script only hides and shows — so the two
cannot disagree about what the tree is, which is ADR-0010's "two readings of one
rule" reached through a different door.

**Status reads Active / Inactive**, which is the FRS's own wording for this column
(§4.1.2), and it is the record's effective range read on the date the screen is
about. The row's **Retire** action keeps its own name because it is a dated
operation with its own screen, its own dry run and its own vocabulary throughout
the module; the badge describes a state and the button names an act.

### Orchard's generic content screens are not a second way in

A Department created through **Content → New** is a content item with a
`DimensionRecordPart` and no `DimensionLink` anywhere: on no structure, under no
parent, invisible to the closure index and to every roll-up built on it. It looks
correct on Orchard's own screens and is missing from the chart, which is the kind
of failure nobody notices until a headcount does not add up.

- **Not creatable.** `DimensionTypeService` writes `Creatable = false` on every
  generated content type and `Migrations.UpdateFrom8Async` applies the same to
  the types a tenant already has — the append-only rule, applied to a definition
  the service rewrites on every save. The same treatment, and the same reasoning,
  as `WorkMate.Records` gave `Employee`.
- **Listable stays on**, so a dimension record still appears in Orchard's content
  list and still obeys its per-type permissions. It simply has no New button.
- **Editing is redirected, not refused.** A content type that cannot be edited
  cannot be used, so `DimensionRecordAdminRedirectFilter` sends anybody reaching
  the generic create or edit URL to the screen that understands dated names,
  placements and the attribute schema, with a note saying why. It keys on the
  controller's type name and the route values rather than on a reference to
  `OrchardCore.Contents`, which this module does not take and must not.

### "Place an employee here…" on a card

Declared in `DesignerCardActions.All` immediately after `sethead` and gated on the
same `AssignEmployees`: placing somebody and appointing a head are the same kind of
act, and splitting them would leave a customer granting two permissions to achieve
one thing. Its controller is in `WorkMate.Records`, so it is named by area and
resolved by name at run time — on a tenant without that module `Url.Action` returns
null and the existing "an action with no URL is one this viewer may not use" rule
drops it.

It is the first action whose availability depends on the **unit** as well as on the
viewer, which a cloned template cannot branch on in Razor. So the link declares
what it needs — `data-designer-action-requires="holdsEmployees"` — the node carries
the answer, and the script removes the link on a card whose unit holds nobody. That
keeps a fetched container's menu identical to a server-rendered container's, which
is the property `OrganisationDesignerViewModel.PermittedActions` exists to protect.

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

`DimensionRecordChanged` carries the record-level operations, and
`DimensionRecordOperation.AliasChanged` is the one member of that enum whose
`EffectiveFrom` is **not** a date somebody chose. An alias is undated, so there is
no effective date to record and the field carries the day the change was *made*
instead. That is stated on the enum member rather than left for a reader to infer
from a date that looks like every other one, and the alias on either side travels
as four plain strings rather than two `DimensionRecordNameState`s — that type's
properties are `NameEn` and `NameAr`, and an auditor reading a serialised entry
whose `NameEn` held an alias would have no way to tell which fact they were
looking at.

**`DimensionRule` is appended to, never inserted into.** The enum is serialised
into audit entries and `System.Text.Json` writes an enum as its numeric value
unless told otherwise, so a member slotted in beside a related one renumbers every
member after it and silently changes what every entry already written means. The
grouping comments in that file are for a reader; the order is a contract.
`InvalidValue` is therefore at the end rather than beside `CodeFormat`.

## Recipe steps
`dimension-types`, `structures`, `dimension-records`, `employee-assignments` and
`unit-heads`, all in `Recipes/`. A recipe lists them in that order: a structure
resolves its level types by code, a record resolves its type and structure, and
the last two resolve units — and people, through `IEmployeeLookup`, which means
`WorkMate.Records`' own `employees` step has to come before either of them.

**`dimension-types` carries the menu entry since ADR-0024**: `pluralEn`,
`pluralAr`, `showInMenu` and `menuOrder`, all nullable so that *absent* and
*stated* are different things. A row that states **none** of them is not describing
the menu and leaves a tenant's authored plural alone; a row that states **any** of
them is describing it, and the ones it leaves out take the documented default
rather than the stored value — because a half-stated menu is still a statement, and
guessing which half was meant is worse than taking the default. The export states
all four unconditionally, for the reason ADR-0008's sort-order addendum gives:
"this dimension is hidden" and "this file says nothing about the menu" are
different tenants, and an export reproduces a tenant.

**A change, not a placement, is `employee-assignments`' unit.** Each entry is one
employee on one axis carrying dated changes, and each change is the whole set of
concurrent placements from that date — one row ordinarily, several for a split
allocation. That is the shape `ReallocateAsync` takes, and it takes it because
the rules that matter are about the set: the allocations effective at once total
100 and exactly one is primary. A file listing placements one at a time would
describe something invalid at every line but the last. An entry may also carry
`endedOn`, which is the day somebody came off the axis altogether — not the same
statement as a change naming a different unit, which is a transfer.

**Two steps rather than one.** A head appointment is not an assignment (ADR-0012):
it carries no allocation and no primary flag, and the employee need not be placed
at the unit at all. A row that carried both would be two things wearing one shape,
and the half that was absent would have to be expressed by nulls that mean
different things in each direction.

**`aliasEn` / `aliasAr` are nullable in `dimension-records`' step model**, so that
*absent* and *stated as empty* are different things — the same reason `sortOrder`
is, and the same reason the lookup-list step's `isActive` and `sortOrder` are. A
row written before aliases existed says nothing about one, and ADR-0008's
comparison is scoped to what a row actually states, so re-running such a recipe
against a tenant whose units have since been given aliases **skips** rather than
refusing. A row that states `""` is claiming the unit has none, and a tenant where
it has one is a difference the step reports by name.

The export goes the other way and states the pair **only when the record has one**
— the judgement Arabic names already get, and the opposite of `sortOrder`'s. An
absent alias and an empty one mean the same thing to the importer, so writing
`"aliasEn": ""` against every unit in a tenant that uses no aliases would be noise
in every exported file for no fact gained. A unit that does have one carries both
halves, including an empty Arabic half, because there the two are genuinely
different answers.

### Export (ADR-0011)

`Deployment/` holds the other half: a deployment step that appears under
**Tools → Deployments** and writes those same three steps. It is deliberately
not an export format of its own — the output is a recipe the importers above
already read, so there is one description of a tenant's organisation rather than
two that can drift.

It carries the attribute schema on a type, ADR-0010's explicit `rootTypeCodes`
and `containment` on a structure, and on a record the **full dated placement
history** — every move for every structure, including the stage-D2 entries that
name no parent, and retired units with their end dates — plus a dated
`nameHistory` where a unit has been substantively renamed. Records are emitted
**parents first**, by a walk over every parent a record has *ever* had, because
the importer resolves a parent by code against records it has already created.

`DimensionExportRoundTripTenantTests` is the proof: it seeds a tenant containing
a rename, a backdated move that splits, a retirement, a move-to-top, a
self-nested unit and a ragged tree, exports it, imports it into a second fresh
tenant and compares the closure on dates either side of every change. A
flattened history produces an identical tree today and a different answer about
last March, which is exactly what that comparison is for.

It also carries a record's own **attribute values** — undated, because they are:
an attribute is a field on the record's content part with no effective range,
unlike its name, its placement and its existence — and its **`sortOrder`**, which
is what sibling order on the chart is made of. That one is stated even when it is
zero: an import assigns an unstated sort order from its own tenant's highest,
which is the right default for a hand-written recipe and the wrong one for an
export, where the order a tenant is in is a fact to reproduce rather than an
intent to express.

**Placements and heads, behind two switches of their own.** `IncludeAssignments`
emits `employee-assignments` and `IncludeHeads` emits `unit-heads`, both **off by
default** — the three above are configuration and travel between environments as
a matter of course, while these are operational data about real people, and
carrying them by accident into a tenant that has its own is worse than having to
tick a box. Two switches rather than one because a head is not a placement
(ADR-0012): a tenant can hold appointments and no placements or the reverse, and
one switch covering both would make the export claim otherwise.

Both name people by code, resolved through `IEmployeeLookup`, so the file they
produce needs `WorkMate.Records`' `employees` step applied first — which is the
plan's ordering to get right, and the reason that step's own deployment step says
so on the screen. Both also need `IncludeRecords`, for the reason records need
types and structures: a placement names its unit by code and cannot apply to a
tenant that has no units.

`PeopleExportRoundTripTenantTests` is the proof for this half, and a second round
trip rather than more cases in the first because the join between the two modules
— a code resolved at import — is what neither module's own tests can see. It
seeds a mid-month transfer, a split allocation, a matrix, a head who is not a
member, one person heading three units, a post left vacant and somebody who came
off an axis entirely; exports both modules into one recipe; imports it into a
fresh tenant; and compares the answers on dates either side of every change.

### The demo recipes

`organisation-designer-demo` is the small three-level example the designer tests
run against.

**Two of its records carry an alias and the rest do not**, which is the shape the
FRS's own figures show and the shape a list has to read well in: `demo-div-sales`
states both halves, `demo-dept-retail` states only the English one, so the display
fallback is exercised by seeded data rather than only by a unit test.

**`demo-section` nests inside itself**, and `demo-sec-counter` sits inside
`demo-sec-instore`. That is what gives one dimension's own tree view a branch to
collapse — without it every value is a root of its own tree, which is Rule 2
working correctly and demonstrating nothing. **Section rather than Department,
deliberately**: adding a unit under a parent that permits two kinds of unit makes
the designer's Add-unit form ask which kind, and the browser tests add departments
under divisions and sections under departments. Section is the one level nothing in
the suite adds beneath, so it is the one that can carry the diagonal without
changing another test's journey. That is worth remembering before giving a second
demo type a diagonal.

**Its dimension types stay prefixed `demo-`**, as every demo recipe's do. This
recipe is a self-contained **sample** rather than a reference company: its stated
selling point is that it runs on any tenant without colliding with anything, and
sharing a type code would give that up.

**All three recipes keep prefixed type codes, and that is the second thing slice F2
got wrong and had to undo.** Decision D17 asked them to share the standard types so
a tenant with all three loaded would hold one Department. It was built, and it put
every Crescent division on the Zenith chart — a dimension type is a level of every
structure that declares it, and `CreateAsync` writes a self closure pair on all of
them, so one record ends up on several axes exactly as ADR-0005 intends.
`DemoCompanyRecipesTenantTests` caught it. A second attempt renamed the data
instead — *Demo Division*, *Zenith Departments* — which disambiguated the menu by
making a tenant read its own company name on every entry.

**Decision D23 fixes it in the menu**, so the names here are plain again: every
recipe says *Division*, *Department*, *Section*, and the types sharing a name share
one menu entry whose values name the chart they are on. Each type carries an
**authored bilingual plural** — identical across the types that share an entry,
because the group reads the first authored one it finds — and a `menuOrder`.

Two worked company examples sit beside the sample:

- **`organisation-designer-zenith`** — two kinds of unit at one level. Under a
  Division sits either a Department or a Project; a Project contains Teams.
  Project carries a real attribute schema and the records carry values for it.
- **`organisation-designer-crescent`** — a ragged tree. One Division holds
  Departments two levels deep while its sibling holds Regions, and Departments
  appear again four levels down under a Branch. Three branches are leaves.

**Both now declare `employeeAttachableTypeCodes`** — Zenith attaches people to
Departments and Teams, Crescent to Branches and Departments — so "Place an
employee here…" is offered on the units that hold staff and not on the containers
above them. Without it, every type was attachable (empty means unconstrained),
and the demos showed the action on an Engineering Division, where an employee
would be counted by the division and again by every roll-up beneath it.

Stating it on a structure a tenant already has raised an ADR-0008 question, and
the answer is that ADR's **11 October addendum**: for this field alone, empty in
the tenant is the *absence* of an answer rather than a different one — the model
documents it that way, and it is the state every structure written before the
field existed deserialises to — so a row that states a set against a structure
declaring none **fills it in** rather than failing the step. A tenant holding a
real, different set is still refused, writing nothing.
`StructureEmployeeAttachmentRecipeTenantTests` covers all four outcomes.

The people on each sit in a **separate recipe** —
`organisation-designer-zenith-employees` and
`organisation-designer-crescent-employees` — at the headcounts in the prompt
library's 7 October backlog note. Separate because a test about the shape of an
organisation should not have to load 227 people to run, and the browser suite
builds a tenant per fixture and would pay for them every time; nothing loads a
recipe it did not name. Each one needs its structure recipe applied first.

Zenith's is where the matrix is: an engineer deployed to a site team carries a
primary at their home department and a secondary at the team, so cost rolls up by
project and HR rolls up by department over the same rows, while foremen, labour
and technicians are project-hired and carry one primary at their team only.
Between them the two recipes also seed the three head shapes ADR-0012 was decided
for — one person over two units, an acting head who is not a member, and a post
left vacant by a term that ended. `DemoEmployeeRecipesTenantTests` applies both to
a tenant of its own and asserts every figure in the table, the matrix on one
person, each head shape, and a clean re-run. The files are generated by
`tools/generate-demo-employees.sh`, which is where the figures are edited.

Every code in each is prefixed, types included, so the three coexist on one
tenant in any order and none can become another's by sharing a code. Both state
`rootTypeCodes` + `containment` rather than `allowSkipLevel` — the shapes they
exist to demonstrate are the ones a chain cannot express.
`DemoCompanyRecipesTenantTests` asserts each tree parent by parent, both unit
counts, a clean re-run, and that the placements their containment forbids are
actually refused.

Each step validates every row of its own JSON array against one
`IDimensionValidator.BeginBatch()` before creating any of them, and throws
`RecipeExecutionException` naming every problem at once if any row fails, so
a bad row never leaves a partial write behind from its own step. `structures`
resolves its level types by code against what `dimension-types` already
created, and states its rules either as a chain (`levelTypeCodes` +
`allowSkipLevel`, derived through the same function the migration uses) or
explicitly (`rootTypeCodes` + `containment`, a map of parent code to the child
codes it may contain). Stating both is **refused** naming the conflict rather
than merged: they are two descriptions of one thing, and picking one silently is
how a recipe comes to do something its author cannot read off it.
`dimension-records` resolves its type and structure the same way,
carries each record's attribute values and, optionally, an explicit `sortOrder`
— absent means the order the file lists the records in, which is what the step
creates them in — and resolves a placement's parent by code too, once the record it names has
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

### For whoever writes `employee-assignments` and `unit-heads`

- **Resolve employees through `IEmployeeLookup`**, by code and never by id, the
  same rule every other step follows. The lookup is resolved as a collection, so
  a step that finds none must say *that* — "the employee record is not enabled on
  this tenant" — rather than reporting every row's employee as missing, which is
  the same failure wearing a misleading message.
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
- **ADR-0012** — the unit head is a dated appointment of its own, not a flag on
  an assignment row and not the undated `DimensionRecordPart.HeadEmployeeId`
  field, which is retired without being deleted. `Migrations.UpdateFrom5Async`
  (schema version 6) creates `UnitHeadIndex`.

  **A new table goes in the new step and `CreateAsync` is left alone**, exactly as
  `UpdateFrom1Async` and `UpdateFrom2Async` created the record and graph layers'
  tables. That is why this step needs none of the `ColumnExistsAsync` guarding
  `UpdateFrom3Async` and `UpdateFrom4Async` need: those add a *column* that
  `CreateAsync` also produces, so they have to handle a brand-new tenant and an
  upgrading one differently, and a table is not in `CreateAsync` at all.

  One thing to know before adding the next table: `DimensionsMigrationUpgradeTenantTests`
  simulates an old tenant by rolling the recorded version back, and a tenant at
  version N does not have the tables the steps above N create — `CreateMapIndexTableAsync`
  fails on one that is already there, which fails the whole catch-up and leaves
  the shared tenant stuck below head, so every later test in the collection fails
  too. `TablesByVersion` in that file is the one place that knowledge lives; add a
  row to it.

- **FRS v2.5, slice F1** — the alias columns. `Migrations.UpdateFrom9Async`
  (schema version 10) adds `DimensionRecordPartIndex.AliasEn` and `AliasAr`.

  **It does not also edit the step that creates the table, and that is the point.**
  `DimensionRecordPartIndex` is created by `UpdateFrom1Async`, which has shipped.
  ADR-0009's exception — "`CreateAsync`, or whichever step currently plays that
  role for a table, keeps being updated to match the latest schema" — exists only
  because a brand-new tenant must reach the current schema in one pass, and it does
  not apply here: a brand-new tenant runs `CreateAsync` and then every
  `UpdateFromNAsync` in order, so it arrives at this step with the table and
  *without* these two columns, which is exactly where a tenant upgrading from
  version 9 arrives. Both states are the same state, so the column is simply added
  and the shipped step is left alone. That is `UpdateFrom5Async`'s reasoning for a
  whole table, applied to a column: what forced `UpdateFrom3Async` and
  `UpdateFrom4Async` to inspect the database first was that *their* column is also
  produced by a step the fresh tenant has already run.

  It is still guarded by `ColumnExistsAsync` — not because a fresh tenant might
  have the columns, but because the step has to tolerate a table that already does:
  that is the state a tenant rolled back for an upgrade test reaches, and the state
  a catch-up that failed after this step and was retried would reach. `AddColumn`
  on an existing column throws on both providers, and when it throws here the whole
  catch-up fails and the tenant is left stuck below head.
  `DimensionsMigrationUpgradeTenantTests.ATenantStuckBeforeTheAliasColumnsUpgrades
  CleanlyAndCanStoreAnAlias` rolls a tenant back to version 9, drops both columns,
  upgrades, and then writes and reads an alias through the real service — the
  operation that would throw "no column named AliasEn" on a tenant the migration
  had missed, which is the same failure on the same table that ADR-0009 exists
  because of.

- **ADR-0024, slice F2** — the per-dimension Organisation menu, revived.
  `Migrations.UpdateFrom10Async` (schema version 11) replaces the plurals
  `UpdateFrom7Async` guessed with the singular.

  **Not a schema change**, so there is no column and nothing for
  `ColumnExistsAsync` to look at: `ShowInMenu`, `PluralName` and `MenuOrder` have
  been document properties since version 8. It is a data repair, and the only one
  in this module's history that touches values a previous step wrote.

  **Why it does not count as destroying data (ADR-0009).** What it clears is one
  frozen function's output, and it detects that by asking the same function —
  `DimensionTypeMenu.DefaultFor` — what it would have produced for the type's name.
  A stored plural that matches is a guess; one that differs is something a person
  typed on the editor that shipped with the withdrawn menu, and is left alone.
  Using the frozen function as the detector for its own output is the only rule
  that cannot drift from what the frozen step actually did.
  `DimensionsMigrationUpgradeTenantTests.ATenantWithGuessedPluralsHasThemReplaced
  AndAuthoredOnesLeftAlone` asserts both halves on one tenant.

- **ADR-0020** — *withdrawn* by decision D19 before any of it was built. A value
  has exactly one parent; `DimensionLinkDocument.ParentOn` keeps returning one and
  the closure index keeps one path per pair. The ADR file is kept for the analysis
  in it, and ADR-0021 is amended to a single parent, Required or Optional.

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
