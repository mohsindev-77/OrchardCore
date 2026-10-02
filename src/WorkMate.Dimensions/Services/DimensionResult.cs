namespace WorkMate.Dimensions.Services;

/// <summary>
/// What every write path on the dimension engine returns: the thing that was written, or the
/// rules that stopped it.
/// </summary>
/// <remarks>
/// A result rather than an exception, because a rule violation is an expected outcome of a
/// user's edit and the caller is expected to show all of them at once. Architecture section 6
/// makes this explicit for imports — "reports all failures rather than stopping at the first" —
/// and the same applies to a single edit with three things wrong with it.
///
/// Genuine faults stay exceptions. A <c>ConcurrencyException</c> from YesSql, a missing tenant
/// service, a cancelled token: none of those is a rule the user broke.
///
/// Construct one through <see cref="DimensionResult"/>, which carries the factories so that the
/// type argument is inferred at the call site.
/// </remarks>
public sealed record DimensionResult<T>
{
    internal DimensionResult(T? value, IReadOnlyList<DimensionError> errors, bool isAuthorised)
    {
        Value = value;
        Errors = errors;
        IsAuthorised = isAuthorised;
    }

    /// <summary>The result of the operation, present only when it succeeded.</summary>
    public T? Value { get; }

    /// <summary>
    /// Every rule violated, blocking and advisory alike. A successful result may still carry
    /// advisory errors — a future-dated parent, most often — which the caller should surface
    /// rather than swallow.
    /// </summary>
    public IReadOnlyList<DimensionError> Errors { get; }

    /// <summary>
    /// False when the caller lacks the permission the operation needs. Separate from a rule
    /// violation because the two want different responses: a 403 against a form that redisplays
    /// with messages.
    /// </summary>
    public bool IsAuthorised { get; }

    /// <summary>True when the operation was performed. Advisory errors do not make it false.</summary>
    public bool Succeeded => IsAuthorised && !Errors.Any(error => !error.IsAdvisory);
}

/// <summary>Factories for <see cref="DimensionResult{T}"/>.</summary>
public static class DimensionResult
{
    /// <summary>The operation was performed, possibly with advisory warnings worth surfacing.</summary>
    public static DimensionResult<T> Success<T>(T value, params DimensionError[] advisories) =>
        new(value, advisories, isAuthorised: true);

    /// <summary>The operation was refused because the caller's request breaks these rules.</summary>
    public static DimensionResult<T> Failed<T>(params DimensionError[] errors) =>
        new(default, errors, isAuthorised: true);

    /// <inheritdoc cref="Failed{T}(DimensionError[])" />
    public static DimensionResult<T> Failed<T>(IReadOnlyList<DimensionError> errors) =>
        new(default, errors, isAuthorised: true);

    /// <summary>
    /// The caller may not do this. Carries no errors, because telling someone which rules their
    /// unauthorised request would also have broken leaks the configuration it was refused.
    /// </summary>
    public static DimensionResult<T> NotAuthorised<T>() => new(default, [], isAuthorised: false);
}
