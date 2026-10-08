using OrchardCore.ContentManagement;
using WorkMate.Core;
using WorkMate.Platform.Services;

namespace WorkMate.Records.Services;

/// <summary>
/// The candidate set behind an employee picker field: specification section 5's "content picker
/// restricted to <c>Employee</c> with visibility filtering".
/// </summary>
/// <remarks>
/// <b>What this is.</b> An Orchard <c>ContentPickerField</c> whose
/// <c>ContentPickerFieldSettings.DisplayedContentTypes</c> is <c>["Employee"]</c> already restricts
/// the <em>type</em>. What it does not do is restrict <em>which</em> employees a particular caller
/// may be offered, which is what section 9's visibility by node is about — so the stock
/// <c>DefaultContentPickerResultProvider</c> is not enough on its own, and this takes its place for
/// a field restricted to employees.
///
/// <b>Why a named provider rather than a replacement.</b> Orchard resolves providers by name and
/// the stock one stays registered for every other content picker in the tenant. Replacing it
/// outright would put this module's visibility rule in front of a picker choosing a taxonomy term.
///
/// <b>Visibility is a stub today</b> — <see cref="IVisibilityService"/> allows everything until
/// prompt 9 — and that is precisely why the filtering is wired now. A picker written without it and
/// retrofitted later is a picker whose call sites nobody can find; with it, prompt 9 changes one
/// implementation and every candidate set in the product narrows at once.
///
/// <b>It searches rather than lists.</b> That is the difference between this and
/// <see cref="EmployeeDirectory"/>, which backs the plain-form tag helper: a tenant with five
/// thousand employees cannot be offered as a list, and Orchard's picker UI is built to ask.
/// </remarks>
internal sealed class EmployeePickerResultProvider : IContentPickerResultProvider
{
    /// <summary>
    /// The name a field's editor asks for. Orchard matches a provider by this.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>"Default"</c>. Taking the stock provider's name would silently take over
    /// every content picker in the tenant, which is the opposite of what a field-specific rule
    /// should do.
    /// </remarks>
    public const string ProviderName = "WorkMateEmployee";

    private readonly IEmployeeService _employees;
    private readonly IVisibilityService _visibility;

    public EmployeePickerResultProvider(IEmployeeService employees, IVisibilityService visibility)
    {
        _employees = employees;
        _visibility = visibility;
    }

    public string Name => ProviderName;

    public async Task<IEnumerable<ContentPickerResult>> Search(ContentPickerSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(searchContext);

        // Only ours. A field that is not restricted to Employee has no business being answered by
        // the employee directory, and a provider that answered anyway would quietly offer staff in
        // a picker meant for something else.
        if (!searchContext.DisplayAllContentTypes &&
            searchContext.ContentTypes?.Contains(EmployeeFieldNames.ContentType, StringComparer.Ordinal) != true)
        {
            return [];
        }

        // Capped. Orchard's picker shows a short list and asks the user to keep typing; an
        // unbounded query here is the one that falls over on the tenant that most needs the picker.
        var page = await _employees.ListAsync(searchContext.Query, status: null, skip: 0, take: 50);

        var visible = await _visibility.FilterEmployeesAsync(
            [.. page.Items.Select(employee => employee.EmployeeId)]);

        var allowed = visible.ToHashSet(StringComparer.Ordinal);

        return
        [
            .. page.Items
                .Where(employee => allowed.Contains(employee.EmployeeId))
                .Select(employee => new ContentPickerResult
                {
                    ContentItemId = employee.EmployeeId,

                    // Name and code together, because two people share a name far more often than
                    // either of them expects and the code is what tells them apart.
                    DisplayText = $"{BilingualText.Display(employee.NameEn, employee.NameAr)} ({employee.Code})",

                    // Employees are not draftable, so the latest version is the published one.
                    HasPublished = true,
                }),
        ];
    }
}
