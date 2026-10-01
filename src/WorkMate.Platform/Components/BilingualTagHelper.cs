using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

namespace WorkMate.Platform.Components;

/// <summary>
/// The shared bilingual name editor: <c>&lt;workmate-bilingual for-en="..." for-ar="..."
/// label="..." /&gt;</c>.
///
/// Specification section 3 requires that a bilingual field looks and behaves identically
/// everywhere, on the admin and on the employee front end. That only holds if there is one
/// implementation, so this and <see cref="Drivers.BilingualTextFieldDisplayDriver"/>'s view
/// render the same markup: two inputs, each carrying its own dir and lang so that Arabic types
/// right to left inside an English page and English left to right inside an Arabic one.
/// </summary>
[HtmlTargetElement("workmate-bilingual", Attributes = "for-en,for-ar", TagStructure = TagStructure.WithoutEndTag)]
public sealed class BilingualTagHelper : TagHelper
{
    private readonly IStringLocalizer S;

    public BilingualTagHelper(IStringLocalizer<BilingualTagHelper> stringLocalizer) => S = stringLocalizer;

    [HtmlAttributeName("for-en")]
    public ModelExpression English { get; set; } = default!;

    [HtmlAttributeName("for-ar")]
    public ModelExpression Arabic { get; set; } = default!;

    /// <summary>The field label. Already localised by the caller; this does not localise data.</summary>
    public string? Label { get; set; }

    public string? Hint { get; set; }

    public bool Required { get; set; }

    public int MaxLength { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "fieldset";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "mb-3 workmate-bilingual");

        var content = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(Label))
        {
            content.Append("<legend class=\"form-label\">")
                   .Append(System.Net.WebUtility.HtmlEncode(Label));

            if (Required)
            {
                content.Append("<span class=\"text-danger\" aria-hidden=\"true\">*</span>");
            }

            content.Append("</legend>");
        }

        content.Append("<div class=\"row g-2\">")
               .Append(Column(English, "en", "ltr", S["English"].Value))
               .Append(Column(Arabic, "ar", "rtl", S["Arabic"].Value))
               .Append("</div>");

        if (!string.IsNullOrWhiteSpace(Hint))
        {
            content.Append("<div class=\"form-text\">")
                   .Append(System.Net.WebUtility.HtmlEncode(Hint))
                   .Append("</div>");
        }

        output.Content.SetHtmlContent(content.ToString());
    }

    private string Column(ModelExpression expression, string language, string direction, string caption)
    {
        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var value = System.Net.WebUtility.HtmlEncode(expression.Model?.ToString() ?? string.Empty);
        var maxLength = MaxLength > 0 ? $" maxlength=\"{MaxLength}\"" : string.Empty;

        return $"""
            <div class="col-md-6">
              <label class="form-label small text-muted" for="{id}">{System.Net.WebUtility.HtmlEncode(caption)}</label>
              <input class="form-control" type="text" id="{id}" name="{name}" value="{value}" dir="{direction}" lang="{language}"{maxLength} />
              <span class="text-danger field-validation-valid" data-valmsg-for="{name}" data-valmsg-replace="true"></span>
            </div>
            """;
    }
}
