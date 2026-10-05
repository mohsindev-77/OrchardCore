# Demo recipes

A demo recipe is test data: small, synthetic, clearly marked in its own
`displayName`, `description`, `categories` and `tags`, and never referenced
by `base.recipe.json` or any edition, starter or pack recipe. It adds data;
it is never required for a tenant to function, and a tenant that never runs
one is in no way incomplete.

**The recipe files themselves live inside the module whose data they seed**
— `src/WorkMate.Dimensions/Recipes/organisation-designer-demo.recipe.json`,
for example — not in this folder. This folder holds only this README.

That is a correction, not the original design: a demo recipe was first put
at the repository root, under `/recipes/demo`, on the reasoning that
`Directory.Build.targets` already copies everything under `/recipes` into
the host's content folder, where `ApplicationRecipeHarvester` finds it. That
copy does happen, but it does not make the recipe appear on the `/Admin/Recipes`
screen: `OrchardCore.Recipes.Services.RecipeHarvester` — the harvester that
feeds the admin screen — is scoped to each **enabled extension's own**
`Recipes/` folder via `IExtensionManager`, not to the application's content
root. `ApplicationRecipeHarvester` is a different, narrower harvester used
for the tenant **setup** screen's recipe picker. A root-level `/recipes/demo`
file reached the setup picker's harvester and the pipeline's "apply every
recipe under /recipes" check, but never the admin screen — which is the one
place an operator can run a recipe against a tenant that already exists,
and so the one place a demo recipe actually needs to show up.

A recipe placed in a module's own `Recipes/` folder needs no special build
wiring — Orchard resolves a module's `Views/`, `wwwroot/` and `Recipes/`
content from the module itself, the same way it already does for this
module's views, with nothing WorkMate had to add. It shows up on
`/Admin/Recipes` whenever that module is enabled, with `issetuprecipe: false`
keeping it out of the tenant setup screen, since none of these are meant to
provision a new tenant.

## organisation-designer-demo.recipe.json

`src/WorkMate.Dimensions/Recipes/organisation-designer-demo.recipe.json`.
Seeds a self-contained `demo-org` structure — Division → Department →
Section, using `demo-division`/`demo-department`/`demo-section` as the
dimension type codes — with thirteen sample records: two divisions, four
departments and six sections placed under them, and one department left
unplaced on purpose, so the organisation designer's unplaced-records panel
has something to show. Exercises the three recipe steps WorkMate.Dimensions
ships (`dimension-types`, `structures`, `dimension-records`), in the order a
real recipe needs them.

Safe to run more than once: per ADR-0008, each recipe step recognises a code
that already matches the recipe and skips it, so running this recipe again
changes nothing rather than erroring or duplicating anything.

To run it: open `/Admin/Recipes`, find "WorkMate demo: organisation designer
sample data", and execute it against the current tenant.
