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
- **`DimensionRecordPartHandler`** — the chokepoint. The generated types are
  creatable, so the admin screens, the API, GraphQL, a recipe and an import can
  all produce a record; the handler is the only point all five share. It stamps
  the dimension type on from the content type, and rejects a record with a
  missing or malformed code, a code another record holds, a name in only one
  language, a missing effective date, or an end before its start.
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

### Graph layer — not yet built
`DimensionLinkIndex`, `DimensionClosureIndex`, `EmployeeAssignmentIndex` and the
services over them: `IDimensionService`, `IDimensionGraphService`,
`IEmployeeAssignmentService`. Storage is decided in ADR-0005.

Two obligations are already known and belong here rather than where they were
raised, both agreed in review on 2 October 2026:

- **Self-closure rows follow the structure, not just the record.** They are
  added when a record is created, and also when a structure gains a level whose
  dimension type already has records — those records become part of that axis
  retrospectively and need their self pairs. A level being removed is the
  mirror case: the closure rows for records of that type on that structure have
  to be closed, and any links through them resolved, not orphaned.
- **The two rename operations** sit on `IDimensionService` over the record
  layer's name history. A corrective rename may target **any** name row, not
  only the open one: a typo can sit in a closed period and fixing it must
  correct history rather than fork it. A substantive rename closes the current
  row and opens a new one from the effective date.

### Validation and caching — not yet built
`IDimensionValidator` as the single validation service. The rule vocabulary it
will grow into already exists as `DimensionRule` and `DimensionError`, and the
configuration rules are enforced in the two services above until it lands.

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
