# ADR-0024: The Organisation menu is one expandable entry per dimension name

**Status:** accepted, **pending second review**.
**Date:** 2026-10-13
**Deciders:** Project lead, on the product owner's decision **D23** of 13 October 2026, which
supersedes the shape agreed in **D3** and **D17**.

## Context

FRS v2.5 §1.2 asks for it plainly:

> *"All dimensional master tables (Business Unit, Division, Department, Section, Position, Grade,
> Cost Centre …) should appear as separate menu items under Organization."*

**This was built once, shipped, and the product owner withdrew it on sight.** On a tenant with the
base recipe and three demo organisations loaded it read:

> Branchs · Cost centres · Departments · Departments · Departments · Divisions · Divisions ·
> Divisions · Projects · Regions · Sections · Teams

Three separate things were wrong, and conflating them is why the first fix did not work.

### 1. The plural was derived

`DimensionTypeMenu.DefaultFor` appended an `"s"` to the English name and left the Arabic alone.
*Branch* became *Branchs*. Arabic has no suffix rule to guess with at all — قسم pluralises to أقسام
by changing the word — so every Arabic entry read as its singular.

### 2. The order came from the caption

`LocalizedString.PrefixPosition()` sorts by the rendered text. An Arabic reader therefore got an
alphabetically-Arabic menu and an English reader an alphabetically-English one: two different menus
for one tenant, for an order that is a product decision — Business Unit above Division above
Department — rather than an accident of spelling.

### 3. The tenant genuinely held three Department types

Not a menu defect at all. Three demo organisations each declared their own `Division` and
`Department` dimension type in one tenant, and the menu rendered one entry per **type**.

**Decision D17 proposed fixing this in the data** — have the demo recipes share the standard types —
and that was **built and reverted** while building slice F2. A dimension type is a *level of every
structure that declares it*, and `IDimensionService.CreateAsync` writes a self pair on all of them.
So a shared `division` type put every Crescent division on the Zenith axis as a parentless root, and
the Zenith chart drew Crescent's divisions. Eight integration tests failed; `DemoCompanyRecipesTenantTests`
named it:

> Expected `ChildCodesAsync(services, "zenith-org", null)` to be equal to `{zenith-div-engineering,
> zenith-div-projects, zenith-div-corporate}`, but `{…, crescent-div-head-office,
> crescent-div-branch-network}` contains 2 item(s) too many.

That is ADR-0005 working exactly as designed — one record, several axes. The demo tenant is the odd
case: two unrelated organisations in one tenant, which no real tenant is.

A second attempt disambiguated the captions instead, by having each recipe author a company-qualified
plural: *Zenith Departments*, *Crescent Departments*. It worked and it was wrong — a tenant reading
its own company name on every menu entry, to work around a defect in the menu.

## Decision

**One menu entry per dimension *name*, expandable to that dimension's own values.**

```
Organisation
    Organisation designer          ← unchanged, first, exactly where it was before slice F2
    (Organisation tree)            ← position reserved; slice F8
    Departments                    ┐
        All Departments            │  the list screen, over every type in the group
        Civil (Zenith …)           │  the dimension's own active values, at most fifteen,
        Credit (Crescent …)        │  alphabetical in the reader's language
        …                          │
        Show all (17)              ┘  only when there are more
    Divisions
        …
    Settings
        Dimension types
        Structures
        (Mapping)                  ← position reserved; slice F3
```

### 1. Dimension types sharing an English singular name are one entry

Grouped on `Name.En`, trimmed, case-insensitively — `DimensionMenuComposition.KeyOf`. The English
name because ADR-0003 makes it the required half and because it is the one value that is the same
for every reader: an Arabic reader and an English reader see the same grouping, which a caption
could not guarantee.

**The group is a menu concept and nothing else.** It owns no records, it is not stored, and nothing
in the graph knows about it. Three Department types remain three types with three separate trees;
what changes is that one entry opens all of them.

### 2. The plural is authored, never derived

`DimensionTypeDocument.PluralName`, in both languages, on the dimension type editor. A group reads
the first authored plural among its types, in `MenuOrder` then code order; a group with none reads
its **singular** — wrong-looking, never wrong. `DimensionTypeMenu.DefaultFor` is frozen (ADR-0009)
and is no longer a source of captions; it survives as the *detector* `Migrations.UpdateFrom10Async`
uses to tell a guessed plural from an authored one.

### 3. Positions are explicit numbers; values are the one exception

The designer at `"1"`, the reserved tree at `"2"`, dimensions at `"5.n"` by `MenuOrder` then group
key, Settings at `"9"`. Never `PrefixPosition()`.

The **values under an entry** are deliberately ordered the other way: alphabetically in the reader's
own language, which is two different lists. Both are computed when the menu is built and both are
cached, because the alternative — taking fifteen by English spelling and rendering them to an Arabic
reader — would be neither order.

### 4. A value names its organisation when the entry spans more than one

*Civil (Zenith Engineering & Construction)*. Asked of the **group**, not of the type: one Department
type on one chart needs no bracket, three on three charts need one on every value — otherwise two of
the three read plainly and a reader cannot tell which company they are in. A type that is itself a
level of several charts gets no bracket, because "which organisation is this in" then has more than
one true answer.

The bracket disambiguates **organisations, not values**. Crescent has two departments called
*Credit*, one per branch, and both read *Credit (Crescent Microfinance)*. A name is not unique within
a tenant; a code is. The list screen behind *All Departments* is where that question is answered.

### 5. Fifteen values, then *Show all (N)*

`DimensionMenuGroup.MaxValues`. Past about this many the entries stop being a shortcut and start
being the thing the list screen already does better, and a sidebar that scrolls for a hundred
departments is the original complaint in a new costume. **Sorted, then cut**, so *Show all* means
"and the rest after these". The count is the whole dimension, which is what the list screen will
contain.

### 6. `ShowInMenu` hides a dimension; its screens stay reachable

D3's switch, per type. A hidden type contributes nothing to its group, and a group with no shown
type has no entry. The **list screen does not apply that filter** — the group is resolved from the
types, not from the menu — because hiding a dimension from a menu is not withdrawing it, and the
Dimension types list's *View units* link is still the way in.

### 7. The menu is cached per tenant per day, invalidated by signal

`DimensionCacheKeys.Menu(tenant, isoDate)`, tagged with `MenuSignal(tenant)`, through
`CacheSignals.SignalNowAndAfterCommitAsync` (ADR-0019) from `DimensionTypeService`,
`StructureService` and `DimensionRecordHandler`.

**This is the only dated read in the module that is cached**, and the module README's "dated graph
queries are not cached" rule still stands. It is the exception that proves it: there is exactly one
date the menu is ever asked about — today — and it is read on *every* admin request by a provider
that runs before the page it decorates. Rebuilding per request would put a query per dimension on
the critical path of every screen in the product.

The date is in the **key** rather than in a sliding expiry, because a menu built on Monday is a
correct answer about Monday: Tuesday asks a different question and gets a fresh build, rather than
being approximately right somewhere near midnight.

The invalidation for values lives on `DimensionRecordHandler`'s content hooks rather than on each of
`DimensionService`'s eleven success paths, so creating, renaming, correcting, retiring and merging
are all covered by one place that cannot be forgotten. Moving and cancelling a move touch only links
and signal for themselves.

### 8. Clicking a value opens that value's own page

`DimensionRecordsAdminController.Details` — code, both names, alias, dimension, parent, status and
dates, head, headcount, attributes, with **Edit**, **Move** and **Retire** beside them. Read-only:
every button is a link to a screen that already owns its rules, so there is no second write path and
no second set of validation messages.

### 9. The organisation designer is unchanged and first

Slice F2 moved it under Settings. That was wrong — a click path that had already shipped, changed
for no reason any brief asked for — and D23 A puts it back, with the same caption, the same action
and the same card actions. `DesignerCardActions.All` is asserted by name so that "unchanged" is a
statement rather than a hope.

## Consequences

**What a tenant sees.** A menu whose length is the number of *dimensions* rather than the number of
dimension types, with each dimension's values one click away and every value's own page one click
after that. On the ordinary tenant — one Department type — nothing is grouped, nothing is bracketed,
and no Organisation column appears.

**What it costs.** The menu now reads *values*, which no navigation provider did before, so the
cache and its invalidation are load-bearing rather than an optimisation. A tenant whose values change
constantly rebuilds the menu that often; the model is bounded at thirty entries per dimension (fifteen
per language) and the rebuild is one query per shown type.

**What is given up.** A reader can no longer tell from the menu that a tenant has three Department
types. That is the point — it is a fact about the tenant's configuration and belongs on the Dimension
types screen, which shows it — but it is a real loss for whoever was using the menu to notice it.

**What D17 keeps.** Its diagnosis, which was right: the duplication came from the demo data and not
from the menu mechanism. Only its remedy is replaced. The demo recipes keep their prefixed type
codes, and the Zenith recipe's `$notes` carry the account of what sharing them did.

**An open question for the product owner.** The bracket uses the **structure's authored name**, so
Zenith's departments read *(Zenith Engineering & Construction)* where D23's example wrote
*(Zenith)*. Nothing here abbreviates it, on the same reasoning that stopped the plural being
derived: a rule that shortens a customer's company name is a rule that will shorten one wrongly. If
a short form is wanted, it is a field on the structure, authored like the plural — recorded as
**Q-l** in the design document.

## Alternatives considered

| | Why not |
| --- | --- |
| **Share the dimension types across demo recipes** (D17) | Built and reverted. A type is a level of every chart that declares it, so each company's chart grew the others' units. ADR-0005 working as designed |
| **Company-qualified plurals** (*Zenith Departments*) | Built and reverted. Disambiguates by making a tenant read its own company name on every entry, which is a worse menu than the one being fixed |
| **One entry per type, deduplicated by caption** | Hides the duplication instead of resolving it, and leaves two entries opening two screens that look identical |
| **A flat entry per dimension, no values** | What the FRS literally asks for, and what shipped. The product owner's D23 asks for the values; a dimension's list screen is a search box away from being the same thing, and the menu is where a reader looks first |
| **Sort the values at render time** | Correct, and would mean caching every value of every dimension rather than fifteen per language. The cap is what makes the cached model bounded |
| **Group on the content type or a new stored group id** | A stored group is a migration, an editor and a second thing to keep in step with the name. The name is already the thing two types share when a reader calls them the same dimension |
