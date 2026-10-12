using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.Admin;
using OrchardCore.ContentManagement.Display;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Records.Services;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Controllers;

/// <summary>
/// The employee's sections — job details, documents, bank details, qualifications, dependants and
/// contacts — on a WorkMate screen.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> Until now the "Edit sections" link went to
/// <c>~/Admin/Contents/ContentItems/{id}/Edit</c>, Orchard's generic content editor. It works, and
/// everything around it belongs to somebody else: the admin menu highlights Content → Content
/// Items, the job details section is introduced as "JobDetails — Provides a collection behavior for
/// your content item where you can place other content items", and above it all sits a disabled
/// Title box explaining that it is auto-generated from a pattern. None of that is wrong; all of it
/// is addressed to a developer building a CMS, and the person reading it is an HR administrator
/// looking for somebody's passport expiry.
///
/// <b>What this is not.</b> It is not a reimplementation of the editor. The fields inside a section
/// are still rendered by Orchard's own display drivers through
/// <see cref="IContentItemDisplayManager"/> — including this module's own lookup editor on the bound
/// ones — because those drivers are the only thing that knows how to edit a field type, and the set
/// of field types grows with the form designer. What changes is the frame around them: the page, its
/// heading, the words introducing each section, and which menu entry lights up.
///
/// Everything a module may do here is an Orchard extension point, per CLAUDE.md's hard boundary: a
/// controller of our own, shape alternates for the section headings
/// (<c>ContentPart-Employee-{Section}.Edit.cshtml</c>), and <c>placement.json</c> to withhold the
/// generated title. Nothing in Orchard is changed.
///
/// <b>It is generic over the sections on purpose.</b> It never names a section or a field: it builds
/// the editor for the whole item and renders whatever parts the tenant's content definition carries.
/// The six are what WorkMate ships today; the form designer adds more in slice B3, and the FRS work
/// on positions will change what job details holds. A screen that knew would need changing twice.
/// </remarks>
[Admin("Employees/Sections/{action}/{id?}", "EmployeeSections{action}")]
public sealed class EmployeeSectionsAdminController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly IContentItemDisplayManager _contentItemDisplayManager;
    private readonly IUpdateModelAccessor _updateModelAccessor;
    private readonly INotifier _notifier;
    private readonly IHtmlLocalizer H;

    public EmployeeSectionsAdminController(
        IEmployeeService employees,
        IRecordsAuthorisation authorisation,
        IContentItemDisplayManager contentItemDisplayManager,
        IUpdateModelAccessor updateModelAccessor,
        INotifier notifier,
        IHtmlLocalizer<EmployeeSectionsAdminController> htmlLocalizer)
    {
        _employees = employees;
        _authorisation = authorisation;
        _contentItemDisplayManager = contentItemDisplayManager;
        _updateModelAccessor = updateModelAccessor;
        _notifier = notifier;
        H = htmlLocalizer;
    }

    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var item = await _employees.GetContentItemAsync(id, cancellationToken);

        if (item is null)
        {
            // One answer for "no such employee" and for "you may not see them", because telling the
            // two apart tells somebody who may not read the record that the record exists.
            return NotFound();
        }

        var employee = await _employees.GetAsync(id, cancellationToken);

        return View(new EmployeeSectionsViewModel
        {
            EmployeeId = id,
            Name = employee?.Name.Display() ?? string.Empty,
            Code = employee?.Code ?? string.Empty,

            // An auditor holds ViewEmployees and not ManageEmployees: they may read somebody's
            // documents and may not change them. They get the screen without a Save button, which
            // is this module's rule — not rendered, rather than rendered and then refused.
            MaySave = await _authorisation.AuthoriseAsync(Permissions.ManageEmployees),

            Editor = await _contentItemDisplayManager.BuildEditorAsync(
                item,
                _updateModelAccessor.ModelUpdater,
                isNew: false,
                groupId: string.Empty,
                htmlFieldPrefix: string.Empty),
        });
    }

    /// <remarks>
    /// The item is loaded again rather than round-tripped through the form, which is how Orchard's
    /// own content editor works and is the only safe way: the post carries the fields the editor
    /// rendered, and anything else on the record — the core fields, the sections a reader's
    /// permissions kept off their screen — has to come from storage rather than from the browser.
    /// </remarks>
    [HttpPost]
    [ActionName(nameof(Edit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(string id, CancellationToken cancellationToken)
    {
        var item = await _employees.GetContentItemAsync(id, cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        var editor = await _contentItemDisplayManager.UpdateEditorAsync(
            item,
            _updateModelAccessor.ModelUpdater,
            isNew: false,
            groupId: string.Empty,
            htmlFieldPrefix: string.Empty);

        var employee = await _employees.GetAsync(id, cancellationToken);

        if (!ModelState.IsValid)
        {
            // Redrawn with the editor the update produced, so a field's own validation message is
            // shown on that field rather than summarised at the top and lost.
            return View(new EmployeeSectionsViewModel
            {
                EmployeeId = id,
                Name = employee?.Name.Display() ?? string.Empty,
                Code = employee?.Code ?? string.Empty,
                MaySave = await _authorisation.AuthoriseAsync(Permissions.ManageEmployees),
                Editor = editor,
            });
        }

        var saved = await _employees.UpdateSectionsAsync(item, cancellationToken);

        if (!saved.IsAuthorised)
        {
            return Forbid();
        }

        if (!saved.Succeeded)
        {
            foreach (var error in saved.Errors)
            {
                // Already in the reader's language: the service raised it through its own localiser,
                // so it is passed through rather than looked up again. See FormsAdminController.
                await _notifier.ErrorAsync(
                    new LocalizedHtmlString(error.Message.Value, error.Message.Value));
            }

            return RedirectToAction(nameof(Edit), new { id });
        }

        await _notifier.SuccessAsync(H["The sections have been saved."]);

        // Back to the employee, because the sections are part of that record and not a destination
        // of their own: somebody who has just filed a passport wants to see the person, not the form
        // they have finished with.
        return RedirectToAction("Edit", "EmployeesAdmin", new { id });
    }
}
