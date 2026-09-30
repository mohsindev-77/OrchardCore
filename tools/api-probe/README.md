# API probe

CLAUDE.md requires every session to verify the Orchard Core APIs it intends to
use against the version pinned in `Directory.Packages.props`, because Orchard's
API surface moves between releases and memory of it goes stale silently. This
tool is how that verification is done: it reads the pinned assemblies' metadata
straight out of the local NuGet cache and prints what is actually there.

It reads the pin from `Directory.Packages.props`, so it follows the version
automatically when ADR-0001 is superseded.

## Running it

```
dotnet run tools/api-probe/probe.cs <query> [query ...]
```

It needs the pinned packages in the NuGet cache, so run `dotnet restore` first.

## Query forms

| Form | Meaning | Example |
| --- | --- | --- |
| `TypeName` | Describe every type with this simple name: kind, base type, interfaces, constructors, methods (including protected and abstract), properties and fields | `SiteDisplayDriver\`1` |
| `Full.Type.Name` | The same, matched on the full name, for when a simple name is ambiguous | `OrchardCore.Settings.ISiteService` |
| `~fragment` | List the full names of every type whose full name contains the fragment. Use this to find out what exists before asking about it | `~PortableObject` |
| `=fragment` | List every method whose name contains the fragment, with its declaring type and signature. Use this to find extension methods | `=AddSiteDisplayDriver` |
| `--features` | List every module and feature id with its display name and dependencies. Use this before naming a feature in a recipe | |

Backticks in generic type names need escaping in a shell, or quote the argument.

## What it does not do

It loads metadata only, through `MetadataLoadContext`. It never executes
Orchard code, so it cannot answer questions about runtime behaviour: whether a
recipe step actually applies, what order handlers run in, or what a migration
does to the schema. Those are answered by integration tests against a real
tenant, not here.

It is a file-based app with no project file and is not in `WorkMate.sln`, so
the solution build does not build it and it cannot reach a product assembly.

## Worked example

Finding out how site settings are registered in the pinned version:

```
dotnet run tools/api-probe/probe.cs "~DisplayDriver\`" =AddSiteDisplayDriver "SiteDisplayDriver\`1"
```

The first query finds `OrchardCore.DisplayManagement.Entities.SiteDisplayDriver\`1`,
the second finds the `AddSiteDisplayDriver<T>()` extension that registers it,
and the third gives the members to override.
