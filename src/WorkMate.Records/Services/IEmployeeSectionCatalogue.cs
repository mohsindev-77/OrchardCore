namespace WorkMate.Records.Services;

/// <summary>
/// The sections <em>this tenant's</em> employee record carries, in the order its definition states.
/// </summary>
/// <remarks>
/// <b>Why a service and not <c>EmployeeSections.All</c>.</b> That constant is what WorkMate
/// <em>ships</em>; it is not what a tenant <em>has</em>. Its own doc comment says so — <em>"these
/// are the standard sections, not the only ones — a tenant adds its own through the form designer
/// in session B, which produces more content types the same way"</em> — and the employee page is
/// the screen that would otherwise freeze the six into markup. The design note for this slice makes
/// it a rule (§9.2, Rule 2): <em>the form's section list is never hard-coded</em>, because a
/// seventh section arriving in B3 must appear without anybody editing a view.
///
/// <b>How a section is recognised.</b> Not by name. A section is a <c>BagPart</c> on the Employee
/// type whose contained item type carries the <c>EmployeeSection</c> stereotype — which is the
/// question <c>EmployeeSections.Stereotype</c> exists to let a caller ask, <em>"without a
/// hard-coded list of six type names that would be wrong the moment a tenant adds a seventh"</em>.
/// A bag of something else on the Employee type is somebody else's part and is left alone.
///
/// <b>What it deliberately does not include.</b> The fixed core — identity, and the organisation
/// block this slice adds — is not a section and must never become one. It is rendered above the
/// loop by the page's own markup, and <c>EmployeeFieldNames.ForbiddenOnSections</c> is what stops a
/// tenant building a shadow copy of it. §9.2, Rule 1.
/// </remarks>
public interface IEmployeeSectionCatalogue
{
    /// <summary>
    /// Every section on this tenant's employee record, in definition order.
    /// </summary>
    /// <remarks>
    /// Definition order rather than a sort: the order is a decision — job details first because it
    /// is what somebody opening a record is usually after, documents second because of expiry — and
    /// B4 lets a tenant change it. Re-sorting here would quietly overrule them.
    /// </remarks>
    Task<IReadOnlyList<EmployeeSectionRef>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One section of the employee record, as a screen needs to name it.
/// </summary>
/// <param name="Name">
/// The part name on the Employee type — <c>JobDetails</c>, <c>Documents</c>, and whatever B4 adds.
/// The stable identifier: an anchor, a permission and a form definition all name a section by this.
/// </param>
/// <param name="DisplayName">
/// What to call it on screen. The tenant's own words where it has given any, which is why this is
/// rendered as data and not passed through the localiser — a section a customer named
/// <em>"Visa details"</em> is not a string this product ships and has no translation to look up.
/// </param>
/// <param name="ItemTypeName">
/// The content type of one row in the section — <c>EmployeeDocument</c>. Carried because the
/// section is a list and a screen that says "add a document" needs to know what a document is.
/// </param>
public sealed record EmployeeSectionRef(string Name, string DisplayName, string ItemTypeName);
