using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// One entry in a dimension type's attribute schema: a field its records carry beyond the
/// standard ones on <c>DimensionRecordPart</c>.
/// </summary>
/// <param name="Name">
/// The technical name. It becomes the field's name in the content definition, so it must be a
/// valid Orchard field name and it is immutable once records exist.
/// </param>
/// <param name="Label">What the editor shows, in both platform languages.</param>
/// <param name="Kind">Which field type backs it.</param>
/// <param name="IsRequired">Whether a record may be saved without it.</param>
public sealed record DimensionAttributeDefinition(
    string Name,
    BilingualText Label,
    DimensionAttributeKind Kind,
    bool IsRequired = false);
