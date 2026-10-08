# ADR-0013: Five ImageSharp advisories are suppressed by id, and must be resolved before production

**Status:** accepted, temporary
**Date:** 2026-10-08
**Deciders:** Project lead

## Context

`dotnet restore` began failing across the whole solution. Five advisories against
**`SixLabors.ImageSharp` 3.1.11** were published after this repository last restored, and
`Directory.Build.props` sets `TreatWarningsAsErrors`, so NuGet's audit warnings (NU1902, NU1903)
are build errors here.

ImageSharp is **not referenced by any WorkMate project**. It arrives transitively through
`OrchardCore.Media`, which the base recipe enables and which this product needs: employee
photographs and the attachments on an employee's documents section are media items, and Orchard
resizes them.

Found on 8 October 2026 during prompt 4 session A1, by forcing a restore. An incremental build
audits nothing, so it was invisible on every developer machine and would have surfaced on the next
clean CI run.

### The five advisories

All five verified against the GitHub advisory database on 8 October 2026.

| Advisory | CVE | Severity | What it is |
| --- | --- | --- | --- |
| [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4) | CVE-2026-106113 | High (7.5) | `HistogramEqualization` indexes the histogram from an unvalidated luminance; a 32-bit float TIFF carrying infinities crashes the process |
| [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q) | CVE-2026-106110 | High (7.5) | TIFF CCITT T4 encoder writes past its compressed output buffer on narrow 1-bit images |
| [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w) | CVE-2026-106115 | High (7.5) | TIFF CCITT T6 (Group 4) encoder allocates an undersized buffer and writes beyond it on 1-bit images |
| [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44) | CVE-2026-106114 | Moderate (5.3) | ICC CLUT parsing allocates from unvalidated channel and grid dimensions before checking the data is present |
| [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g) | CVE-2026-106116 | Moderate (5.3) | A BigTIFF entry count can hold a decoder thread in a loop that consumes time without consuming input |

**Every one is an availability failure** — a crash, a buffer overrun that terminates the process, an
unbounded allocation, or a thread held in a non-progressing loop. None is remote code execution and
none discloses data. That bounds the exposure without excusing it: a worker process that dies on an
uploaded file is a denial of service against the tenant, and on a multi-tenant host it is a denial
of service against every tenant sharing that process.

**Four of the five are TIFF.** The fifth (ICC CLUT) applies to any image carrying an ICC profile,
which includes ordinary JPEGs and PNGs.

### Our exposure, after the mitigation below

`OrchardCore.Media` processes **user-uploaded images** — on this product that is employee
photographs and the files attached to an employee's documents section, which is precisely the
untrusted input these advisories concern. The upload path is authenticated and permissioned, so the
attacker has to be someone the tenant has already let in; that lowers the likelihood and does not
remove it, because an HR administrator forwarding a malformed scan is not an attacker and the
process dies just the same.

**Four of the five are now out of reach.** They are TIFF-only, and a WorkMate tenant does not accept
TIFF uploads — see the mitigation below. A file that is refused never reaches the decoder.

**One remains, and it is why this is still blocking.** GHSA-gwg2-r3hj-4w44, the ICC CLUT parser,
applies to any image carrying an ICC profile, which includes ordinary JPEGs and PNGs — exactly the
formats an employee photograph is. Moderate (5.3), an unbounded allocation rather than a crash, and
in reach on every upload.

### Why we cannot simply fix it

**There is no fixed 3.1.x.** All five advisories are patched in **4.1.2** and affect everything from
2.x through 4.1.1. Verified empirically as well as from the advisories: pinning 3.1.12 — the newest
3.1.x — reproduces all five.

Moving to 4.1.2 is a **major-version override of a transitive dependency of a pinned Orchard
release**. `OrchardCore.Media` 3.0.1 is compiled against 3.1.x, and ImageSharp 4.x is not
API-compatible with 3.x. That override may work and it has not been tried; trying it properly means
exercising Orchard's image resizing and caching against it, which is a piece of work with its own
test plan, not a line in a props file.

## Decision

**Suppress exactly these five advisory ids, by URL, in `Directory.Build.props`. Keep `NuGetAudit`
on. Treat it as temporary and blocking.**

```xml
<ItemGroup>
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-gwg2-r3hj-4w44" />
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-wmxv-xphr-5c9g" />
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-j3p4-wp97-rph4" />
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-j9gm-c75j-xc9q" />
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-jjfr-hcj7-qf5w" />
</ItemGroup>
```

One entry per advisory, deliberately. A sixth advisory against ImageSharp still fails the build, and
so does any advisory against anything else — which is the entire difference between suppressing by
id and suppressing by package, by severity, or by turning the audit off.

**That claim is tested, not asserted.** A sixth, unrelated vulnerable package (`Newtonsoft.Json`
12.0.1) was added temporarily and the restore failed on it — NU1903, naming Newtonsoft — while no
ImageSharp advisory appeared. The probe was then reverted.

### Review trigger — still blocking

**This must be resolved before any production deployment**, and the media allow-list does not
discharge it: it takes four of the five out of reach and leaves GHSA-gwg2-r3hj-4w44, the ICC
parser, live on every JPEG and PNG an employee photograph could be.

Either of:

- **Orchard ships a patch release that moves off ImageSharp 3.1.11.** Check on every Orchard
  version bump; ADR-0001 pins the version, so that bump is already a reviewed decision and this is
  one more thing it has to answer.
- **A 4.1.2 override is verified against `OrchardCore.Media` 3.0.1** — upload, resize, re-serve and
  cache-bust a JPEG, a PNG and an animated GIF, and confirm the media cache and the image-sharp
  middleware still work.

Whichever lands, delete the matching lines. The build going green on its own is the signal that a
suppression has outlived its advisory.

### Mitigation taken: an explicit media allow-list

`src/WorkMate.Web/appsettings.json` now states
`OrchardCore:OrchardCore_Media:AllowedFileExtensions` rather than leaving Orchard's default in
place:

```
.jpg  .jpeg  .png  .gif  .webp        images, for employee photographs
.pdf  .docx  .xlsx                    documents, for the documents section
```

**Orchard 3.0.1's default already excluded TIFF.** Measured on a real tenant rather than inferred —
the ADR could not previously say, and the answer is that the default list is
`.3gp .avi .doc .docx .gif .ico .jpeg .jpg .m4a .m4v .mov .mp3 .mp4 .mpg .odt .ogg .ogv .pdf .png
.pps .ppsx .ppt .pptx .psd .svg .wav .webm .webp .wmv .xls .xlsx`, with no `.tif` and no `.tiff`.

So the four TIFF advisories were already out of reach. **What stating the list adds is that they
stay out of reach**: a default nobody in this repository controls is not a control, and an Orchard
release that widened it would silently undo the mitigation with nothing to notice.

**It is also narrower than the default, on purpose.** The default permits `.svg` — a document that
can carry script, served from the tenant's own origin — along with `.ico`, `.psd` and eleven audio
and video types an HCM product has no use for. An allow-list is only worth having if it lists what
is actually wanted.

`MediaUploadPolicyTenantTests` holds both halves: the configuration, and the behaviour. The second
matters more — it uploads a real TIFF header to the real endpoint and asserts it is refused, and
uploads a real PNG and asserts it is not, because a list that were configured and never consulted
would satisfy every assertion about configuration alone.

This is host configuration, not code. A customer who needs more widens it, and the ADR's assessment
of what is in reach changes with it.

## Alternatives considered

**Turn `NuGetAudit` off, or set `NuGetAuditLevel` above high.** Rejected, and this is the decision
this ADR mostly exists to refuse. It would silence every future advisory against every package in
the solution to avoid five in one of them, and it would do so invisibly — nothing would report that
auditing had stopped.

**Suppress by package rather than by id.** Rejected for a narrower version of the same reason: the
next ImageSharp advisory, which may well be remote code execution rather than a crash, would be
suppressed by a decision taken about these five.

**Pin ImageSharp 3.1.12.** Rejected: it carries all five. Pinning a version that fixes nothing would
leave a line in `Directory.Packages.props` implying the problem had been dealt with.

**Pin ImageSharp 4.1.2 now.** Not rejected — deferred. It is the real fix, and it is a major-version
override of a transitive dependency of a pinned Orchard release, which needs a test plan rather than
a props-file edit mid-slice.

**Accept a failing build until Orchard patches.** Rejected: a repository that cannot restore cannot
be worked on, and a permanently red CI run is a CI run nobody reads.

## Consequences

`dotnet restore` and `dotnet build -warnaserror` are clean again, with auditing fully on, and the
suppression is five lines that can each be deleted independently.

The repository now carries an accepted, documented, time-bound security exception. It is listed as a
blocking open issue in the prompt 4 pull request summary so that it travels with the work rather
than sitting in a file nobody opens.
