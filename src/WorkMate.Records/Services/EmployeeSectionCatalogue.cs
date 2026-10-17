using System.Text;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Flows.Models;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <inheritdoc cref="IEmployeeSectionCatalogue"/>
/// <remarks>
/// Walked from the content definition, exactly as <see cref="LookupValueReconciliation"/> walks it
/// for bound fields and for the same reason: the definition is what the tenant has, and the
/// constant is only what the product shipped.
///
/// A pure read path, so <c>GetTypeDefinitionAsync</c> rather than <c>Load…</c> — ADR-0006: the
/// cached read is wrong only inside a scope that has just changed a definition, and no scope that
/// draws an employee page does.
/// </remarks>
internal sealed class EmployeeSectionCatalogue : IEmployeeSectionCatalogue
{
    private readonly IContentDefinitionManager _contentDefinitionManager;

    public EmployeeSectionCatalogue(IContentDefinitionManager contentDefinitionManager) =>
        _contentDefinitionManager = contentDefinitionManager;

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmployeeSectionRef>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var employeeType = await _contentDefinitionManager.GetTypeDefinitionAsync(
            EmployeeFieldNames.ContentType);

        if (employeeType is null)
        {
            // A tenant whose Records feature is not enabled. An empty list rather than a throw:
            // the caller is a screen, and "no sections" renders correctly.
            return [];
        }

        var sections = new List<EmployeeSectionRef>();

        foreach (var typePart in employeeType.Parts.Where(
            part => part.PartDefinition.Name == nameof(BagPart)))
        {
            var contained = typePart.GetSettings<BagPartSettings>().ContainedContentTypes ?? [];

            // One kind of item per section — the migration sets ContainedContentTypes to exactly
            // one — so the first is the section's row type. A bag somebody configured to hold
            // several is still a section; it is named by the first, which is what the heading says.
            var itemTypeName = contained.FirstOrDefault();

            if (string.IsNullOrEmpty(itemTypeName))
            {
                continue;
            }

            var itemType = await _contentDefinitionManager.GetTypeDefinitionAsync(itemTypeName);

            // The stereotype is the test, never the name. A bag of something else on the Employee
            // type belongs to whoever put it there and is not this screen's to render as a section.
            if (itemType?.GetSettings<ContentTypeSettings>().Stereotype
                != EmployeeSections.Stereotype)
            {
                continue;
            }

            sections.Add(new EmployeeSectionRef(
                typePart.Name,
                DisplayNameOf(typePart),
                itemTypeName));
        }

        return sections;
    }

    /// <summary>
    /// What to call a section on screen.
    /// </summary>
    /// <remarks>
    /// The definition's own display name where it differs from the part name, because that is where
    /// B4 puts the words a tenant typed. Where it does not — every section WorkMate ships, whose
    /// display name the migration set to the part name — the <c>PascalCase</c> identifier is spaced
    /// out, so <c>BankDetails</c> reads as <em>Bank details</em> rather than as a symbol.
    ///
    /// <b>Not localised, deliberately.</b> A heading that may be a customer's own words cannot be
    /// a resource lookup: there is nothing to look it up in. The strings around the loop — the
    /// page's heading, the Edit button — are localised in the view, which is where this module's
    /// own words live.
    /// </remarks>
    private static string DisplayNameOf(ContentTypePartDefinition typePart)
    {
        var declared = typePart.GetSettings<ContentTypePartSettings>().DisplayName;

        return string.IsNullOrWhiteSpace(declared) || string.Equals(declared, typePart.Name, StringComparison.Ordinal)
            ? Spaced(typePart.Name)
            : declared;
    }

    /// <summary>Splits a PascalCase identifier into words: <c>BankDetails</c> → "Bank details".</summary>
    private static string Spaced(string name)
    {
        var text = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];

            if (index > 0 && char.IsUpper(character))
            {
                text.Append(' ').Append(char.ToLowerInvariant(character));
            }
            else
            {
                text.Append(character);
            }
        }

        return text.ToString();
    }
}
