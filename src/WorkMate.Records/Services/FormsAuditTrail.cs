using Microsoft.Extensions.Options;
using OrchardCore.AuditTrail.Services.Models;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// The audit trail category and event names this module's form configuration records under.
/// </summary>
/// <remarks>
/// <b>A second category beside <c>Employee</c>, not more events inside it.</b> An auditor filtering
/// the employee category is asking about people — who was hired, who left, whose join date moved.
/// A lookup list being edited is a change to how the tenant is configured, and it changes what
/// every employee record can say rather than what any one of them does say. Mixing the two would
/// mean reading a thousand employee events to find the configuration change that explains them.
///
/// Constants rather than literals at the call sites, for the reason <c>RecordsAuditTrail</c> gives:
/// the same name appears in the options that declare the event, in the manager call that records
/// one, and in the filter an administrator uses to find them. A typo in any one of the three
/// produces an event that is recorded and never surfaced — worse than no audit trail, because it
/// looks like one.
///
/// The category is named for what will live in it rather than for what is in it today: slice B3's
/// form definitions and publishes are the same kind of change and file here.
/// </remarks>
public static class FormsAuditTrail
{
    /// <summary>The category form configuration is filed under.</summary>
    public const string Category = "Forms";

    /// <summary>A lookup list was created, changed or deleted.</summary>
    public const string LookupListChanged = "LookupListChanged";
}

/// <summary>
/// Declares the form configuration category and its events, so an administrator can find and filter
/// them alongside Orchard's own.
/// </summary>
/// <remarks>
/// Mandatory rather than merely enabled by default, like this module's other three. An event an
/// administrator can quietly switch off is not an audit trail — and "who retired the grade every
/// report groups by" is exactly the question this exists to answer.
/// </remarks>
public sealed class FormsAuditTrailOptionsConfiguration : IConfigureOptions<AuditTrailOptions>
{
    public void Configure(AuditTrailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.For<FormsAuditTrailOptionsConfiguration>(
                FormsAuditTrail.Category,
                S => S["Forms"])
            .WithEvent(
                FormsAuditTrail.LookupListChanged,
                S => S["Lookup list changed"],
                S => S["A list of choices was created, renamed, or had entries added or retired."],
                enableByDefault: true,
                isMandatory: true);
    }
}

/// <summary>What a lookup list said, for the before-and-after on an audit entry.</summary>
/// <remarks>
/// The whole list, entries included, rather than a summary. A list is small — tens of entries, not
/// thousands — and the question an auditor brings to this entry is nearly always "which entry
/// changed", which a count cannot answer.
/// </remarks>
public sealed class LookupListState
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public List<LookupListItemState> Items { get; set; } = [];

    public static LookupListState Of(LookupListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new LookupListState
        {
            Code = document.Code,
            NameEn = document.Name.En,
            NameAr = document.Name.Ar,
            Items =
            [
                .. document.Items.Select(item => new LookupListItemState
                {
                    Code = item.Code,
                    NameEn = item.Name.En,
                    NameAr = item.Name.Ar,
                    IsActive = item.IsActive,
                    SortOrder = item.SortOrder,
                }),
            ],
        };
    }
}

/// <summary>One entry, as an audit entry records it.</summary>
public sealed class LookupListItemState
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>The payload of a <see cref="FormsAuditTrail.LookupListChanged"/> entry.</summary>
public sealed class LookupListAuditEvent
{
    public string LookupListId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    /// <summary>What it said before, or null on creation.</summary>
    public LookupListState? Before { get; set; }

    /// <summary>What it says now, or null on deletion.</summary>
    public LookupListState? After { get; set; }

    /// <summary>
    /// The operation, named, so a reader does not have to infer it from the diff.
    /// </summary>
    /// <remarks>
    /// The same reasoning as <c>EmployeeAuditEvent.Operation</c>: a retire and a rename look alike
    /// in a before-and-after of two entry lists, and what they mean to a report that groups by this
    /// list does not.
    /// </remarks>
    public string Operation { get; set; } = string.Empty;
}
