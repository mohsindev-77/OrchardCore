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
| `PlatformRoles` | The six role names of specification section 3, as constants, because the base recipe and every module's permission stereotypes have to agree on them. |
| `Permissions.GetDefaultStereotypes` | Grants `ManageWorkMateSettings` to the two administrator roles. Orchard's `RoleUpdater` applies it when a role is created and when this feature is enabled, so the order of the two does not matter. |
| `BilingualTextField` | The bilingual name field every later module uses, with its settings, drivers and views. ADR-0003 records why it is here rather than in `WorkMate.Core`. |
| The component set | `<workmate-bilingual>`, `<workmate-date-range>`, and placeholders for `<workmate-dimension-picker>` and `<workmate-employee-picker>`. One implementation each, so a control looks and behaves the same on the admin and on the employee front end. |
| `WorkMateNavbarDisplayDriver` | WorkMate's chrome in the admin header — the approval badge — and the shared `workmate-admin` stylesheet. |
| `WorkMateLogScope` | Tenant, user and correlation id on every log line. |

## Decisions taken

Approved on 1 October 2026 after review of prompt 1. They are recorded here
rather than only in a pull request so that the next person to read this module
finds the reasoning beside the code.

| Decision | What was approved |
| --- | --- |
| Defaults are Bahraini | Currency **BHD**, working week **Sunday to Thursday**, week starts **Sunday**. The first tenants are in Bahrain. Nothing in the platform assumes them: a tenant elsewhere changes them on the settings screen or in its own recipe, and `BaseRecipeTests` keeps the recipe and the code defaults in step. |
| Hijri shows alongside Gregorian | The default calendar is **Gregorian with Hijri**. Specification section 9 says Hijri is shown "where the tenant enables it" and does not say whether that is on by default; for these customers it is. Dates are stored Gregorian regardless. |
| Role definitions are the delivery team's | **`ManageRoles` goes to Platform Administrator only.** Tenant Administrator keeps `AssignRoleToUsers`, so it can put people into existing roles, which is the day-to-day need. This stands until product decision 2 — who configures a customer — is settled, at which point it is revisited rather than assumed. `PermissionNameTests` and the integration suite both hold it. |

## The shell

The admin theme is Orchard's, extended rather than replaced. TheAdmin's views
are compiled into its assembly and a theme's templates outrank a module's, so a
module cannot override the admin `Layout`. Orchard Core 3.0.1 provides a
`Navbar` model that modules contribute to through a display driver —
`OrchardCore.Admin`, `OrchardCore.Localization` and `OrchardCore.Notifications`
all add their navbar items that way — so WorkMate uses the same door.

Direction is not reimplemented. 3.0.1 already lays the admin out right to left
under an RTL culture, ships a `bootstrap-rtl` resource, and exposes
`Orchard.IsRightToLeft()` and `Orchard.CultureDir()` to views. WorkMate's own
stylesheet uses CSS logical properties, so there is one sheet rather than a
mirrored pair.

The approval badge renders in its empty state because `WorkMate.Approvals` owns
the pending-task count and does not exist yet. The driver moves to that module
when there is something to count.

The two pickers are placeholders that render a disabled control saying what
they are waiting for, with a hidden field preserving any value already on the
record — so opening and saving a form cannot erase a placement that a recipe or
an import put there.

## Depends on

- `WorkMate.Core` — for `BilingualText` and `EffectiveRange`.
- Orchard features: `OrchardCore.Settings`, `OrchardCore.Roles`,
  `OrchardCore.Localization`, `OrchardCore.Admin`, `OrchardCore.Navigation`,
  `OrchardCore.Resources`.

Every other WorkMate module depends on this one.

## Recipe steps

This module ships no recipe step of its own. It is configured by the base
recipe, `recipes/base.recipe.json`, which owns the six steps of specification
section 3 and is verified by applying it to a fresh tenant.

`WorkMate.Integration.Tests` stands a real tenant up with it — the same file,
through the same setup endpoint, on SQLite — and asserts what the tenant
actually holds: the six roles, each role's effective permissions, a reachable
`/Login` with no `/Register`, and the `en`/`ar` cultures. One tenant serves the
whole collection, so the suite runs in about twenty seconds.

Two things about recipes in Orchard Core 3.0.1 that the base recipe had to be
built around, both found by applying it rather than by reading:

- Orchard finds recipes inside an extension or in the application's own content
  folder, and nowhere else. `/recipes` is neither, so
  `Directory.Build.targets` copies them into the host's content root on build.
- The `Roles` step ignores a permission name it does not recognise, without
  warning. `ManageGroupSettings` is a template for per-group permissions rather
  than a permission a role can hold, so granting it read correctly and did
  nothing. Until the integration suite asserts effective permissions after an
  apply, a permission name added to a recipe is worth checking on a real
  tenant.

## Localisation

Arabic translations live in `Localization/ar/WorkMate.Platform.po`, tracked
beside the code that raises the strings. `Directory.Build.targets` copies every
module's PO files into the host's content root at build time, where Orchard's
`ModularPoFileLocationProvider` finds them as
`Localization/{culture}/{FeatureId}.po`. English needs no file: it is the
msgid.

`LocalisationResourceTests` fails the build if a localised string has no
Arabic entry, or if an entry survives a string that has gone.
`UserVisibleStringTests` fails it if a string reaches a user without going
through the localiser at all: every Razor view is checked, and so is every call
to a declared list of C# sinks whose string argument is rendered. Permission
descriptions are the one exclusion, because Orchard localises those as data
through `OrchardCore.DataLocalization`; the exclusion is pinned by a test so it
cannot grow quietly.

`dotnet publish` from a clean checkout is part of this: MSBuild evaluates
content globs before any target runs, so the build-time copy has not happened
yet when publish decides what to include. `Directory.Build.targets` adds the PO
files and the recipes to `ResolvedFileToPublish` directly. Verified by
publishing from a clean state and running the output.

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
