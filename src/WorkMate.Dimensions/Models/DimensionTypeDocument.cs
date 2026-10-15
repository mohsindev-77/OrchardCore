using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// A kind of unit: Business Unit, Division, Department, Section, Branch, Cost Centre, Project, or
/// anything a customer invents. Configuration, not content, per decision 3 of the dimension
/// engine architecture.
/// </summary>
/// <remarks>
/// A YesSql document. The properties carry public setters, which the coding standard otherwise
/// reserves for content parts, because YesSql materialises a document by deserialising into it
/// and assigns <see cref="Id"/> and <see cref="Version"/> itself. Mutation still goes through
/// <c>IDimensionTypeService</c>; nothing outside this module loads one of these to change it.
/// </remarks>
public sealed class DimensionTypeDocument
{
    /// <summary>YesSql's document id. Assigned on first save; never meaningful to the business.</summary>
    public long Id { get; set; }

    /// <summary>
    /// YesSql's optimistic concurrency token, per ADR-0005. Every write goes through
    /// <c>SaveAsync(document, checkConcurrency: true)</c>, so two administrators editing the same
    /// type concurrently get a <c>ConcurrencyException</c> rather than a silent overwrite.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// The stable identity other records point at. Generated once and never reused, so that
    /// correcting a <see cref="Code"/> does not orphan every record of this type.
    /// </summary>
    public string DimensionTypeId { get; set; } = string.Empty;

    /// <summary>
    /// The customer-facing code, unique within the tenant. The backing content type is named for
    /// it, so it is validated as an identifier and it is immutable once created.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The name shown to a user, in both platform languages.</summary>
    /// <remarks>
    /// Defaults to a fresh instance, never to <see cref="BilingualText.Empty"/>. That field is one
    /// shared object; giving every new document the same reference is how two unrelated dimension
    /// types ended up reading each other's name — see ADR-0007 and
    /// <c>DocumentIdentityTenantTests</c> in the integration suite.
    /// </remarks>
    public BilingualText Name { get; set; } = new(string.Empty, string.Empty);

    /// <summary>
    /// True for the types WorkMate ships in a recipe and the customer may not delete. A
    /// system-defined type can still have its name and attribute schema extended.
    /// </summary>
    public bool IsSystemDefined { get; set; }

    /// <summary>
    /// Whether a record of this type may sit directly under another of the same type — a Section
    /// inside a Section. Off by default, because most types are a single level of a hierarchy and
    /// accidental self-nesting is how an organisation chart quietly grows a thirteenth level.
    /// </summary>
    public bool AllowsSelfNesting { get; set; }

    /// <summary>The fields this type's records carry beyond the standard ones.</summary>
    public IReadOnlyList<DimensionAttributeDefinition> AttributeSchema { get; set; } = [];

    /// <summary>
    /// The part this dimension type plays in the product, where the product has behaviour that
    /// belongs to one kind of dimension and not to the others. <see cref="DimensionTypeRole.None"/>
    /// for almost every type.
    /// </summary>
    /// <remarks>
    /// Declared rather than derived, and <see cref="DimensionTypeRole"/> sets out at length why
    /// every available derivation — by code, by name, by menu group — is wrong. A document property
    /// with a zero default, so every type in every existing tenant deserialises to
    /// <see cref="DimensionTypeRole.None"/> and nothing changes; no migration and no column.
    /// </remarks>
    public DimensionTypeRole StandardRole { get; set; } = DimensionTypeRole.None;

    /// <summary>
    /// The content type created for this dimension type. Stored rather than derived on every read,
    /// so that changing how a name is derived from a code cannot strand an existing tenant's
    /// records behind a content type nothing looks for any more.
    /// </summary>
    public string ContentTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this dimension gets its own entry in the Organisation menu.
    /// </summary>
    /// <remarks>
    /// <b>Revived, not re-invented (ADR-0024).</b> FRS v2.5 §1.2 and decision D3 require every
    /// dimension to be its own menu item. These three properties were built for exactly that in
    /// prompt 4 session D, shipped, and were withdrawn by the product owner on sight: on a real
    /// tenant the menu read <em>Branchs, Cost centres, Departments, Departments, Departments,
    /// Divisions, Divisions, Divisions, Projects, Regions, Sections, Teams</em>. They have sat on
    /// this document unread ever since, because ADR-0009 made the step that writes them
    /// (<c>Migrations.UpdateFrom7Async</c>) unremovable.
    ///
    /// <b>The three things that caused the withdrawal, and what each one is now.</b>
    ///
    /// <list type="table">
    /// <item>
    ///   <term><em>Branchs</em></term>
    ///   <description>The plural was guessed by appending "s", and Arabic was not pluralised at all
    ///   (قسم pluralises to أقسام by changing the word). <see cref="PluralName"/> is now
    ///   <b>authored</b>, in both languages, on the dimension type editor. A type with no authored
    ///   plural falls back to its singular — wrong-looking, never wrong.</description>
    /// </item>
    /// <item>
    ///   <term><em>Departments, Departments, Departments</em></term>
    ///   <description>Not the menu's fault at all, which is what D17 established: three demo
    ///   organisations each defined their own Department and Division types in one tenant, and the
    ///   menu rendered one entry per dimension <em>type</em>. <b>D17 proposed that the demos share
    ///   the standard types; that was built and reverted</b>, because a type is a level of every
    ///   chart that declares it, so a shared Department put each company's units on the others'
    ///   charts (ADR-0005). <b>Decision D23 groups the menu instead</b>: types sharing an English
    ///   singular name become one entry. See <c>DimensionMenuComposition</c>.</description>
    /// </item>
    /// <item>
    ///   <term>A menu nobody could shorten</term>
    ///   <description>This flag is the switch D3 asks for. A tenant turns off what it does not
    ///   use, and the dimension's screens stay reachable from the Dimension types list.</description>
    /// </item>
    /// </list>
    ///
    /// <b>Default: true.</b> A dimension a customer has gone to the trouble of defining is one they
    /// use, so the useful default is to show it; the shipped set that F5 brings turns off the ones a
    /// starter tenant does not need. Off-by-default would mean every new type is invisible until
    /// somebody finds the checkbox, which is the kind of thing that reads as a missing screen.
    /// </remarks>
    public bool ShowInMenu { get; set; } = true;

    /// <summary>
    /// What the menu entry reads, in both platform languages. Authored, never derived.
    /// </summary>
    /// <remarks>
    /// Empty is a legitimate state and means "use the singular" — see
    /// <see cref="MenuCaption"/>. Two kinds of tenant have it empty: one created after the per-type
    /// menu was withdrawn, where nothing ever set it, and one whose guessed plural
    /// <c>Migrations.UpdateFrom10Async</c> replaced with the singular.
    /// </remarks>
    public BilingualText PluralName { get; set; } = new(string.Empty, string.Empty);

    /// <summary>
    /// Where this dimension sits among the others in the menu. Lower first; ties break on the
    /// caption.
    /// </summary>
    /// <remarks>
    /// A number rather than an alphabetical order, because the order of the organisation's levels
    /// is a fact about the organisation — Business Unit above Division above Department — and
    /// sorting by caption gives an Arabic reader a different order from an English one. That is the
    /// same defect <c>Navigation.AdminMenu</c> already avoids by giving its fixed entries explicit
    /// numeric positions rather than using <c>LocalizedString.PrefixPosition()</c>.
    /// </remarks>
    public int MenuOrder { get; set; }

    /// <summary>
    /// What this dimension's menu entry reads: the authored plural, falling back to the singular
    /// rather than to a guess or a blank.
    /// </summary>
    /// <param name="culture">
    /// The reader's language. Null means the current UI culture, which is what every caller but a
    /// test wants.
    /// </param>
    /// <remarks>
    /// <b>The fallback is the whole of why this is a method and not a property read.</b> Three
    /// states have to produce something a person can read — an authored plural, a plural authored
    /// in only one language, and no plural at all — and the one thing none of them may do is invent
    /// a word. <c>BilingualText.Display</c> handles the second, and the singular handles the third.
    /// </remarks>
    public string MenuCaption(System.Globalization.CultureInfo? culture = null)
    {
        var plural = PluralName.Display(culture);

        return string.IsNullOrWhiteSpace(plural) ? Name.Display(culture) : plural;
    }

    /// <summary>
    /// The date this type stopped being offered, or null while it is current. Retiring is dated
    /// rather than boolean because a type retired in June must still resolve for a May report.
    /// </summary>
    public DateOnly? RetiredOn { get; set; }

    /// <summary>Whether the type is retired as at <paramref name="asAt"/>.</summary>
    public bool IsRetiredOn(DateOnly asAt) => RetiredOn is not null && asAt >= RetiredOn;
}
