using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Internal.Lookups;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Every document a dated list query returns keeps its own nested values, and no two documents
/// ever share the same nested object. See ADR-0007: <c>DimensionTypeDocument.Name</c> and
/// <c>StructureDocument.Name</c> both defaulted to the single shared <c>BilingualText.Empty</c>
/// instance, so after enough types or structures existed in a tenant, every one of them read back
/// whichever had been created most recently — scalars such as <c>Code</c> stayed correct, because
/// only the nested <c>Name</c> object was shared.
/// </summary>
/// <remarks>
/// Two separate shell scopes per document, deliberately, because the defect only showed up across
/// scopes: a single scope creating several documents and listing them in the same session never
/// reproduced it. Each creation here runs in its own <see cref="BaseTenantFixture.InTenantAsync"/>
/// call for exactly that reason.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DocumentIdentityTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DocumentIdentityTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    [Fact]
    public async Task ListedDimensionTypesEachKeepTheirOwnNameAndNoneShareAnInstance()
    {
        var names = new (string Code, string En, string Ar)[]
        {
            ("identity-type-a", "Identity A", "هوية أ"),
            ("identity-type-b", "Identity B", "هوية ب"),
            ("identity-type-c", "Identity C", "هوية ج"),
        };

        foreach (var (code, en, ar) in names)
        {
            await InTenantAsSystemAsync(async services =>
            {
                var types = services.GetRequiredService<IDimensionTypeService>();
                var created = await types.CreateAsync(code, new BilingualText(en, ar), [], allowsSelfNesting: false);

                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
            });
        }

        await _tenant.InTenantAsync(async services =>
        {
            var lookup = services.GetRequiredService<DimensionTypeLookup>();
            var documents = (await lookup.ListAsync())
                .Where(document => names.Select(n => n.Code).Contains(document.Code))
                .ToList();

            documents.Should().HaveCount(names.Length);

            foreach (var (code, en, ar) in names)
            {
                var document = documents.Single(d => d.Code == code);
                document.Name.En.Should().Be(en, "each document must keep the name it was created with");
                document.Name.Ar.Should().Be(ar);
            }

            for (var i = 0; i < documents.Count; i++)
            {
                for (var j = i + 1; j < documents.Count; j++)
                {
                    ReferenceEquals(documents[i].Name, documents[j].Name).Should().BeFalse(
                        $"'{documents[i].Code}' and '{documents[j].Code}' must not share a Name instance");
                }
            }
        });
    }

    [Fact]
    public async Task ListedStructuresEachKeepTheirOwnNameAndNoneShareAnInstance()
    {
        var names = new (string Code, string En, string Ar)[]
        {
            ("identity-structure-a", "Structure A", "هيكل أ"),
            ("identity-structure-b", "Structure B", "هيكل ب"),
        };

        var typeId = await InTenantAsSystemReturningAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var created = await types.CreateAsync(
                "identity-structure-level", new BilingualText("Level", "مستوى"), [], allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

            return created.Value!.DimensionTypeId;
        });

        foreach (var (code, en, ar) in names)
        {
            await InTenantAsSystemAsync(async services =>
            {
                var structures = services.GetRequiredService<IStructureService>();
                var created = await structures.CreateAsync(
                    code, new BilingualText(en, ar), [typeId], allowSkipLevel: false, isStrict: true, isPrimaryOrganisation: false);

                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
            });
        }

        await _tenant.InTenantAsync(async services =>
        {
            var lookup = services.GetRequiredService<StructureLookup>();
            var documents = (await lookup.ListAsync())
                .Where(document => names.Select(n => n.Code).Contains(document.Code))
                .ToList();

            documents.Should().HaveCount(names.Length);

            foreach (var (code, en, ar) in names)
            {
                var document = documents.Single(d => d.Code == code);
                document.Name.En.Should().Be(en);
                document.Name.Ar.Should().Be(ar);
            }

            ReferenceEquals(documents[0].Name, documents[1].Name).Should().BeFalse(
                "two different structures must not share a Name instance");
        });
    }

    private async Task<T> InTenantAsSystemReturningAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        var result = default(T)!;

        await InTenantAsSystemAsync(async services =>
        {
            result = await work(services);
        });

        return result;
    }
}
