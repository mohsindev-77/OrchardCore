using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

namespace WorkMate.Platform.Components;

/// <summary>
/// The shared effective-date range control:
/// <c>&lt;workmate-date-range for-from="..." for-to="..." label="..." /&gt;</c>.
///
/// Rule 3 of the specification says every write on a dated model takes an explicit effective
/// date, and rule 9's reads default to today. That makes a from/to pair the most repeated
/// control on the platform, so it is one control rather than a pattern each module retypes.
///
/// The range is closed at both ends: an empty "to" means open-ended, and a "to" that is set is
/// the last day the record applies, not the first day it does not. That matches
/// <see cref="Core.EffectiveRange"/> and the worked example in the dimension engine architecture.
/// </summary>
[HtmlTargetElement("workmate-date-range", Attributes = "for-from,for-to", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DateRangeTagHelper : TagHelper
{
    private readonly IStringLocalizer S;

    public DateRangeTagHelper(IStringLocalizer<DateRangeTagHelper> stringLocalizer) => S = stringLocalizer;

    [HtmlAttributeName("for-from")]
    public ModelExpression From { get; set; } = default!;

    [HtmlAttributeName("for-to")]
    public ModelExpression To { get; set; } = default!;

    /// <summary>The label for the pair. Already localised by the caller.</summary>
    public string? Label { get; set; }

    /// <summary>Whether the range may be left open-ended. Most dated records may.</summary>
    public bool AllowOpenEnded { get; set; } = true;

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "fieldset";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "mb-3 workmate-date-range");

        var content = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(Label))
        {
            content.Append("<legend class=\"form-label\">")
                   .Append(System.Net.WebUtility.HtmlEncode(Label))
                   .Append("</legend>");
        }

        content.Append("<div class=\"row g-2\">")
               .Append(Column(From, S["Effective from"].Value, required: true))
               .Append(Column(To, S["Effective to"].Value, required: !AllowOpenEnded))
               .Append("</div>");

        if (AllowOpenEnded)
        {
            content.Append("<div class=\"form-text\">")
                   .Append(System.Net.WebUtility.HtmlEncode(
                       S["Leave the end date empty while the record is still current. An end date is the last day it applies."].Value))
                   .Append("</div>");
        }

        output.Content.SetHtmlContent(content.ToString());
    }

    private string Column(ModelExpression expression, string caption, bool required)
    {
        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var value = expression.Model switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

        return $"""
            <div class="col-md-6">
              <label class="form-label small text-muted" for="{id}">{System.Net.WebUtility.HtmlEncode(caption)}</label>
              <input class="form-control" type="date" id="{id}" name="{name}" value="{value}"{(required ? " required" : string.Empty)} />
              <span class="text-danger field-validation-valid" data-valmsg-for="{name}" data-valmsg-replace="true"></span>
            </div>
            """;
    }
}
