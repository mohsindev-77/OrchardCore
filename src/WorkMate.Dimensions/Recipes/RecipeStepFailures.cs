using System.Linq;
using OrchardCore.Recipes.Models;

namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// How every recipe step in this module reports a failure: by throwing, which stops this step and
/// the recipe run cold before anything from the failing step was written, with every problem named
/// rather than just the first.
/// </summary>
/// <remarks>
/// Orchard's own <c>RecipeExecutor</c> catches whatever a step throws, replaces it with a new
/// <see cref="RecipeExecutionException"/> carrying only "Unexpected error occurred while executing
/// the '{0}' step." — discarding <see cref="RecipeStepResult.Errors"/> entirely — and re-throws
/// that instead. That generic text is what the admin screen shows for a genuine crash and for a
/// deliberate validation refusal alike; a step cannot make the screen show more than this, because
/// the replacement happens one level up, outside this module.
///
/// The one thing a step controls is what gets logged before that happens: the two-argument
/// <see cref="RecipeExecutionException(Exception, RecipeStepResult)"/> constructor carries an
/// inner exception, and Orchard logs the exception it caught — this one, inner exception included —
/// before replacing it. Every error this module's steps report is therefore also in the inner
/// exception's message, so the file log <c>UseNLogHost</c> enables (see WorkMate.Web's Program.cs)
/// is where "which record, and why" actually shows up.
/// </remarks>
internal static class RecipeStepFailures
{
    public static void Throw(RecipeExecutionContext context, IEnumerable<string> errors)
    {
        var errorList = errors.ToList();

        throw new RecipeExecutionException(
            new InvalidOperationException(string.Join(" ", errorList)),
            new RecipeStepResult
            {
                StepName = context.Name,
                IsCompleted = true,
                IsSuccessful = false,
                Errors = [.. errorList],
            });
    }
}
