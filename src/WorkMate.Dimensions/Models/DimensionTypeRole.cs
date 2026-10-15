namespace WorkMate.Dimensions.Models;

/// <summary>
/// The part a dimension type plays in the product, where the product has behaviour that belongs to
/// one kind of dimension and not to the others.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> Positions and Grades are ordinary dimension types — ADR-0022, "no
/// new aggregate, no new table, no new service" — and almost everything about them is what any
/// dimension gets. Four things are not: a Position carries an approved headcount and a filled count
/// and a set of allowed grades, the employee form's two drop-downs have to know which dimension to
/// offer, the Positions list shows three columns no other list shows, and a Position's value page
/// opens on its reporting line. All four need the product to be able to answer *"which dimension
/// type is the Grade one?"*, and nothing in the model answered it.
///
/// <b>Declared, never derived — which is the whole point.</b> Three derivations were available and
/// each is wrong for a reason this codebase has already paid for once:
///
/// <list type="bullet">
/// <item><description><b>By code.</b> Base ships <c>grade</c> and the Zenith demo ships
/// <c>zenith-grade</c>; a rule of "the code is grade, or ends in -grade" is a guess, and guessing is
/// what produced <em>Branchs</em> (ADR-0024).</description></item>
/// <item><description><b>By name.</b> FRS Rule 6 says renaming a dimension renames it everywhere —
/// so a customer who calls their grades <em>Bands</em> must not thereby lose the grade drop-down.
/// Behaviour keyed off a name is behaviour a rename breaks.</description></item>
/// <item><description><b>By the menu group.</b> D23 B groups types by English singular name, and
/// says in terms that the grouping is <em>"a menu concept only: it is not stored, it owns no
/// records"</em>. Reading a business rule off it would make that sentence false.</description></item>
/// </list>
///
/// So a recipe states it, the Dimension types screen shows it, and a customer who builds their own
/// grade dimension says which it is once. That is the same answer ADR-0010 gave for containment and
/// ADR-0024 gave for the plural.
///
/// <b>Additive and free.</b> A document property, like <c>StructureDocument.ShortName</c>: a type
/// written before it existed deserialises to <see cref="None"/>, which is exactly how every type in
/// every existing tenant should read. No migration, no column.
///
/// <b>The member names are persisted</b> in <c>DimensionTypeDocument.StandardRole</c>, so renaming
/// one is a data migration and not a refactor, and a new member is appended rather than inserted —
/// the contract <see cref="DimensionAttributeKind"/> and <c>DimensionRule</c> both carry.
/// </remarks>
public enum DimensionTypeRole
{
    /// <summary>
    /// An ordinary dimension. Divisions, departments, branches, cost centres — everything the
    /// engine treats alike, which is almost everything.
    /// </summary>
    None = 0,

    /// <summary>
    /// The post an employee holds (D20, D26). Carries an approved headcount, a set of allowed
    /// grades and a reporting line, and is what the employee form's Position drop-down offers.
    /// </summary>
    Position = 1,

    /// <summary>
    /// A level within a position (D26 C). What the employee form's Grade drop-down offers, narrowed
    /// to the grades the chosen position allows. There is no step below it.
    /// </summary>
    Grade = 2,
}
