# ADR-0007: A document property must never default to a shared static instance

**Status:** accepted
**Date:** 2026-10-04
**Deciders:** Project lead

## Context

Building the dimension types admin screen in prompt 3 was the first place anything in this
solution listed more than one `DimensionTypeDocument` and rendered the `Name` field of each
side by side. Once a tenant held a second dimension type, every row in the list showed the same
name — whichever type had most recently been created anywhere in the tenant — while `Code`,
`ContentTypeName` and every other scalar field on the same rows stayed correct.

The investigation ran deep before finding the real cause, and the path is worth recording because
the first, more alarming hypothesis was wrong. A minimal reproduction using nothing but
`ISession.SaveAsync` across two shell scopes, with no WorkMate service code involved at all,
showed the same corruption — which pointed at YesSql 5.4.7 itself. Raw SQL against the SQLite
`Document.Content` column, read in the very same session immediately after the corrupted
`ListAsync()` call, showed the stored JSON for both rows was completely correct. That ruled out
the database and narrowed the suspect to the deserialisation path — but the actual defect turned
out to be one line in WorkMate's own model, not in YesSql or in Orchard's content serialiser:

```csharp
public BilingualText Name { get; set; } = BilingualText.Empty;
```

`BilingualText.Empty` is one `static readonly` field — one object, shared by every
`DimensionTypeDocument` and `StructureDocument` ever constructed in the process, because the C#
property initialiser runs once per *type* in the sense that it always evaluates to the same
reference, not once per *instance* in the sense of allocating something new each time a `new
DimensionTypeDocument()` runs. Confirmed directly: for two freshly created, unrelated documents,
`ReferenceEquals(docA.Name, docB.Name)` was `true`.

The mechanism that turns a shared reference into cross-document corruption is deserialisation
writing into the object it finds on a property rather than replacing it outright, which something
in the pinned stack does for this shape of property — the same family of bug this platform has
already hit once, in prompt 1: `WorkMateSettings.WorkingDays` defaulted to the contents of the
shared `DefaultWorkingDays` array, and a saved Sunday-to-Thursday week came back as the union of
that and the Monday-to-Friday default. That one was fixed by typing the property as an array
rather than a list — an array has no `Add`, so it can only ever be replaced wholesale, never
written into. The exact component responsible was not pinned down this time either (`YesSql`'s
content serialiser is `System.Text.Json`-based per `YesSql.Serialization.DefaultContentJsonSerializer`,
and none of the 194 pinned Orchard assemblies reference `JsonObjectCreationHandling` or
`PreferredObjectCreationHandling` anywhere, so whatever causes it is not an explicit opt-in
anywhere in the stack this solution controls) — and it does not need to be, because the fix does
not depend on knowing the exact mechanism: if nothing ever hands two different documents the same
object, nothing can write one document's value where another reads it, regardless of how the
deserialiser behaves.

## Decision

**A settable property (`{ get; set; }`) on any YesSql document or content part must never default
to a bare reference to a `static` field or property.** It must default to a fresh instance —
`new(string.Empty, string.Empty)`, not `BilingualText.Empty`; `new()`, not `SomeType.Default` — or,
where the value is a collection, to a type that is always replaced rather than populated into,
which an array already is (`WorkMateSettings.WorkingDays`'s fix from prompt 1) and which
`IReadOnlyList<T> Property { get; set; } = [];` also is, because that collection expression
compiles to `Array.Empty<T>()` for an interface-typed property.

This does not apply to an immutable record's `{ get; init; }` properties on a type built from a
matched constructor — `DimensionAttributeDefinition`, `StructureLevel`, `ParentLink`, and the rest
of this module's small immutable records — because each one is constructed fresh, with every
value supplied as a constructor argument, every time. There is no pre-existing default instance
for anything to write into.

`DimensionTypeDocument.Name` and `StructureDocument.Name` are fixed to `new(string.Empty,
string.Empty)`. `tests/WorkMate.Integration.Tests/DocumentIdentityTenantTests.cs` is the
regression test: it creates several dimension types and several structures, each in its own shell
scope exactly as the original defect required, lists them back, and asserts every document kept
its own name and that no two share a `Name` instance.

**A guard applies this rule to every present and future module, not only this one.**
`tests/WorkMate.Platform.Tests/SharedStaticDocumentDefaultTests.cs` scans every module's source
for a settable auto-property whose initialiser is a bare `Type.Member` access, excluding `string`
(immutable; nothing can populate into one) and every enum the scan finds declared anywhere in the
module (an enum value is copied, not referenced). It is a heuristic text scan rather than a
reflection-based check, matching `UserVisibleStringTests`'s own reasoning for the same choice: it
has to run without a shell and without a compiled reference to a module that does not exist yet
when this test is written, which rules out reflecting over module assemblies from
`WorkMate.Platform.Tests`.

## Consequences

Every module written after this one inherits the guard test for free, the same way the
localisation gate already applies to a module the moment its `src/<Module>` directory exists —
nothing has to be registered or opted in.

The guard is a heuristic over source text, not a full C# parser, so it can be fooled by a shape
nobody has written yet (an aliased `using Empty = BilingualText.Empty;` well outside anything in
this codebase's style, for instance). `SharedStaticDocumentDefaultDetectorTests` documents exactly
what it catches and what it deliberately lets through, the same contract
`UserVisibleStringDetectorTests` keeps for the localisation scanner, so a gap found later is a
one-line addition to a known list rather than a rewrite.

The exact System.Text.Json mechanism that turns the shared reference into cross-document
corruption remains unidentified. That is an acceptable gap: the fix removes the one precondition
every instance of this bug family has shared across both occurrences (prompt 1's working week,
this one) — a mutable-looking property defaulting to an object two different instances both
point at — rather than depending on first fully reverse-engineering a closed-source serialisation
path in a pinned third-party dependency.
