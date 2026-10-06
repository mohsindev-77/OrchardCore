using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// A test that needs real tenant data to reproduce against, and reports itself as skipped — with
/// the reason — when nobody has offered any.
/// </summary>
/// <remarks>
/// These tests are opt-in by <c>WORKMATE_REAL_APPDATA</c>. On a build agent, on a clean clone and
/// on any machine but the one a defect was reported from, there is no such data, and a suite that
/// failed there would be reporting on the absence of a developer's folder rather than on the
/// product.
///
/// Reported as skipped rather than quietly returning from the test body: a test that passes
/// without asserting anything looks, in a run summary, exactly like a test that checked something.
/// xUnit v2 has no runtime <c>Assert.Skip</c>, so the decision is made at discovery by overriding
/// <see cref="FactAttribute.Skip"/>, which is also why it can only consider the environment —
/// whether the named tenant actually holds the demo organisation is not knowable until a host is
/// running, and <see cref="RealDataTenantFixture.SkipReason"/> covers that case.
/// </remarks>
public sealed class RealDataFactAttribute : FactAttribute
{
    public override string? Skip
    {
        get => RealDataTenantFixture.EnvironmentSkipReason ?? base.Skip;
        set => base.Skip = value;
    }
}

/// <inheritdoc cref="RealDataFactAttribute"/>
public sealed class RealDataTheoryAttribute : TheoryAttribute
{
    public override string? Skip
    {
        get => RealDataTenantFixture.EnvironmentSkipReason ?? base.Skip;
        set => base.Skip = value;
    }
}
