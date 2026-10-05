# ADR-0009: Migrations are append-only

**Status:** accepted
**Date:** 2026-10-05
**Deciders:** Project lead

## Context

A tenant hit `SQLite Error 1: 'table DimensionRecordPartIndex has no column named NameAr'` the
moment the `dimension-records` recipe step tried to create a record, followed by a knock-on
"there is no dimension record with the id …" once the failed write left nothing to find.

`DimensionRecordPartIndex.NameAr` has existed on the index class since the record layer was first
built. The column was missing from one tenant's actual table because of how it got there: it was
added by editing the body of `UpdateFrom1Async` — already shipped, in an earlier commit — rather
than by a new `UpdateFromNAsync` step. Orchard's migration runner records, per tenant, the highest
version number each `DataMigration` class has reached; a tenant that had already executed
`UpdateFrom1Async` before the edit landed was recorded at version 2 and never ran it again, because
nothing about the recorded version changed. `CreateAsync` and `UpdateFrom1Async`'s own body — the
one place `DimensionRecordPartIndex` is created — were otherwise untouched; this was the only such
edit in either module's migration history, confirmed by reading every commit that has ever touched
`src/WorkMate.Dimensions/Migrations.cs` and `src/WorkMate.Platform/Migrations.cs`.

The tests never caught it because every test builds a tenant from nothing, on the current code —
exactly the one case this mistake does not break. Nothing exercised a tenant that had stopped
partway through an older version of the schema.

## Decision

**A migration step that has shipped is never edited again.** `CreateAsync`'s job is to produce the
complete, current schema for a tenant that has never run a migration before; every later schema
change — a column, an index, a new table — is a new `UpdateFromNAsync`, regardless of how small the
change is or how recently the step it would otherwise touch was written. `CreateAsync` (or
whichever step currently plays that role for a given table) keeps being updated to match the latest
schema, because a brand-new tenant must get it in one pass without a second step patching the
first; what never happens is touching the body of a step a tenant could already be past.

**Every schema change ships with an upgrade test**: build the tables as the version before the
change left them, run the migration forward, and assert the result matches a fresh install.
`tests/WorkMate.Integration.Tests/DimensionsMigrationUpgradeTenantTests.cs` is both the regression
test for this specific defect and the general safety net: one test reproduces the exact historical
state — version 2, `NameAr` physically absent — upgrades it, and then writes and reads a record
with a real Arabic name through `IDimensionService`, the same operation that failed in the field,
not merely a check that a column exists; a second, `Theory`-driven test compares every index
table's live columns on a freshly migrated tenant against its C# class's own declared properties,
for all seven tables this module defines, so a future instance of this mistake — on any table, not
only this one — fails immediately on a fresh tenant rather than waiting for a tenant stuck on an
old schema to hit it at runtime.

**The fix itself — `Migrations.UpdateFrom3Async`** — has to tolerate both states at once, because
`UpdateFrom1Async`'s body still creates `NameAr` for a brand-new tenant (per the rule above, that
edit was not reverted): it checks whether the column already exists before adding it, since SQLite
and SQL Server both refuse to add a column that is already there. That check could not use the
obviously provider-agnostic `DbConnection.GetSchema("Columns", …)` — verified directly against
`Microsoft.Data.Sqlite` 10.0.8, the provider this solution's tests run against, it does not
implement the `"Columns"` schema collection at all, and calling it took down tenant setup entirely
for every test until this was found. The check instead queries each dialect directly: SQLite's
`PRAGMA table_info`, ANSI `INFORMATION_SCHEMA.COLUMNS` for SQL Server.

## Consequences

CLAUDE.md's migrations rule is now explicit about append-only and the upgrade-test requirement, not
left implicit in "migrations are additive."

A tenant on the affected version upgrades automatically on its next restart: the application's
normal startup activation detects the pending `UpdateFrom3Async` step and runs it, the same path
`IDataMigrationManager.UpdateAllFeaturesAsync` exercises in the test. No manual database change is
needed.

The audit this ADR is based on covered both modules with schema today (`WorkMate.Dimensions`;
`WorkMate.Platform` owns no tables, so there was nothing to check there) and found exactly one
instance of the mistake. Nothing else needed a repair migration.
