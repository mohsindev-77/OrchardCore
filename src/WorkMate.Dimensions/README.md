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
- **`IDimensionService`** — create, update, retire, move, merge; the two
  renames; resolve by id and by code. Merge computes its plan once and
  `apply` only decides whether the writes follow, so the dry run cannot drift
  from what happens.
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

### Validation and caching — not yet built
`IDimensionValidator` as the single validation service, and the per-tenant
cache. The rule vocabulary it will grow into already exists as `DimensionRule`
and `DimensionError`, and the rules implemented so far are enforced in the
services that own them until it lands.

**Record code uniqueness is the known gap, and it is deliberate.** It cannot be
done reliably from a content validation handler: by the time validation runs,
the content manager has already saved the item under validation, so its own
index row is hard to tell apart from a real clash — and a record saved earlier
in the same unit of work is not in the index at all, so an import creating five
hundred rows in one batch would miss duplicates *within* the batch, which is
where they actually happen. Several approaches were tried and none was sound
enough to keep. The rule therefore moves to `IDimensionValidator`, which has to
track codes seen within a batch regardless, because architecture section 6
requires an import to validate row by row and report every failure. Until then
uniqueness is enforced on **dimension types and structures**, where it works
and is tested, but **not on records**.

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

## What it records in the audit trail
Category `Dimension`, three mandatory events: `DimensionTypeChanged`,
`StructureChanged`, and `ContentDefinitionChanged` — the last carrying a diff of
the generated content type, as section 4 requires.

## Recipe steps
_None yet._ `dimension-types`, `structures`, `dimension-records` and
`employee-assignments` arrive with prompt 3.

## Decisions recorded
- **ADR-0002** — the dimension engine rather than taxonomies.
- **ADR-0005** — graph storage: documents behind the index tables, dated
  closure rows, and why every calendar date is stored as a midnight `DateTime`.
  Its addendum settles what a backdated move does to the moves after it.
- **ADR-0006** — write paths read content definitions with `Load…`, never
  `Get…`, and why a record created in the same scope as its type needs its
  parts welded by hand. **Read this before writing prompt 4's form designer**,
  which creates content types at runtime the same way.

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
