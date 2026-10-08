using Microsoft.Extensions.Localization;

namespace WorkMate.Records.Services;

/// <summary>
/// One rule violation on an employee record: which rule, which thing broke it, and what to tell the
/// user.
/// </summary>
/// <param name="Rule">The rule, in a form a caller can branch on and a test can assert.</param>
/// <param name="Subject">
/// The employee code or id the rule is about. Empty only for a rule about the request as a whole.
/// </param>
/// <param name="Message">The localised sentence. No user is ever shown an enum.</param>
/// <param name="Field">
/// The form field this belongs under, when there is one, so a screen can show it where the reader
/// has to act rather than only in the summary at the top. A hint, not a contract; a recipe step
/// ignores it.
/// </param>
public sealed record RecordError(
    RecordRule Rule,
    string Subject,
    LocalizedString Message,
    string? Field = null);

/// <summary>
/// What every write path on the employee record returns: the thing that was written, or the rules
/// that stopped it.
/// </summary>
/// <remarks>
/// Deliberately the same shape as <c>WorkMate.Dimensions.Services.DimensionResult&lt;T&gt;</c>
/// rather than that type itself. The two modules have different rule vocabularies —
/// <see cref="RecordRule"/> and <c>DimensionRule</c> — and a result type that carried the other
/// module's enum would make every employee-record failure describe itself in the dimension engine's
/// terms.
///
/// <b>This is duplication, and it is on purpose for now.</b> A shared
/// <c>WorkMateResult&lt;TRule&gt;</c> in <c>WorkMate.Core</c> is the obvious end state and is worth
/// doing; hoisting it would mean editing every write path in <c>WorkMate.Dimensions</c> for no
/// behaviour change, which is its own reviewable piece of work and not this one. Recorded in the
/// module README so it is a decision rather than an oversight.
///
/// A result rather than an exception, for the reason the dimension engine gives: a rule violation
/// is an expected outcome of a user's edit, and three things wrong with one form should produce
/// three messages rather than the first one. Genuine faults stay exceptions.
/// </remarks>
public sealed record RecordResult<T>
{
    internal RecordResult(T? value, IReadOnlyList<RecordError> errors, bool isAuthorised)
    {
        Value = value;
        Errors = errors;
        IsAuthorised = isAuthorised;
    }

    /// <summary>The result of the operation, present only when it succeeded.</summary>
    public T? Value { get; }

    /// <summary>Every rule violated.</summary>
    public IReadOnlyList<RecordError> Errors { get; }

    /// <summary>
    /// False when the caller lacks the permission the operation needs. Separate from a rule
    /// violation because the two want different responses: a 403, against a form that redisplays
    /// with messages on it.
    /// </summary>
    public bool IsAuthorised { get; }

    /// <summary>True when the operation was performed.</summary>
    public bool Succeeded => IsAuthorised && Errors.Count == 0;

    /// <summary>Every message, joined — for a test failure or a log line, never for a screen.</summary>
    public string Describe() => string.Join("; ", Errors.Select(error => error.Message.Value));
}

/// <summary>Factories for <see cref="RecordResult{T}"/>, so the type argument is inferred.</summary>
public static class RecordResult
{
    public static RecordResult<T> Success<T>(T value) => new(value, [], isAuthorised: true);

    public static RecordResult<T> Failed<T>(params RecordError[] errors) =>
        new(default, errors, isAuthorised: true);

    /// <inheritdoc cref="Failed{T}(RecordError[])" />
    public static RecordResult<T> Failed<T>(IReadOnlyList<RecordError> errors) =>
        new(default, errors, isAuthorised: true);

    /// <summary>
    /// The caller may not do this. Carries no errors: telling somebody which rules their
    /// unauthorised request would also have broken leaks the configuration it was refused.
    /// </summary>
    public static RecordResult<T> NotAuthorised<T>() => new(default, [], isAuthorised: false);
}
