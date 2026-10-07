# Prompt 3 — Organisation designer, recipe steps and export

Branch `prompt-03-org-designer`. Spec section 4; architecture sections 5, 6 and 7.

---

## What this adds

### Screens

| Screen | What it does |
| --- | --- |
| **Dimension types** | List and editor, with the attribute schema builder (field name, kind, required, bilingual label). Creating a type generates the backing content type and shows its name. |
| **Structures** | Four blocks: the types this structure uses in reading order; which of them may sit at the top; a **grid of what may sit under what**; and a *build from a simple chain* button that fills the last two from the first. Changing a structure that already has records placed on it previews the impact and refuses a change that would invalidate a live placement. |
| **Organisation designer** | One tree per structure, in **chart** or **list** view. Expand/collapse, search, pan, zoom, fit-to-screen, an effective-date control, and an unplaced-records panel. Card actions: **Add unit, Rename, Move, Merge, Retire, Cancel a move**, each with a dry-run preview before commit. Move by drag or by picker. Bilingual, RTL, keyboard-reachable. |

### Engine changes

- **Containment map (ADR-0010).** A structure carries explicit `(parent type → child type)` pairs and a set of root types, replacing arithmetic on level ordinals. This is what makes ragged and mixed-type organisations expressible: a Division containing either a Department or a Project, one Division holding Departments while its sibling holds Regions, the same type reachable at two depths.
- **Dated "no parent" entry (stage D2).** Taking a unit off the tree is a decision on its record, not the absence of one. `ParentLink.ParentRecordId` is nullable; cancel-move can undo a departure; the unplaced panel distinguishes three states.
- **Arabic optional (ADR-0003 addendum).** English required, Arabic optional unless the tenant turns `RequireArabicNames` on. One fallback rule, `BilingualText.Display`.
- **Export (ADR-0011).** A deployment step that writes the import format.
- **Recipe steps** `dimension-types`, `structures`, `dimension-records` — now carrying attribute schemas, explicit containment, full dated placement history and dated name history.

### ADRs

| ADR | Decision |
| --- | --- |
| **0007** | A document default must not share an instance — a `static readonly` `BilingualText.Empty` let two dimension types read each other's name. |
| **0008** | Recipe re-run behaviour: per-step idempotency, not cross-step atomicity. Identical → skip; different → fail naming the difference; never silently overwrite. |
| **0009** | Migrations are append-only. `CreateAsync` and shipped `UpdateFromNAsync` bodies are never edited. |
| **0010** | Containment is a per-structure map, not a chain of level ordinals. Records the measured evidence and why per-structure beat per-type. |
| **0011** | Exporting dimensions is a deployment step that writes the import format — not a bespoke format, not a controller action, not a CLI command. |
| **0003 addendum** | English required, Arabic optional by default, with the display fallback rule and what empty actually cost. |
| **0005 addendum** (earlier) | A backdated move splits rather than overwrites; the designer must warn. |

### Migrations

`WorkMate.Dimensions.Migrations` is at **version 5**.

| Step | Version | What |
| --- | --- | --- |
| `CreateAsync` | 1 | Config layer: `DimensionTypeIndex`, `StructureIndex` (now including `ContainmentRuleCount`). |
| `UpdateFrom1Async` | 2 | Record layer: `DimensionRecordPartIndex`, `DimensionNameIndex`. |
| `UpdateFrom2Async` | 3 | Graph layer: `DimensionLinkIndex`, `DimensionClosureIndex`, `EmployeeAssignmentIndex`. |
| `UpdateFrom3Async` | 4 | Repairs tenants missing `DimensionRecordPartIndex.NameAr`. Guarded by a column probe. |
| `UpdateFrom4Async` | **5** | ADR-0010: adds `StructureIndex.ContainmentRuleCount` (guarded) and derives the containment map for every existing structure. **Lossless** — the upgrade test asserts pairwise that the derived map permits exactly what the v4 arithmetic did. |

Stage D2 needed **no migration**: `DimensionLinkIndex.ParentId` has always been a string that is sometimes empty. Arabic-optional needed **no migration**: English was always required, and `BilingualText` normalises a stored null to empty on read.

---

## Decisions taken with your approval

1. **Containment per structure, not per type** — a record participates in several axes, so "what may contain a Department" is an axis-specific fact.
2. **The company is the structure, not a record** — the root card is the axis's name; the top *unit* is whatever employees can belong to.
3. **Three independent demo recipes** with fully prefixed codes rather than shared types — ADR-0008's fail-on-difference would weld them together permanently. *(Recipes themselves are stage D3, not yet built — see gaps.)*
4. **Arabic optional by default**, with a tenant setting to restore the old behaviour, and a never-blank display fallback.
5. **Search matches both halves regardless of UI culture** — an Arabic reader must be able to find a unit a colleague named in English.
6. **`CostCentre` is the only system dimension type**, and the base `Organisation` structure ships **empty**.

---

## Known gaps and backlog

| Gap | Where it is recorded |
| --- | --- |
| **`employee-assignments` recipe step and its export.** Not built: employees do not exist until prompt 4. The full shape — JSON, code-based references, validation, re-run comparison key, and the export switch — is specified in prompt 4 of the prompt library. | `docs/prompt-library.md`, prompt 4 |
| **Audit display of an empty bilingual half as "(not set)".** Agreed, and *not* implemented: the audit trail currently has no WorkMate-authored display driver, so Orchard renders the serialised payload generically and there is nowhere for the placeholder to appear. Needs a display driver for the four `Dimension*` events. I removed the helper rather than ship dead code behind a dangling string. | this document |
| **Three demo recipes (Al Noor, Crescent, Zenith)** — stage D3. The importer work they need (attribute schemas) is now done by ADR-0011; the recipe files are not written. | this document |
| **Record attribute *values* are not exported.** Type attribute *schemas* are. A record's own attribute values do not survive a round trip. | this document |
| **The unit head** shows `Head: —` because no employee can exist yet. | `docs/prompt-library.md`, prompt 4 |
| **`DimensionRule.LevelSkipping`** is retained in the enum and no longer emitted; audit entries written before ADR-0010 reference it. | ADR-0010 |
| **Arabic search cannot match a record with no Arabic name** — inherent, not a defect. The record stays findable by its English name and its code. | `DimensionSearchTenantTests` |

---

## Test counts

| Assembly | Passed | Skipped |
| --- | ---: | ---: |
| `WorkMate.Dimensions.Tests` | 179 | 0 |
| `WorkMate.Platform.Tests` | 183 | 0 |
| `WorkMate.Integration.Tests` | 182 | 0 |
| `WorkMate.Browser.Tests` | 58 | 3 |
| **Total** | **602** | **3** |

The three skips are the real-data browser tests, which skip with a reason unless `WORKMATE_REAL_APPDATA` is set.

Tests worth knowing about:

- **`DimensionExportRoundTripTenantTests`** — seeds a tenant containing a substantive rename, a backdated move that splits, a retirement, a move-to-top, a self-nested unit and a ragged tree; exports it; imports into a second, genuinely fresh tenant; and compares types, attribute schemas, structures, containment, records, **closure on a date either side of every change**, and names as at those dates.
- **`DimensionsMigrationUpgradeTenantTests`** — proves the ADR-0010 derivation is lossless pairwise.
- **`ViewModelsSurviveNullBindingTests`** — reflects over every view model, nulls every settable string, reads every getter. Written after six HTTP 500s that were one defect in two files.
- **`DimensionContainmentTenantTests`** — the Zenith and Crescent shapes, the refusals that matter, and a property test that the pickers and the validator agree over every ordered pair of types.

---

## Manual test checklist

**Setup.** Stop any running dev server, `dotnet build -warnaserror`, start the host, sign in as administrator.

### Structures and containment
1. **Dimensions → Structures → Edit** the demo structure. Confirm the four blocks render and the grid reproduces the existing rules.
2. Untick one cell (e.g. Division → Section) and save. If units are placed that way it is refused, naming them; otherwise it saves.
3. **Designer → that Division's ⋯ → Add unit.** The unticked kind is no longer offered. Re-tick and save.
4. **Create a structure** with no Arabic name and one type. It saves.

### The designer
5. Add, rename, move, merge, retire a unit — each shows a preview first.
6. Drag a card onto another: the preview opens, nothing is written. Press **Escape** mid-drag.
7. Drag empty canvas: it pans, nothing navigates.
8. Open a ⋯ menu, then click blank canvas / another card / the side panel / press **Escape** — it closes each time, only one is ever open, and the card you clicked does not expand.
9. Move a unit to **(top of the structure)**. The unplaced panel reads *"Removed from the tree on …"* — not "never placed". Set the date picker to the day before: it is back on the tree.
10. That unit's ⋯ → **Cancel a move…** offers *"… — removed from the tree"*. Undo it.

### Arabic
11. Create a type, a structure and a unit with the **Arabic box empty**. All save.
12. Switch to Arabic: the untranslated unit shows its **English** name, no blank line, no empty brackets.
13. Search an **English** name from the Arabic UI → found. Search an **Arabic** name from the English UI → found.
14. **Configuration → Settings → WorkMate → Require Arabic names** on. Creating with Arabic empty is now refused **under the Arabic box**. Turn it off.
15. Submit a type with **no English name**: a field error under the English box, never a 500.

### Export and import
16. **Tools → Deployments → Deployment Plans → Add a plan**, name it *Organisation export*.
17. **Add step → Organisation dimensions.** Three switches, all on by default. Untick *Dimension types* while leaving *Units* ticked: it refuses, explaining that records reference their type and structure by code.
18. Re-tick, save the step, then **Execute → Download** the plan. Open `Recipe.json` in the zip: three steps in order — `dimension-types` (with `attributes`), `structures` (with `rootTypeCodes` and `containment`), `dimension-records` (with `placements`, including entries whose `parentCode` is `null`, and `nameHistory` where a unit has been renamed).
19. **Create a new tenant** (or use a clean one), then **Tools → Import → Import a recipe/package** and upload that zip.
20. Open the designer on the new tenant: the same structure, the same tree. Set the date picker to a date **before** a move you made and confirm the old shape resolves.

### Base recipe
21. On a **brand-new tenant**: Dimension types contains **Cost centre** and nothing else; Structures contains **Organisation**, primary, with no types. The designer opens on it and shows the structure card with nothing under it.
22. Give it types and containment through the Structures editor and build an org chart.

---

## Open question for the reviewer

**Should `CostCentre` get its own "Cost" structure in the base recipe?**

My recommendation: **no, not in the base recipe.** The type is seeded because the platform itself refers to cost centres — payroll, cost allocation and the GL reference on every record all mean the same thing by it — so a tenant without the type is one those features cannot describe. A *structure* is a different claim: it asserts that this customer's cost centres form a hierarchy of their own, separate from the organisation. Many customers' cost centres are their departments, and for them a second axis is an empty screen they have to be told to ignore; others want a genuinely independent cost tree. That is a configuration decision with a real answer per customer, and the designer now makes it a two-minute job.

If you want it anyway, the cheapest shape is an empty `cost` structure exactly like `organisation` — no levels, not primary — so the choice is visible without being pre-made. Say the word and it is a four-line recipe change plus a test.
