using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrchardCore.Environment.Shell;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// System authority: the explicit opt-in a recipe step or background task enters when it really
/// does run as the platform.
/// </summary>
/// <remarks>
/// The rule this replaces — treat "no HTTP context" as full authority — was rejected in review
/// because it would exempt every background job from every permission check by default. These
/// tests pin the replacement: authority exists only inside a scope something deliberately
/// entered, and it ends when that scope does.
/// </remarks>
public sealed class SystemOperationTests
{
    private static SystemOperation Subject() => new(
        new ShellSettings { Name = "acme-prod" },
        NullLogger<SystemOperation>.Instance);

    [Fact]
    public void AuthorityIsOffUntilSomethingAsksForIt() =>
        Subject().IsActive.Should().BeFalse(
            "a scope that never opted in is an ordinary scope, whatever else is true of it");

    [Fact]
    public void AuthorityHoldsInsideTheScope()
    {
        var subject = Subject();

        using (subject.Begin("applying the dimension-types recipe step"))
        {
            subject.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public void AuthorityEndsWithTheScope()
    {
        var subject = Subject();

        using (subject.Begin("applying the dimension-types recipe step"))
        {
        }

        subject.IsActive.Should().BeFalse(
            "authority that outlives the operation that claimed it is a standing bypass");
    }

    [Fact]
    public void AuthorityEndsEvenWhenTheOperationThrows()
    {
        var subject = Subject();

        try
        {
            using (subject.Begin("a recipe step that fails"))
            {
                throw new InvalidOperationException("the step failed");
            }
        }
        catch (InvalidOperationException)
        {
        }

        subject.IsActive.Should().BeFalse(
            "a failed recipe step must not leave the scope running as the platform");
    }

    [Fact]
    public void NestedScopesDoNotDropAuthorityEarly()
    {
        var subject = Subject();

        using (subject.Begin("outer: applying a recipe"))
        {
            using (subject.Begin("inner: a step within it"))
            {
                subject.IsActive.Should().BeTrue();
            }

            subject.IsActive.Should().BeTrue(
                "the outer operation is still running; only the step it called finished");
        }

        subject.IsActive.Should().BeFalse();
    }

    [Fact]
    public void DisposingOutOfOrderStillEndsAuthority()
    {
        // Not expected, but the failure mode matters: if disposal order were trusted blindly, a
        // stray handle could hold authority open for the rest of the scope.
        var subject = Subject();

        var outer = subject.Begin("outer");
        _ = subject.Begin("inner that is never disposed");

        outer.Dispose();

        subject.IsActive.Should().BeFalse(
            "everything opened inside the outer operation ends with it");
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var subject = Subject();

        var handle = subject.Begin("a step");
        handle.Dispose();
        handle.Dispose();

        subject.IsActive.Should().BeFalse();
    }

    [Fact]
    public void AReasonIsRequired()
    {
        var subject = Subject();

        // The reason is what makes "what ran as the platform, and when" answerable from the
        // logs. An empty one makes the log line useless at exactly the moment it is needed.
        subject.Invoking(s => s.Begin(string.Empty)).Should().Throw<ArgumentException>();
        subject.Invoking(s => s.Begin("   ")).Should().Throw<ArgumentException>();
        subject.Invoking(s => s.Begin(null!)).Should().Throw<ArgumentException>();
    }
}
