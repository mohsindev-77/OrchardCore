# ADR-0003: The bilingual text field type lives in WorkMate.Platform

**Status:** accepted
**Date:** 2026-09-30
**Deciders:** Head of Technology

## Context
Prompt 1 of the prompt library asks for "a bilingual text field type in
WorkMate.Core with edit and display drivers, used by every later module".

That cannot be built where it is asked for. The technical specification's
repository layout (section 2) describes `WorkMate.Core` as "shared
abstractions, no Orchard dependency", CLAUDE.md repeats the rule, and
`WorkMate.Core.csproj` carries it as a comment with no package references at
all.

A bilingual field type in Orchard Core 3.0.1 is not an abstraction. Verified
against the pinned version, it requires:

- `OrchardCore.ContentManagement.ContentField` as its base class, from
  `OrchardCore.ContentManagement.Abstractions`;
- `OrchardCore.ContentManagement.Display.ContentDisplay.ContentFieldDisplayDriver<TField>`
  for the edit and display drivers, from `OrchardCore.ContentManagement.Display`;
- registration through
  `services.AddContentField<TField>().UseDisplayDriver<TDriver>()`, from
  `OrchardCore.ContentManagement.Display`;
- Razor views, and therefore the `Microsoft.NET.Sdk.Razor` project SDK.

Putting any of that in `WorkMate.Core` makes the project an Orchard module in
all but name and breaks the one rule the project exists to keep.

## Decision
Split the concern along the line the specification already draws.

`WorkMate.Core` keeps `BilingualText`, the plain `record` value object holding
`En` and `Ar`. It stays free of Orchard, so services, validators, calculation
engines and their unit tests can use it without a shell.

`WorkMate.Platform` owns the Orchard surface: `BilingualTextField`, its
settings type, its edit and display drivers, its views, and its registration.
`BilingualTextField` converts to and from `BilingualText`, so the value object
remains the currency between services.

This is recorded as a deviation from prompt 1 of the prompt library, not from
the technical specification, which never placed the field type in
`WorkMate.Core`.

## Consequences
Every later module that wants the bilingual field depends on
`WorkMate.Platform`, which section 3 of the specification already requires of
every WorkMate module, so no new dependency edge is created.

Services and their unit tests can keep using `BilingualText` without
referencing Orchard, which keeps the unit test level inside the one-minute
budget that section 10 sets.

The prompt library should be corrected at prompt 1 to say
`WorkMate.Platform`. The technical specification needs no change; section 5's
field table names the bilingual field without saying which project holds it,
and section 2's layout already gives `WorkMate.Platform` the shared UI.
