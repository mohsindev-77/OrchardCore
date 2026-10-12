using FluentAssertions;
using WorkMate.Records.Internal;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// Every cache key and every signal name carries the tenant.
/// </summary>
/// <remarks>
/// <b>Specification section 9 states the rule — "every cache key begins with the tenant name" — and
/// this is the thing that would break it silently.</b> <c>IMemoryCache</c> in this host is a
/// singleton shared across every tenant in the process: Orchard Core does not re-register it per
/// shell. So a key built without the tenant is not a key that fails; it is a key two customers
/// share. Two tenants whose grade lists both have the code <c>grade</c> would see each other's
/// entries, and the only symptom would be somebody noticing a grade on a screen that should not
/// have it.
///
/// A unit test rather than a tenant one, deliberately, which is the same judgement
/// <c>DimensionCacheKeysTests</c> makes: key construction is pure string work, and a test that
/// needed a shell to assert it would be slower, flakier, and no more convincing.
///
/// Reflection over the class rather than one assertion per key, so a key added later is covered on
/// the day it is written — the failure mode is somebody adding the eleventh key and forgetting the
/// eleventh assertion.
/// </remarks>
public sealed class RecordsCacheKeyTests
{
    private const string Tenant = "acme-prod";
    private const string Other = "globex-prod";

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();

        foreach (var key in KeysFor(Tenant))
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryKeyNamesTheTenant(string key) =>
        key.Should().Contain(Tenant, "a key without the tenant is a key two customers share");

    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryKeyNamesTheModule(string key) =>
        key.Should().StartWith(
            "WorkMate.Records",
            "the cache is shared with every other module in the process as well as with every other tenant");

    [Fact]
    public void NoTwoTenantsShareAKey()
    {
        var mine = KeysFor(Tenant);
        var theirs = KeysFor(Other);

        mine.Should().NotBeEmpty();
        mine.Should().OnlyHaveUniqueItems("two of this tenant's own reads must not collide either");

        mine.Should().NotIntersectWith(theirs);
    }

    /// <summary>
    /// Every key this class can produce, for one tenant.
    /// </summary>
    /// <remarks>
    /// By reflection so that a key added to <see cref="RecordsCacheKeys"/> without a line here is
    /// still asserted. Only methods taking a tenant and strings are callable this way, which is
    /// every one of them — a key that did not take the tenant would not compile into this shape and
    /// would be caught by its absence from the list.
    /// </remarks>
    private static List<string> KeysFor(string tenant) =>
    [
        .. typeof(RecordsCacheKeys)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(method =>
                method.ReturnType == typeof(string) &&
                method.GetParameters().All(parameter => parameter.ParameterType == typeof(string)) &&
                method.GetParameters().Length > 0)
            .Select(method => (string)method.Invoke(
                null,
                [.. method.GetParameters().Select((_, index) => index == 0 ? tenant : "sample")])!),
    ];

    /// <summary>
    /// The signal and the keys it invalidates are the same shape, so one write clears all of them.
    /// </summary>
    /// <remarks>
    /// Not a formality. The cached lookup tags every entry with the signal's change token and the
    /// service signals that token after every write; if the two were built from different tenant
    /// strings the cache would never be invalidated and a retired entry would go on being offered
    /// until the process restarted.
    /// </remarks>
    [Fact]
    public void TheSignalAndTheKeysAgreeAboutTheTenant()
    {
        var signal = RecordsCacheKeys.LookupListsSignal(Tenant);

        signal.Should().Contain(Tenant);

        RecordsCacheKeys.LookupListByCode(Tenant, "grade").Should().Contain(Tenant);
        RecordsCacheKeys.LookupListList(Tenant).Should().Contain(Tenant);
    }
}
