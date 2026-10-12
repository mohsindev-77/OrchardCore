using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkMate.Core;
using WorkMate.Records.Models;

namespace WorkMate.Records.ViewModels;

/// <summary>One lookup list, as the list of lists shows it.</summary>
public sealed class LookupListListItemViewModel
{
    public string LookupListId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public bool IsSystemDefined { get; set; }

    public int ItemCount { get; set; }

    public int ActiveItemCount { get; set; }

    /// <summary>How many entries are retired. Shown as a badge only when there are any.</summary>
    public int RetiredItemCount => ItemCount - ActiveItemCount;

    public static LookupListListItemViewModel Of(LookupListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new LookupListListItemViewModel
        {
            LookupListId = document.LookupListId,
            Code = document.Code,
            NameEn = document.Name.En,
            NameAr = document.Name.Ar,
            IsSystemDefined = document.IsSystemDefined,
            ItemCount = document.Items.Count,
            ActiveItemCount = document.Items.Count(item => item.IsActive),
        };
    }
}

/// <summary>
/// The editor for one lookup list and the whole of its entries.
/// </summary>
/// <remarks>
/// Every string property is <c>string?</c> and every getter survives all of them being null. An
/// empty text box binds to <c>null</c> rather than to <c>""</c> — <c>ModelMetadata
/// .ConvertEmptyStringToNull</c> defaults to true — and MVC's <c>ComplexObjectModelBinder</c> reads
/// every public getter while binding, so a computed property that dereferenced one would throw
/// inside the binder, before the action method and before <c>ModelState</c>, with no way to show a
/// field error instead of a 500. ADR-0003's addendum records the six that cost;
/// <c>ViewModelsSurviveNullBindingTests</c> is what keeps the class of defect from recurring.
/// </remarks>
public sealed class LookupListEditViewModel
{
    public string? LookupListId { get; set; }

    /// <summary>
    /// The code. Editable on create, shown and ignored on edit.
    /// </summary>
    /// <remarks>
    /// The controller overwrites a posted value from the stored document before anything is
    /// validated, so a hand-made POST cannot rename a list by sending a different code — the same
    /// shape <c>DimensionTypesAdminController.EditPost</c> uses for a dimension type's code.
    /// </remarks>
    [StringLength(64)]
    public string? Code { get; set; }

    [StringLength(255)]
    public string? NameEn { get; set; }

    [StringLength(255)]
    public string? NameAr { get; set; }

    public bool IsNew { get; set; }

    public bool IsSystemDefined { get; set; }

    /// <summary>The entries, in the order the editor shows them.</summary>
    public List<LookupListItemRowViewModel> Items { get; set; } = [];

    /// <summary>The name as the service takes it.</summary>
    public BilingualText Name => new(NameEn ?? string.Empty, NameAr ?? string.Empty);

    /// <summary>The entries as the service takes them.</summary>
    public IReadOnlyList<LookupListItem> ToItems() =>
    [
        .. Items.Select(row => new LookupListItem(
            row.Code ?? string.Empty,
            new BilingualText(row.NameEn ?? string.Empty, row.NameAr ?? string.Empty),
            row.IsActive,
            row.SortOrder)),
    ];

    public static LookupListEditViewModel Of(LookupListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new LookupListEditViewModel
        {
            LookupListId = document.LookupListId,
            Code = document.Code,
            NameEn = document.Name.En,
            NameAr = document.Name.Ar,
            IsNew = false,
            IsSystemDefined = document.IsSystemDefined,
            Items =
            [
                .. document.Items.Select(item => new LookupListItemRowViewModel
                {
                    Code = item.Code,
                    NameEn = item.Name.En,
                    NameAr = item.Name.Ar,
                    IsActive = item.IsActive,
                    SortOrder = item.SortOrder,
                    IsStored = true,
                }),
            ],
        };
    }
}

/// <summary>One row of the entries table.</summary>
public sealed class LookupListItemRowViewModel
{
    [StringLength(64)]
    public string? Code { get; set; }

    [StringLength(255)]
    public string? NameEn { get; set; }

    [StringLength(255)]
    public string? NameAr { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>
    /// Whether the tenant already holds this entry, which is what decides the row's affordances:
    /// a stored entry's code is read-only and it has no Remove button, only the Active tick.
    /// </summary>
    /// <remarks>
    /// <b>A hint to the view, never the authority.</b> It is posted, so a hand-made request can
    /// claim anything about it; <c>ILookupListService.UpdateAsync</c> compares the submitted set
    /// against what is actually stored and refuses a dropped or renamed entry regardless of what
    /// this says. The field exists so the screen can show the right controls, not so the rule can
    /// be enforced in the browser.
    /// </remarks>
    [BindNever]
    public bool IsStored { get; set; }
}

/// <summary>Confirming the deletion of an empty lookup list.</summary>
public sealed class LookupListDeleteViewModel
{
    public string? LookupListId { get; set; }

    /// <summary>
    /// Shown, never trusted. <c>[BindNever]</c> so a posted value cannot make the confirmation page
    /// name a different list from the one the id deletes.
    /// </summary>
    [BindNever]
    public string? Code { get; set; }

    /// <inheritdoc cref="Code"/>
    [BindNever]
    public string? NameEn { get; set; }
}

/// <summary>
/// The "Check stored values" screen: what records hold that the list has no entry for.
/// </summary>
public sealed class LookupValuesViewModel
{
    public string? LookupListId { get; set; }

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    /// <summary>Where this list's values are stored, in words a reader recognises.</summary>
    /// <remarks>
    /// Shown because a reader who has just bound a list needs to know the scan looked where they
    /// expect. An empty list here is the answer to "why did it find nothing".
    /// </remarks>
    public List<string> BoundLocations { get; set; } = [];

    public int EmployeesScanned { get; set; }

    public bool WasTruncated { get; set; }

    /// <summary>The entries a value may be mapped onto: the active ones.</summary>
    public List<LookupValueTargetViewModel> Targets { get; set; } = [];

    public List<UnmatchedValueRowViewModel> Rows { get; set; } = [];
}

/// <summary>One entry offered as a mapping target.</summary>
public sealed class LookupValueTargetViewModel
{
    public string? Code { get; set; }

    public string? Label { get; set; }
}

/// <summary>One unmatched value, and what the reader has chosen to do with it.</summary>
public sealed class UnmatchedValueRowViewModel
{
    public string? Value { get; set; }

    public int Count { get; set; }

    public string? SampleEmployeeCodes { get; set; }

    /// <summary>
    /// <c>leave</c>, <c>add</c> or <c>map</c>. Defaults to leave, so a reader who presses Apply
    /// without choosing anything changes nothing.
    /// </summary>
    public string? Action { get; set; } = UnmatchedValueActions.Leave;

    /// <summary>The entry code to map onto, when <see cref="Action"/> is <c>map</c>.</summary>
    public string? MapTo { get; set; }
}

/// <summary>What a reader may do with one unmatched value.</summary>
/// <remarks>
/// Constants rather than an enum because they are posted as form values and read in a view: an
/// enum would be bound by name anyway, and three strings used in three places are clearer named
/// once than parsed twice.
/// </remarks>
public static class UnmatchedValueActions
{
    /// <summary>Do nothing. The default, and a supported permanent state.</summary>
    public const string Leave = "leave";

    /// <summary>Add the value to the list as a new entry. Touches no record.</summary>
    public const string Add = "add";

    /// <summary>Rewrite the value on every record holding it. The one irreversible action.</summary>
    public const string Map = "map";
}

/// <summary>
/// The Forms screen: the one new admin menu entry, and what is reachable from it.
/// </summary>
/// <remarks>
/// A view model with counts rather than a static page, because the whole job of this screen in
/// slice B1 is to be the honest way in to the lookup lists — and a reader deciding whether to click
/// through wants to know whether there are any.
/// </remarks>
public sealed class FormsIndexViewModel
{
    public int LookupListCount { get; set; }

    /// <summary>Whether the reader may open the lookup lists at all.</summary>
    /// <remarks>
    /// A link to a screen that will answer 403 is worse than no link: it tells somebody the product
    /// has a feature and then refuses it. The same rule the lifecycle controls follow — not
    /// rendered, rather than rendered and disabled.
    /// </remarks>
    public bool MayManageLookupLists { get; set; }
}
