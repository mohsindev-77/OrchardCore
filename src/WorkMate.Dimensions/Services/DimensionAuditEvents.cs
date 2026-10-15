using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// What a <see cref="DimensionAuditTrail.DimensionTypeChanged"/> event carries.
/// </summary>
/// <remarks>
/// A plain class, not a record, because Orchard stores the event item by serialising it and
/// reads it back by deserialising into it. The before state is null on a creation.
/// </remarks>
public sealed class DimensionTypeAuditEvent
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    /// <summary>What the type looked like before, or null when this event created it.</summary>
    public DimensionTypeState? Before { get; set; }

    public DimensionTypeState After { get; set; } = new();
}

/// <summary>
/// Which record-level operation an audit entry records. Serialised by name.
/// </summary>
public enum DimensionRecordOperation
{
    /// <summary>A unit was created and placed on a structure in one act.</summary>
    Added,

    /// <summary>A corrective rename: the record was always called this and the old text was wrong.</summary>
    NameCorrected,

    /// <summary>A substantive rename: the record became something else on a date.</summary>
    Renamed,

    /// <summary>The record was closed with an end date.</summary>
    Retired,

    /// <summary>
    /// The short label the dimension's list and tree show beside the name was set or changed.
    /// </summary>
    /// <remarks>
    /// <b>The one operation here whose <c>EffectiveFrom</c> is not a date somebody chose.</b> An
    /// alias is undated by design — <c>DimensionRecordPart.AliasEn</c> sets out why — so there is no
    /// effective date to record, and the field carries the day the change was <em>made</em> instead.
    /// That is stated here rather than left for a reader to infer from a date that looks like every
    /// other one on this enum.
    /// </remarks>
    AliasChanged,

    /// <summary>
    /// The record's references of one kind were stated — today, a Position's allowed grades
    /// (D26 C).
    /// </summary>
    /// <remarks>
    /// <b>Appended, never inserted.</b> This enum is serialised into audit entries and
    /// <c>System.Text.Json</c> writes an enum as its number unless told otherwise, so adding a
    /// member anywhere but the end silently changes what every entry already written means. The
    /// same contract <c>DimensionRule</c> states on its own last member.
    ///
    /// <b>Undated, like <see cref="AliasChanged"/>.</b> A reference has no effective range — see
    /// <c>DimensionReferenceKinds</c> — so <c>EffectiveFrom</c> carries the day the change was
    /// <em>made</em> rather than a date somebody chose.
    ///
    /// Audited because removing a grade is a change a reader may have to account for later: the
    /// employee who holds it keeps it (D26 G), so the only record that it stopped being offered is
    /// this one.
    /// </remarks>
    ReferencesChanged,
}

/// <summary>
/// What a <see cref="DimensionAuditTrail.DimensionRecordChanged"/> event carries.
/// </summary>
/// <remarks>
/// The question an auditor asks of a unit is "what was it called in March, and who changed that",
/// so the payload is the dated shape of the change rather than a field-by-field diff: the
/// operation, the date it takes effect from, and the name on either side of it. A plain class,
/// like the others here, because Orchard serialises it.
/// </remarks>
public sealed class DimensionRecordAuditEvent
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public DimensionRecordOperation Operation { get; set; }

    /// <summary>The date the change takes effect, which is never implied and never today by default.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>The structure the unit was added to. Set on <see cref="DimensionRecordOperation.Added"/> only.</summary>
    public string? StructureId { get; set; }

    /// <summary>The parent it was placed under, or null for a root. Added only.</summary>
    public string? ParentRecordId { get; set; }

    /// <summary>
    /// The unit whose retirement closed this one as well, when it was not retired in its own
    /// right. Null for a unit somebody retired directly.
    /// </summary>
    /// <remarks>
    /// Without it, a cascade leaves a row of units all closing on the same day with nothing to
    /// say they were one decision — which is exactly the question an auditor asks when a whole
    /// branch disappears at once.
    /// </remarks>
    public string? CascadedFromRecordId { get; set; }

    /// <summary>What it was called before, or null when this event created it.</summary>
    public DimensionRecordNameState? Before { get; set; }

    /// <summary>What it is called after. Null on a retirement, which changes no name.</summary>
    public DimensionRecordNameState? After { get; set; }

    /// <summary>
    /// The alias on either side of the change. Set on
    /// <see cref="DimensionRecordOperation.AliasChanged"/> only, and null everywhere else.
    /// </summary>
    /// <remarks>
    /// Four plain strings rather than two <see cref="DimensionRecordNameState"/>s, even though the
    /// shape is the same pair. That type's properties are called <c>NameEn</c> and <c>NameAr</c>,
    /// and an auditor reading a serialised entry whose <c>NameEn</c> held an alias would have no way
    /// to tell which of the two facts they were looking at.
    /// </remarks>
    public string? AliasEnBefore { get; set; }

    /// <inheritdoc cref="AliasEnBefore"/>
    public string? AliasArBefore { get; set; }

    /// <inheritdoc cref="AliasEnBefore"/>
    public string? AliasEnAfter { get; set; }

    /// <inheritdoc cref="AliasEnBefore"/>
    public string? AliasArAfter { get; set; }

    /// <summary>
    /// Which kind of reference changed, and the codes on either side. Set on
    /// <see cref="DimensionRecordOperation.ReferencesChanged"/> only.
    /// </summary>
    /// <remarks>
    /// By <b>code</b> rather than by id, for the reason <c>StructureState</c> writes its containment
    /// map by code: an audit entry is read by a person, and a generated id tells them nothing about
    /// which grade left the list.
    /// </remarks>
    public string? ReferenceKind { get; set; }

    /// <inheritdoc cref="ReferenceKind"/>
    public IReadOnlyList<string>? ReferencesBefore { get; set; }

    /// <inheritdoc cref="ReferenceKind"/>
    public IReadOnlyList<string>? ReferencesAfter { get; set; }
}

/// <summary>A record's bilingual name at one point in an audit entry.</summary>
public sealed class DimensionRecordNameState
{
    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public static DimensionRecordNameState Of(WorkMate.Core.BilingualText name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new DimensionRecordNameState { NameEn = name.En, NameAr = name.Ar };
    }
}

/// <summary>What a <see cref="DimensionAuditTrail.StructureChanged"/> event carries.</summary>
public sealed class StructureAuditEvent
{
    public string StructureId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public StructureState? Before { get; set; }

    public StructureState After { get; set; } = new();
}

/// <summary>
/// What a <see cref="DimensionAuditTrail.ContentDefinitionChanged"/> event carries: the diff of
/// the definition and the dimension type on whose behalf it was changed.
/// </summary>
public sealed class ContentDefinitionAuditEvent
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string ContentTypeName { get; set; } = string.Empty;

    public ContentTypeDefinitionDiff? Diff { get; set; }
}

/// <summary>
/// What a <see cref="DimensionAuditTrail.MoveCancelled"/> event carries.
/// </summary>
/// <remarks>
/// Carries <see cref="Reason"/>, which no other event in this module does: cancelling a move is
/// the one record-level operation the caller is required to justify, never a silent delete.
/// </remarks>
public sealed class MoveCancelledAuditEvent
{
    public string StructureId { get; set; } = string.Empty;

    public string RecordId { get; set; } = string.Empty;

    /// <summary>The date the cancelled move was effective from.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>The parent the cancelled move had placed the record under.</summary>
    public string? CancelledParentId { get; set; }

    /// <summary>The parent the record is restored to, or null when it is restored to being a root.</summary>
    public string? RestoredParentId { get; set; }

    /// <summary>How far the restored placement now runs, or null when it is open-ended.</summary>
    public DateOnly? RestoredUntil { get; set; }

    /// <summary>The mandatory, free-text justification the acting user gave for the cancellation.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A dimension type's auditable state, flattened so the trail stays readable.</summary>
public sealed class DimensionTypeState
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool IsSystemDefined { get; set; }

    public bool AllowsSelfNesting { get; set; }

    public string ContentTypeName { get; set; } = string.Empty;

    public DateOnly? RetiredOn { get; set; }

    /// <summary>The attribute schema as <c>Name (Kind, required)</c>, so a reader sees it at a glance.</summary>
    public IReadOnlyList<string> AttributeSchema { get; set; } = [];

    /// <summary>
    /// The part this dimension plays in the product — Position, Grade or neither.
    /// </summary>
    /// <remarks>
    /// Audited because changing it changes what two screens offer: demoting the Position dimension
    /// empties the employee form's position drop-down, and nothing else on the type says so. Carried
    /// as its name rather than its number, because an audit entry is read by a person.
    /// </remarks>
    public string StandardRole { get; set; } = nameof(DimensionTypeRole.None);

    public static DimensionTypeState Of(DimensionTypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new DimensionTypeState
        {
            Code = document.Code,
            NameEn = document.Name.En,
            NameAr = document.Name.Ar,
            IsSystemDefined = document.IsSystemDefined,
            AllowsSelfNesting = document.AllowsSelfNesting,
            ContentTypeName = document.ContentTypeName,
            RetiredOn = document.RetiredOn,
            StandardRole = document.StandardRole.ToString(),
            AttributeSchema =
            [
                .. document.AttributeSchema.Select(attribute =>
                    $"{attribute.Name} ({attribute.Kind}{(attribute.IsRequired ? ", required" : string.Empty)})"),
            ],
        };
    }
}

/// <summary>A structure's auditable state, flattened so the trail stays readable.</summary>
public sealed class StructureState
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; }

    public bool IsPrimaryOrganisation { get; set; }

    /// <summary>The levels in order, by dimension type code, which is what a reader recognises.</summary>
    public IReadOnlyList<string> Levels { get; set; } = [];

    /// <summary>The types that may sit at the top of the axis, by code.</summary>
    public IReadOnlyList<string> RootTypes { get; set; } = [];

    /// <summary>
    /// The containment map, each pairing written "parent &gt; child" by code.
    /// </summary>
    /// <remarks>
    /// As text rather than as a structured pair, because this is a diff a person reads: a line
    /// appearing or disappearing is exactly the change that was made, and ADR-0010 made this the
    /// part of a structure most worth being able to see change.
    /// </remarks>
    public IReadOnlyList<string> Containment { get; set; } = [];

    public static StructureState Of(StructureDocument document, IReadOnlyDictionary<string, string> typeCodesById)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(typeCodesById);

        return new StructureState
        {
            Code = document.Code,
            NameEn = document.Name.En,
            NameAr = document.Name.Ar,
            AllowSkipLevel = document.AllowSkipLevel,
            IsStrict = document.IsStrict,
            IsPrimaryOrganisation = document.IsPrimaryOrganisation,
            Levels =
            [
                .. document.Levels
                    .OrderBy(level => level.Ordinal)
                    .Select(level => Label(level.DimensionTypeId, typeCodesById)),
            ],
            RootTypes =
            [
                .. document.RootDimensionTypeIds
                    .Select(id => Label(id, typeCodesById))
                    .OrderBy(code => code, StringComparer.Ordinal),
            ],
            Containment =
            [
                .. document.Containment
                    .Select(pair =>
                        $"{Label(pair.ParentDimensionTypeId, typeCodesById)} > {Label(pair.ChildDimensionTypeId, typeCodesById)}")
                    .OrderBy(line => line, StringComparer.Ordinal),
            ],
        };
    }

    /// <summary>The type's code, which a reader recognises, falling back to the id if it is gone.</summary>
    private static string Label(string dimensionTypeId, IReadOnlyDictionary<string, string> typeCodesById) =>
        typeCodesById.TryGetValue(dimensionTypeId, out var code) ? code : dimensionTypeId;
}
