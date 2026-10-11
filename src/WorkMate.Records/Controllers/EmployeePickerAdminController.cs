using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.Admin;
using WorkMate.Platform.Services;

namespace WorkMate.Records.Controllers;

/// <summary>
/// What the employee picker asks as somebody types.
/// </summary>
/// <remarks>
/// <b>Server-side search, because the alternative does not scale and quietly breaks.</b> The picker
/// was a <c>&lt;select&gt;</c> of the first hundred employees of any status. On the Zenith demo
/// organisation that is a hundred of a hundred and fifty-eight: fifty-eight people could not be
/// named as a head, a line manager or anything else, from any screen, and nothing said so. A
/// control that filters a list it already holds has the same hole — it can only narrow what was
/// sent, and what was sent is the first hundred.
///
/// So the filtering happens here, against the whole tenant, and the browser holds one page at a
/// time.
///
/// <b>It answers only what a picker needs</b> — an id, a code, a name and where somebody sits —
/// and it answers it through <see cref="IEmployeeDirectory"/>, which applies
/// <c>IVisibilityService</c>. It is not a general employee API: a caller who wants the record asks
/// the employee screens, which check <c>ViewEmployees</c>.
/// </remarks>
[Admin("Employees/Picker/{action}", "WorkMateEmployeePicker{action}")]
[Authorize]
public sealed class EmployeePickerAdminController : Controller
{
    private readonly IEnumerable<IEmployeeDirectory> _directories;

    public EmployeePickerAdminController(IEnumerable<IEmployeeDirectory> directories) =>
        _directories = directories;

    /// <summary>
    /// A page of people matching <paramref name="q"/>.
    /// </summary>
    /// <param name="includeExited">
    /// Whether people who have left are offered. The caller says, because the answer differs by
    /// form — see <c>EmployeeDirectoryQuery</c> — and the control carries the form's answer in a
    /// data attribute so the two cannot drift.
    /// </param>
    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        int skip = 0,
        int take = 20,
        bool includeExited = true,
        CancellationToken cancellationToken = default)
    {
        var directory = _directories.FirstOrDefault();

        if (directory is null)
        {
            return Json(new { items = Array.Empty<object>(), total = 0 });
        }

        var found = await directory.SearchAsync(
            new EmployeeDirectoryQuery(q, skip, take, includeExited), cancellationToken);

        return Json(new
        {
            items = found.Items.Select(item => new
            {
                id = item.EmployeeId,
                code = item.Code,
                name = item.Name,
                where = item.Where,
            }),
            total = found.Total,
        });
    }
}
