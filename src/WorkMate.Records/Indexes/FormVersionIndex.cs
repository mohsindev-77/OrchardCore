using WorkMate.Records.Models;
using WorkMate.Records.Services;
using YesSql.Indexes;

namespace WorkMate.Records.Indexes;

/// <summary>
/// The columns a version list and a pinned submission look a version up by. Slice B3.
/// </summary>
/// <remarks>
/// Two questions, and both are a query rather than a scan: "every version of this form, newest
/// first" draws the version list, and "version 3 of this form" is what slice B7's pinned
/// submission resolves on every render of a stored request.
/// </remarks>
public sealed class FormVersionIndex : MapIndex
{
    /// <summary>The definition this is a version of, by its immutable code.</summary>
    public string FormCode { get; set; } = string.Empty;

    /// <summary>Folded, for the same reason every other code column here is folded.</summary>
    public string FormCodeUpper { get; set; } = string.Empty;

    /// <summary>Which version this is. Unique within a form code, by construction.</summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// When it was published, as a column so a list can order by it without loading documents.
    /// </summary>
    /// <remarks>
    /// <c>DateTime</c> rather than <c>DateTimeOffset</c>: YesSql 5.4.7's schema builder has no
    /// mapping for the latter, which is the same reason <c>EmployeeIndex</c>'s date columns are
    /// <c>DateTime</c>. Stored in UTC, which is what the document holds.
    /// </remarks>
    public DateTime PublishedOnUtc { get; set; }

    /// <summary>Who published it, or empty where the platform did.</summary>
    public string PublishedBy { get; set; } = string.Empty;

    /// <summary>How many fields the form had at this version.</summary>
    public int FieldCount { get; set; }
}

public sealed class FormVersionIndexProvider : IndexProvider<FormVersionDocument>
{
    public override void Describe(DescribeContext<FormVersionDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<FormVersionIndex>()
            .Map(document => new FormVersionIndex
            {
                FormCode = document.FormCode,
                FormCodeUpper = FormCodes.Normalise(document.FormCode).ToUpperInvariant(),
                VersionNumber = document.VersionNumber,
                PublishedOnUtc = document.PublishedOnUtc,
                PublishedBy = document.PublishedBy,
                FieldCount = document.Fields.Count,
            });
    }
}
