namespace WorkMate.Dimensions.Models;

/// <summary>
/// One permitted parent-child pairing on a structure: a record of
/// <paramref name="ChildDimensionTypeId"/> may sit directly under a record of
/// <paramref name="ParentDimensionTypeId"/>.
/// </summary>
/// <remarks>
/// The whole of ADR-0010 in one record. Containment used to be arithmetic on level ordinals, which
/// can only describe a chain: a type is above another type or below it, never beside it. Real
/// organisations branch — under a Division either a Department or a Project; under one Division
/// Departments and under its sibling Regions — and a chain has no way to say so except by turning
/// on level skipping, which says it by permitting everything.
///
/// A set of pairs says it exactly. It is also the shape the editor draws: types down the side,
/// types across the top, a tick where a pair is in this list.
/// </remarks>
/// <param name="ParentDimensionTypeId">The containing type.</param>
/// <param name="ChildDimensionTypeId">
/// The type it may contain. Equal to <paramref name="ParentDimensionTypeId"/> for self-nesting,
/// which this pair is the whole of: the dimension type used to hold a veto over it that no
/// structure could grant past, and ADR-0010's addendum removed it. A Department inside a Department
/// is normal in one company and wrong in another, which makes it the same kind of fact as every
/// other cell in this map.
/// </param>
public sealed record StructureContainment(string ParentDimensionTypeId, string ChildDimensionTypeId);
