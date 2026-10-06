using Microsoft.Extensions.Options;
using OrchardCore.AuditTrail.Services.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// Declares this module's audit trail category and its events, so that an administrator can
/// enable, disable and filter them alongside Orchard's own.
/// </summary>
/// <remarks>
/// All five events are mandatory. Specification section 9 puts structure change and
/// content-definition change on the list of WorkMate event types, and the point of auditing a
/// structure change is that the structure is what every historical payroll and approval figure
/// resolves against. Move cancelled is mandatory for the same reason the operation requires a
/// reason in the first place: it is never a silent delete. An event an administrator can quietly
/// switch off is not an audit trail, so these are declared mandatory rather than merely enabled
/// by default.
/// </remarks>
public sealed class DimensionAuditTrailOptionsConfiguration : IConfigureOptions<AuditTrailOptions>
{
    public void Configure(AuditTrailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.For<DimensionAuditTrailOptionsConfiguration>(
                DimensionAuditTrail.Category,
                S => S["Dimensions"])
            .WithEvent(
                DimensionAuditTrail.DimensionTypeChanged,
                S => S["Dimension type changed"],
                S => S["A dimension type was created, changed or retired."],
                enableByDefault: true,
                isMandatory: true)
            .WithEvent(
                DimensionAuditTrail.StructureChanged,
                S => S["Structure changed"],
                S => S["A structure or its levels were created or changed."],
                enableByDefault: true,
                isMandatory: true)
            .WithEvent(
                DimensionAuditTrail.ContentDefinitionChanged,
                S => S["Content definition changed"],
                S => S["A content type was created or altered on behalf of a dimension type."],
                enableByDefault: true,
                isMandatory: true)
            .WithEvent(
                DimensionAuditTrail.DimensionRecordChanged,
                S => S["Dimension record changed"],
                S => S["A unit was added to a structure, renamed or retired."],
                enableByDefault: true,
                isMandatory: true)
            .WithEvent(
                DimensionAuditTrail.MoveCancelled,
                S => S["Move cancelled"],
                S => S["A move was cancelled and the placement it displaced was restored."],
                enableByDefault: true,
                isMandatory: true);
    }
}
