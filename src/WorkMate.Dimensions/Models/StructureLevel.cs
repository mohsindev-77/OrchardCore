namespace WorkMate.Dimensions.Models;

/// <summary>
/// One level of a structure: which dimension type sits at this depth.
/// </summary>
/// <param name="Ordinal">
/// Zero for the root level, counting down the hierarchy. Ordinals are contiguous and unique
/// within a structure; <c>IStructureService</c> renumbers them on every write so that a caller
/// can reorder levels by supplying them in the order it wants.
/// </param>
/// <param name="DimensionTypeId">The type permitted at this level.</param>
public sealed record StructureLevel(int Ordinal, string DimensionTypeId);
