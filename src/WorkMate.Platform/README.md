# WorkMate.Platform

Tenancy, site settings, roles, cultures, base recipe and the shared UI shell.

This module knows about tenants, settings, roles, cultures and layout. It knows
nothing about employees, and it holds no business logic.

## Owns

| Thing | What it is |
| --- | --- |
| `WorkMateSettings` | The platform-wide site settings section: default locale, calendar, text direction, fiscal year start, currency, working week and customer code. Module-specific settings live in their own settings parts and are never added here. |
| `IWorkMateSettingsService` | The only way to read or change those settings. Reading is unrestricted, because currency, working week and direction are needed to render any screen. Writing requires `ManageWorkMateSettings` and is validated, both checked in the service so the API and background jobs are covered. |
| `WorkMateSettingsDisplayDriver` | The settings editor, at `Admin/Settings/workmate`. It shapes data for the view and delegates every decision to the service. |
| `Permissions.ManageWorkMateSettings` | Read and change the platform settings. |
| `AdminMenu` | Puts the editor under Configuration → Settings → WorkMate, hidden from anyone without the permission. |

Still to come in this slice: the six platform roles and their base permissions,
`recipes/base.recipe.json`, the bilingual text field (ADR-0003), the shared
component set, the RTL-aware layout extension, and structured logging scopes.

## Depends on

- `WorkMate.Core` — for `BilingualText` and `EffectiveRange`.
- Orchard features: `OrchardCore.Settings`, `OrchardCore.Roles`,
  `OrchardCore.Localization`, `OrchardCore.Admin`, `OrchardCore.Navigation`,
  `OrchardCore.Resources`.

Every other WorkMate module depends on this one.

## Recipe steps

None yet. `recipes/base.recipe.json` is rewritten in the next step of this
slice and will set `WorkMateSettings` through Orchard's `Settings` step.

## Localisation

Arabic translations live in `Localization/ar/WorkMate.Platform.po`, tracked
beside the code that raises the strings. `Directory.Build.targets` copies every
module's PO files into the host's content root at build time, where Orchard's
`ModularPoFileLocationProvider` finds them as
`Localization/{culture}/{FeatureId}.po`. English needs no file: it is the
msgid.

`LocalisationResourceTests` fails the build if a localised string has no
Arabic entry, or if an entry survives a string that has gone.

## Notes on the pinned Orchard Core version

Three things about 3.0.1 that this module had to be built around, all of them
verified with `tools/api-probe` or found by running it:

- `IEntity.As<T>()` is obsolete; `GetOrCreate<T>()` is the replacement.
- A shape's view model must not be `sealed`. Orchard builds it through Castle
  DynamicProxy, which subclasses it.
- A settings property that is a mutable collection can be *populated* rather
  than replaced on deserialisation, merging a saved value with the type's
  default. `WorkingDays` is an array for that reason.

## Specification

Section 3 of `/docs/technical-specification.md`.
