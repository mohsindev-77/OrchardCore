# ADR-0001: Pin the Orchard Core version

**Status:** accepted
**Date:** 2026-09-30
**Deciders:** Head of Technology

## Context
Orchard Core's APIs move between releases. The technical specification names
mechanisms, not exact API shapes, and requires every mechanism to be verified
against one pinned version. Without a pin, two sessions can build against two
different API surfaces.

## Decision
Pin Orchard Core **3.0.1** (latest stable, released 2026-07-09, confirmed on
NuGet 2026-09-30) via the `OrchardCoreVersion` property in
`Directory.Packages.props`.

Orchard Core 3.0.1 targets .NET 10 only, so the solution target framework moves
from net9.0 to **net10.0** and `global.json` requires SDK 10.0.100 or later.

The NuGet audit flagged two packages Orchard Core pulls in. They are pinned to
patched versions with central transitive pinning:
- AngleSharp 1.4.0 -> 1.8.2 (GHSA-pgww-w46g-26qg, fixed in 1.5.0)
- System.Security.Cryptography.Xml 10.0.8 -> 10.0.12 (GHSA-23rf-6693-g89p and
  four related advisories, fixed in 10.0.10)

Upgrading the pin is itself a new ADR, because it can change how the
specification's mechanisms behave.

## Consequences
Every prompt in the library begins by verifying APIs against 3.0.1.
Divergences are recorded as ADRs, not worked around. The security overrides are
reviewed at each Orchard Core upgrade and removed once Orchard ships the patched
versions itself.