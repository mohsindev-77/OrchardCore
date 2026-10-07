namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// Comparing a name the tenant holds against the one a recipe states, for ADR-0008's
/// "identical, so skip" rule.
/// </summary>
/// <remarks>
/// Shared by all three steps because the rule has to be the same in all three, and because getting
/// it subtly wrong is silent: a step that reports a difference where there is none does not
/// corrupt anything, it just refuses to re-run, which is exactly what ADR-0008 exists to prevent.
///
/// Two normalisations, both earned rather than defensive.
///
/// <b>Null and empty are the same name.</b> Since the ADR-0003 addendum Arabic may be absent, and
/// it can be absent in two spellings: a step entry defaults to an empty string, but
/// <c>context.Step.ToObject</c> overwrites that with null when the JSON states <c>"nameAr": null</c>
/// explicitly. An ordinal comparison calls those different, so a tenant matching its recipe
/// perfectly would be reported as differing and the re-run would abort.
///
/// <b>Surrounding whitespace is not part of a name.</b> Every write path through the UI trims; the
/// recipe steps did not. A recipe carrying <c>" "</c> stored <c>" "</c>, a later edit through a
/// screen normalised it to <c>""</c>, and the next re-run of the same unchanged recipe then failed.
/// <see cref="WorkMate.Core.BilingualText.Display"/> already treats whitespace as absent; this
/// makes the comparison agree with the display.
/// </remarks>
internal static class RecipeNameComparison
{
    /// <summary>Whether the two spellings mean the same name.</summary>
    public static bool Same(string? tenant, string? recipe) =>
        string.Equals(Normalise(tenant), Normalise(recipe), StringComparison.Ordinal);

    /// <summary>
    /// A difference, phrased so it reads when either side is absent.
    /// </summary>
    /// <remarks>
    /// The interpolated form produced "the Arabic name is '' in the tenant but '' in the recipe" —
    /// two empty quote pairs, which tells an operator nothing about which side is missing and
    /// looks like a bug in the message rather than a difference in the data.
    /// </remarks>
    public static string Describe(string language, string? tenant, string? recipe)
    {
        var inTenant = Normalise(tenant);
        var inRecipe = Normalise(recipe);

        return (inTenant.Length, inRecipe.Length) switch
        {
            (0, _) => $"the {language} name is not set in the tenant but is '{inRecipe}' in the recipe",
            (_, 0) => $"the {language} name is '{inTenant}' in the tenant but is not set in the recipe",
            _ => $"the {language} name is '{inTenant}' in the tenant but '{inRecipe}' in the recipe",
        };
    }

    private static string Normalise(string? value) => value?.Trim() ?? string.Empty;
}
