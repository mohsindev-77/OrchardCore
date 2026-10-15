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
using WorkMate.Platform.Components;

namespace WorkMate.Dimensions.Components;

/// <summary>
/// Chooses grades. Two modes, because there are two questions. Decisions D26 C and D26 G.
/// </summary>
/// <remarks>
/// <b>Mode one — a Position's allowed grades</b> (<c>multiple</c>, no <c>position-field</c>). Every
/// grade the tenant has, searchable, several choosable. This is D26 C: a position is given a
/// <em>set</em> of grades and those are its levels.
///
/// <b>Mode one is a list of checkboxes, not a multi-select.</b> It was a
/// <c>&lt;select multiple&gt;</c> and manual testing was right to object. A listbox requires
/// control-click to add a second grade and shift-click for a run, it silently drops every other
/// choice on a plain click, it shows six rows at a time with no way to search, and on a touch
/// screen it is close to unusable. None of that is discoverable, and the failure is silent: somebody
/// who clicks four grades one after another saves one. Checkboxes have none of those properties,
/// are keyboard-operable with nothing learnt, and post the same field the same way.
///
/// <b>What is chosen is rendered first</b>, so a position with four grades out of two hundred shows
/// its four without scrolling. Order is settled when the page is drawn and the script does not
/// re-sort on a tick: a row that jumped to the top under the reader's cursor would make the next
/// click land on something else.
///
/// <b>The search box is rendered hidden and unhidden by the script</b>, the rule the unit picker's
/// <em>Within</em> filter follows for the same reason — every grade is already in the page, so
/// without the script the box would be a control that visibly does nothing, which is worse than one
/// that is not there.
///
/// <b>Mode two — an employee's grade</b> (<c>position-field</c> naming the position control beside
/// it). Only the chosen position's allowed grades, <b>empty and disabled until a position is
/// chosen</b>, and refreshed when it changes. This is D26 G, and the disabling is the point rather
/// than a nicety: a grade box that is populated before a position is chosen invites somebody to pick
/// a grade the position does not carry, which the validator then refuses.
///
/// <b>One control for both, because the list is the same kind of thing and the difference is which
/// question was asked.</b> Two controls would be two renderings of a grade option, and they would
/// come to disagree about what a grade looks like.
///
/// <b>The &lt;select&gt; is still the form control</b>, named, posted, bound and validated whether or
/// not the script runs — the rule every picker in this module follows. In mode two with no script
/// the box is rendered with whatever the server already knew the position to be, so the form still
/// works; it simply does not update as the position changes.
///
/// <b>Nothing here is the rule.</b> <c>IDimensionValidator.ValidateEmployeeGradeAsync</c> is, and it
/// is checked on the write. A control that merely populates itself from the position's set is a
/// control; a recipe, an import or a replayed form post goes round it.
/// </remarks>
[HtmlTargetElement("workmate-grade-picker", Attributes = "for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class GradePickerTagHelper : TagHelper
{
    private readonly IPositionService _positions;
    private readonly IStringLocalizer S;

    public GradePickerTagHelper(
        IPositionService positions,
        IStringLocalizer<GradePickerTagHelper> stringLocalizer)
    {
        _positions = positions;
        S = stringLocalizer;
    }

    /// <summary>How many grades are rendered before the reader has typed anything.</summary>
    private const int FirstPage = 50;

    /// <summary>
    /// How many grades the checkbox list renders. Every one of them, up to a sane limit.
    /// </summary>
    /// <remarks>
    /// A checkbox the page never drew cannot be ticked, and the search filters what is there rather
    /// than asking the server — so unlike the combobox pickers this control's page size is its whole
    /// answer. The cap exists only so a tenant that has made a grade per employee cannot render a
    /// megabyte of markup, and when it bites the control says so rather than quietly offering part
    /// of the set.
    /// </remarks>
    private const int AllGrades = 500;

    [HtmlAttributeName("for")]
    public ModelExpression For { get; set; } = default!;

    /// <summary>Whether several grades may be chosen — a Position's allowed set. Off for one grade.</summary>
    [HtmlAttributeName("multiple")]
    public bool Multiple { get; set; }

    /// <summary>
    /// The id of the position control on the same form, when this is an <em>employee's</em> grade.
    /// </summary>
    /// <remarks>
    /// Its presence is what puts the control in mode two: the options become the chosen position's
    /// allowed grades, and the box is disabled until there is one. Carried as an element id rather
    /// than a model expression because the script is what reads it.
    /// </remarks>
    [HtmlAttributeName("position-field")]
    public string? PositionField { get; set; }

    /// <summary>The position already chosen, when the form is rendered with one. Mode two only.</summary>
    [HtmlAttributeName("position-id")]
    public string? PositionId { get; set; }

    /// <summary>The date the choice takes effect, ISO-8601, so the control offers what is active then.</summary>
    [HtmlAttributeName("as-at")]
    public string? AsAt { get; set; }

    /// <summary>The id of the date input on the same form, so changing the date re-asks the question.</summary>
    [HtmlAttributeName("date-field")]
    public string? DateField { get; set; }

    /// <summary>The label. Already localised by the caller — see <see cref="TagHelperLabel"/>.</summary>
    public object? Label { get; set; }

    /// <summary>A line under the control. Already localised by the caller.</summary>
    public object? Hint { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var selected = SelectedIds();
        var boundToAPosition = !string.IsNullOrEmpty(PositionField);

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "mb-3 workmate-grade-picker");

        DateOnly? asAt = IsoDate.TryParse(AsAt, out var parsed) ? parsed : null;

        // Mode one is a different control, not a differently-configured one. Everything below this
        // is the single-grade combobox.
        if (Multiple && !boundToAPosition)
        {
            await WriteChecklistAsync(output, id, name, selected, asAt);

            return;
        }

        // Mode two with no position chosen yet is an empty, disabled box — D26 G, and the one state
        // the two modes genuinely render differently.
        var options = boundToAPosition
            ? string.IsNullOrEmpty(PositionId)
                ? []
                : await _positions.GetAllowedGradesAsync(PositionId, asAt)
            : (await _positions.SearchGradesAsync(searchText: null, asAt, FirstPage)).Items;

        var disabled = boundToAPosition && string.IsNullOrEmpty(PositionId);
        var markup = new StringBuilder();

        if (TagHelperLabel.Html(Label) is { } label)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<label class=\"form-label\" for=\"{id}\">{label}</label>");
        }

        markup.Append(
            CultureInfo.InvariantCulture,
            $"<select class=\"form-select\" id=\"{id}\" name=\"{name}\" data-grade-picker"
            + $"{(Multiple ? " multiple size=\"6\"" : string.Empty)}"
            + $"{(disabled ? " disabled" : string.Empty)}>");

        // Single-select carries an empty prompt; a multi-select does not, because "none" is
        // expressed by selecting nothing and an option saying so would be a second way to say it.
        if (!Multiple)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"\">{Encode(S["Choose a grade…"].Value)}</option>");
        }

        foreach (var grade in options)
        {
            var isSelected = selected.Contains(grade.RecordId) ? " selected" : string.Empty;

            markup.Append(
                CultureInfo.InvariantCulture,
                $"<option value=\"{Encode(grade.RecordId)}\"{isSelected} data-code=\"{Encode(grade.Code)}\">"
                + $"{Encode($"{ViewModels.BilingualDisplay.Name(grade.NameEn, grade.NameAr)} ({grade.Code})")}</option>");
        }

        markup.Append("</select>");

        // A disabled control posts nothing, so the value the form already held would be erased on
        // save. The hidden field is what keeps a stored grade through a round trip on a form whose
        // position has not been chosen in this request.
        if (disabled)
        {
            foreach (var value in selected)
            {
                markup.Append(
                    CultureInfo.InvariantCulture,
                    $"<input type=\"hidden\" name=\"{name}\" value=\"{Encode(value)}\" data-grade-picker-held />");
            }
        }

        if (TagHelperLabel.Html(Hint) is { } hint)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<div class=\"form-text\">{hint}</div>");
        }

        if (disabled)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text\" data-grade-picker-waiting>"
                + $"{Encode(S["Choose a position first, and this will offer the grades it allows."].Value)}</div>");
        }
        else if (options.Count == 0)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text text-warning\" data-grade-picker-empty>"
                + $"{Encode(boundToAPosition
                    ? S["This position allows no grades, so there is nothing to choose."].Value
                    : S["No grade is active on that date. Create one in the Grades dimension first."].Value)}</div>");
        }

        output.Attributes.SetAttribute("data-grade-picker-url", PositionPickerAdminController.GradesUrl);
        output.Attributes.SetAttribute("data-grade-picker-position-field", PositionField ?? string.Empty);
        output.Attributes.SetAttribute("data-grade-picker-as-at", AsAt ?? string.Empty);
        output.Attributes.SetAttribute("data-grade-picker-date-field", DateField ?? string.Empty);
        output.Attributes.SetAttribute("data-grade-picker-multiple", Multiple ? "true" : "false");
        output.Attributes.SetAttribute("data-grade-picker-strings", Strings());

        output.Content.SetHtmlContent(markup.ToString());
    }

    /// <summary>
    /// Mode one: every grade in the tenant as a searchable list of checkboxes, chosen ones first.
    /// </summary>
    /// <remarks>
    /// <b>It works with no script at all.</b> Every checkbox is in the page, named and valued, so
    /// ticking and saving is a plain form post — the rule every picker in this module follows. The
    /// script adds the search box and keeps the count live; neither is needed to use the control.
    ///
    /// <b>Nothing here is the rule.</b> <c>IDimensionValidator</c> is, and it is checked on the
    /// write — a recipe, an import or a replayed post goes round this entirely.
    /// </remarks>
    private async Task WriteChecklistAsync(
        TagHelperOutput output,
        string id,
        string name,
        HashSet<string> selected,
        DateOnly? asAt)
    {
        var found = await _positions.SearchGradesAsync(searchText: null, asAt, AllGrades);

        // Chosen first, and within each half the order the server gave them. A position's four
        // grades out of two hundred are then visible without scrolling, which is the whole point.
        var options = found.Items
            .OrderByDescending(grade => selected.Contains(grade.RecordId))
            .ToList();

        var markup = new StringBuilder();

        if (TagHelperLabel.Html(Label) is { } label)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<span class=\"form-label d-block\" id=\"{id}_label\">{label}</span>");
        }

        if (TagHelperLabel.Html(Hint) is { } hint)
        {
            markup.Append(CultureInfo.InvariantCulture, $"<div class=\"form-text mb-2\">{hint}</div>");
        }

        // Hidden until the script unhides it: the filtering is client-side over markup that is
        // already here, so without the script this box would do nothing.
        markup.Append(
            CultureInfo.InvariantCulture,
            $"<input type=\"search\" class=\"form-control form-control-sm mb-2\" id=\"{id}__search\""
            + $" placeholder=\"{Encode(S["Search grades"].Value)}\""
            + $" aria-label=\"{Encode(S["Search grades"].Value)}\""
            + $" aria-controls=\"{id}\" autocomplete=\"off\" data-grade-checklist-search hidden />");

        // A group rather than a listbox: these are ordinary checkboxes and naming them as anything
        // else would promise a screen reader keystrokes that do not exist. The inline height is two
        // declarations that belong to this control alone; a stylesheet for them would be a file
        // every form carrying a picker had to remember to include.
        markup.Append(
            CultureInfo.InvariantCulture,
            $"<div class=\"border rounded p-2\" id=\"{id}\" role=\"group\" aria-labelledby=\"{id}_label\""
            + $" style=\"max-height: 16rem; overflow-y: auto;\" data-grade-checklist>");

        var box = 0;

        foreach (var grade in options)
        {
            var boxId = $"{id}__{box++}";
            var text = $"{ViewModels.BilingualDisplay.Name(grade.NameEn, grade.NameAr)} ({grade.Code})";

            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-check\" data-grade-option data-code=\"{Encode(grade.Code)}\""
                + $" data-label=\"{Encode(text)}\">"
                + $"<input class=\"form-check-input\" type=\"checkbox\" id=\"{boxId}\" name=\"{name}\""
                + $" value=\"{Encode(grade.RecordId)}\""
                + $"{(selected.Contains(grade.RecordId) ? " checked" : string.Empty)} />"
                + $"<label class=\"form-check-label\" for=\"{boxId}\">{Encode(text)}</label></div>");
        }

        // No grade matches what was typed. Rendered empty and filled by the script, because the
        // server never has a search term to report on.
        markup.Append("<div class=\"form-text text-warning\" data-grade-checklist-no-match hidden></div>");

        markup.Append("</div>");

        // Live, and announced: a reader who has ticked four grades in a scrolling box has no other
        // way to know how many are on.
        markup.Append(
            CultureInfo.InvariantCulture,
            $"<div class=\"form-text\" aria-live=\"polite\" data-grade-checklist-count>"
            + $"{Encode(Chosen(selected.Count))}</div>");

        if (options.Count == 0)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text text-warning\" data-grade-picker-empty>"
                + $"{Encode(S["No grade is active on that date. Create one in the Grades dimension first."].Value)}</div>");
        }
        else if (found.HasMore)
        {
            markup.Append(
                CultureInfo.InvariantCulture,
                $"<div class=\"form-text text-warning\" data-grade-checklist-capped>"
                + $"{Encode(S["Showing {0} of {1} grades. Narrow the set on the Grades screen before choosing.", options.Count, found.Total].Value)}</div>");
        }

        output.Attributes.SetAttribute("data-grade-checklist-strings", ChecklistStrings());

        output.Content.SetHtmlContent(markup.ToString());
    }

    /// <summary>
    /// The count line: <em>Selected: 3</em>.
    /// </summary>
    /// <remarks>
    /// <b>A label and a number, deliberately, rather than "3 grades selected".</b> The count changes
    /// in the browser and the script has no localiser, so a plural-aware phrase would have to be
    /// chosen there — and Arabic has six forms, which is not a rule a script can carry. This shape
    /// has no plural to get wrong in either language, and the script substitutes one number into
    /// one template.
    /// </remarks>
    private string Chosen(int count) => S["Selected: {0}", count].Value;

    private string ChecklistStrings() => System.Text.Json.JsonSerializer.Serialize(
        new Dictionary<string, string>
        {
            // The template rather than a rendered count, so the script fills in a number without
            // having to know the words around it.
            ["selected"] = S["Selected: {0}"].Value,
            ["noMatch"] = S["No grade matches. Try part of a name or a code."].Value,
        });

    /// <summary>
    /// What is already chosen: one id, or several. The model is a string for a single grade and a
    /// collection for a set, and both reach here through <see cref="For"/>.
    /// </summary>
    private HashSet<string> SelectedIds()
    {
        var model = For.Model;

        return model switch
        {
            null => new HashSet<string>(StringComparer.Ordinal),
            string single => string.IsNullOrEmpty(single)
                ? new HashSet<string>(StringComparer.Ordinal)
                : [single],
            IEnumerable<string> many => [.. many.Where(value => !string.IsNullOrEmpty(value))],
            _ => [model.ToString() ?? string.Empty],
        };
    }

    private string Strings() => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["choose"] = S["Choose a grade…"].Value,
        ["waiting"] = S["Choose a position first, and this will offer the grades it allows."].Value,
        ["none"] = S["This position allows no grades, so there is nothing to choose."].Value,
    });

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
