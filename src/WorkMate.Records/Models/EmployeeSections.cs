namespace WorkMate.Records.Models;

/// <summary>
/// The six standard sections specification section 5 names, and the content types that carry them.
/// </summary>
/// <remarks>
/// <b>Why a <c>BagPart</c> of typed items rather than a set of fields on the employee.</b> Every one
/// of these is a list: an employee has several documents, several qualifications, more than one
/// dependant, two bank accounts when their salary is split, and a job history rather than a job.
/// Flattening a list onto the record means either one row only — which is wrong for all six — or
/// numbered fields, which is a list written out longhand and cannot be sorted, filtered or expired.
/// Documents in particular feed the expiry alerts, and an alert needs a row per document with its
/// own date.
///
/// Verified against the pinned Orchard Core 3.0.1: <c>BagPart</c> lives in
/// <c>OrchardCore.Flows.Core</c> and carries <c>List&lt;ContentItem&gt; ContentItems</c>;
/// <c>BagPartSettings.ContainedContentTypes</c> is what restricts a bag to one kind of item. The
/// <c>OrchardCore.Flows</c> feature is enabled by the base recipe for this.
///
/// <b>These are the standard sections, not the only ones.</b> A tenant adds its own through the form
/// designer in session B, which produces more content types the same way. What it may never do is
/// touch the fixed core — see <c>EmployeeFieldNames</c>.
///
/// <b>Why none of these types is creatable on its own.</b> A bank account with no employee is not a
/// thing, and a listable one would appear in the content picker of every form in the tenant. They
/// carry a stereotype instead, which is how the bag's own editor finds them.
/// </remarks>
public static class EmployeeSections
{
    /// <summary>The stereotype every section item type carries.</summary>
    /// <remarks>
    /// So that a picker, a report or session B's designer can ask for "an employee section" without
    /// a hard-coded list of six type names that would be wrong the moment a tenant adds a seventh.
    /// </remarks>
    public const string Stereotype = "EmployeeSection";

    /// <summary>Grade, job title, contract and probation. Dated, because all of them change.</summary>
    public const string JobDetails = "JobDetails";

    /// <summary>The item type the job details bag contains.</summary>
    public const string JobDetailType = "EmployeeJobDetail";

    /// <summary>Identity and right-to-work documents, each with its own expiry.</summary>
    public const string Documents = "Documents";

    /// <inheritdoc cref="Documents"/>
    public const string DocumentType = "EmployeeDocument";

    /// <summary>Where salary is paid, including a split across accounts.</summary>
    public const string BankDetails = "BankDetails";

    /// <inheritdoc cref="BankDetails"/>
    public const string BankAccountType = "EmployeeBankAccount";

    /// <summary>Education and professional qualifications.</summary>
    public const string Qualifications = "Qualifications";

    /// <inheritdoc cref="Qualifications"/>
    public const string QualificationType = "EmployeeQualification";

    /// <summary>Family members, which drive sponsorship, air tickets and medical cover.</summary>
    public const string Dependants = "Dependants";

    /// <inheritdoc cref="Dependants"/>
    public const string DependantType = "EmployeeDependant";

    /// <summary>Addresses, telephone numbers and the emergency contact.</summary>
    public const string Contacts = "Contacts";

    /// <inheritdoc cref="Contacts"/>
    public const string ContactType = "EmployeeContact";

    /// <summary>Every section, in the order the editor shows them.</summary>
    /// <remarks>
    /// Job details first because it is what somebody opening an employee record is most often
    /// looking for, and dependants and contacts last because they are the ones consulted least
    /// often. Documents second because of expiry: a document about to lapse is the one thing on this
    /// record with a deadline attached.
    /// </remarks>
    public static readonly IReadOnlyList<(string Section, string ItemType)> All =
    [
        (JobDetails, JobDetailType),
        (Documents, DocumentType),
        (BankDetails, BankAccountType),
        (Qualifications, QualificationType),
        (Dependants, DependantType),
        (Contacts, ContactType),
    ];
}
