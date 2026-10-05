using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Reads the fields a browser would actually submit for a rendered form, so a POST test exercises
/// exactly what the screen sends rather than a hand-picked subset the test author remembered to
/// include.
/// </summary>
/// <remarks>
/// Written after a bug a hand-built <c>FormUrlEncodedContent</c> dictionary could not have caught:
/// the dimension type Create form carries a hidden <c>ContentTypeName</c> field that posts an
/// empty string, which <c>DimensionTypeEditViewModel</c> required implicitly because the property
/// was a non-nullable <c>string</c> — invisible to every test here, because none of them posted a
/// field named <c>ContentTypeName</c> at all, so the implicit-required check that fires on an
/// empty string never had anything to fire on. A test built from the rendered page's own fields
/// posts that empty string the same way a browser does, and would have caught it immediately.
///
/// Only the three field types this codebase's forms actually use are handled: text-like
/// <c>&lt;input&gt;</c>, <c>&lt;select&gt;</c> and the checkbox-plus-hidden-companion pair every
/// bool property renders as (see <c>DateRangeTagHelper</c> and <c>WorkMateSettings.Edit.cshtml</c>
/// for the convention). Disabled fields are excluded, matching what a browser does; an unchecked
/// checkbox is excluded too, leaving its hidden "false" companion as the only value for that name,
/// which is what makes a bool bind correctly either way.
/// </remarks>
internal static class RenderedForm
{
    /// <summary>
    /// Every field the first <c>&lt;form&gt;</c> in <paramref name="html"/> would submit, in
    /// document order, with its current (default) value. Multiple entries may share a name — a
    /// checked checkbox and its hidden companion both do — which is why this is a list of pairs,
    /// not a dictionary: <see cref="FormUrlEncodedContent"/> accepts exactly this shape and posts
    /// every entry, the same way a browser posts every field with a given name.
    /// </summary>
    public static List<KeyValuePair<string, string>> FieldsOf(string html)
    {
        var document = new HtmlParser().ParseDocument(html);

        // The admin layout renders its own sign-out form in the top nav, before the page's own
        // content — the first <form> in document order is reliably that one, not the screen's.
        // Every content screen's form is the last one on the page, after any layout chrome.
        var forms = document.QuerySelectorAll("form");
        var form = (forms.Count > 0 ? forms[forms.Count - 1] : null) as IHtmlFormElement
            ?? throw new InvalidOperationException("The page has no <form> to read fields from.");

        var fields = new List<KeyValuePair<string, string>>();

        foreach (var element in form.QuerySelectorAll("input, select, textarea"))
        {
            switch (element)
            {
                case IHtmlInputElement { IsDisabled: false } input when !string.IsNullOrEmpty(input.Name):
                    if (input.Type is "checkbox" or "radio")
                    {
                        if (input.IsChecked)
                        {
                            fields.Add(new(input.Name, input.Value));
                        }
                    }
                    else
                    {
                        fields.Add(new(input.Name, input.Value));
                    }

                    break;

                case IHtmlSelectElement { IsDisabled: false } select when !string.IsNullOrEmpty(select.Name):
                    fields.Add(new(select.Name, select.Value ?? string.Empty));
                    break;

                case IHtmlTextAreaElement { IsDisabled: false } textarea when !string.IsNullOrEmpty(textarea.Name):
                    fields.Add(new(textarea.Name, textarea.Value));
                    break;
            }
        }

        return fields;
    }

    /// <summary>
    /// Sets a field to <paramref name="value"/>, replacing every existing entry of that name —
    /// for a text input or a select, there is only ever one to replace — or adding it if the
    /// rendered form had none, which is how a test fills in a row a page's own script would add
    /// after load, such as an attribute or level row with no server-rendered equivalent.
    /// </summary>
    public static List<KeyValuePair<string, string>> With(
        this List<KeyValuePair<string, string>> fields, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var result = fields.Where(field => field.Key != name).ToList();
        result.Add(new(name, value));
        return result;
    }
}
