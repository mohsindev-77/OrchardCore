# ADR-0004: Lucene is the search provider for shared cloud

**Status:** accepted
**Date:** 2026-09-30
**Accepted:** 2026-10-01
**Deciders:** Head of Technology

## Context
Open technical decision 7 in section 11 of the technical specification asks
which search provider WorkMate uses, with the stated default "Lucene for
shared cloud; Elasticsearch offered at Enterprise". The base recipe has to
name a search feature, so the decision cannot stay open any longer than the
base recipe's first working version.

Verifying the feature ids against the pinned Orchard Core 3.0.1 found that the
id the base recipe was drafted with has been retired. In 3.0.1:

| Feature id | Display name | Depends on |
| --- | --- | --- |
| `OrchardCore.Lucene` | Lucene | `OrchardCore.Queries.Core`, `OrchardCore.Indexing`, `OrchardCore.ContentTypes` |
| `OrchardCore.Search.Lucene` | **Lucene (Obsolete)** | `OrchardCore.Lucene` |
| `OrchardCore.Search.Elasticsearch` | **Elasticsearch (Obsolete)** | `OrchardCore.Elasticsearch` |

`recipes/base.recipe.json` as bootstrapped enabled `OrchardCore.Search.Lucene`,
the obsolete id. It would still apply, because the obsolete feature depends on
the current one, but it names a feature Orchard has marked for removal and
would fail a future upgrade.

Orchard Core 3.0.1 also reorganised search generally: `OrchardCore.Indexing`
is now the common indexing feature and `OrchardCore.Search` the common search
surface, with Lucene and Elasticsearch as providers underneath rather than as
two parallel stacks.

## Decision
Lucene is the search provider for shared cloud, as the specification's default
proposed. The base recipe enables `OrchardCore.Lucene`, the current feature id
in 3.0.1, not `OrchardCore.Search.Lucene`.

Elasticsearch is offered at the Enterprise edition later. It is not enabled by
the base recipe; it will be an edition-recipe concern, decided when the first
Enterprise deployment is scoped, and it will use `OrchardCore.Elasticsearch`
rather than the obsolete `OrchardCore.Search.Elasticsearch`.

No WorkMate code takes a dependency on either provider. Modules index and
query through Orchard's provider-neutral indexing and search abstractions, so
that offering Elasticsearch at Enterprise stays a recipe change.

## Consequences
Section 11 decision 7 is settled and has been struck from the open list.

Shared cloud gets search with no additional infrastructure, which is the point
of choosing Lucene: it is in-process and needs no separate cluster to operate
or pay for.

Lucene's index lives on the tenant's file system, so a multi-node shared cloud
deployment either pins a tenant to a node or rebuilds the index per node. That
constraint is the reason Elasticsearch exists as the Enterprise answer, and it
should be checked against open decision 6 (distributed cache) when that is
taken, since both bear on whether a tenant can move between nodes freely.

Because no module depends on a provider directly, the provider-neutral rule
has to be enforced by review: a module that reaches for a Lucene type makes
the Enterprise offer a code change instead of a recipe change.
