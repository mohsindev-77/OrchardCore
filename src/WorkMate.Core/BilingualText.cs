namespace WorkMate.Core;

/// <summary>Every user-facing name on the platform carries both languages.</summary>
public sealed record BilingualText(string En, string Ar)
{
    /// <summary>
    /// A new, empty instance every time it is read — never a shared one.
    /// </summary>
    /// <remarks>
    /// This was a <c>static readonly</c> field until ADR-0007: one object, handed out to every
    /// caller, including every document property that used it as a default value. A document
    /// property that defaults to the same shared instance as every other document of its kind is
    /// exactly the shape that let two unrelated dimension types read each other's name once a
    /// tenant held more than one — see <c>DimensionTypeDocument.Name</c>'s history. A property
    /// getter that allocates removes the hazard at the source: nothing that reads
    /// <see cref="Empty"/> can ever receive the same object another caller also holds, so there is
    /// nothing left for a deserialiser — or anything else — to write into on one caller's behalf
    /// and have a second caller see.
    /// </remarks>
    public static BilingualText Empty => new(string.Empty, string.Empty);
}
