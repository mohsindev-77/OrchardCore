namespace WorkMate.Dimensions.Services;

/// <summary>
/// The audit trail category and event names this module records under.
/// </summary>
/// <remarks>
/// Constants rather than literals at the call sites, because the same names appear three times:
/// in the options that declare the events, in the manager call that records one, and in the
/// filter an administrator uses to find them. A typo in any one of those produces an event that
/// is recorded but never surfaced, which is worse than no audit trail at all because it looks
/// like one.
///
/// Specification section 9 lists the WorkMate event types Orchard's audit trail is extended with.
/// Two of them belong to this module: structure change, and content-definition change.
/// </remarks>
public static class DimensionAuditTrail
{
    /// <summary>The category every event in this module is filed under.</summary>
    public const string Category = "Dimension";

    /// <summary>A dimension type was created, changed or retired.</summary>
    public const string DimensionTypeChanged = "DimensionTypeChanged";

    /// <summary>A structure or its levels were created or changed.</summary>
    public const string StructureChanged = "StructureChanged";

    /// <summary>
    /// A content type or part definition was created or altered at runtime on behalf of a
    /// dimension type. Separate from <see cref="DimensionTypeChanged"/> because specification
    /// section 9 names content-definition change as its own event type: it is the one change in
    /// this module that alters the tenant's schema rather than its data, and an auditor looking
    /// for schema drift should not have to read every dimension type edit to find it.
    /// </summary>
    public const string ContentDefinitionChanged = "ContentDefinitionChanged";

    /// <summary>
    /// A move was cancelled: the link it created was removed and the placement it displaced was
    /// restored. Its own event, not folded into a general "record changed" entry, because a
    /// cancellation is the one record-level operation this module requires a reason for and must
    /// never perform silently.
    /// </summary>
    public const string MoveCancelled = "MoveCancelled";
}
