using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using WorkMate.Dimensions.Controllers;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Components;

namespace WorkMate.Dimensions.Components;

/// <summary>
/// Chooses a position: searchable, showing each post's unit and how full it is. Decision D26 F.
/// </summary>
/// <remarks>
/// <b>Built in slice F5+F7 for the Add form's <em>Reports to</em>, and the same control the employee
/// form uses in F6.</b> One control rather than two, because D26 F describes one list — active
/// positions, searchable by code and name, each showing its unit and its filled/approved count — and
/// two implementations of that would come to disagree about which positions are offered.
///
/// <b>The &lt;select&gt; is still the form control</b>, named, posted, bound and validated whether or
/// not the script runs. These forms commit dated decisions, so one that only worked with script
/// would be one that silently did nothing when a script failed to load. The same rule
/// <c>UnitPickerTagHelper</c> follows, and this reuses its combobox rather than restating it.
///
/// <b>A full position is offered and marked, not withheld.</b> D26 F: choosing one warns and is
/// allowed, because refusing would stop the product recording what an organisation has actually
/// done. A <em>retired</em> position is withheld, because that is not a post anybody can be put into
/// and offering it greyed out is offering it.
/// </remarks>
[HtmlTargetElement("workmate-position-picker", Attributes = "for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PositionPickerTagHelper : TagHelper
{
    private readonly IPositionService _positions;
    private readonly IDimensionService _records;
    private readonly IStructureService _structures;
    private readonly IStringLocalizer S;

    public PositionPickerTagHelper(
        IPositionService positions,
        IDimensionService records,
        IStructureService structures,
        IStringLocalizer<PositionPickerTagHelper> stringLocalizer)
    {
        _positions = positions;
        _records = records;
        _structures = structures;
        S = stringLocalizer;
    }

    /// <summary>How many options are rendered before the reader has typed anything.</summary>
    private const int FirstPage = PositionPickerAdminController.PageSize;

    [HtmlAttributeName("for")]
    public ModelExpression For { get; set; } = default!;

    /// <summary>The date the choice takes effect, ISO-8601, so the control offers what is active then.</summary>
    [HtmlAttributeName("as-at")]
    public string? AsAt { get; set; }

    /// <summary>The id of the date input on the same form, so changing the date re-asks the question.</summary>
    [HtmlAttributeName("date-field")]
    public string? DateField { get; set; }

    /// <summary>
    /// A position to leave out: the one being edited, when this is choosing what it reports to.
    /// </summary>
    /// <remarks>
    /// A position reporting to itself is a loop the validator refuses, so offering it is offering a
    /// choice whose only outcome is a refusal — which is the thing every picker in this module is
    /// written not to do. The <em>descendants</em> of that position are a loop too and are not
    /// excluded here: the list is capped and searched, so filtering a subtree client-side would be
    /// filtering a page, and the refusal on save is the authority either way.
    /// </remarks>
    [HtmlAttributeName("excluding")]
    public string? Excluding { get; set; }

    /// <summary>
    /// The axis the choice will be written on, so the control offers only positions that may go on
    /// it.
    /// </summary>
    /// <remarks>
    /// <b>For the tenant that has more than one Position type</b> — the demo one, where each company
    /// ships its own for the reason every demo type is prefixed. Without this the <em>Reports to</em>
    /// control would offer a Crescent position to a Zenith one, and the save would be refused
    /// because that type is not a level of Zenith's reporting axis: a picker offering a choice whose
    /// only outcome is a refusal, which is the thing every picker in this module is written not to
    /// do.
    ///
    /// Absent on a tenant with one Position type, where there is nothing to narrow.
    /// </remarks>
    [HtmlAttributeName("structure-id")]
    public string? StructureId { get; set; }

    /// <summary>Whether "nobody" is a choice — the CEO reports to no one.</summary>
    [HtmlAttributeName("allow-none")]
    public bool AllowNone { get; set; } = true;

    /// <summary>What the "nobody" choice reads as. Already localised by the caller.</summary>
    [HtmlAttributeName("none-label")]
    public object? NoneLabel { get; set; }

    /// <summary>
    /// The value the "nobody" option posts, when "nobody" must be <em>chosen</em> rather than
    /// merely left alone.
    /// </summary>
    /// <remarks>
    /// <b>Empty by default, which conflates two different answers on purpose and has to stop where
    /// a choice is required.</b> Ordinarily the empty option is both "I have not chosen" and
    /// "nobody" — fine on a form where leaving it blank is a legitimate answer, as it is for an
    /// employee's optional position.
    ///
    /// It is not fine on a position's <em>Reports to</em>: a post saved with it untouched was being
    /// read as "top of the reporting line", which is a claim nobody made, and in fact wrote no
    /// reporting-line row at all. Giving "nobody" a value of its own separates the two, so the
    /// empty option can go back to meaning "unanswered" and the server can refuse it.
    ///
    /// Set it and the control renders a <em>Choose…</em> prompt at <c>value=""</c>, then the
    /// "nobody" option at this value, then the positions.
    /// </remarks>
    [HtmlAttributeName("none-value")]
    public string? NoneValue { get; set; }

    /// <summary>
    /// Whether choosing a position should redraw the form it sits on. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>For a form that derives something from the choice.</b> The employee forms show the
    /// organisation chain and the line manager, both computed on the server from the position —
    /// so until the form goes back to the server they describe the <em>previous</em> position, and
    /// a reader choosing a different post watched the block below it stay wrong.
    ///
    /// The redraw is the same <c>IsRefresh</c> round trip the structure chooser takes: it is
    /// navigation, validates nothing, and the flag is found by its <c>data-refresh-flag</c>
    /// attribute so one control works on forms whose own flags are named differently.
    ///
    /// <b>Off by default</b>, because the position's own Add form derives nothing from this control
    /// and a round trip there would cost a reader their place for no gain.
    /// </remarks>
    [HtmlAttributeName("refresh-on-change")]
    public bool RefreshOnChange { get; set; }

    /// <summary>The label. Already localised by the caller — see <see cref="TagHelperLabel"/>.</summary>
    public object? Label { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var selected = For.Model?.ToString() ?? string.Empty;

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "mb-3 workmate-position-picker");

        DateOnly? asAt = IsoDate.TryParse(AsAt, out var parsed) ? parsed : null;

        var found = await _positions.SearchSelectableAsync(searchText: null, asAt, FirstPage);
        var onTheAxis = await TypesOnTheAxisAsync();

        var options = found.Items
            .Where(option => string.IsNullOrEmpty(Excluding) ||
                !string.Equals(option.Position.RecordId, Excluding, StringComparison.Ordinal))
            .Where(option => onTheAxis is null || onTheAxis.Contains(option.Position.DimensionTypeId))
            .ToList();

        // The stored value may not be on the first page, and a control that silently dropped it
        // would change the row the next time anybody saved the form.
        //
        // The "nobody" sentinel is excluded: it is a choice, not a record id, so looking it up
        // would find nothing and the control would report the operator's own answer back to them
        // as "a position that is not active on that date".
        var missing = !string.IsNullOrEmpty(selected) &&
            !string.Equals(selected, NoneValue, StringComparison.Ordinal) &&
            !options.Any(option => string.Equals(option.Position.RecordId, selected, StringComparison.Ordinal));

        var markup = new StringBuilder();

        if (TagHelperLabel.Html(Label) is { } label)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<label class=\"form-label\" for=\"{id}\">{label}</label>");
        }

        markup.Append(
            CultureInfo.InvariantCulture,
            $"<select class=\"form-select\" id=\"{id}\" name=\"{name}\" data-position-picker{RefreshAttribute()}>");

        // Three shapes, and the third is the one a required choice needs.
        //
        //   none-value unset, allow-none      → one empty option reading "nobody": blank IS nobody
        //   none-value unset, !allow-none     → one empty option reading "Choose…": blank is unanswered
        //   none-value set                    → "Choose…" at blank, THEN "nobody" at its own value
        //
        // The third keeps "unanswered" and "nobody" apart, which is what lets the server refuse the
        // first while accepting the second.
        if (string.IsNullOrEmpty(NoneValue))
        {
            var prompt = AllowNone ? NoneText() : Encode(S["Choose a position…"].Value);

            markup.Append(CultureInfo.InvariantCulture, $"<option value=\"\">{prompt}</option>");
        }
        else
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"\">{Encode(S["Choose…"].Value)}</option>"
                + $"<option value=\"{Encode(NoneValue)}\""
                + $"{(string.Equals(selected, NoneValue, StringComparison.Ordinal) ? " selected" : string.Empty)}>"
                + $"{NoneText()}</option>");
        }

        if (missing && await _records.GetAsync(selected, asAt) is { } current)
        {
            // Named as what it is rather than dropped, for the reason the unit picker keeps one: a
            // field that quietly empties itself is how a decision disappears with nobody deciding.
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(selected)}\" selected>"
                + $"{Encode($"{Describe(current.NameEn, current.NameAr)} ({current.Code})")}</option>");
        }
        else if (missing)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(selected)}\" selected>"
                + $"{Encode(S["A position that is not active on that date"].Value)}</option>");
        }

        foreach (var option in options)
        {
            var isSelected = string.Equals(option.Position.RecordId, selected, StringComparison.Ordinal)
                ? " selected"
                : string.Empty;

            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(option.Position.RecordId)}\"{isSelected}"
                + $" data-code=\"{Encode(option.Position.Code)}\""
                + $" data-full=\"{(option.IsFull ? "true" : "false")}\">"
                + $"{Encode(Caption(option))}</option>");
        }

        markup.Append("</select>");

        if (options.Count == 0)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text text-warning\" data-position-picker-empty>"
                + $"{Encode(S["No position is active on that date. Create one first."].Value)}</div>");
        }
        else if (found.HasMore)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text\" data-position-picker-fallback-note>"
                + $"{Encode(S["Showing {0} of {1} positions.", options.Count, found.Total].Value)}</div>");
        }

        // D26 F: a full position warns and is allowed. Rendered on the server for the case where the
        // stored value is already a full one, and kept in step by the script as the choice changes.
        markup.Append(
            CultureInfo.InvariantCulture,
            $"<div class=\"form-text text-warning\" data-position-picker-full hidden>"
            + $"{Encode(S["This position is already at its approved headcount. You can still choose it."].Value)}</div>");

        output.Attributes.SetAttribute("data-position-picker-url", PositionPickerAdminController.SearchUrl);
        output.Attributes.SetAttribute("data-position-picker-as-at", AsAt ?? string.Empty);
        output.Attributes.SetAttribute("data-position-picker-date-field", DateField ?? string.Empty);
        output.Attributes.SetAttribute("data-position-picker-excluding", Excluding ?? string.Empty);
        output.Attributes.SetAttribute("data-position-picker-structure", StructureId ?? string.Empty);
        output.Attributes.SetAttribute("data-position-picker-allow-none", AllowNone ? "true" : "false");
        output.Attributes.SetAttribute("data-position-picker-strings", Strings());

        output.Content.SetHtmlContent(markup.ToString());
    }

    /// <summary>
    /// One option's text: the name, then the unit it sits in, then how full it is.
    /// </summary>
    /// <remarks>
    /// All three are load-bearing (D26 F). The <b>unit</b> is what tells two posts called
    /// <em>Supervisor</em> apart. The <b>count</b> is what lets a reader see they are about to
    /// over-fill a post before they do it. The <b>code</b> is what somebody searching by code
    /// recognises.
    /// </remarks>
    private static string Caption(PositionOption option)
    {
        var unit = string.IsNullOrEmpty(option.UnitPath) ? string.Empty : $" — {option.UnitPath}";

        return $"{Describe(option.Position.NameEn, option.Position.NameAr)} ({option.Position.Code})"
            + $"{unit} · {option.Establishment.Filled}/{option.Establishment.Approved}";
    }

    private static string Describe(string nameEn, string nameAr) =>
        ViewModels.BilingualDisplay.Name(nameEn, nameAr);

    /// <summary>
    /// The dimension types <see cref="StructureId"/> declares, or null when no axis was named and
    /// nothing is narrowed.
    /// </summary>
    private async Task<HashSet<string>?> TypesOnTheAxisAsync()
    {
        if (string.IsNullOrEmpty(StructureId))
        {
            return null;
        }

        var structure = await _structures.GetAsync(StructureId);

        return structure is null ? null : [.. structure.DimensionTypeIds];
    }

    /// <summary>
    /// The onchange that redraws the form, when the caller asked for one. Decision D31.
    /// </summary>
    /// <remarks>
    /// Marked with <c>data-position-picker-refreshes</c> as well, so the picker's own script knows
    /// the control it enhances will navigate and a test can assert the behaviour is switched on
    /// without executing it.
    /// </remarks>
    private string RefreshAttribute() => RefreshOnChange
        ? " data-position-picker-refreshes onchange=\""
            // ONLY ON AN ACTUAL CHOICE. The combobox clears the select and fires change the moment
            // somebody starts typing in the search box — workmate-picker.js does it so the posted
            // value and the visible text can never disagree — so an unguarded handler submitted
            // the form on the first keystroke and the control could not be used at all.
            + "if(this.value){"
            + "var f=document.querySelector('[data-refresh-flag]');"
            + "if(f){f.value='true';}"
            + "this.form.submit();}\""
        : string.Empty;

    private string NoneText() =>
        TagHelperLabel.Html(NoneLabel) ?? Encode(S["— reports to nobody —"].Value);

    private string Strings() => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["none"] = WebUtility.HtmlDecode(NoneText()),
        ["search"] = S["Search by position name, unit or code"].Value,
        ["empty"] = S["No position matches. Try part of a name, a unit's name, or a code."].Value,
        ["more"] = S["Showing {0} of {1}. Keep typing to narrow it."].Value,
        ["all"] = S["Showing all {0}."].Value,
        ["chosen"] = S["Chosen: {0}"].Value,
        ["unresolved"] = S["Choose a position from the list, or clear the box."].Value,
    });

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
