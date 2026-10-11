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
using WorkMate.Dimensions.ViewModels;
using WorkMate.Platform.Components;

namespace WorkMate.Dimensions.Components;

/// <summary>
/// Chooses a unit on a structure, by its full path, on a plain admin form.
/// </summary>
/// <remarks>
/// <b>Why it is not a flat <c>&lt;select&gt;</c>.</b> The Crescent reference tenant has two
/// departments called "Credit" and three called "Operations", on one structure. A list of bare
/// names offers a reader several identical rows and no way to tell which is which — so the option
/// text is the path, "Crescent › Retail Banking › Credit", and the control searches the server
/// rather than filtering a page it was handed. That is the same pair of decisions the employee
/// picker was rebuilt around, and this reuses its combobox rather than restating it.
///
/// <b>The &lt;select&gt; is still the form control.</b> It is named, posted, bound and validated
/// whether or not the script runs; the script hides it and sets its value. These forms commit
/// dated decisions, so a form that only worked with script would be a form that silently did
/// nothing when one failed to load.
///
/// <b>It offers only what the validator would accept.</b> The options come from
/// <see cref="IDimensionGraphService.SearchEmployeeAttachableUnitsAsync"/>, which reads the same
/// <c>PermitsEmployeesAt</c> that <c>IDimensionValidator.ValidateAssignmentAsync</c> reads
/// backwards, resolved on the effective date rather than today. ADR-0010's lesson applied again:
/// one rule, two directions, one implementation.
/// </remarks>
[HtmlTargetElement("workmate-unit-picker", Attributes = "for,structure-id", TagStructure = TagStructure.WithoutEndTag)]
public sealed class UnitPickerTagHelper : TagHelper
{
    private readonly IDimensionGraphService _graph;
    private readonly IDimensionService _records;
    private readonly IStringLocalizer S;

    public UnitPickerTagHelper(
        IDimensionGraphService graph,
        IDimensionService records,
        IStringLocalizer<UnitPickerTagHelper> stringLocalizer)
    {
        _graph = graph;
        _records = records;
        S = stringLocalizer;
    }

    /// <summary>How many options are rendered before the reader has typed anything.</summary>
    private const int FirstPage = UnitPickerAdminController.PageSize;

    [HtmlAttributeName("for")]
    public ModelExpression For { get; set; } = default!;

    /// <summary>The structure the units are read from. Without one there is nothing to offer.</summary>
    [HtmlAttributeName("structure-id")]
    public string? StructureId { get; set; }

    /// <summary>
    /// The date the placement takes effect, ISO-8601, so the control offers the units active then.
    /// </summary>
    [HtmlAttributeName("as-at")]
    public string? AsAt { get; set; }

    /// <summary>
    /// The id of the date input on the same form, so changing the date re-asks the question.
    /// </summary>
    /// <remarks>
    /// Optional, and carried as the element's id rather than as a model expression because the
    /// script is what reads it. A form that leaves it out gets a picker fixed on
    /// <see cref="AsAt"/>, which is correct until somebody changes the date and then is not.
    /// </remarks>
    [HtmlAttributeName("date-field")]
    public string? DateField { get; set; }

    /// <summary>Whether to offer the "Within" scope filter. On by default.</summary>
    [HtmlAttributeName("allow-within")]
    public bool AllowWithin { get; set; } = true;

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
        output.Attributes.SetAttribute("class", "mb-3 workmate-unit-picker");

        if (string.IsNullOrEmpty(StructureId))
        {
            // No structure chosen yet. The field still posts whatever it holds, so opening and
            // saving a form does not erase a placement a recipe put there.
            output.Content.SetHtmlContent(Waiting(id, name, selected));

            return;
        }

        DateOnly? asAt = IsoDate.TryParse(AsAt, out var parsed) ? parsed : null;

        var choices = await _graph.SearchEmployeeAttachableUnitsAsync(
            StructureId, searchText: null, withinRecordId: null, asAt, FirstPage);

        var options = choices.Items.ToList();

        // The stored value may not be on the first page, and a control that silently dropped it
        // would change the row the next time anybody saved the form.
        var missing = !string.IsNullOrEmpty(selected) &&
            !options.Any(option => string.Equals(option.RecordId, selected, StringComparison.Ordinal));

        if (missing && await _records.GetAsync(selected, asAt) is { } current)
        {
            options.Insert(0, current);
            missing = false;
        }

        var paths = await _graph.GetAncestorPathsAsync(
            StructureId, [.. options.Select(option => option.RecordId)], asAt);

        var markup = new StringBuilder();

        if (TagHelperLabel.Html(Label) is { } label)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<label class=\"form-label\" for=\"{id}\">{label}</label>");
        }

        markup.Append(
            CultureInfo.InvariantCulture,
            $"<select class=\"form-select\" id=\"{id}\" name=\"{name}\" data-unit-picker>");

        // The empty option is the prompt, not a choice: a placement at nowhere is not a thing that
        // exists, and the form refuses a row with no unit rather than storing one.
        markup.Append(CultureInfo.InvariantCulture, $"<option value=\"\">{Encode(S["Choose a unit…"].Value)}</option>");

        // A stored unit this structure can no longer name — retired, or moved off the axis — still
        // has to survive a save. Named as what it is rather than dropped, because a field that
        // quietly empties itself is how a placement disappears without anybody deciding to end it.
        if (missing)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(selected)}\" selected>"
                + $"{Encode(S["A unit that is not on this structure on that date"].Value)}</option>");
        }

        foreach (var option in options)
        {
            var isSelected = string.Equals(option.RecordId, selected, StringComparison.Ordinal)
                ? " selected"
                : string.Empty;

            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(option.RecordId)}\"{isSelected} data-code=\"{Encode(option.Code)}\">"
                + $"{Encode(Describe(option, paths.GetValueOrDefault(option.RecordId)))}</option>");
        }

        markup.Append("</select>");

        if (AllowWithin)
        {
            markup.Append(await WithinAsync(id, asAt));
        }

        if (options.Count == 0)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text text-warning\" data-unit-picker-empty>"
                + $"{Encode(S["This structure has no units that may hold employees on that date."].Value)}</div>");
        }
        else if (choices.HasMore)
        {
            // The fallback caption, for the case where the script did not run. The script removes
            // it, because once there is a search box "narrow the list" is advice about a control
            // that is no longer there.
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text\" data-unit-picker-fallback-note>"
                + $"{Encode(S["Showing {0} of {1} units.", options.Count, choices.Total].Value)}</div>");
        }

        output.Attributes.SetAttribute("data-unit-picker-url", UnitPickerAdminController.SearchUrl);
        output.Attributes.SetAttribute("data-unit-picker-structure", StructureId);
        output.Attributes.SetAttribute("data-unit-picker-as-at", AsAt ?? string.Empty);
        output.Attributes.SetAttribute("data-unit-picker-date-field", DateField ?? string.Empty);

        // Localised once here, rather than in the script where there is no localiser.
        output.Attributes.SetAttribute("data-unit-picker-strings", Strings());

        output.Content.SetHtmlContent(markup.ToString());
    }

    /// <summary>
    /// The "Within" scope filter: a container on this structure, limiting the search to its
    /// subtree.
    /// </summary>
    /// <remarks>
    /// Rendered hidden and unhidden by the script. Without the script the <c>&lt;select&gt;</c>
    /// above already holds its units and there is no round trip behind this, so it would be a
    /// control that visibly does nothing — which is worse than one that is not there.
    /// </remarks>
    private async Task<string> WithinAsync(string id, DateOnly? asAt)
    {
        var scopes = await _graph.GetEmployeeScopeUnitsAsync(StructureId!, asAt);

        if (scopes.Count == 0)
        {
            return string.Empty;
        }

        var paths = await _graph.GetAncestorPathsAsync(
            StructureId!, [.. scopes.Select(scope => scope.RecordId)], asAt);

        var markup = new StringBuilder();

        markup.Append(
            CultureInfo.InvariantCulture,
            $"<div class=\"mt-2\" data-unit-picker-within-row hidden>"
            + $"<label class=\"form-label small\" for=\"{id}__within\">{Encode(S["Within"].Value)}</label>"
            + $"<select class=\"form-select form-select-sm\" id=\"{id}__within\" data-unit-picker-within>"
            + $"<option value=\"\">{Encode(S["Anywhere in this structure"].Value)}</option>");

        foreach (var scope in scopes)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(scope.RecordId)}\">"
                + $"{Encode(Describe(scope, paths.GetValueOrDefault(scope.RecordId)))}</option>");
        }

        markup.Append(
            CultureInfo.InvariantCulture,
            $"</select><div class=\"form-text\">"
            + $"{Encode(S["Narrows the units offered to those at or below the one chosen."].Value)}</div></div>");

        return markup.ToString();
    }

    /// <summary>
    /// The placeholder for a form that has not chosen a structure yet. It preserves the stored
    /// value rather than erasing it on save.
    /// </summary>
    private string Waiting(string id, string name, string value)
    {
        var label = TagHelperLabel.Html(Label) is { } text
            ? $"<label class=\"form-label\" for=\"{id}\">{text}</label>"
            : string.Empty;

        return $"""
            {label}
            <input type="hidden" name="{name}" value="{Encode(value)}" />
            <input class="form-control" type="text" id="{id}" value="{Encode(value)}" disabled aria-describedby="{id}_waiting" />
            <div class="form-text" id="{id}_waiting">{Encode(S["Choose a structure first, and this will offer the units on it."].Value)}</div>
            """;
    }

    /// <summary>One option's text: the full path, then the code.</summary>
    /// <remarks>
    /// The path leads because it is what tells two units of the same name apart; the code follows
    /// because it is what somebody searching by code recognises.
    /// </remarks>
    private static string Describe(DimensionNodeRef unit, IReadOnlyList<DimensionNodeRef>? ancestors) =>
        $"{BilingualDisplay.Path(ancestors, unit)} ({unit.Code})";

    private string Strings() => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["search"] = S["Search by unit name, path or code"].Value,
        ["empty"] = S["No unit matches. Try part of a name, a parent's name, or a code."].Value,
        ["more"] = S["Showing {0} of {1}. Keep typing to narrow it."].Value,
        ["all"] = S["Showing all {0}."].Value,
        ["chosen"] = S["Chosen: {0}"].Value,
        ["unresolved"] = S["Choose a unit from the list, or clear the box."].Value,
    });

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
