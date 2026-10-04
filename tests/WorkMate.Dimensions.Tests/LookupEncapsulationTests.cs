using FluentAssertions;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The three read-only lookups the module README's lookup pattern describes stay internal.
/// </summary>
/// <remarks>
/// Each bypasses something a public read path checks — permissions, retirement, effective
/// dating — because that is exactly what lets <see cref="IDimensionValidator"/> and
/// <see cref="IDimensionGraphService"/> answer "does this reference exist" without depending on
/// the full write service that would cycle back through them. That shortcut is only safe while
/// nothing outside this module can take it. A test pins it rather than leaving it to review,
/// because a later change that widens one of these back to <c>public</c> would compile cleanly
/// and look like a harmless convenience.
/// </remarks>
public sealed class LookupEncapsulationTests
{
    [Fact]
    public void TheDimensionTypeLookupIsInternal() =>
        typeof(IDimensionTypeLookup).IsPublic.Should().BeFalse(
            "it bypasses retirement, and a type that is retired must still look retired to everything outside this module");

    [Fact]
    public void TheStructureLookupIsInternal() =>
        typeof(IStructureLookup).IsPublic.Should().BeFalse(
            "it bypasses permissions, and nothing outside this module should read a structure without that check");

    [Fact]
    public void TheDimensionRecordLookupIsInternal() =>
        typeof(IDimensionRecordLookup).IsPublic.Should().BeFalse(
            "it is undated, and an undated read path reaching outside this module is how a historical "
            + "question quietly starts returning a present-day answer");
}
