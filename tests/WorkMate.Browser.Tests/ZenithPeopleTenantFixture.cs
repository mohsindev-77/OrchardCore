using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Zenith with its people on it: the only browser tenant that loads an employee recipe.
/// </summary>
/// <remarks>
/// <b>Its own tenant, deliberately.</b> The standing instruction is that the structure-only tests
/// and the browser suite must not load the employee recipes — 158 people is a tenant every other
/// fixture would pay to build and none of them would use. That rule is about the shared fixtures,
/// not about never testing the data: the picker's whole defect was that it broke at a scale the
/// demo organisation never reaches, so the one test that must see that scale gets a tenant that
/// has it, and nothing else is slowed down.
///
/// Crescent is left out for the same reason: this fixture exists for one question, and a second
/// company would be a second minute of setup answering none of it.
/// </remarks>
public sealed class ZenithPeopleTenantFixture : BrowserTenantFixture
{
    protected override IReadOnlyList<(string FileName, string Evidence)> Recipes =>
    [
        // Without the ampersand: the name is HTML-encoded on the page and this is a raw string
        // search over the markup.
        ("organisation-designer-zenith.recipe.json", "Zenith Engineering"),
        ("organisation-designer-zenith-employees.recipe.json", "zenith-civil-01"),
    ];
}

[CollectionDefinition(Name)]
public sealed class UsesTheZenithPeopleTenant : ICollectionFixture<ZenithPeopleTenantFixture>
{
    public const string Name = "zenith people tenant";
}
