using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using WorkMate.Platform.Components;
using WorkMate.Records.Services;

namespace WorkMate.Records.Components;

/// <summary>
/// Chooses a value from a managed lookup list.
/// </summary>
/// <remarks>
/// <b>One control, two surfaces</b>, the same duality the employee picker already has and
/// documented in the module README — except that here there is literally one implementation rather
/// than two things that look alike:
/// <list type="bullet">
/// <item>this module's own admin forms, which are not content items — the employee's nationality
/// on the Add and Edit screens;</item>
/// <item><c>Views/TextField-WorkMateLookup.Edit.cshtml</c>, the alternate editor Orchard renders
/// for a bound content field, where the control posts the field's own <c>Text</c> value.</item>
/// </list>
///
/// <b>A real <c>&lt;select&gt;</c>, with no script at all.</b> Unlike the employee picker there is
/// nothing to search: a lookup list is tens of entries, not thousands, so the whole of it is on the
/// page and the browser's own control is better than anything this could add.
///
/// <b>It falls back to a text box when the list has nothing to offer</b> — see
/// <see cref="ILookupChoiceProvider"/>. That is what lets the binding ship to a tenant that has not
/// loaded the standard lists without changing a single screen until it does.
///
/// <b>It never drops the value the record holds.</b> A value matching no entry is rendered as its
/// own option, selected and marked, so opening a record and saving it cannot erase what was there.
/// That is the mechanism half of "no data lost"; the reconciliation screen is the half that clears
/// it up deliberately.
/// </remarks>
[HtmlTargetElement("workmate-lookup", TagStructure = TagStructure.WithoutEndTag)]
public sealed class LookupTagHelper : TagHelper
{
    private readonly ILookupChoiceProvider _choices;
    private readonly IStringLocalizer S;

    public LookupTagHelper(
        ILookupChoiceProvider choices,
        IStringLocalizer<LookupTagHelper> stringLocalizer)
    {
        _choices = choices;
        S = stringLocalizer;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary>The model property to bind, on a form that has a model.</summary>
    /// <remarks>
    /// Optional, because the content-field editor has no model property to point at: Orchard gives
    /// it a prefix and a field name instead. One of this and <see cref="Name"/> must be set.
    /// </remarks>
    public ModelExpression? For { get; set; }

    /// <summary>The posted field name, when there is no model expression to derive it from.</summary>
    public string? Name { get; set; }

    /// <summary>The value the record holds, when there is no model expression to read it from.</summary>
    public string? Value { get; set; }

    /// <summary>The code of the list to offer.</summary>
    public string? ListCode { get; set; }

    /// <summary>The label, or absent for a control that another element already labels.</summary>
    public object? Label { get; set; }

    /// <summary>Whether choosing nothing is allowed.</summary>
    /// <remarks>
    /// True by default. Every field this binds today is optional on the record, and a required one
    /// is the owning service's rule to enforce rather than a control's — a form that refuses an
    /// empty value in the browser and accepts it from a recipe has two rules.
    /// </remarks>
    public bool AllowNone { get; set; } = true;

    /// <summary>Marks the control required in the browser, for a field the server also requires.</summary>
    public bool Required { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var name = Name ?? (For is null
            ? string.Empty
            : ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name));

        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException(
                "<workmate-lookup> needs either a 'for' model expression or an explicit 'name'.");
        }

        var value = Value ?? For?.Model?.ToString() ?? string.Empty;
        var id = TagBuilder.CreateSanitizedId(name, "_");

        var choices = await _choices.ForAsync(ListCode, value);

        var markup = new System.Text.StringBuilder();

        if (TagHelperLabel.Html(Label) is { } label)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<label class=\"form-label\" for=\"{id}\">{label}</label>");
        }

        if (!choices.IsBound)
        {
            // Exactly what the screen rendered before the field was bound. Not an error and not an
            // empty dropdown: a tenant that has not loaded the lists must be able to go on typing.
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<input class=\"form-control workmate-lookup-free\" type=\"text\" id=\"{id}\" name=\"{name}\""
                + $" value=\"{Encode(value)}\"{(Required ? " required" : string.Empty)} />");

            Write(output, markup.ToString());

            return;
        }

        markup.Append(
            CultureInfo.InvariantCulture,
            $"<select class=\"form-select workmate-lookup\" id=\"{id}\" name=\"{name}\""
            + $" data-lookup-list=\"{Encode(ListCode ?? string.Empty)}\""
            + $"{(Required ? " required" : string.Empty)}>");

        if (AllowNone)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"\"{(value.Length == 0 ? " selected" : string.Empty)}>{Encode(S["— none —"].Value)}</option>");
        }

        foreach (var option in choices.Options)
        {
            var isSelected = LookupCodes.Same(option.Value, value) ? " selected" : string.Empty;

            var suffix = option switch
            {
                { IsUnmatched: true } => $" — {S["not in the list"].Value}",
                { IsRetired: true } => $" — {S["retired"].Value}",
                _ => string.Empty,
            };

            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(option.Value)}\"{isSelected}"
                + $" data-unmatched=\"{(option.IsUnmatched ? "true" : "false")}\">"
                + $"{Encode(option.Label + suffix)}</option>");
        }

        markup.Append("</select>");

        if (choices.StoredValueIsUnmatched)
        {
            // Said on the control rather than only in the option, because a reader scanning a form
            // of twenty fields reads the hints, not the closed dropdowns.
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text workmate-lookup-unmatched\">{Encode(S["This record holds a value that is not in the list. It is kept exactly as it is until somebody changes it here, or maps it on the lookup list's \"Check stored values\" screen."].Value)}</div>");
        }

        Write(output, markup.ToString());
    }

    private static void Write(TagHelperOutput output, string markup)
    {
        output.TagName = null;
        output.Content.SetHtmlContent(markup);
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
