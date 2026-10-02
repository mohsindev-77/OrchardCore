using WorkMate.Dimensions.Models;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Indexes;

/// <summary>
/// The queryable shape of <see cref="StructureDocument"/>: resolve an axis by id or code, and
/// find the primary organisation axis, without loading the document.
/// </summary>
public sealed class StructureIndex : MapIndex
{
    public string StructureId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public bool IsPrimaryOrganisation { get; set; }

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; }

    /// <summary>
    /// How many levels the axis declares. Indexed so that the designer's list can show the shape
    /// of every structure without loading each one.
    /// </summary>
    public int LevelCount { get; set; }
}
