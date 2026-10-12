using Microsoft.Extensions.Localization;
using OrchardCore.Modules;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// Loading the lookup lists WorkMate ships, from the screen that needs them.
/// </summary>
/// <remarks>
/// <b>Why a button exists at all.</b> A tenant created before slice B2 has the bindings and none of
/// the lists, so every bound field renders a free-text box until somebody applies
/// <c>WorkMate standard lookup lists</c>. Until now the only way to do that was Configuration →
/// Recipes, which is an operator's screen: it lists every recipe in the installation, it warns that
/// the tenant will be reloaded, and it is three clicks away from anything an HR administrator has
/// any business touching. Asking them to go there to finish configuring their own forms is asking
/// them to use a tool built for somebody else.
///
/// <b>Why it runs the recipe rather than creating the lists itself.</b> The entries are a hundred
/// and sixty codes and three hundred and twenty names in two languages. A second copy of them in C#
/// would be a second thing to keep in step with
/// <c>Recipes/standard-lookup-lists.recipe.json</c> — and the first time they disagreed, a tenant
/// seeded by the button and a tenant seeded by the recipe would hold different lists under the same
/// codes, which nothing would report. So there is one file, and both paths run it.
///
/// That also means the button inherits everything the recipe step already guarantees: it is safe to
/// press twice (ADR-0008's comparison rules), it fills in entries a later version adds, it leaves
/// the customer's own entries alone, and it writes through <c>ILookupListService</c> so every list
/// is validated and audited exactly as a hand-typed one is.
/// </remarks>
public interface IStandardLookupListsLoader
{
    /// <summary>
    /// The codes in <see cref="StandardLookupLists.All"/> that this tenant does not hold.
    /// </summary>
    /// <remarks>
    /// What the Forms screen shows the button for. A tenant holding all ten is not offered it,
    /// because a button that does nothing visible is a button that teaches people to doubt the
    /// screen.
    /// </remarks>
    Task<IReadOnlyList<string>> MissingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the standard lists recipe, and reports which lists the tenant was missing when it
    /// started.
    /// </summary>
    Task<RecordResult<IReadOnlyList<string>>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class StandardLookupListsLoader : IStandardLookupListsLoader
{
    /// <summary>The recipe's name, as <c>standard-lookup-lists.recipe.json</c> declares it.</summary>
    /// <remarks>
    /// The same name <c>recipes/base.recipe.json</c>'s <c>recipes</c> step uses to apply it at
    /// setup, so a new tenant and an upgraded one are seeded from one file by one name.
    /// </remarks>
    public const string RecipeName = "WorkMate.StandardLookupLists";

    private readonly ILookupListService _lookupLists;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly IEntitlementService _entitlement;
    private readonly IEnumerable<IRecipeHarvester> _harvesters;
    private readonly IRecipeExecutor _recipeExecutor;
    private readonly IEnumerable<IRecipeEnvironmentProvider> _environmentProviders;
    private readonly IStringLocalizer S;

    public StandardLookupListsLoader(
        ILookupListService lookupLists,
        IRecordsAuthorisation authorisation,
        IEntitlementService entitlement,
        IEnumerable<IRecipeHarvester> harvesters,
        IRecipeExecutor recipeExecutor,
        IEnumerable<IRecipeEnvironmentProvider> environmentProviders,
        IStringLocalizer<StandardLookupListsLoader> stringLocalizer)
    {
        _lookupLists = lookupLists;
        _authorisation = authorisation;
        _entitlement = entitlement;
        _harvesters = harvesters;
        _recipeExecutor = recipeExecutor;
        _environmentProviders = environmentProviders;
        S = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> MissingAsync(CancellationToken cancellationToken = default)
    {
        var held = (await _lookupLists.ListAsync(cancellationToken))
            .Select(list => list.Code)
            .ToHashSet(LookupCodes.Comparer);

        return [.. StandardLookupLists.All.Where(code => !held.Contains(code))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>Permission and entitlement are checked here, before anything else</b>, and that is not
    /// belt and braces over the recipe step's own checks — it is the only check that applies. The
    /// step runs under <see cref="ISystemOperation"/>, so by the time
    /// <c>ILookupListService.CreateAsync</c> asks, the answer is yes because the platform is acting.
    /// The question "may this person seed this tenant" can only be asked out here, while the caller
    /// is still a person. CLAUDE.md rule 5.
    /// </remarks>
    public async Task<RecordResult<IReadOnlyList<string>>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageLookupLists) ||
            !await _entitlement.IsAllowedAsync(Capabilities.FormFieldConfiguration, cancellationToken))
        {
            return RecordResult.NotAuthorised<IReadOnlyList<string>>();
        }

        var missing = await MissingAsync(cancellationToken);

        var recipe = await FindTheRecipeAsync();

        if (recipe is null)
        {
            return RecordResult.Failed<IReadOnlyList<string>>(new RecordError(
                RecordRule.StandardListsUnavailable,
                RecipeName,
                S["The standard lookup lists cannot be loaded, because the recipe that carries them is not available to this tenant. An administrator of the installation can apply it from Configuration, under Recipes."]));
        }

        var environment = new Dictionary<string, object>();

        foreach (var provider in _environmentProviders.OrderBy(provider => provider.Order))
        {
            await provider.PopulateEnvironmentAsync(environment);
        }

        try
        {
            await _recipeExecutor.ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                recipe,
                environment,
                cancellationToken);
        }
        catch (RecipeExecutionException failure)
        {
            // The step's own messages, which name the list and what differs, rather than a sentence
            // of this method's own invention. A list that the tenant has edited into a shape the
            // file disagrees with is the case that gets here, and ADR-0008 is deliberate that the
            // operator is told which one and told to resolve it themselves.
            return RecordResult.Failed<IReadOnlyList<string>>(
                [.. (failure.StepResult?.Errors ?? []).Select(error => new RecordError(
                    RecordRule.StandardListsUnavailable,
                    RecipeName,
                    new LocalizedString(error, error)))]);
        }

        return RecordResult.Success(missing);
    }

    /// <summary>
    /// The standard lists recipe, from whichever harvester offers it, or null.
    /// </summary>
    /// <remarks>
    /// By name rather than by path, because the path depends on how the module is deployed and the
    /// name is what the file declares. Every harvester is asked, so the recipe is still found if it
    /// is ever moved from this module's own <c>Recipes</c> folder into the host's.
    /// </remarks>
    private async Task<RecipeDescriptor?> FindTheRecipeAsync()
    {
        foreach (var harvester in _harvesters)
        {
            var recipe = (await harvester.HarvestRecipesAsync())
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, RecipeName, StringComparison.OrdinalIgnoreCase));

            if (recipe is not null)
            {
                return recipe;
            }
        }

        return null;
    }
}
