using System.Globalization;
using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Navigation;

/// <summary>
/// The "Organisation" admin menu: the designer, one expandable entry per dimension, and the
/// configuration screens under Settings.
/// </summary>
/// <remarks>
/// <b>Shape (FRS v2.5 §1.2, decisions D3 and D23, ADR-0024):</b>
///
/// <code>
/// Organisation
///     Organisation designer      ← unchanged, and first, exactly where it was before slice F2
///     Departments                ┐
///         All Departments        │  one entry per dimension NAME, expanding to its own values
///         Civil (Zenith)         │
///         Credit (Crescent)      │
///         …                      │
///         Show all (37)          ┘
///     Divisions
///         …
///     Settings
///         Dimension types
///         Structures
///         (no third entry — the Mapping screen is dropped, D27)
/// </code>
///
/// <b>This shipped once as one entry per dimension <em>type</em>, and the product owner withdrew it
/// on sight.</b> On a real tenant it read <em>Branchs, Cost centres, Departments, Departments,
/// Departments, Divisions, Divisions, Divisions, Projects, Regions, Sections, Teams</em>. Three
/// things caused that, and all three are fixed here rather than worked around:
///
/// <list type="number">
/// <item><description><b><em>Branchs</em>.</b> The plural was derived by appending an "s", and
/// Arabic was not pluralised at all — قسم pluralises to أقسام by changing the word.
/// <see cref="DimensionTypeDocument.PluralName"/> is now <b>authored</b> on the dimension type
/// editor, in both languages, and a type with none falls back to its singular. No rule guesses a
/// word.</description></item>
/// <item><description><b><em>Departments, Departments, Departments</em>.</b> Three demo
/// organisations each defined their own Department and Division types in one tenant, and the menu
/// rendered one entry per type. <b>Decision D17 proposed fixing that in the data — have the demos
/// share one Department type — and that was built and reverted</b>, because a dimension type is a
/// level of every structure that declares it, so a shared type put Crescent's divisions on Zenith's
/// chart. <b>Decision D23 fixes it in the menu instead</b>: types sharing an English singular name
/// become one entry, and where that entry's values span more than one organisation each value names
/// its own. See <see cref="DimensionMenuGroup"/>.</description></item>
/// <item><description><b>A menu nobody could shorten.</b>
/// <see cref="DimensionTypeDocument.ShowInMenu"/> is the switch D3 asks for.</description></item>
/// </list>
///
/// <b>Positions are explicit numbers, never <c>LocalizedString.PrefixPosition()</c>.</b> That
/// orders by the caption in whatever language the reader is in, so an Arabic reader got a different
/// order from an English one — for a menu whose order is a product decision rather than an
/// alphabetical accident. The same reasoning is why <see cref="DimensionTypeDocument.MenuOrder"/> is
/// a number a customer sets rather than a sort by name. The <em>values</em> under an entry are the
/// deliberate exception: D23 asks for them alphabetically in the reader's own language, so the two
/// languages list them differently on purpose and
/// <see cref="DimensionMenuGroup.Values(CultureInfo)"/> holds both orders.
///
/// <b>Reached through the cached menu service, not a query.</b> This runs on every admin request, so
/// it reads <see cref="IDimensionMenuService"/> — built once per tenant per day and invalidated by
/// signal (ADR-0019) — rather than asking the database for a dimension's values while a page is
/// waiting to render. Two reasons beyond speed: it applies no permission check, which is right here
/// because Orchard filters each entry by the <c>Permission</c> it declares; and it needs no
/// authorisation scope, which a navigation provider has no business entering.
/// </remarks>
internal sealed class AdminMenu : INavigationProvider
{
    /// <summary>
    /// The organisation designer, first. <b>Unchanged by slice F2 and put back by decision D23 A.</b>
    /// </summary>
    /// <remarks>
    /// <b>It briefly moved under Settings and that was wrong.</b> The designer is the screen an
    /// administrator building a chart lives in, not a thing you set up once; burying it two clicks
    /// deep changed a click path that had shipped, for no reason the brief asked for. D23 A puts it
    /// back at the top with the same caption, the same action and the same card actions, and
    /// <c>OrganisationDesignerMenuTenantTests</c> asserts that the entry and the card actions are
    /// what they were.
    /// </remarks>
    private const string DesignerPosition = "1";

    /// <summary>
    /// Reserved for the read-only enquiry report of FRS §5. <b>Built by slice F8.</b>
    /// </summary>
    /// <remarks>
    /// The position is claimed now and nothing is rendered at it, deliberately. Pointing it at the
    /// organisation designer in the meantime would be worse than an absence: the designer is the
    /// editable chart, and the FRS's own heading for this screen is "Enquiry Report (No Data
    /// Entry!)" — a menu item that promised one and opened the other is the kind of thing a reader
    /// trusts once.
    /// </remarks>
    private const string OrganisationTreePosition = "2";

    /// <summary>
    /// Where the per-dimension entries sit: between the designer and Settings.
    /// </summary>
    /// <remarks>
    /// A sub-position under one prefix rather than a flat number per dimension, so a tenant's own
    /// <c>MenuOrder</c> decides the order <em>within</em> this band and can never collide with the
    /// fixed entries above and below it. A customer who numbers a dimension 0 or 9999 still gets it
    /// between the designer and Settings.
    /// </remarks>
    private const string DimensionsPosition = "5";

    /// <summary>
    /// Configuration, last. FRS §1.2's own shape: the things you look at, then the things you set up.
    /// </summary>
    private const string SettingsPosition = "9";

    private readonly IDimensionMenuService _menu;
    private readonly IStringLocalizer S;

    public AdminMenu(IDimensionMenuService menu, IStringLocalizer<AdminMenu> stringLocalizer)
    {
        _menu = menu;
        S = stringLocalizer;
    }

    public async ValueTask BuildNavigationAsync(string name, NavigationBuilder builder)
    {
        if (!string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(builder);

        var menu = await _menu.GetAsync();

        builder.Add(S["Organisation"], organisation =>
        {
            organisation.Add(S["Organisation designer"], DesignerPosition, designer => designer
                .Action("Index", "OrganisationDesignerAdmin", new { area = "WorkMate.Dimensions" })
                .Permission(Permissions.ManageDimensionRecords)
                .LocalNav());

            // Position 2 is held for the Organisation tree; see OrganisationTreePosition.

            AddDimensions(organisation, menu);

            organisation.Add(S["Settings"], SettingsPosition, settings => settings
                .Add(S["Dimension types"], "1", types => types
                    .Action("Index", "DimensionTypesAdmin", new { area = "WorkMate.Dimensions" })
                    .Permission(Permissions.ManageDimensionTypes)
                    .LocalNav())
                .Add(S["Structures"], "2", structures => structures
                    .Action("Index", "StructuresAdmin", new { area = "WorkMate.Dimensions" })
                    .Permission(Permissions.ManageStructures)
                    .LocalNav()));

            // Settings holds two entries and no third. The Mapping screen that was reserved here
            // is dropped (D27): the containment map on the Structures editor stays the single
            // authority on what may sit under what, and ADR-0021 is withdrawn.
        });
    }

    /// <summary>
    /// One expandable entry per dimension: <em>All Departments</em>, then the departments
    /// themselves, then a way to the rest.
    /// </summary>
    /// <remarks>
    /// <b>Why the captions are computed here rather than in a view.</b> A dimension's plural and a
    /// value's name are the customer's own data, already resolved into the reader's language by the
    /// time <see cref="DimensionMenuGroup.Caption"/> has chosen a half. They are not resources and
    /// must not go near the PO files, which is why they do not go through <c>S[...]</c>. Orchard's
    /// navigation API takes a <see cref="LocalizedString"/> regardless, so each is passed as one
    /// whose name and value are the same.
    ///
    /// <b><em>All Departments</em> is a composed string and does go through the localiser</b>, with
    /// the plural as its argument — one msgid, "All {0}", which Arabic renders in its own word order.
    /// That is the difference between translating a sentence and translating a customer's noun.
    ///
    /// <b>The group is a menu concept only.</b> <em>All Departments</em> opens the ordinary list
    /// screen scoped to the group rather than to one type, which is what lets a tenant with three
    /// Department types see them on one screen with an Organisation column — and a tenant with one
    /// see exactly what it saw before.
    /// </remarks>
    private void AddDimensions(NavigationItemBuilder organisation, DimensionMenu menu)
    {
        var culture = CultureInfo.CurrentUICulture;
        var position = 0;

        foreach (var group in menu.Groups)
        {
            var caption = group.Caption(culture);
            var values = group.Values(culture);

            organisation.Add(
                new LocalizedString(caption, caption),
                // "5.1", "5.2", … — the tenant's own order, rendered into Orchard's dotted
                // position scheme so it sorts within this band and never outside it.
                $"{DimensionsPosition}.{++position}",
                dimension =>
                {
                    // With a plural, "All Departments". Without one the caption is the singular,
                    // and "All Workstream" is broken English the moment the word is in a sentence
                    // — so the phrasing changes rather than the word being invented. D24.
                    var allCaption = group.HasPlural(culture)
                        ? S["All {0}", caption]
                        : S["All {0} units", caption];

                    dimension
                        .Add(allCaption, "1", all => all
                            .Action("Index", "DimensionRecordsAdmin", new
                            {
                                area = "WorkMate.Dimensions",
                                group = group.GroupKey,
                            })
                            .Permission(Permissions.ManageDimensionRecords)
                            .LocalNav());

                    var valuePosition = 1;

                    foreach (var value in values)
                    {
                        var label = value.Caption(culture);

                        dimension.Add(
                            new LocalizedString(label, label),
                            $"2.{++valuePosition}",
                            entry => entry
                                .Action("Details", "DimensionRecordsAdmin", new
                                {
                                    area = "WorkMate.Dimensions",
                                    id = value.RecordId,
                                })
                                .Permission(Permissions.ManageDimensionRecords)
                                .LocalNav());
                    }

                    if (!group.HasMore)
                    {
                        return;
                    }

                    // Last, and only when there is a rest to show. The count is the whole dimension,
                    // not the remainder: "Show all (37)" is what the list screen will contain, which
                    // is the number a reader is deciding whether to open.
                    dimension.Add(S["Show all ({0})", group.TotalValues], "9", all => all
                        .Action("Index", "DimensionRecordsAdmin", new
                        {
                            area = "WorkMate.Dimensions",
                            group = group.GroupKey,
                        })
                        .Permission(Permissions.ManageDimensionRecords)
                        .LocalNav());
                });
        }
    }
}
