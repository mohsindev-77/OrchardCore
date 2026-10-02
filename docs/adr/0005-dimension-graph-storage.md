# ADR-0005: Graph storage for the dimension engine

**Status:** accepted
**Date:** 2026-10-01
**Deciders:** Head of Technology

## Context
Section 4 of the technical specification stores `DimensionLink`,
`DimensionClosure` and `EmployeeAssignment` as "custom YesSql index tables
with no content item behind them", and section 4 of the dimension engine
architecture describes the closure index as four columns: structure,
ancestor, descendant, depth.

Verifying this against the pinned Orchard Core 3.0.1, and the YesSql 5.4.7
those packages declare, found three things.

**A YesSql index row cannot exist on its own.** `YesSql.Indexes.MapIndex`
carries a `Document`, index rows are produced by an `IIndexProvider` from a
document, and `YesSql.ISession` has no API that inserts an index row
directly. The only ways to populate a bare table are raw SQL outside a
migration, which rule 2 of the specification forbids, or a document behind
the index. The specification's phrase "no content item behind them" rules out
content items, which is the real constraint; it does not and cannot rule out
a document.

**The four-column closure cannot answer a dated descendant query.** Links are
effective-dated and section 5 requires that "a report for a prior period
resolves the links effective then and returns the old shape". An undated
closure describes only today, so every historical descendant query would have
to walk the link table instead — a second code path for the queries payroll
reproduction depends on, and the slow one.

**`DateOnly` cannot be an index column.** Measured against YesSql 5.4.7
rather than recalled: both `SqliteDialect` and `SqlServerDialect` map
`typeof(DateOnly)` to `DbType.Object`, and `GetTypeName(DbType.Object, …)`
then throws `DbType not found for: Object`. `DateTime` and `Int32` map
normally. A migration that declares a `DateOnly` column therefore fails at
tenant setup, not at review.

## Decision

**Documents behind the index tables.** Each of the three graph tables is a
YesSql map index over a plain WorkMate document — not a content item. The
document grain is one document per maintenance and validation unit:

| Document | Grain | Index rows it produces |
| --- | --- | --- |
| `DimensionLinkDocument` | one per structure and record | one per dated parent edge |
| `DimensionClosureDocument` | one per structure and descendant | one per dated ancestor pair, including the self pair at depth zero |
| `EmployeeAssignmentDocument` | one per employee and structure | one per dated assignment |

This grain is chosen because it is simultaneously the validation boundary and
the maintenance boundary. No-overlap, allocations totalling 100 percent and
exactly-one-primary are all rules about one employee on one structure, so
they are decidable within a single document. A move re-saves one document per
node in the moved subtree, which is exactly the "only the subtree is touched"
rule in architecture section 4. Because YesSql regenerates a document's index
rows on save, an index row cannot drift from its own document, which narrows
the verification command's job to comparing closure against links.

Writes to all three use `ISession.SaveAsync(document, checkConcurrency: true)`
and each document carries a `long Version`. Two administrators editing the
same subtree concurrently get a `YesSql.ConcurrencyException` on the losing
write rather than a silent overwrite.

**Dated closure rows.** A closure row carries an effective range as well as a
depth: structure, ancestor, descendant, depth, effective from, effective to.
The range is the intersection of the effective ranges of the links along the
path from ancestor to descendant. One dated query then answers both "under
this node today" and "under this node last March", with one code path and one
index. Future-dated links produce closure rows whose range begins in the
future, so a pre-built structure resolves as empty today and correctly on the
day it opens, with no separate activation step.

The closure table is indexed on structure, ancestor, effective from and
effective to, which is the column order the dated descendant query uses.

**Calendar dates are stored as midnight `DateTime`.** `EffectiveRange` and
`DateOnly` remain the currency of every service signature and every domain
model. Conversion to and from the index columns happens in one type,
`EffectiveDates`, which also owns the open-ended sentinel. It sits beside the
index classes rather than inside the internal graph namespace, because the
configuration indexes carry dates too. The sentinel
is 9999-12-31 at midnight, not `DateTime.MaxValue`: SQL Server's `datetime`
tops out at 9999-12-31 23:59:59.997 and `DateTime.MaxValue` overflows it.

## Consequences

**This changes the architecture document.** Section 4's closure index is
described as four columns and must become six; the table of queries in that
section gains an effective-date predicate on every row. The maintenance
description ("Create", "Move", "Delete") is unchanged in shape but now writes
dated rows. Section 4 of the technical specification needs the word
"document" where it currently implies a bare table. Neither change alters a
decision in either document; both make explicit what the pinned version
forces.

Storage grows with history rather than staying proportional to the live
structure, because a reparenting closes dated closure rows instead of
deleting them. That makes open question 6 in the architecture document — the
retention policy for closure rows and assignment history on a long-lived
tenant — a question that now has to be answered, not merely noted.

The documents are an extra table's worth of rows in the tenant's shared
`Document` table: roughly one per node per structure for links, the same
again for closure, and one per employee per structure for assignments. A
YesSql collection would move them to their own `Document` table, but
collections are passed as a string on every `Save`, `Query` and
`CreateMapIndexTable` call, and one missed call silently reads the wrong
table. That trade is not worth taking before there is a measurement saying it
is needed; if it becomes needed it is a data migration, not an additive one.

Because `DateOnly` never reaches the database, a reviewer cannot tell from an
index class whether a date column was converted correctly. `EffectiveDates`
is therefore covered by a round-trip test over the boundary values — the
sentinel, an early date, and the day either side of each — on every database
provider the test suite can reach.
