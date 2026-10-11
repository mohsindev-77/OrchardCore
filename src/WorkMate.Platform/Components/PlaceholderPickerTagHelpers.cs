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


    /// <summary>
    /// Whether people who have left are offered, and the hint that says so when they are not.
    /// </summary>
    /// <remarks>
    /// Default true, because most forms here record something dated and must be able to name
    /// somebody who has since left. The one that must not is appointing a head — the service
    /// refuses a head who had left by the effective date, so offering them would be offering a
    /// choice that cannot be made. When this is false the control says why, because a picker that
    /// silently withholds a person reads as a missing record rather than as a rule.
    /// </remarks>
    public bool IncludeExited { get; set; } = true;

    /// <summary>How many options are rendered before the reader has typed anything.</summary>
    private const int FirstPage = 20;

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

        var choices = await directory.SearchAsync(
            new EmployeeDirectoryQuery(Query, Skip: 0, Take: FirstPage, IncludeExited));

        // The stored value may not be on the first page — it can be somebody who has since left,
        // or somebody the search has not reached — and a control that silently dropped them would
        // change the record the next time anybody saved the form.
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

        if (TagHelperLabel.Html(Label) is { } label)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<label class=\"form-label\" for=\"{id}\">{label}</label>");
        }

        // A real <select>, still, and it is the form control: the script below hides it and drives
        // it rather than replacing it, so the posted value, the model binding and the validation
        // are the same whether or not the script ran. What the script adds is the search.
        markup.Append(
            CultureInfo.InvariantCulture,
            $"<select class=\"form-select\" id=\"{id}\" name=\"{name}\" data-employee-picker>");

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
                $"<option value=\"{Encode(option.EmployeeId)}\"{isSelected} data-code=\"{Encode(option.Code)}\""
                + $" data-where=\"{Encode(option.Where ?? string.Empty)}\">"
                + $"{Encode(Describe(option))}</option>");
        }

        markup.Append("</select>");

        if (!IncludeExited)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text\">{Encode(S["People who have left are not offered: an appointment cannot start after somebody's last day."].Value)}</div>");
        }

        // The fallback caption, for the case where the script did not run. The script removes it,
        // because once there is a search box "narrow the list" is advice about a control that is
        // no longer there.
        if (choices.IsTruncated)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text\" data-employee-picker-fallback-note>"
                + $"{Encode(S["Showing {0} of about {1} people.", options.Count, choices.Total].Value)}</div>");
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"mb-3 {CssClass}");

        // Everything the script needs, on the wrapper: where to search, and the two rules this
        // particular form has. Carried as data rather than guessed, so the typed search and the
        // rendered options cannot disagree about who is eligible.
        output.Attributes.SetAttribute("data-employee-picker-url", SearchUrl);
        output.Attributes.SetAttribute("data-employee-picker-include-exited", IncludeExited ? "true" : "false");
        output.Attributes.SetAttribute("data-employee-picker-allow-none", AllowNone ? "true" : "false");
        output.Attributes.SetAttribute("data-employee-picker-query", Query ?? string.Empty);

        // Localised once here, rather than in the script where there is no localiser.
        output.Attributes.SetAttribute("data-employee-picker-strings", Strings());

        output.Content.SetHtmlContent(markup.ToString());
    }

    /// <summary>
    /// Where the search endpoint lives.
    /// </summary>
    /// <remarks>
    /// A constant rather than <c>IUrlHelper</c>, because this tag helper is in
    /// <c>WorkMate.Platform</c> and the controller is in <c>WorkMate.Records</c>, which this module
    /// must not reference — the whole reason <see cref="IEmployeeDirectory"/> exists. The route is
    /// pinned by <c>EmployeePickerRouteTests</c>, which resolves it for real, so a rename cannot
    /// break this silently.
    /// </remarks>
    public const string SearchUrl = "/Admin/Employees/Picker/Search";

    /// <summary>One option's text: who, which code, and where they sit.</summary>
    /// <remarks>
    /// Where they sit is in brackets after the code and omitted when they are not placed, rather
    /// than rendered as a dash. An unplaced new starter is ordinary; a dash would read as a lookup
    /// that failed.
    /// </remarks>
    private static string Describe(EmployeeChoice option) =>
        string.IsNullOrEmpty(option.Where)
            ? $"{option.Name} ({option.Code})"
            : $"{option.Name} ({option.Code}) — {option.Where}";

    private string Strings() => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["search"] = S["Search by name or employee code"].Value,
        ["none"] = S["— nobody —"].Value,
        ["empty"] = S["No match. Try part of a name or an employee code."].Value,
        ["more"] = S["Showing {0} of about {1}. Keep typing to narrow it."].Value,
        ["all"] = S["Showing all {0}."].Value,
        ["chosen"] = S["Chosen: {0}"].Value,
        ["clear"] = S["Clear"].Value,
        ["unresolved"] = S["Choose somebody from the list, or clear the box."].Value,
    });

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
    public object? Label { get; set; }

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

        var label = TagHelperLabel.Html(Label) is { } text
            ? $"<label class=\"form-label\" for=\"{id}\">{text}</label>"
            : string.Empty;

        output.Content.SetHtmlContent($"""
            {label}
            <input type="hidden" name="{name}" value="{value}" />
            <input class="form-control" type="text" id="{id}" value="{value}" disabled aria-describedby="{id}_waiting" />
            <div class="form-text" id="{id}_waiting">{System.Net.WebUtility.HtmlEncode(Waiting.Value)}</div>
            """);
    }
}
