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
                    .Select(level => typeCodesById.TryGetValue(level.DimensionTypeId, out var code)
                        ? code
                        : level.DimensionTypeId),
            ],
        };
    }
}
