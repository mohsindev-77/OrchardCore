using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.DisplayManagement.Notify;

namespace WorkMate.Records.Services;

/// <summary>
/// The one place the product says "this person's position changed — look at their access roles".
/// Decision D26 I.
/// </summary>
/// <remarks>
/// <b>A reminder, and deliberately nothing more.</b> D26 I: <em>"Changing somebody's position
/// raises a 'review access roles' reminder rather than changing anything by itself, because what
/// permissions a post implies is a customer's policy and not a derivation."</em> A Position carries
/// no permissions and an Access role carries no headcount (D26 B); what joins them is a person, and
/// a human decides.
///
/// <b>One class so the security slice has one thing to replace.</b> Three screens raise this —
/// Place, Change position and the Create form's first placement — and a message written three times
/// is three messages that drift. When the security slice arrives it changes the text here once and
/// gives it somewhere to link to, rather than finding every caller.
///
/// <b>Non-blocking.</b> It rides with a success notification and never stops a save: the position
/// change has already happened and is correct, and refusing it until somebody reviewed a role would
/// be the product making a customer's policy decision for them.
/// </remarks>
public sealed class AccessRoleReminder
{
    private readonly INotifier _notifier;
    private readonly IHtmlLocalizer H;

    public AccessRoleReminder(INotifier notifier, IHtmlLocalizer<AccessRoleReminder> htmlLocalizer)
    {
        _notifier = notifier;
        H = htmlLocalizer;
    }

    /// <summary>
    /// Raises the reminder for somebody whose position has just changed.
    /// </summary>
    /// <remarks>
    /// Named for the employee, because an operator who has just moved three people needs to know
    /// which one this is about. A notification with no name is one somebody dismisses.
    /// </remarks>
    public ValueTask RaiseAsync(string employeeName) =>
        _notifier.WarningAsync(H["Position changed — review {0}'s access roles.", employeeName]);
}
