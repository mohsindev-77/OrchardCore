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

> **Amended 14 October 2026 (D27).** There is no third entry under *Settings*. The Mapping screen is
> dropped and ADR-0021 is withdrawn, so *Settings* holds **Dimension types** and **Structures** and
> nothing else. Nothing else in this ADR changes.

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

---

## Amendment — what manual testing on a real tenant changed (2026-10-14)

**Status:** accepted, **pending second review**. Decision **D24**. Where this and the text above
disagree, this wins.

The menu above was built, shipped to a reviewer and tested on an existing tenant. Four things were
wrong in practice and one was missing. None of them changes the decision; all of them change what
it has to do to hold.

### 1. An existing tenant has no authored plurals, and the fallback is visible everywhere

The decision says a plural is authored and a type with none reads as its singular — *wrong-looking,
never wrong*. On a tenant created before the editor had the field that is **every entry**:
*Department*, *Division*, *All Department*, down the whole menu. "Never wrong" was the right trade
for one dimension and the wrong one for all of them.

`Migrations.UpdateFrom11Async` fills an unauthored plural from **`StandardDimensionPlurals`**, a
frozen lookup of the names this product ships. It is a lookup and not a rule: there is no suffix, no
algorithm, and nothing at all for a name that is not on the list. The two halves are looked up
independently — English by the English name, Arabic by the Arabic — because the reference recipes
use both قطاع and قسم for *Division* and each pluralises its own way.

**"Unauthored" has to mean blank *or the singular repeated*.** `UpdateFrom10Async` cleared session
D's guesses by writing the singular as a deliberate, visible placeholder, so that is the state every
upgraded tenant is actually in. A step that only filled blanks would have reached none of them.

**For a name the table does not know, the phrasing changes rather than the word.** The group keeps
its singular, and the entry that opens the whole dimension reads **"All Workstream units"**. That is
the same principle one level out: the sentence is the localiser's, the noun is the customer's, and
nothing invents either.

### 2. Two values of the same name are indistinguishable

The decision anticipated this and declined to fix it: *the bracket disambiguates organisations, not
values… the list screen is where that question is answered*. On Crescent, which has two departments
called **Credit** and two called **Operations**, that produced two identical menu entries — and
sending a reader to another screen to find out which is which is not an answer.

**When two values in a group read the same, the caption gains the parent**: *Credit – Lahore Main
Branch*. Asked **per language**, because a tenant can translate one of two same-named units and not
the other. The parent rather than the code, because *Lahore Main Branch* is what a reader knows.

The values are now also **ordered by caption rather than by name**, so the two Credits sort by their
parents instead of by whichever generated id happened to fall first.

### 3. The organisation bracket was unreadable, and Q-l is answered

*Civil (Zenith Engineering & Construction)* in a sidebar is a caption nobody can read, and the part
that gets truncated is the part that disambiguates. The ADR raised this as **Q-l** and recommended
an authored short name; the product owner has taken it.

**`StructureDocument.ShortName`** is a new optional bilingual field on the structure editor, carried
by the `structures` recipe step and the export, falling back to the full name. Authored and never
derived, for the reason the plural is.

### 4. A long caption still needs its full text, and Orchard has nowhere to put it

`MenuItem` carries `Text`, `Href`, `Id`, `Classes`, `Permissions` and `RouteValues` — **no title and
no attribute bag** (verified against the pinned assemblies with `tools/api-probe`) — and the admin
theme's templates are compiled into `TheAdmin.dll`, where a module cannot override them and a theme
would win if it could.

So the tooltip is a script this module ships (`workmate-organisation-menu`), required on admin pages
by `OrganisationMenuTooltipFilter`. It copies each menu link's own text into its `title`; the text is
already complete in the DOM and the truncation is CSS. **This is an extension of Orchard's rendered
output, not a change to Orchard** — the alternative would have been editing the theme, which
CLAUDE.md forbids outright.

### 5. The value page needed a tree, and two honest headcounts

**"0 here" was a lie by omission.** A division's own headcount is zero and always will be: the
engine refuses to place an employee on a unit that has units under it, because they would be counted
by it and again by every roll-up beneath. The page now says **"N directly · M including units
below"**, each linking to the employee list — the second with *Include sub-units* ticked — and M is a
**distinct** count across the subtree rather than a sum of the direct ones.

**And a value with children now shows what is under it.** "Units under *name*": every dimension, not
just this one, as at today, active by default with an **Include retired** toggle, indented, with
expand/collapse per node and Expand all / Collapse all. Each row links to that unit's own page.
**A leaf shows no section at all.**

Two things make that affordable. The whole subtree costs a fixed handful of queries — one closure
read, one batched ancestor-path read for the parents, one batched direct headcount, and one read of
the subtree's placements rolled up in memory as sets of employee ids — with nothing asked per node.
And the rows carry the **same contract** as the dimension list's tree view, so the collapse script
that already existed serves both screens rather than a second one being written and kept in step.

**"Include retired" is a different question, not a filter.** Retiring a unit closes its record *and*
its placement, so a retired department is not "placed here but ineffective" — it is gone from
today's subtree. Showing it means asking an undated question, which is
`IDimensionGraphService.GetDescendantsEverAsync`: everything that is or has ever been under this
node, one row per unit at the shallowest depth it ever sat.

---

## Second amendment — three views of a value's subtree (2026-10-14)

**Status:** accepted, **pending second review**. Decision **D25**.

The first amendment gave a value's page a tree of everything under it. A reviewer asked for two more
renderings of the same thing and, more importantly, for an explicit guarantee about what must *not*
change.

### What must not change, and is now asserted

The **organisation designer** and the **"All *plural*"** list screens stay exactly as they are.
`UnchangedScreensTenantTests` says so in four assertions: the list screen carries none of the value
page's controls and keeps its own toolbar and Tree/List toggle; the designer still renders editable
cards with their ⋯ menus, its own zoom controls and its nine named card actions.

This is a test rather than a note because three briefs running have added something to the value
page, and each time the cheapest implementation would have put a little of it on one of these two
screens as well. Nothing stops that except an assertion that says what they are.

### The three views

A switch at the top right of the section — **Hierarchy | Tree | List** — with **Tree** the default,
because Tree is what the section already was.

**One subtree read feeds all three.** The controller reads the subtree once, as before, and shapes
the same rows into a flat list, an indented tree and a nested chart. That is the point: three
renderings of one answer cannot disagree about what is under what, which is the drift the designer's
own two views were built to avoid.

**The hierarchy reuses the designer's layout and none of its behaviour.** The boxes are
`li.designer-node > .designer-card + ul.designer-children` inside a `.designer-surface`, so the
connector lines, the row levelling and the RTL mirroring come from `organisation-designer.css` —
already written in logical properties, so already correct in both directions. Three CSS rules were
added and no existing selector was touched. The designer's *script* is deliberately not reused: it
drags, fetches and edits, and this chart is read-only, so its expand, zoom and fit are a few lines
in `workmate-dimension-records`.

**`zoom`, not `transform: scale`** — the designer's own reasoning, which applies here for the same
reason: a transform leaves the layout box at its original size, so the viewport's scrollbars keep
describing the unscaled chart and half of a zoomed-in org chart becomes unreachable. Scaling the
layout is what keeps the horizontal scroll honest at every level, in both directions.

### The view is in the URL, and only then in storage

`?view=hierarchy|tree|list`. A view that lives only in a cookie or in storage is a view nobody can
link to and the back button cannot return to, which is the same argument the dimension list's own
Tree/List toggle was built on.

The reader's last choice is remembered in `localStorage`, wrapped in `try`/`catch` because storage
throws rather than returning null in a browser configured to forbid it. **A URL that names a view is
never overridden**, and a remembered one is applied with `location.replace` so that restoring a
preference does not put a page the reader never asked for into their history.

### What this does not add

No editing. D25 is explicit, and the chart has no ⋯ menu, no drag, and no action of any kind beyond
following a link to a value's own page. Editing stays where it already is: the organisation designer
and the value pages.
