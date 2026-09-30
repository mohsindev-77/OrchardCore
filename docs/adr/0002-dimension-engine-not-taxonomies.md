# ADR-0002: Organisation structure uses the dimension engine, not taxonomies

**Status:** accepted
**Date:** 2026-09-29
**Deciders:** Product and technology leadership

## Context
Orchard Core taxonomies store a term tree as nested data in a single content
item, declare one term content type per taxonomy, and carry no effective
dating. Organisation structure needs per-level attributes, several axes per
record, mid-period transfers, historical resolution and fast descendant
queries at thousands of employees.

## Decision
Build the dimension engine described in `docs/dimension-engine-architecture.md`:
definitions as configuration, records as runtime content types, links and a
closure index as custom tables, employee assignments effective-dated and
allocatable. Taxonomies are permitted only for flat lookup lists.

## Consequences
The first build slice is the engine (prompts 2 and 3). Any customer built on a
taxonomy-based structure would have to migrate; none should be started that way.
