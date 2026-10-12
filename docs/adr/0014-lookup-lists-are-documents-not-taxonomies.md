# ADR-0014: Managed lookup lists are WorkMate documents, not Orchard taxonomies

**Status:** accepted, **pending second review**
**Date:** 2026-10-12
**Deciders:** Project lead
**Departs from:** technical specification section 5, "Field types" table

## Context

Specification section 5 names the mechanism for a lookup field:

| WorkMate field | Backed by | Notes |
| --- | --- | --- |
| Lookup | Taxonomy field | The one place taxonomies are used: flat lookup lists only |

CLAUDE.md repeats the permission: "Taxonomies are permitted only for flat lookup lists in
`WorkMate.Records`." The specification's own reading instructions say that where it states a
mechanism, "the mechanism is the decision and the exact API is a detail to confirm" — so
departing from it is an ADR, which is what this is.

The requirement is unchanged either way. A tenant maintains lists — job title, grade,
employment type, nationality, document type, relationship, currency — where each entry has a
**code**, an **English name**, an **Arabic name**, and a flag saying whether it is still
offered. Entries are **retired**, never deleted, because last year's records still refer to
them and a report about last year still has to read them. The lists travel in recipes and
exports like every other piece of tenant configuration.

## The decisive fact

Verified against the pinned assemblies with `tools/api-probe`:

```
OrchardCore.Taxonomies.Fields.TaxonomyField  [OrchardCore.Taxonomies]
   prop String TaxonomyContentItemId { get; set; }
   prop String[] TermContentItemIds { get; set; }

OrchardCore.Taxonomies.Models.TaxonomyPart  [OrchardCore.Taxonomies.Core]
   prop String TermContentType { get; set; }
   prop List`1 Terms { get; set; }
```

A taxonomy field stores **content item ids**. Every portable thing on this platform is named
by **code**, deliberately and at length. ADR-0011 states the rule and the reason in one
sentence: "References by code, never by id — the rule every other step follows, and the reason
a recipe is portable at all." Dimension types, structures, dimension records, employees, head
appointments and employee assignments all obey it; `WorkMate.Dimensions`' export emits parents
before children precisely so that every reference resolves by code at import.

A content item id is generated in the tenant that created it. A recipe carrying one is a
recipe that means something in exactly one tenant, which is the opposite of what a recipe is
for. So a taxonomy-backed lookup needs a code-to-term-id resolution layer on the import side
and a term-id-to-code layer on the export side — and that resolution layer is most of what a
document gives us for nothing.

## Three secondary costs

**The model gets built either way.** A taxonomy term is a content item. To carry a code, an
Arabic name and an active flag it needs a term content type with a part defining those three
fields — which is the same model this decision writes as a document, stored less directly and
queried through a content type created at runtime.

**It adds an admin menu entry.** Enabling `OrchardCore.Taxonomies` brings Orchard's own
Taxonomies screen into the admin menu. The product owner has ruled that the form designer adds
**one** menu entry, `People → Forms`, and that everything else is reached from an existing
screen. `OrchardCore.Taxonomies` is not enabled in `recipes/base.recipe.json` today, so this is
a cost we would be choosing to take on rather than one we already carry.

**Orchard's generic taxonomy editor bypasses the retire rule.** It offers Delete on a term.
Retire-not-delete is the whole safety property of a lookup list: a deleted grade is a grade
that last year's job-detail rows still name and that nothing can now resolve. A model whose
own editor can break its invariant is not holding the invariant.

## Precedent

ADR-0002 already decided that the dimension engine is not taxonomies, for a related family of
reasons — per-level attributes, effective dating, and descendant queries at scale. This is a
smaller case of the same judgement: Orchard's taxonomy is a content-authoring facility, and
what is wanted here is tenant configuration with a natural key.

## Decision

**A managed lookup list is a `LookupListDocument` in `WorkMate.Records`, owned by
`ILookupListService`, with its own `LookupListIndex`, its own two admin screens, its own
`lookup-lists` recipe step, and its own export switch on `RecordsDeploymentStep`.**

- A **list** has a code (immutable, unique in the tenant), a bilingual name, and an ordered set
  of items.
- An **item** has a code (immutable), a bilingual name, an active flag and a sort order.
- **Items are never removed, only retired.** The service refuses a write that drops an item the
  tenant already holds, and refuses a change to an item's code, with the message naming the
  item and saying to retire it instead. This is the same rule
  `DimensionTypeService.UpdateAsync` already enforces on an attribute schema, reached for the
  same reason: removing something that holds data destroys the data.
- **A list may be deleted only while it has no items.** With no items there is provably nothing
  to lose, which makes "I mistyped the code" recoverable without opening a hole.
- English names are required, Arabic optional unless the tenant sets `RequireArabicNames`
  (ADR-0003's addendum), asked through `IBilingualNamePolicy` like every other write path.
- Everything goes in and out as JSON naming things by code, with ADR-0008's comparison rules on
  re-run and ADR-0011's "the export writes the import format" rule.

**`OrchardCore.Taxonomies` is not enabled and this module does not reference it.**

## What this does not decide

**How a field binds to a list.** That is slice B2 and its own decision (proposed as ADR-0016):
a bound field keeps the field type it already has and stores a plain code string; what changes
is which editor renders it. Nothing in this ADR depends on that, and nothing in that decision
depends on this one — a taxonomy-backed list would have needed the same binding mechanism, and
would have stored a tenant-specific id in it.

**Whether a lookup list is the right home for positions or employee types.** Both are open
product questions. Nothing here presumes an answer: a list stores codes and names, and a code
in a text field is the one representation that does not foreclose becoming an entity later.

## Consequences

A tenant's lookup lists are portable from the day they exist, with no resolution layer, and
`LookupListsExportRoundTripTenantTests` proves it the way ADR-0011's round trip proves the
dimension importers.

The module ships two screens and one editor it would otherwise have got free from Orchard.
That is the price, and it is paid once: roughly four hundred lines, against a resolution layer
on both sides of every recipe and export, for ever, plus an admin screen that can delete a term
that records still name.

**The specification's section 5 field table should be corrected when this is confirmed**, so
that the "Lookup" row reads `LookupListDocument` and the sentence about taxonomies being
permitted for flat lookup lists is removed from CLAUDE.md's hard boundaries — or kept, as a
permission nothing currently uses. That is a documentation change for the reviewer to direct,
not one taken here.

**If this is overturned**, the blast radius is contained and is worth stating so the reviewer
can price it: `LookupListDocument`, `LookupListIndex`, `LookupListService`, the two admin
screens, `LookupListsRecipeStep` and the export's `IncludeLookupLists` branch — about six files
— are replaced by a taxonomy content type, Orchard's screens, and the two resolution layers.
`ILookupListService`'s surface is the seam: nothing outside it knows how a list is stored, so
callers written in later slices do not change either way.
