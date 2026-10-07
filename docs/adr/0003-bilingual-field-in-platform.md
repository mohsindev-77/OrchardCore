# ADR-0003: The bilingual text field type lives in WorkMate.Platform

**Status:** accepted
**Date:** 2026-09-30
**Deciders:** Head of Technology

## Context
Prompt 1 of the prompt library asks for "a bilingual text field type in
WorkMate.Core with edit and display drivers, used by every later module".

That cannot be built where it is asked for. The technical specification's
repository layout (section 2) describes `WorkMate.Core` as "shared
abstractions, no Orchard dependency", CLAUDE.md repeats the rule, and
`WorkMate.Core.csproj` carries it as a comment with no package references at
all.

A bilingual field type in Orchard Core 3.0.1 is not an abstraction. Verified
against the pinned version, it requires:

- `OrchardCore.ContentManagement.ContentField` as its base class, from
  `OrchardCore.ContentManagement.Abstractions`;
- `OrchardCore.ContentManagement.Display.ContentDisplay.ContentFieldDisplayDriver<TField>`
  for the edit and display drivers, from `OrchardCore.ContentManagement.Display`;
- registration through
  `services.AddContentField<TField>().UseDisplayDriver<TDriver>()`, from
  `OrchardCore.ContentManagement.Display`;
- Razor views, and therefore the `Microsoft.NET.Sdk.Razor` project SDK.

Putting any of that in `WorkMate.Core` makes the project an Orchard module in
all but name and breaks the one rule the project exists to keep.

## Decision
Split the concern along the line the specification already draws.

`WorkMate.Core` keeps `BilingualText`, the plain `record` value object holding
`En` and `Ar`. It stays free of Orchard, so services, validators, calculation
engines and their unit tests can use it without a shell.

`WorkMate.Platform` owns the Orchard surface: `BilingualTextField`, its
settings type, its edit and display drivers, its views, and its registration.
`BilingualTextField` converts to and from `BilingualText`, so the value object
remains the currency between services.

This is recorded as a deviation from prompt 1 of the prompt library, not from
the technical specification, which never placed the field type in
`WorkMate.Core`.

## Consequences
Every later module that wants the bilingual field depends on
`WorkMate.Platform`, which section 3 of the specification already requires of
every WorkMate module, so no new dependency edge is created.

Services and their unit tests can keep using `BilingualText` without
referencing Orchard, which keeps the unit test level inside the one-minute
budget that section 10 sets.

The prompt library should be corrected at prompt 1 to say
`WorkMate.Platform`. The technical specification needs no change; section 5's
field table names the bilingual field without saying which project holds it,
and section 2's layout already gives `WorkMate.Platform` the shared UI.

---

## Addendum, 2026-10-07: English is required, Arabic is optional by default

**Status:** accepted
**Deciders:** Project lead

### Context

Every name on the platform is bilingual, and until now every name was *required* to be bilingual:
`DimensionValidator.ValidateName`, `DimensionRecordPartHandler.ValidateAsync` and
`BilingualTextFieldDisplayDriver.UpdateAsync` each refused a name with an empty Arabic half, in
three separate copies of the same rule.

That is right for a customer who has already translated their organisation and wrong for every
customer before that point. A tenant being set up has an English organisation chart in a
spreadsheet and no Arabic at all; insisting on Arabic at the first unit does not get the
translation done, it stops the chart being built. It is a discipline some customers want and none
should be forced into, and nothing in the specification required it — section 5 says names are
bilingual, which is a statement about the shape of the data, not about which halves may be blank.

### Decision

**English is required. Arabic is optional, unless the tenant turns it on.**

- A new tenant setting, `WorkMateSettings.RequireArabicNames`, **off by default**. On, every name
  must carry both halves exactly as before.
- Asked through `IBilingualNamePolicy`, a one-question seam over the settings service, so that
  every write path — dimension types, structures, records, attribute labels, the content part
  handler, the recipe steps — reads the same answer. A caller that forgets to ask gets the
  permissive behaviour, which is the right way round for a rule being relaxed.
- Enforced in the service layer, not only in the UI. The recipe steps already route their names
  through `IDimensionValidator`, so they inherit it; a recipe with no `nameAr` applies on a default
  tenant and is refused on one that requires Arabic.
- The per-field `BilingualTextFieldSettings.RequireArabic` survives as a way to insist on Arabic
  for one particular field whatever the tenant's answer. The two are an OR, not an override.

### The fallback rule

**A reader is never shown a blank name.** `BilingualText.Display(en, ar)` returns the half the
reader of the current UI culture can read and falls back to the other when it is empty — so an
Arabic reader looking at an untranslated unit sees its English name, which they can act on, rather
than nothing, which they cannot. It is the single implementation; `BilingualDisplay.Name` in
Dimensions delegates to it and `BilingualTextField.ForCulture` already did the same thing.

Three consequences follow, and each is a place the old "both are always present" assumption was
baked in:

- **Bilingual sentences** must not render empty brackets or a stray separator.
  `BilingualDisplay.NameWithAlternate` omits the bracketed alternate entirely when there is none,
  so "Support (الدعم) closed on 6 Oct 2026" becomes "Support closed on 6 Oct 2026" rather than
  "Support () closed on…".
- **Two-line displays** — the designer's cards, the dimension type and structure lists — render the
  Arabic line only when there is Arabic, rather than leaving an empty element where a name should
  be. On a card that empty line is also height the row-levelling script would reserve.
- **Form controls**: `workmate-bilingual` marks the English input `required` and never the Arabic
  one. A tenant setting is not something a tag helper can see, so it marks the half it can be sure
  about and leaves the other to the server, which is the authority either way.

### Migration

**None needed, and none written.** English has been required throughout, so every existing name
already has the half that is still required; this change only stops refusing writes it used to
refuse. `RequireArabicNames` defaults to `false` through `GetOrCreate<WorkMateSettings>`, so a
tenant that has never saved the section gets the new default without being touched. A customer who
wants the old behaviour turns the setting on; nothing they already hold becomes invalid either way.

`base.recipe.json` states the new setting explicitly rather than relying on the default, so that a
fresh tenant's settings document says what it means.

### What empty actually turned out to cost

Making Arabic optional exposed three assumptions that had been safe only because the half was
always filled. All three are now closed, and none needed a data migration.

**`BilingualText` normalises null to empty in its constructor.** Null and empty both mean "no name
in this language", and a type that can represent that two ways makes every consumer handle both —
a guarantee nobody can keep. Records deserialise through their primary constructor, so a document
written by any earlier path is normalised on the way out of the database as well as on the way in.
That is why **no repair migration was needed**: there is no stored shape a reader can still trip
over.

**An empty text box binds to `null`, not to `""`.** `ModelMetadata.ConvertEmptyStringToNull`
defaults to true, and MVC's `ComplexObjectModelBinder` reads every public getter on the model while
binding it — so a computed `Name => new(NameEn.Trim(), NameAr.Trim())` threw *inside the binder*,
before the action method, before `ModelState`, and therefore with no way to show a field error
instead of a 500. `ViewModelsSurviveNullBindingTests` reflects over every view model, nulls every
settable string and reads every getter, so the class of defect fails on the day it is written
rather than the day it is deployed.

**A non-nullable reference type property gets an implicit `required`.** The bilingual halves are
declared `string?` for that reason as much as for the first: a non-nullable `NameAr` failed
`ModelState.IsValid` with "The NameAr field is required" before the controller called anything,
which would have refused an optional Arabic name whatever the tenant's setting said. Whether Arabic
is required is `IDimensionValidator`'s question, and it is the only thing that knows the answer.

Three smaller asymmetries went with them: `DimensionAttributeValue.IsEmpty` tested *both* halves,
so a required bilingual attribute could be satisfied in Arabic alone — the inverse of this rule;
two name lookups fell back to the record id in English and to nothing in Arabic, so a missing row
left an Arabic reader with a blank badge; and the recipe steps compared the two halves ordinally
and untrimmed, so `"nameAr": null` in the JSON and `""` in the tenant read as a difference and
aborted a re-run that ADR-0008 promises is safe.
