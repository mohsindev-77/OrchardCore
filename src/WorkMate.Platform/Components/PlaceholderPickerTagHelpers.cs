using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using WorkMate.Platform.Services;

namespace WorkMate.Platform.Components;

/// <summary>
/// Placeholder for the dimension picker, which is implemented in WorkMate.Dimensions.
///
/// It exists now so that the component set is complete and later modules can write the markup
/// they will keep. It renders a disabled control that says plainly what it is waiting for,
/// rather than an input that looks usable and silently stores nothing. The hidden field
/// preserves any value already on the record, so opening and saving a form does not erase a
/// placement that a recipe or an import put there.
/// </summary>
[HtmlTargetElement("workmate-dimension-picker", Attributes = "for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DimensionPickerTagHelper : PlaceholderPickerTagHelper
{
    public DimensionPickerTagHelper(IStringLocalizer<DimensionPickerTagHelper> stringLocalizer)
        : base(stringLocalizer)
    {
    }

    protected override string CssClass => "workmate-dimension-picker";

    protected override LocalizedString Waiting =>
        S["The organisation picker arrives with the dimension engine. Placement is set through the dimension records for now."];
}

/// <summary>
/// Chooses a person, on a plain admin form.
/// </summary>
/// <remarks>
/// A real control since prompt 4 session A2, where it was a placeholder before. It renders a
/// <c>&lt;select&gt;</c> populated on the server from <see cref="IEmployeeDirectory"/>, so it works
/// with no JavaScript at all — which is the same rule the organisation designer follows, and which
/// matters more here because this control appears on forms that commit dated decisions.
///
/// <b>It keeps the placeholder as its fallback.</b> <see cref="IEmployeeDirectory"/> is resolved as
/// a collection and is implemented by <c>WorkMate.Records</c>; on a tenant where that feature is
/// off there is nobody to offer, and the control says so rather than rendering an empty list that
/// looks like a tenant with no staff.
///
/// <b>It is not the employee picker <em>field</em>.</b> Specification section 5's "content picker
/// restricted to Employee with visibility filtering" is an Orchard <c>ContentPickerField</c> served
/// by <c>EmployeePickerResultProvider</c>, which searches rather than lists and is what session B's
/// form designer generates. This is for forms that are not content items.
/// </remarks>
[HtmlTargetElement("workmate-employee-picker", Attributes = "for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class EmployeePickerTagHelper : PlaceholderPickerTagHelper
{
    private readonly IEnumerable<IEmployeeDirectory> _directories;

    public EmployeePickerTagHelper(
        IEnumerable<IEmployeeDirectory> directories,
        IStringLocalizer<EmployeePickerTagHelper> stringLocalizer)
        : base(stringLocalizer)
    {
        _directories = directories;
    }

    protected override string CssClass => "workmate-employee-picker";

    protected override LocalizedString Waiting =>
        S["The employee record is not enabled on this tenant, so there is nobody to choose from."];

    /// <summary>
    /// Narrows the list, when the caller already knows it should be narrow. Optional.
    /// </summary>
    public string? Query { get; set; }

    /// <summary>Whether the control offers "nobody", which is not the same as not choosing.</summary>
    /// <remarks>
    /// On "set a unit's head" there is no empty option: clearing a head is its own dated operation,
    /// because a post with nothing selected cannot say which day the unit became vacant. On a line
    /// manager there is one, because the top of a reporting line genuinely reports to nobody.
    /// </remarks>
    public bool AllowNone { get; set; } = true;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var directory = _directories.FirstOrDefault();

        if (directory is null)
        {
            // No employee record on this tenant: the placeholder, which says so and preserves
            // whatever value is already stored rather than erasing it on save.
            Process(context, output);

            return;
        }

        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var selected = For.Model?.ToString() ?? string.Empty;

        var choices = await directory.SearchAsync(Query);

        // The stored value may not be in the offered set — it can be somebody who has since left,
        // or somebody past the cap — and a control that silently dropped them would change the
        // record the next time anybody saved the form.
        var options = choices.Items.ToList();

        if (!string.IsNullOrEmpty(selected) &&
            !options.Any(option => string.Equals(option.EmployeeId, selected, StringComparison.Ordinal)))
        {
            var current = await directory.GetAsync(selected);

            if (current is not null)
            {
                options.Insert(0, current);
            }
        }

        var markup = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(Label))
        {
            markup.Append(CultureInfo.InvariantCulture, $"<label class=\"form-label\" for=\"{id}\">{Encode(Label)}</label>");
        }

        markup.Append(CultureInfo.InvariantCulture, $"<select class=\"form-select\" id=\"{id}\" name=\"{name}\">");

        if (AllowNone)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<option value=\"\">{Encode(S["— nobody —"].Value)}</option>");
        }

        foreach (var option in options)
        {
            var isSelected = string.Equals(option.EmployeeId, selected, StringComparison.Ordinal)
                ? " selected"
                : string.Empty;

            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(option.EmployeeId)}\"{isSelected}>"
                + $"{Encode(option.Name)} ({Encode(option.Code)})</option>");
        }

        markup.Append("</select>");

        if (choices.IsTruncated)
        {
            // Said out loud. A control showing the first hundred of four hundred without a word is
            // a control somebody will use to pick the wrong person.
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text\">{Encode(S["Showing {0} of {1} people. Narrow the list to see the rest.", options.Count, choices.Total].Value)}</div>");
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"mb-3 {CssClass}");
        output.Content.SetHtmlContent(markup.ToString());
    }

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
}

/// <summary>
/// Shared behaviour for the two pickers that are declared now and implemented later.
/// </summary>
public abstract class PlaceholderPickerTagHelper : TagHelper
{
    protected PlaceholderPickerTagHelper(IStringLocalizer stringLocalizer) => S = stringLocalizer;

    protected IStringLocalizer S { get; }

    protected abstract string CssClass { get; }

    protected abstract LocalizedString Waiting { get; }

    [HtmlAttributeName("for")]
    public ModelExpression For { get; set; } = default!;

    /// <summary>The label. Already localised by the caller.</summary>
    public string? Label { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var value = System.Net.WebUtility.HtmlEncode(For.Model?.ToString() ?? string.Empty);

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"mb-3 {CssClass}");

        var label = string.IsNullOrWhiteSpace(Label)
            ? string.Empty
            : $"<label class=\"form-label\" for=\"{id}\">{System.Net.WebUtility.HtmlEncode(Label)}</label>";

        output.Content.SetHtmlContent($"""
            {label}
            <input type="hidden" name="{name}" value="{value}" />
            <input class="form-control" type="text" id="{id}" value="{value}" disabled aria-describedby="{id}_waiting" />
            <div class="form-text" id="{id}_waiting">{System.Net.WebUtility.HtmlEncode(Waiting.Value)}</div>
            """);
    }
}
