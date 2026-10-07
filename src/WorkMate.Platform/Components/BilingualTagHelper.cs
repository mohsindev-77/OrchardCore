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

        // English carries the browser's own required marker; Arabic never does. Since the
        // ADR-0003 addendum Arabic is optional unless the tenant has turned "Require Arabic names"
        // on, and a tenant setting is not something this control can see — so it marks the half it
        // can be sure about and leaves the other to the server, which is the authority either way.
        content.Append("<div class=\"row g-2\">")
               .Append(Column(English, "en", "ltr", S["English"].Value, required: Required))
               .Append(Column(Arabic, "ar", "rtl", S["Arabic"].Value, required: false))
               .Append("</div>");

        if (!string.IsNullOrWhiteSpace(Hint))
        {
            content.Append("<div class=\"form-text\">")
                   .Append(System.Net.WebUtility.HtmlEncode(Hint))
                   .Append("</div>");
        }

        output.Content.SetHtmlContent(content.ToString());
    }

    private string Column(
        ModelExpression expression, string language, string direction, string caption, bool required)
    {
        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var value = System.Net.WebUtility.HtmlEncode(expression.Model?.ToString() ?? string.Empty);
        var maxLength = MaxLength > 0 ? $" maxlength=\"{MaxLength}\"" : string.Empty;
        var isRequired = required ? " required" : string.Empty;

        // Any error the server already put against this field, rendered into the same span the
        // client-side validator writes into. Without this a service-level refusal — "a name is
        // required in English" — could only appear in the summary at the top of the page, leaving
        // the reader to work out which of the two boxes it was about.
        var state = ViewContext.ViewData.ModelState[name];
        var problem = state?.Errors.Count > 0 ? state.Errors[0].ErrorMessage : null;

        var invalid = problem is null ? string.Empty : " is-invalid";
        var validationClass = problem is null ? "field-validation-valid" : "field-validation-error";
        var message = problem is null ? string.Empty : System.Net.WebUtility.HtmlEncode(problem);

        return $"""
            <div class="col-md-6">
              <label class="form-label small text-muted" for="{id}">{System.Net.WebUtility.HtmlEncode(caption)}</label>
              <input class="form-control{invalid}" type="text" id="{id}" name="{name}" value="{value}" dir="{direction}" lang="{language}"{maxLength}{isRequired} />
              <span class="text-danger {validationClass}" data-valmsg-for="{name}" data-valmsg-replace="true">{message}</span>
            </div>
            """;
    }
}
