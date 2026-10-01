using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

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
/// Placeholder for the employee picker, which is implemented in WorkMate.Records once the
/// employee record exists. Same reasoning as <see cref="DimensionPickerTagHelper"/>.
/// </summary>
[HtmlTargetElement("workmate-employee-picker", Attributes = "for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class EmployeePickerTagHelper : PlaceholderPickerTagHelper
{
    public EmployeePickerTagHelper(IStringLocalizer<EmployeePickerTagHelper> stringLocalizer)
        : base(stringLocalizer)
    {
    }

    protected override string CssClass => "workmate-employee-picker";

    protected override LocalizedString Waiting =>
        S["The employee picker arrives with the employee record."];
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
