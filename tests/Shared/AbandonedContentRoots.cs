namespace WorkMate.Tests.Shared;

/// <summary>
/// Removes the temporary content roots earlier test runs left behind — <b>once per test
/// assembly</b>, however many fixtures ask.
/// </summary>
/// <remarks>
/// <b>What this is for.</b> Cleanup at the end of a run is best effort and sometimes loses: SQLite
/// can still hold a tenant's database file when the fixture is disposed, and a killed run never
/// gets to try. Sweeping at the start, once the files are long since released, keeps a developer's
/// machine and a build agent from accumulating a tenant per run for ever.
///
/// <b>Why once per assembly, which is the whole point of this type.</b> It used to be called from
/// the fixture's constructor, and the fixtures here are not all collection fixtures — most test
/// classes new one up per class. So a run swept ~170 times, and each sweep enumerated every
/// directory left by every run before it. The cost is quadratic in the number of leftovers: a
/// machine with several hundred of them spent minutes of every suite doing nothing but stat-ing
/// the same folders again. Measured at 698 leftover roots on the machine this was written on.
///
/// <b>A static, not an xunit assembly fixture.</b> xunit v2 has no assembly-level fixture, and the
/// guarantee wanted here is exactly the one a static initialiser gives: once per process, before
/// anybody uses the thing it guards, thread-safe without a lock of our own.
///
/// <b>Only roots older than an hour are touched</b>, so a run can never delete a tenant belonging
/// to a run happening beside it.
/// </remarks>
internal static class AbandonedContentRoots
{
    /// <summary>How old a leftover root must be before a run will delete it.</summary>
    private static readonly TimeSpan OldEnough = TimeSpan.FromHours(1);

    /// <summary>
    /// How many leftover roots one run will delete, before it gets on with the testing.
    /// </summary>
    /// <remarks>
    /// <b>Measured, not guessed.</b> The first run after the browser suite started sweeping found
    /// 470 accumulated tenants — each a directory with a SQLite database in it — and deleting them
    /// all took <b>forty-five minutes</b>, during which every other collection sat waiting on the
    /// one-shot below and two tests failed on timeouts that had nothing to do with them. A 3-minute
    /// suite reported 49.
    ///
    /// So the backlog is cleared over several runs rather than all at once. A run deletes a slice
    /// and moves on; the oldest go first, so nothing is left behind for ever, and a machine that
    /// has only a few to clear pays what they cost and no more.
    /// </remarks>
    private const int MostToDeletePerRun = 100;

    private static readonly Dictionary<string, Lazy<bool>> Swept = new(StringComparer.Ordinal);

    /// <summary>
    /// Sweeps the folder this assembly's fixtures create their content roots under, the first time
    /// it is asked and never again in this process.
    /// </summary>
    /// <param name="folderName">
    /// The folder under the temp directory — <c>workmate-integration</c>, <c>workmate-browser</c>.
    /// Each assembly owns its own, so one suite never sweeps another's.
    /// </param>
    public static void SweepOnce(string folderName)
    {
        Lazy<bool> once;

        lock (Swept)
        {
            if (!Swept.TryGetValue(folderName, out once!))
            {
                once = new Lazy<bool>(
                    () => Sweep(folderName),
                    LazyThreadSafetyMode.ExecutionAndPublication);

                Swept[folderName] = once;
            }
        }

        _ = once.Value;
    }

    private static bool Sweep(string folderName)
    {
        var parent = Path.Combine(Path.GetTempPath(), folderName);

        if (!Directory.Exists(parent))
        {
            return true;
        }

        var cutoff = DateTime.UtcNow - OldEnough;

        // Oldest first, so a capped run always makes progress on the backlog rather than
        // re-examining whichever ones the file system happened to list first.
        var stale = Directory.EnumerateDirectories(parent)
            .Select(path => (Path: path, Created: SafeCreationTimeUtc(path)))
            .Where(candidate => candidate.Created < cutoff)
            .OrderBy(candidate => candidate.Created)
            .Take(MostToDeletePerRun)
            .ToList();

        foreach (var candidate in stale)
        {
            try
            {
                Directory.Delete(candidate.Path, recursive: true);
            }
            catch (IOException)
            {
                // Still held by a process that has not finished letting go. Next run gets it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return true;
    }

    /// <summary>
    /// When the root was created, or <see cref="DateTime.MaxValue"/> if that cannot be read —
    /// which sorts it last and leaves it alone, the safe answer for a directory in an odd state.
    /// </summary>
    private static DateTime SafeCreationTimeUtc(string path)
    {
        try
        {
            return Directory.GetCreationTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MaxValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MaxValue;
        }
    }
}
