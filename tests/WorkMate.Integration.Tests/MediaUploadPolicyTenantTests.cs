using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrchardCore.Media;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// What a WorkMate tenant accepts as an upload, and why that list is stated rather than defaulted.
/// </summary>
/// <remarks>
/// <b>This is a security control, recorded in ADR-0013.</b> <c>OrchardCore.Media</c> resizes
/// user-uploaded images through SixLabors.ImageSharp, which currently carries five open advisories —
/// four of them TIFF-only. An upload refused never reaches the decoder, so the allowed extension
/// list is what puts those four out of reach.
///
/// It is asserted rather than assumed because ADR-0013 originally could not say whether Orchard's
/// default even contained TIFF. It does not — measured here — and that is exactly why the list is
/// now stated explicitly: a default nobody controls is not a control, and a future Orchard release
/// widening it would silently undo this.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class MediaUploadPolicyTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public MediaUploadPolicyTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    public async Task TiffIsNotAnAllowedExtension(string extension) =>
        await _fixture.InTenantAsync(services =>
        {
            Allowed(services).Should().NotContain(
                extension,
                "four of the five open ImageSharp advisories are TIFF-only, and an upload that is "
                + "refused never reaches the decoder");

            return Task.CompletedTask;
        });

    /// <summary>
    /// The formats an employee photograph and a scanned document actually need are kept.
    /// </summary>
    /// <remarks>
    /// An allow-list that refused the product's own use cases would be removed by the first
    /// customer who hit it, which is a worse outcome than not having one.
    /// </remarks>
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    public async Task TheFormatsThisProductNeedsAreAllowed(string extension) =>
        await _fixture.InTenantAsync(services =>
        {
            Allowed(services).Should().Contain(extension);

            return Task.CompletedTask;
        });

    /// <summary>
    /// SVG is refused, which is a second security control riding along with the first.
    /// </summary>
    /// <remarks>
    /// Orchard's default permits it. An SVG is a document that can carry script, served from the
    /// tenant's own origin — so an allow-list drawn up for one reason is worth drawing up properly.
    /// </remarks>
    [Fact]
    public async Task ScriptableAndIrrelevantFormatsAreRefused() =>
        await _fixture.InTenantAsync(services =>
        {
            Allowed(services).Should().NotContain([".svg", ".ico", ".psd", ".mp4", ".mp3"]);

            return Task.CompletedTask;
        });

    /// <summary>
    /// The policy is enforced on the real upload endpoint, not only in the options object.
    /// </summary>
    /// <remarks>
    /// The assertions above pin configuration; this pins behaviour. A list that were configured and
    /// not consulted would pass every one of them, and the TIFF mitigation would be a comment.
    ///
    /// The payload is a real, if minimal, TIFF header — <c>II*\0</c>, little-endian — so the refusal
    /// cannot be attributed to the bytes being unrecognisable rather than to the extension being
    /// refused.
    /// </remarks>
    [Fact]
    public async Task ARealUploadOfATiffIsRefusedAndAPngIsAccepted()
    {
        var tiff = await UploadAsync("employee-scan.tif", [0x49, 0x49, 0x2A, 0x00], "image/tiff");
        var png = await UploadAsync("employee-photo.png", PngBytes, "image/png");

        // Orchard's upload endpoint answers 200 with a per-file result rather than a failing
        // status, so the body is what distinguishes the two. Both halves are asserted: a test that
        // only checked the refusal would pass just as well against an endpoint that refused
        // everything, including the formats this product needs.
        tiff.Should().Contain("error", "a .tif is not an allowed extension on this tenant");
        tiff.Should().NotContain("\"url\"", "and nothing was stored");

        png.Should().NotContain("error");
        png.Should().Contain("employee-photo.png", "the formats an employee photograph needs still upload");
    }

    /// <summary>
    /// Posts a file to the real media endpoint, with the antiforgery token the page issues.
    /// </summary>
    /// <remarks>
    /// The token is not optional and its absence is not visible: without it the endpoint answers
    /// with an empty body and nothing else, which reads exactly like a refusal — so a test that
    /// skipped it would "prove" the TIFF mitigation works while proving only that the request was
    /// rejected before anything looked at the file.
    /// </remarks>
    private async Task<string> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        var page = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Media");

        using var content = new MultipartFormDataContent
        {
            { new StringContent(BaseTenantFixture.AntiforgeryTokenIn(page)), "__RequestVerificationToken" },
        };

        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "files", fileName);

        var response = await _fixture.Administrator.PostAsync("/Admin/Media/Upload?path=", content);

        response.IsSuccessStatusCode.Should().BeTrue(
            "the endpoint reports a refused file in its body, so a failing status here means the "
            + "request never reached the policy at all");

        return await response.Content.ReadAsStringAsync();
    }

    private static HashSet<string> Allowed(IServiceProvider services) =>
        services.GetRequiredService<IOptions<MediaOptions>>().Value.AllowedFileExtensions;

    /// <summary>The smallest valid PNG: an 8-bit, 1×1, fully transparent image.</summary>
    private static byte[] PngBytes =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
}
