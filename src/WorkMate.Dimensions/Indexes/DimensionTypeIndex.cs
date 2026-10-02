using WorkMate.Dimensions.Models;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Indexes;

/// <summary>
/// The queryable shape of <see cref="DimensionTypeDocument"/>: enough to resolve a type by id or
/// code, to find the type behind a content type, and to filter the retired ones out of a picker,
/// without loading the document.
/// </summary>
/// <remarks>
/// <c>RetiredOn</c> is a <c>DateTime</c> rather than a <c>DateOnly</c> because YesSql 5.4.7 has
/// no mapping for <c>DateOnly</c> — the dialect resolves it to <c>DbType.Object</c> and the
/// schema builder then throws. ADR-0005 records this; <c>EffectiveDates</c> owns the conversion
/// so that no other code has to know about it.
/// </remarks>
public sealed class DimensionTypeIndex : MapIndex
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    /// <summary>The content type created for this dimension type, for the reverse lookup a record handler needs.</summary>
    public string ContentTypeName { get; set; } = string.Empty;

    public bool IsSystemDefined { get; set; }

    public bool AllowsSelfNesting { get; set; }

    /// <summary>Null while the type is current. A midnight <c>DateTime</c>; see the remark on the class.</summary>
    public DateTime? RetiredOn { get; set; }
}
