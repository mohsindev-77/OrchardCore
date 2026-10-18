using OrchardCore.ContentManagement;

namespace WorkMate.Records.Models;

/// <summary>
/// Where designer-added fields on the employee record itself live. Decision D33 (Q2), slice B3.
/// </summary>
/// <remarks>
/// <b>Why this part exists at all.</b> The specification does not ask for it; the design note
/// proposed it and Q2 accepted it. A tenant wanting one more fact about a person — a staff-card
/// number, a locker, a parking bay — should not have to invent a one-row section to hold it, and
/// the alternative of adding fields to <c>EmployeePart</c> is closed by construction: that part
/// is the fixed core, is declared not attachable, and <c>EmployeeContentDefinitionGuard</c>
/// refuses every addition to it.
///
/// <b>It declares no properties, and that is the point.</b> Everything on it is a content field
/// the compiler writes onto the part's definition from a form definition. A property here would
/// be a field the designer did not put there and cannot see, which is the shape of thing that
/// drifts out of the model it is supposed to belong to.
///
/// <b>Not the fixed core, and the distinction is enforced rather than described.</b>
/// <c>EmployeePart</c> holds what payroll, leave and attendance depend on by name and is beyond
/// an administrator's reach; this holds what a customer added and nothing in the product reads by
/// name. The guard's reserved set, its placement vocabulary and D22's retired names all apply to
/// the fields written here, so a designer field can neither shadow a core one nor reintroduce the
/// department field architecture section 2 rules out.
///
/// <b>Attachable = false</b>, like <c>EmployeePart</c>, so Orchard's own type editor does not
/// offer it in "Add parts" and it cannot be welded onto another content type. The compiler welds
/// it onto <c>Employee</c> on the first record-level publish and never anywhere else.
///
/// <b>Public setters and not sealed</b>, like every other content part here: Orchard materialises
/// a part by deserialising the content item's JSON into it and builds parts and editor shapes
/// through proxies. There is nothing to set today, and the rule still holds the day there is.
/// </remarks>
public class EmployeeProfilePart : ContentPart
{
}
