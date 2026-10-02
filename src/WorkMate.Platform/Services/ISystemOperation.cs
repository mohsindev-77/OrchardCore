namespace WorkMate.Platform.Services;

/// <summary>
/// Marks the current scope as running on the platform's own authority rather than a user's.
/// </summary>
/// <remarks>
/// Specification section 3 rule 5 requires permissions to be checked in services "so the API and
/// background jobs are covered". That creates a problem for the three callers that legitimately
/// have no user to check: a recipe applied at tenant setup, a background task, and a maintenance
/// command.
///
/// The tempting answer — treat "no HTTP context" as full authority — is wrong, and was rejected
/// in review. It would exempt every background job ever written from every permission check, by
/// default and silently, which is the opposite of what the rule asks for. The absence of
/// evidence of a user is not evidence of authority.
///
/// So system authority is an explicit, scoped opt-in. A caller that genuinely runs as the
/// platform says so, in one place, with a reason that is logged. Anything else with no
/// authenticated user is refused. The scope is a scoped service rather than an
/// <c>AsyncLocal</c>, so it cannot leak out of the shell scope that entered it: a service
/// resolved from a different scope sees a fresh, inactive instance and fails closed.
/// </remarks>
public interface ISystemOperation
{
    /// <summary>
    /// Whether the current scope is running on the platform's authority. Services consult this
    /// only after finding no authenticated user.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Enters system authority until the returned handle is disposed. Nests safely: authority
    /// ends when the outermost handle is disposed, so a recipe step that calls another does not
    /// drop authority halfway through.
    /// </summary>
    /// <param name="reason">
    /// Why this scope claims system authority — the recipe step or background task doing it.
    /// Logged, so that "what ran as the system, and when" is answerable from the logs rather
    /// than by reading the code.
    /// </param>
    IDisposable Begin(string reason);
}
