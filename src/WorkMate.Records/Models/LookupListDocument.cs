using WorkMate.Core;

namespace WorkMate.Records.Models;

/// <summary>
/// A managed list of choices a tenant maintains — job title, grade, employment type, nationality,
/// document type, relationship, currency — so that those fields stop being free text.
/// </summary>
/// <remarks>
/// <b>A document of our own rather than an Orchard taxonomy, per ADR-0014.</b> Specification
/// section 5's field table names a taxonomy field; verified against the pinned version,
/// <c>TaxonomyField</c> stores content item <em>ids</em>, and everything portable on this platform
/// is named by <em>code</em> — ADR-0011's "references by code, never by id, the rule every other
/// step follows, and the reason a recipe is portable at all". Read ADR-0014 before changing this.
///
/// A YesSql document. The properties carry public setters, which the coding standard otherwise
/// reserves for content parts, because YesSql materialises a document by deserialising into it and
/// assigns <see cref="Id"/> and <see cref="Version"/> itself — the same exception
/// <c>DimensionTypeDocument</c> takes and for the same reason. Mutation still goes through
/// <c>ILookupListService</c>; nothing outside this module loads one of these to change it.
/// </remarks>
public sealed class LookupListDocument
{
    /// <summary>YesSql's document id. Assigned on first save; never meaningful to the business.</summary>
    public long Id { get; set; }

    /// <summary>
    /// YesSql's optimistic concurrency token. Every write goes through
    /// <c>RecordsDocuments.SaveCheckedAsync</c>, so two administrators editing the same list at the
    /// same time get a <c>ConcurrencyException</c> rather than one of them silently losing their
    /// entries.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// The stable identity the admin screens route by.
    /// </summary>
    /// <remarks>
    /// Generated once and never reused. A field binds a list by <see cref="Code"/>, not by this,
    /// because a binding travels in a recipe and an id means nothing in the tenant a recipe lands
    /// in. This exists so that a URL survives whatever is decided later about correcting a code.
    /// </remarks>
    public string LookupListId { get; set; } = string.Empty;

    /// <summary>
    /// The customer-facing code, unique within the tenant and immutable once the list exists.
    /// </summary>
    /// <remarks>
    /// The natural key. It is what a recipe names, what an export writes, and what a field's
    /// binding will state in slice B2 — so changing it would orphan every one of those at once,
    /// silently, because a binding to a code that no longer resolves is a field that offers nothing
    /// rather than a field that errors.
    /// </remarks>
    public string Code { get; set; } = string.Empty;

    /// <summary>What the list is called, in both platform languages.</summary>
    /// <remarks>
    /// Defaults to a fresh instance, never to <see cref="BilingualText.Empty"/>. ADR-0007: that
    /// field is one shared object, and giving every new document the same reference is how two
    /// unrelated dimension types ended up reading each other's name.
    /// </remarks>
    public BilingualText Name { get; set; } = new(string.Empty, string.Empty);

    /// <summary>
    /// True for the lists WorkMate ships in a recipe. A system-defined list may have entries added
    /// and retired like any other; it simply cannot be deleted.
    /// </summary>
    public bool IsSystemDefined { get; set; }

    /// <summary>
    /// The entries, in the order they were given. Retired ones stay.
    /// </summary>
    /// <remarks>
    /// <c>IReadOnlyList&lt;T&gt; { get; set; } = []</c> is ADR-0007-safe: the collection expression
    /// compiles to <c>Array.Empty&lt;T&gt;()</c> for an interface-typed property, which is shared
    /// but can only ever be replaced wholesale rather than written into.
    /// </remarks>
    public IReadOnlyList<LookupListItem> Items { get; set; } = [];
}

/// <summary>One entry in a lookup list.</summary>
/// <remarks>
/// An immutable record constructed fresh every time, so ADR-0007's rule about shared default
/// instances does not apply: there is no pre-existing object for a deserialiser to write into.
/// </remarks>
/// <param name="Code">
/// The stored value. This is what a field holds once slice B2 binds one, so it is immutable: a
/// changed code leaves every record that already names it pointing at nothing.
/// </param>
/// <param name="Name">What a reader sees, in both platform languages.</param>
/// <param name="IsActive">
/// Whether the entry is still offered. <b>Retiring is how an entry goes away</b>: it stops being
/// offered to new records and stays readable on old ones. Deleting it would make last year's
/// records name something nothing can resolve, and nothing would report that — the figures would
/// simply be short.
/// </param>
/// <param name="SortOrder">
/// Where it sits in the list a reader is offered. Ties break on the English name, the same rule
/// the organisation chart uses for siblings.
/// </param>
public sealed record LookupListItem(
    string Code,
    BilingualText Name,
    bool IsActive = true,
    int SortOrder = 0);
