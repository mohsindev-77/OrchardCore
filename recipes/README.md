# Recipes

| Folder | Purpose |
|---|---|
| `base.recipe.json` | Runs on every tenant. Never customer-specific. |
| `editions/` | Essential, Professional, Enterprise — each layers on base and enables entitled features. |
| `starters/` | A working generic organisation so a new tenant never starts empty. |
| `packs/` | Sector starting points: pre-configured structures, policies, forms, workflows, reports. |
| `reference/` | The two reference tenants (Khaleej University, Gulf Trading Co) used by scenario tests. |

A recipe that fails to apply to a fresh tenant fails the build. Recipes contain no secrets and no environment-specific values.
