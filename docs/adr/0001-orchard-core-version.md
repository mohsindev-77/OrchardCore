# ADR-0001: Pin the Orchard Core version

**Status:** proposed — becomes accepted when the version is confirmed and the first restore succeeds
**Date:** 2026-09-29
**Deciders:** Head of Technology

## Context
Orchard Core's APIs move between releases. The technical specification names
mechanisms, not exact API shapes, and requires every mechanism to be verified
against one pinned version. Without a pin, two sessions can build against two
different API surfaces.

## Decision
Pin a single Orchard Core version in `Directory.Packages.props` via the
`OrchardCoreVersion` property. The placeholder is the latest stable observed on
2026-09-29; confirm with `dotnet package search OrchardCore.Module.Targets`
before the first restore, record the confirmed version here, and update the
"Pinned version" line in `docs/technical-specification.md`.

Upgrading the pin is itself a new ADR, because it can change how the
specification's mechanisms behave.

## Consequences
Every prompt in the library begins by verifying APIs against this version.
Divergences are recorded as ADRs, not worked around.
