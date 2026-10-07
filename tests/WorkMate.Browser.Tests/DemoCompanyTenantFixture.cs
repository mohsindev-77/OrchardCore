using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// A tenant carrying the two demo companies — Zenith and Crescent — instead of the demo
/// organisation.
/// </summary>
/// <remarks>
/// Its own tenant rather than a few more recipes on the shared one. The designer opens on
/// whichever configured structure sorts first by code, and <c>crescent-org</c> sorts before
/// <c>demo-org</c>: adding these two to the shared tenant would silently move every other browser
/// test in the suite onto a different chart.
///
/// Worth a second Kestrel and a second browser because these two companies are the only data that
/// shows the defects in question. Codes like <c>zenith-dept-mechanical</c> are wider than a card;
/// the demo organisation's <c>demo-div-sales</c> is not. A tree fifteen units across overflows a
/// viewport; one with six does not. And both recipes deliberately list siblings out of
/// alphabetical order, which is the only way to tell sort order from a coincidence.
/// </remarks>
public sealed class DemoCompanyTenantFixture : BrowserTenantFixture
{
    public const string ZenithStructureCode = "zenith-org";
    public const string CrescentStructureCode = "crescent-org";

    protected override IReadOnlyList<(string FileName, string Evidence)> Recipes =>
    [
        // Without the ampersand: the name is HTML-encoded on the page and this is a raw string
        // search over the markup.
        ("organisation-designer-zenith.recipe.json", "Zenith Engineering"),
        ("organisation-designer-crescent.recipe.json", "Crescent Microfinance"),
    ];
}

[CollectionDefinition(Name)]
public sealed class UsesTheDemoCompanyTenant : ICollectionFixture<DemoCompanyTenantFixture>
{
    public const string Name = "demo company tenant";
}
