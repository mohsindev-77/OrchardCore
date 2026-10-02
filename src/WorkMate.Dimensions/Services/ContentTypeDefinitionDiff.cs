namespace WorkMate.Dimensions.Services;

/// <summary>
/// What changed about a dimension type's backing content type, as the audit trail records it.
/// </summary>
/// <remarks>
/// Specification section 4 requires that "every content-definition change made this way is
/// recorded in the audit trail with the acting user and a diff of the definition", and section 9
/// requires that each audit event "carries the before and after values".
///
/// Both are kept rather than one or the other. <see cref="Before"/> and <see cref="After"/> are
/// the complete state, which is what lets a reader reconstruct the definition at any point in
/// the trail; the computed lists are what lets them see what happened without doing that
/// reconstruction in their head. Fields in particular are what customers add and remove over
/// years, and a diff that says "two fields removed" and names them is the difference between an
/// answerable question and an archaeology exercise.
/// </remarks>
public sealed record ContentTypeDefinitionDiff(
    ContentTypeDefinitionSnapshot? Before,
    ContentTypeDefinitionSnapshot After)
{
    /// <summary>True when the type did not exist before this change.</summary>
    public bool IsCreation => Before is null;

    /// <summary>Parts attached by this change.</summary>
    public IReadOnlyList<string> PartsAdded => Difference(After.Parts, Before?.Parts);

    /// <summary>Parts detached by this change.</summary>
    public IReadOnlyList<string> PartsRemoved => Difference(Before?.Parts, After.Parts);

    /// <summary>Fields added by this change, as <c>Part.Field (FieldType)</c>.</summary>
    public IReadOnlyList<string> FieldsAdded => Difference(Describe(After.Fields), Describe(Before?.Fields));

    /// <summary>Fields removed by this change, as <c>Part.Field (FieldType)</c>.</summary>
    public IReadOnlyList<string> FieldsRemoved => Difference(Describe(Before?.Fields), Describe(After.Fields));

    /// <summary>
    /// The flags whose value this change altered, as <c>Flag: before -&gt; after</c>. Empty on a
    /// creation, where there is no before to compare against.
    /// </summary>
    public IReadOnlyList<string> SettingsChanged
    {
        get
        {
            if (Before is null)
            {
                return [];
            }

            var changes = new List<string>();

            Compare(nameof(After.Creatable), Before.Creatable, After.Creatable);
            Compare(nameof(After.Listable), Before.Listable, After.Listable);
            Compare(nameof(After.Securable), Before.Securable, After.Securable);
            Compare(nameof(After.Draftable), Before.Draftable, After.Draftable);

            if (!string.Equals(Before.DisplayName, After.DisplayName, StringComparison.Ordinal))
            {
                changes.Add($"{nameof(After.DisplayName)}: {Before.DisplayName} -> {After.DisplayName}");
            }

            return changes;

            void Compare(string name, bool before, bool after)
            {
                if (before != after)
                {
                    changes.Add($"{name}: {before} -> {after}");
                }
            }
        }
    }

    /// <summary>True when nothing about the definition actually moved.</summary>
    public bool IsEmpty =>
        !IsCreation &&
        PartsAdded.Count == 0 &&
        PartsRemoved.Count == 0 &&
        FieldsAdded.Count == 0 &&
        FieldsRemoved.Count == 0 &&
        SettingsChanged.Count == 0;

    private static IReadOnlyList<string> Describe(IReadOnlyList<ContentFieldSnapshot>? fields) =>
        fields is null
            ? []
            : [.. fields.Select(field => $"{field.PartName}.{field.Name} ({field.FieldType})")];

    private static IReadOnlyList<string> Difference(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null
            ? []
            : [.. left.Except(right ?? [], StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
}
