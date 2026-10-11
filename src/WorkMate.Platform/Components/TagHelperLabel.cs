using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Localization;

namespace WorkMate.Platform.Components;

/// <summary>
/// Turns whatever a view passed as a label into HTML that is safe to write.
/// </summary>
/// <remarks>
/// <b>The defect this exists for.</b> Every label on every WorkMate tag helper was a
/// <c>string</c>, and a view that wrote <c>label="@T[the field name]"</c> got the text
/// <c>Microsoft.AspNetCore.Mvc.Localization.LocalizedHtmlString</c> on the screen. Razor binds an
/// expression to a <c>string</c> property by calling <c>ToString()</c>, and
/// <see cref="LocalizedHtmlString"/> does not override it — so the compiler is happy, the page
/// renders, and the label is the name of a type.
///
/// Four views had it right with <c>@T[…].Value</c> and three had it wrong without. That split is
/// the real defect: a property type that accepts the natural thing to write and then renders it
/// wrongly is a trap, and spelling <c>.Value</c> correctly at every call site for ever is not a
/// design.
///
/// So the label properties take <c>object?</c> and come through here, which knows the three things
/// a view can hand it apart and encodes each one correctly:
/// <list type="bullet">
/// <item><see cref="IHtmlContent"/> — <c>@T[…]</c>, and anything else already rendered as HTML.
/// Written out as it is, because encoding it again would turn an apostrophe into
/// <c>&amp;amp;#39;</c>.</item>
/// <item><see cref="LocalizedString"/> — <c>@S[…]</c>, which is text. Encoded.</item>
/// <item><see cref="string"/> — a literal, or tenant data such as a dimension attribute's label.
/// Encoded, which is the half that matters: that one is not ours to trust.</item>
/// </list>
/// </remarks>
public static class TagHelperLabel
{
    /// <summary>
    /// The label as HTML ready to write, or null when there is no label to write.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string, so a caller can tell "no label" from "a label that is
    /// blank" and leave the element out entirely rather than rendering an empty one.
    /// </remarks>
    public static string? Html(object? label)
    {
        switch (label)
        {
            case null:
                return null;

            // Already HTML, and already encoded by whoever produced it. This is the case every
            // @T[…] takes, and the one that used to print a type name.
            case IHtmlContent content:
                {
                    using var writer = new StringWriter(CultureInfo.InvariantCulture);

                    content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);

                    var rendered = writer.ToString();

                    return string.IsNullOrWhiteSpace(rendered) ? null : rendered;
                }

            // Text from the localiser. IStringLocalizer's indexer, which services use and a view
            // can reach through @S.
            case LocalizedString localized:
                return string.IsNullOrWhiteSpace(localized.Value)
                    ? null
                    : WebUtility.HtmlEncode(localized.Value);

            case string text:
                return string.IsNullOrWhiteSpace(text) ? null : WebUtility.HtmlEncode(text);

            default:
                {
                    // Anything else: its text, encoded. Reached by a view passing a number or a
                    // model property, which is a reasonable thing to do and not worth refusing.
                    var text = label.ToString();

                    return string.IsNullOrWhiteSpace(text) ? null : WebUtility.HtmlEncode(text);
                }
        }
    }
}
