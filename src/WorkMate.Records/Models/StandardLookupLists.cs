namespace WorkMate.Records.Models;

/// <summary>
/// The lookup lists WorkMate ships, and which fields are bound to them.
/// </summary>
/// <remarks>
/// <b>Declared, and then used by three things that must agree:</b> the migration that binds the
/// fields, the recipe that ships the entries, and the reconciliation screen that has to know where
/// a list's values are stored. Generating any one of them from another was the alternative and is
/// worse — the migration binds by content-definition path, the recipe names lists by code, and the
/// reconciliation screen reads the live content definitions, so the three are different views of
/// one decision and the decision is here.
///
/// <b>The codes are the free-text fields the six standard sections already define.</b>
/// <c>Migrations.CreateSectionTypesAsync</c> declared <c>GradeCode</c>, <c>EmploymentTypeCode</c>,
/// <c>DocumentTypeCode</c>, <c>RelationshipCode</c>, <c>ContactTypeCode</c>, <c>CurrencyCode</c>,
/// <c>CountryCode</c>, <c>WorkLocation</c> and <c>DependantNationalityCode</c> as plain text, which
/// is exactly the problem managed lists exist to fix: three people type "Full Time", "Full time"
/// and "FT" and every report that groups by employment type is quietly wrong.
///
/// <b><c>JobTitle</c> is deliberately not here.</b> It is a <c>BilingualTextField</c>, not a text
/// field, so binding it would mean a control that posts two halves from one choice — which cannot
/// be done without script, or without resolving a posted code back to a pair of names in a driver
/// of our own. The <c>job-title</c> list still ships, because a tenant wants it either way and
/// because the decision about how a job title is stored is bound up with the undecided positions
/// model. See the module README.
/// </remarks>
public static class StandardLookupLists
{
    /// <summary>Job titles. Shipped, and deliberately not bound to anything yet.</summary>
    public const string JobTitle = "job-title";

    public const string Grade = "grade";

    public const string EmploymentType = "employment-type";

    public const string WorkLocation = "work-location";

    public const string DocumentType = "document-type";

    /// <summary>
    /// Nationalities. Bound to the employee core's <c>NationalityCode</c> and to a dependant's.
    /// </summary>
    /// <remarks>
    /// The core is not editable through the form designer and this does not make it so: the core
    /// already stores a <em>code</em>, so binding a list changes which control edits it and nothing
    /// about what it holds. <c>EmployeeFieldNames.Reserved</c> is untouched and
    /// <c>EmployeeContentDefinitionGuard</c> still refuses every attempt to define a field with
    /// that name.
    /// </remarks>
    public const string Nationality = "nationality";

    public const string Country = "country";

    public const string Currency = "currency";

    public const string Relationship = "relationship";

    public const string ContactType = "contact-type";

    /// <summary>One content field bound to one list.</summary>
    /// <param name="PartName">
    /// The part carrying the field, which for a section is named for its item content type —
    /// <c>EmployeeJobDetail</c>, <c>EmployeeDocument</c>, and so on.
    /// </param>
    /// <param name="FieldName">The field on that part.</param>
    /// <param name="ListCode">The list it offers.</param>
    public sealed record Binding(string PartName, string FieldName, string ListCode);

    /// <summary>
    /// Every content field this module binds, applied by the migration and read by the
    /// reconciliation screen.
    /// </summary>
    /// <remarks>
    /// The employee core's <c>NationalityCode</c> is <b>not</b> in this list, because it is not a
    /// content field — it is a property on <c>EmployeePart</c>, edited by this module's own form.
    /// It is bound by the view rather than by the content definition, and the reconciliation screen
    /// names it separately. Putting it here would mean the migration tried to define a field with a
    /// reserved name, which <c>EmployeeContentDefinitionGuard</c> would refuse at tenant setup —
    /// correctly.
    /// </remarks>
    public static readonly IReadOnlyList<Binding> Bindings =
    [
        new(EmployeeSections.JobDetailType, "GradeCode", Grade),
        new(EmployeeSections.JobDetailType, "EmploymentTypeCode", EmploymentType),
        new(EmployeeSections.JobDetailType, "WorkLocation", WorkLocation),
        new(EmployeeSections.DocumentType, "DocumentTypeCode", DocumentType),
        new(EmployeeSections.BankAccountType, "CurrencyCode", Currency),
        new(EmployeeSections.QualificationType, "CountryCode", Country),
        new(EmployeeSections.DependantType, "RelationshipCode", Relationship),
        new(EmployeeSections.DependantType, "DependantNationalityCode", Nationality),
        new(EmployeeSections.ContactType, "ContactTypeCode", ContactType),
        new(EmployeeSections.ContactType, "CountryCode", Country),
    ];

    /// <summary>
    /// The lists a dimension now answers for. D22, slice F6c.
    /// </summary>
    /// <remarks>
    /// <b>Retired, never removed from <see cref="All"/>.</b> A tenant that has them keeps them and
    /// every code already typed still resolves to a name; what retirement stops is their being
    /// offered for a new binding. <c>job-title</c> is superseded by the Position dimension and
    /// <c>grade</c> by the Grade dimension, which is the whole of D22 steps 5 and 6.
    ///
    /// They are still loaded by the standard-lists action, deliberately: a tenant upgrading into
    /// F6 may have records holding these codes and no lists to resolve them, and refusing to load
    /// them would leave those records showing a bare code for ever.
    /// </remarks>
    public static readonly IReadOnlyList<string> Superseded = [JobTitle, Grade];

    /// <summary>Every list this module ships, by code.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        JobTitle,
        Grade,
        EmploymentType,
        WorkLocation,
        DocumentType,
        Nationality,
        Country,
        Currency,
        Relationship,
        ContactType,
    ];
}
