using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using YesSql;
using YesSql.Services;

namespace WorkMate.Records.Services;

/// <summary>
/// This module's answer to the dimension engine's question "who is this employee".
/// </summary>
/// <remarks>
/// The implementation of <see cref="IEmployeeLookup"/>, which <c>WorkMate.Dimensions</c> declares
/// because it needs the answer and must not depend on the module that holds it. Registered here;
/// the dimension engine resolves it as one of possibly none, and says so by name when there are
/// none.
///
/// <b>Reads the index, never the content items.</b> Its callers are a recipe step resolving a
/// column of codes, a validator checking one appointment, and a row of designer cards resolving
/// head names — none of which should pay for loading a content item, and the last of which would
/// otherwise load one per card on every expand.
///
/// <b>Bypasses permissions, deliberately and narrowly.</b> It answers existence and a name, which
/// is exactly what the dimension engine needs to validate an appointment and to draw a card, and
/// nothing a permission would be protecting. The same reasoning as that module's own internal
/// lookups: a reference check must not depend on who is asking, or validation would pass or fail
/// according to the caller's role.
/// </remarks>
internal sealed class EmployeeLookup : IEmployeeLookup
{
    private readonly ISession _session;

    public EmployeeLookup(ISession session) => _session = session;

    public async Task<EmployeeRef?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var upper = EmployeeCodes.Normalise(code).ToUpperInvariant();

        if (upper.Length == 0)
        {
            return null;
        }

        var row = await _session
            .QueryIndex<EmployeeIndex>(index => index.CodeUpper == upper && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToRef(row);
    }

    public async Task<EmployeeRef?> GetAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(employeeId))
        {
            return null;
        }

        var row = await _session
            .QueryIndex<EmployeeIndex>(index => index.ContentItemId == employeeId && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToRef(row);
    }

    public async Task<IReadOnlyDictionary<string, EmployeeRef>> GetManyAsync(
        IReadOnlyList<string> employeeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        var ids = employeeIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<string, EmployeeRef>(StringComparer.Ordinal);
        }

        // An IN list, which is safe here for the reason CountEmployeesAtAsync's is: the caller is a
        // row of designer cards or one page of a list — tens of ids — never a whole tenant. A
        // caller that wants everybody asks IEmployeeService.ListAsync, which is paged.
        var rows = await _session
            .QueryIndex<EmployeeIndex>(index => index.ContentItemId.IsIn(ids) && index.Latest)
            .ListAsync(cancellationToken);

        return rows.ToDictionary(row => row.ContentItemId, ToRef, StringComparer.Ordinal);
    }

    private static EmployeeRef ToRef(EmployeeIndex row) => new(
        row.ContentItemId,
        row.Code,
        row.NameEn,
        row.NameAr,
        // The last day of service, not the first day of ex-employment: the dimension engine asks
        // "had they left by this date", and the two answers differ by one day on exactly the day
        // somebody leaves. EmployeePart.ExitedOn does the same conversion from the same column.
        row.ExitedFrom is null ? null : EffectiveDates.FromColumn(row.ExitedFrom.Value).AddDays(-1));
}
