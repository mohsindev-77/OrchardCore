namespace WorkMate.Records;

/// <summary>
/// The entitlement capabilities this module declares.
/// </summary>
/// <remarks>
/// Specification section 8: "Capabilities are string constants declared per module in a
/// <c>Capabilities.cs</c>. Services check them at the point of use." That is what these are and
/// where they are checked — <c>LookupListService</c> asks
/// <c>IEntitlementService.IsAllowedAsync</c> before every write, beside the permission check.
///
/// <b>Nothing is refused yet, and that is deliberate on two counts.</b>
///
/// First, there is nothing to ask. <c>WorkMate.Entitlement</c> is a skeleton; the signed
/// entitlement record arrives in prompt 8. Until then <c>AllowAllEntitlementService</c> answers
/// yes to everything, which is the right stub for the reason <c>IEntitlementService</c> gives: a
/// tenant where every capability is refused is not a safer failure, only a more confusing one.
///
/// Second, <b>which edition each of these belongs to is an open product decision</b>. The product
/// specification's packaging table reads "Forms | Standard forms, field configuration | Full form
/// designer | Form designer plus definition versioning" across Essential, Professional and
/// Enterprise, which maps onto the three constants below — but that reading has not been
/// confirmed, and guessing it here would turn a commercial decision into a code one taken by
/// accident. The constants are declared and asked; the mapping is recorded in the module README's
/// known limitations until the decision lands.
/// </remarks>
public static class Capabilities
{
    /// <summary>
    /// Configuring the fields on the forms the product ships: labels, help text, order, required,
    /// and the managed lists a choice field offers.
    /// </summary>
    /// <remarks>
    /// What lookup lists are gated on. A tenant that can keep employee records at all needs to be
    /// able to say that its grades are G1 to G9 rather than whatever the last person typed; this is
    /// the capability the product specification's Essential column appears to describe.
    /// </remarks>
    public const string FormFieldConfiguration = "WorkMate.Records.FormFieldConfiguration";

    /// <summary>
    /// The full designer: new fields, new sections, new request forms, conditional visibility and
    /// cross-field rules.
    /// </summary>
    /// <remarks>
    /// Declared now and used from slice B3. It is here rather than added later so that the three
    /// capabilities are decided as a set, which is how an edition boundary has to be read.
    /// </remarks>
    public const string FormDesigner = "WorkMate.Records.FormDesigner";

    /// <summary>Keeping and comparing the published versions of a form definition.</summary>
    /// <inheritdoc cref="FormDesigner"/>
    public const string FormDefinitionVersioning = "WorkMate.Records.FormDefinitionVersioning";
}
