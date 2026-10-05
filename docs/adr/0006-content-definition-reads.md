# ADR-0006: Write paths read content definitions with Load, never Get

**Status:** accepted
**Date:** 2026-10-03
**Deciders:** Head of Technology

## Context

WorkMate creates content types at runtime. The dimension engine does it when
an administrator defines a dimension type, and the form designer in prompt 4
will do the same thing for employee record forms. Both then need to create
content *items* of those types, sometimes in the same unit of work — a recipe
importing an industry pack defines the types and the records together.

That did not work, and the way it failed is the reason this ADR exists.

`OrchardCore.ContentManagement.IContentDefinitionManager` exposes two reads for
every definition: `GetTypeDefinitionAsync` and `LoadTypeDefinitionAsync`. The
names suggest a performance difference. The actual difference is visibility.
`ContentDefinitionManager` is constructed with an `IMemoryCache` and serves
`Get…` from a cached `ContentDefinitionRecord` obtained through
`IContentDefinitionStore.GetContentDefinitionAsync`. That cache is invalidated
after the scope commits. `Load…` reads the mutable document directly.

So **inside the scope that created a content type, `Get…` cannot see it.**
Measured on the pinned version, for a type created moments earlier in the same
shell scope:

```
cachedDefinition=NULL  loadedDefinition=present  indexLookup=found
partWelded=no          stampedTypeId=EMPTY
```

The consequence is worse than a stale read, because `IContentManager.NewAsync`
is one of the callers of `Get…`. Given a type it cannot see, it returns a
content item with **no parts welded at all** — not the module's own part, not
`TitlePart`, and not the part carrying the type's attribute fields. Nothing
throws. The caller receives an item that looks usable, silently discards every
field set on it, and then fails validation with a message about the wrong
thing entirely. Three sessions of symptoms were traced to this one cause.

There is no supported way to refresh the cache mid-scope. The whole surface of
`IContentDefinitionManager` and `IContentDefinitionStore` was checked; neither
exposes an invalidation hook.

### What was established about the recipe executor

Verified from the pinned assemblies: `OrchardCore.Recipes.Services.RecipeExecutor`
is constructed with `IShellHost` and `ShellSettings`, references
`IShellHost.GetScopeAsync`, `ShellScope` and `ShellScope.UsingAsync`, and reads
`RecipeDescriptor.RequireNewScope`. The executor therefore manages shell scopes
itself and per-recipe scope behaviour is a declared concern of its API.

**Its exact default step-to-step behaviour was not determined.** Establishing it
would have needed a recipe step handler registered in the shell purely to
observe the scope, which means test-only code in a product assembly. That was
judged not worth it, because of the decision below: the module is correct
whichever way the executor behaves, so the answer does not change any code.
Prompt 3 adds the first real recipe steps and will be able to answer it in one
line if it ever matters.

**Answered in prompt 3.** `DimensionsRecipeStepsTenantTests` runs a real
recipe — `dimension-types`, then `structures`, then `dimension-records` — with
`RecipeDescriptor.RequireNewScope = false`, and the record step's `NewAsync`
call welds every part correctly for a type the `dimension-types` step created
moments earlier in the same execution. That is the decision above doing its
job, not evidence the cache problem went away: `DimensionTypeService` still
reads its own just-created definition with `LoadTypeDefinitionAsync`, per the
rule below. What was not independently re-confirmed is the harvester's
default for `RequireNewScope` when a recipe's JSON omits it, as every WorkMate
recipe so far does — the test sets it explicitly to exercise the harder,
same-scope case, which is also the one this module is built to survive.

## Decision

**Any code that writes, and any code running in a scope that has just created
or altered a content definition, uses `LoadTypeDefinitionAsync` and
`LoadPartDefinitionAsync`. Pure read paths may keep `GetTypeDefinitionAsync`
and `GetPartDefinitionAsync`.**

The rule is narrow on purpose. The cached read exists because content
definitions are consulted on nearly every request and the cache is what keeps
that cheap; making every read uncached to avoid one window would be a real
performance cost paid everywhere to fix a problem that occurs in one place.
A read path has nothing to be stale about: it is not in a scope that changed
the definition. A write path, or anything downstream of a definition change in
the same scope, is exactly the case the cache gets wrong.

**Where the rule is not enough, repair rather than avoid.** `NewAsync` is
Orchard's code and reads the cache; we cannot make it use `Load…`.
`DimensionRecordHandler.ActivatingAsync` therefore checks the cached definition
first — present for every type that existed before the scope began, so
virtually always, and the check costs one cache read — and only when it is
missing does it consult the dimension type index, load the real definition, and
weld whatever Orchard did not. Every part on the definition is welded, not only
the module's own, because the part named for the content type is the one
carrying the dimension type's attribute schema.

## Consequences

Prompt 3's recipe import can create dimension types and their records in one
unit of work, which is what an industry pack does, and it works regardless of
how the recipe executor scopes its steps.

**Prompt 4 inherits this.** The form designer creates content types at runtime
and will create items of them; it must follow the same rule and will need the
same repair if it creates items in the scope that defined the type. This ADR is
the thing to read before writing it.

A reviewer cannot tell a correct `Get…` from an incorrect one by looking at the
call alone — it depends on whether the scope changed a definition. The
mitigation is that the module has few definition reads and all of them are in
`DimensionTypeService` and `DimensionRecordHandler`, each commented with which
kind it is and why.

`SameScopeCreationTenantTests.TheCachedDefinitionIsStillBlindToATypeCreatedInThisScope`
pins the Orchard behaviour itself rather than our workaround. If a future
version makes the cached read see a same-scope write, that test fails, and the
repair in `ActivatingAsync` can be deleted — which is a far better way to
discover it than finding dead code years later.
