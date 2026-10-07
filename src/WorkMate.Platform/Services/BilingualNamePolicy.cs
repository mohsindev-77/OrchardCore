namespace WorkMate.Platform.Services;

/// <inheritdoc />
internal sealed class BilingualNamePolicy : IBilingualNamePolicy
{
    private readonly IWorkMateSettingsService _settings;

    public BilingualNamePolicy(IWorkMateSettingsService settings) => _settings = settings;

    /// <inheritdoc />
    public async Task<bool> RequiresArabicAsync(CancellationToken cancellationToken = default) =>
        (await _settings.GetAsync(cancellationToken)).RequireArabicNames;
}
