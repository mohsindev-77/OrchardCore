using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// What a dimension type's backing content type looked like at a point in time: enough to say
/// what changed, and nothing else.
/// </summary>
/// <remarks>
/// Deliberately not Orchard's own <c>ContentTypeDefinition</c>. Serialising that into the audit
/// trail would pin the record to an internal shape that moves between Orchard releases, and it
/// would bury the four facts an auditor actually asks about — which parts, which fields, which
/// flags — inside a settings blob. This is the projection that answers those questions and can
/// still be read in five years.
/// </remarks>
public sealed record ContentTypeDefinitionSnapshot(
    string Name,
    string DisplayName,
    bool Creatable,
    bool Listable,
    bool Securable,
    bool Draftable,
    IReadOnlyList<string> Parts,
    IReadOnlyList<ContentFieldSnapshot> Fields)
{
    /// <summary>Reads the current definition, or returns null when the type does not exist yet.</summary>
    public static ContentTypeDefinitionSnapshot? Of(ContentTypeDefinition? definition)
    {
        if (definition is null)
        {
            return null;
        }

        var settings = definition.GetSettings<ContentTypeSettings>();

        var fields = definition.Parts
            .SelectMany(part => part.PartDefinition.Fields
                .Select(field => new ContentFieldSnapshot(
                    part.PartDefinition.Name,
                    field.Name,
                    field.FieldDefinition.Name)))
            .OrderBy(field => field.PartName, StringComparer.Ordinal)
            .ThenBy(field => field.Name, StringComparer.Ordinal)
            .ToList();

        return new ContentTypeDefinitionSnapshot(
            definition.Name,
            definition.DisplayName ?? string.Empty,
            settings.Creatable,
            settings.Listable,
            settings.Securable,
            settings.Draftable,
            [.. definition.Parts.Select(part => part.PartDefinition.Name).OrderBy(name => name, StringComparer.Ordinal)],
            fields);
    }
}

/// <summary>One field on one part of a content type.</summary>
public sealed record ContentFieldSnapshot(string PartName, string Name, string FieldType);
