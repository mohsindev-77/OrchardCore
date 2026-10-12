# ADR-0019: A cache invalidation is signalled again after the write commits

**Status:** accepted, **pending second review**
**Date:** 2026-10-12
**Deciders:** Project lead
**Applies to:** every WorkMate module that caches a read and invalidates it on write

> ADR numbers 0015 to 0018 are reserved by `docs/design/prompt-04-session-b-form-designer.md`
> for the form designer's own decisions. This takes the next free number rather than one of
> those.

## Context

Two modules cache small, frequently read, rarely written documents per tenant and evict them
with an Orchard `ISignal` change token:

- `WorkMate.Dimensions` — dimension types and structures, through `CachedDimensionTypeLookup`
  and `CachedStructureLookup`, signalled by `DimensionTypeService` and `StructureService`.
- `WorkMate.Records` — managed lookup lists, through `CachedLookupListLookup`, signalled by
  `LookupListService`.

Both called `ISignal.SignalTokenAsync(key)` immediately after staging the write, and both said
in a comment that this happened "after the write, so that a reader racing the write refills the
cache from the committed state". **It does not, and the comment was the same mistake made
twice.**

## The decisive fact

A module writes through YesSql's `ISession`. `ISession.SaveAsync` stages the write; the commit
happens when the shell scope is disposed. Verified against the pinned Orchard Core 3.0.1:
`OrchardCore.Data.YesSql` registers `IDocumentStore.CommitAsync()` through
`ShellScope.RegisterBeforeDispose`, and `ShellScope.BeforeDisposeAsync` runs those callbacks
first, then sends deferred signals, then runs deferred tasks.

So between the write and its commit there is a window — the rest of an admin request, the rest
of a recipe step — in which the database still holds the old state. Signalling inside that
window cancels the change token at once, and **any read arriving before the commit refills the
cache from the state the write is about to replace, under a token that has already been used
and will never be signalled again.** The entry is then wrong for the life of the shell.

Reproduced on a real tenant, in `LookupListInvalidationTenantTests`:

```
write succeeded: True        CreateAsync stages the list and signals the token now
reader saw: <null>           an overlapping scope reads, and caches the absence
after commit — cached: <null>, direct: race-probe
```

This is the defect found in manual testing of slice B2: on a tenant created before the slice,
an operator applied the `WorkMate standard lookup lists` recipe, the ten lists were written,
and every bound field went on rendering a free-text box until the application was restarted.
Ten writes and their audit entries in one scope is a wide window, and a browser making requests
through it is all it takes.

**What it is not.** Three other mechanisms were suspected first, and each was measured rather
than reasoned about, because the fix differs for each:

- *`IMemoryCache` is shared across tenants, so a released shell leaves entries behind.* False.
  Orchard registers `IMemoryCache` in the tenant container, and a released tenant gets a new
  one — measured on a live tenant, the instance changes. Two module comments asserted the
  opposite and have been corrected.
- *Running a recipe releases the shell, so the signal lands on an `ISignal` instance nothing is
  reading through.* The release is real — `OrchardCore.Recipes`' admin controller calls
  `ReleaseShellContextAsync` after a run, and both instances do change — but it **hides** this
  defect rather than causing it, because the rebuilt container starts with an empty cache. That
  is why an end-to-end test through Configuration → Recipes passed while the product was
  broken.
- *The lists were never written.* They were; `ILookupListService.ListAsync` is uncached and
  reported all ten throughout.

## The symmetrical mistake, which is why this is not simply "signal later"

Deferring the signal and sending nothing immediately is wrong, and the dimension engine says so
loudly: some thirty of its tests fail. That module writes and then reads **in one scope** as a
matter of course — a recipe step creating types and then the records that name them, a designer
action, a scenario builder — and those reads go through the cached lookup. The immediate signal
is what makes them correct: the reader is on the same session, which already sees the staged
write, so evicting at once lets it refill with the new state.

The immediate signal was therefore never wrong. It was only ever **insufficient**: it is the
wrong *last* signal.

## Decision

**A WorkMate module invalidates a cached read through
`WorkMate.Platform.Services.CacheSignals.SignalNowAndAfterCommitAsync`. No module calls
`ISignal.SignalTokenAsync` directly.**

That helper signals at the three moments the cached answer can become wrong:

1. **Now**, so a read later in the same scope — same session, staged write already visible —
   refills with the new state. This is the behaviour the dimension engine depends on.
2. **After the commit**, through `ShellScope.AddDeferredSignal`, which Orchard sends in
   `BeforeDisposeAsync` once the commit callback has run. This is the one that was missing, and
   the only one that can evict an entry a reader in *another* scope put there mid-write.
3. **On the way out of a failure**, through `ShellScope.AddExceptionHandler`. If the scope
   fails, YesSql cancels the document store and nothing is written — but a reader in this scope
   may already have cached the staged state, and the deferred signal is never reached. A recipe
   step that writes five rows and fails on the sixth is the reachable case. This hole existed
   before and is closed here rather than left as the mirror image of the one being fixed.

It lives in `WorkMate.Platform` rather than being copied into each module, because the rule is
the same rule everywhere and a second copy is a second chance to get it wrong — which is
exactly what happened with the comment this ADR replaces. A source-level guard in each module's
test project fails the build if any module source calls `SignalTokenAsync` directly, in the
manner of the existing `ConcurrencyCheckedSaveTests`.

**The one case it cannot cover** is a caller with no ambient shell scope, where
`ShellScope.AddDeferredSignal` is a silent no-op. Every write path in this product runs inside
one. There the immediate signal is all there is, which is no worse than the behaviour being
replaced.

### Second line, in `WorkMate.Records` only: an absence is not cached

`CachedLookupListLookup` no longer stores "there is no such list" or "there are no lists". A
cached absence is the only value on that path whose staleness is invisible — a stale list shows
the wrong entries, which somebody reports; a stale absence shows a free-text box, which is
exactly what an unconfigured tenant is meant to show. That is how this defect survived to
manual testing, and how it would have survived any fix that made it merely less likely. It is
also only cacheable at all in the few minutes between upgrading a tenant and loading the lists,
and the one write in those minutes is the write that fills them in. The cost is one indexed
query per render on a tenant with no lists, and nothing at all on every other tenant.

This is a judgement about one path, not a general rule, so it is not imposed on the dimension
engine, where a missing type is a programming error rather than a state a tenant sits in.

## Consequences

Every cached read in both modules is now evicted at every moment the state behind it can have
changed, including the one that was missing. Nothing about same-scope behaviour changes, so no
existing caller is affected; the test that had to change was one asserting the old race
directly, and it now asks its question in a second scope.

If a distributed cache is ever configured (specification section 11, open decision 6), `ISignal`
resolves to the distributed implementation and both signals publish over the message bus at the
same, now correct, moments. Nothing in this decision changes.

**A module that adds a cache after this date inherits the rule**, and the source-level guard is
how it is held to it. If Orchard ever makes the commit observable as an event a module can
subscribe to, the helper becomes a subscriber and the guard stays.
