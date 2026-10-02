namespace WorkMate.Dimensions.Models;

/// <summary>
/// The field kinds a dimension type's attribute schema may declare.
/// </summary>
/// <remarks>
/// This is deliberately a closed list rather than "any Orchard field type". A dimension type's
/// schema is customer configuration, and a customer who can name any field type installed on the
/// tenant can create a content definition this module cannot reason about, export or migrate.
/// The richer field palette belongs to the form designer in WorkMate.Records, which is specified
/// for it; this list covers what a unit of organisation structure actually carries.
///
/// The member names are persisted in <see cref="DimensionAttributeDefinition"/>, so renaming one
/// is a data migration, not a refactor.
/// </remarks>
public enum DimensionAttributeKind
{
    /// <summary>A single line of text in one language. Orchard's <c>TextField</c>.</summary>
    Text = 0,

    /// <summary>A name or label carried in both platform languages. WorkMate's <c>BilingualTextField</c>.</summary>
    BilingualText = 1,

    /// <summary>A number. Orchard's <c>NumericField</c>.</summary>
    Number = 2,

    /// <summary>A yes or no. Orchard's <c>BooleanField</c>.</summary>
    Boolean = 3,

    /// <summary>A calendar date. Orchard's <c>DateField</c>.</summary>
    Date = 4,
}

/// <summary>
/// Maps each <see cref="DimensionAttributeKind"/> to the Orchard field type that implements it.
/// </summary>
/// <remarks>
/// The field type is named by string rather than by <c>nameof</c> on the field class, because
/// doing the latter would make this module reference OrchardCore.ContentFields for four type
/// names it never otherwise touches. The names are verified against the pinned version by
/// <c>DimensionAttributeKindTests</c>, which resolves each one through the content field options
/// registered on a running tenant, so a rename in Orchard fails a test rather than a customer's
/// tenant setup.
/// </remarks>
public static class DimensionAttributeKinds
{
    public static string FieldTypeNameFor(DimensionAttributeKind kind) => kind switch
    {
        DimensionAttributeKind.Text => "TextField",
        DimensionAttributeKind.BilingualText => "BilingualTextField",
        DimensionAttributeKind.Number => "NumericField",
        DimensionAttributeKind.Boolean => "BooleanField",
        DimensionAttributeKind.Date => "DateField",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Every kind, for the tests and for the schema builder's picker in prompt 3.</summary>
    public static readonly IReadOnlyList<DimensionAttributeKind> All = Enum.GetValues<DimensionAttributeKind>();
}
