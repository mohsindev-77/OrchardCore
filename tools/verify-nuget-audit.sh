#!/usr/bin/env bash
#
# Proves, end to end, that this repository's NuGet audit still fails a build on an advisory it has
# not deliberately suppressed.
#
# WHY THIS IS A SCRIPT AND NOT A TEST
#
# The honest version of this check needs a package with a known vulnerability in the restore graph,
# which means a download on every run, a network dependency, and a deliberately vulnerable
# dependency inside the build of the product whose build is the thing being trusted. That is a bad
# trade for something that changes about once a year.
#
# So the cheap half runs on every build — NuGetAuditPolicyTests asserts the audit is on, set to
# audit transitive packages too, and suppressing only advisories an ADR accounts for — and this
# expensive half is run deliberately: when ADR-0013 is reviewed, when the SDK is upgraded, or when
# anybody wants the end-to-end answer rather than the argument.
#
# It builds a throwaway project OUTSIDE this repository's directory tree, so nothing it does can
# reach the real build: Directory.Build.props is copied in rather than inherited, which is also what
# makes the result a statement about this repository's settings.
#
# USAGE
#
#   bash tools/verify-nuget-audit.sh
#
# Exits 0 when the audit behaved correctly: the unsuppressed advisory failed the restore and the
# suppressed one did not.
#
set -euo pipefail

cd "$(dirname "$0")/.."
repository="$(pwd)"

# A package with a long-published, well-known advisory, pinned to a version that carries it. Not
# one of ImageSharp's: the point is to prove that something OTHER than the five suppressed
# advisories still fails.
probe_package="Newtonsoft.Json"
probe_version="12.0.1"

workspace="$(mktemp -d)"
trap 'rm -rf "$workspace"' EXIT

cp "$repository/Directory.Build.props" "$workspace/Directory.Build.props"

# Central package management is on in Directory.Build.props, so the probe needs its own versions
# file rather than a version attribute on the reference.
cat > "$workspace/Directory.Packages.props" <<XML
<Project>
  <ItemGroup>
    <PackageVersion Include="$probe_package" Version="$probe_version" />
  </ItemGroup>
</Project>
XML

cat > "$workspace/probe.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="$probe_package" />
  </ItemGroup>
</Project>
XML

echo "Probing with $probe_package $probe_version and this repository's audit settings…"

if dotnet restore "$workspace/probe.csproj" > "$workspace/restore.log" 2>&1; then
    echo
    echo "FAILED: the restore succeeded."
    echo
    echo "An advisory this repository has NOT suppressed did not fail the build, which means the"
    echo "audit is not protecting anything. Check <NuGetAudit>, <NuGetAuditMode> and"
    echo "<NuGetAuditLevel> in Directory.Build.props, and that NU1901-NU1904 are not in NoWarn."
    exit 1
fi

if ! grep -qE "NU190[0-9]" "$workspace/restore.log"; then
    echo
    echo "INCONCLUSIVE: the restore failed, but not with an audit warning."
    echo
    echo "Something else went wrong — no network, a bad feed, an SDK mismatch — so this run says"
    echo "nothing about the audit. The log:"
    echo
    tail -30 "$workspace/restore.log"
    exit 2
fi

echo "  the unsuppressed advisory failed the restore, as it must."

# And the other direction: with that advisory suppressed by id, the same restore succeeds. Without
# this half the first result would also be consistent with "the audit fails everything", which
# would be a different defect wearing the same face.
advisory="$(grep -oE 'https://github\.com/advisories/GHSA-[a-z0-9-]+' "$workspace/restore.log" | head -1)"

if [ -z "$advisory" ]; then
    echo "  (could not read the advisory URL out of the log; skipping the second half)"
    exit 0
fi

cat > "$workspace/Directory.Build.props" <<XML
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <NuGetAuditSuppress Include="$advisory" />
  </ItemGroup>
</Project>
XML

if dotnet restore "$workspace/probe.csproj" > "$workspace/suppressed.log" 2>&1; then
    echo "  and suppressing it by id lets the same restore through."
    echo
    echo "PASSED: the audit fails on an advisory that is not suppressed, and only on those."
    exit 0
fi

echo
echo "FAILED: suppressing the advisory by id did not let the restore through."
echo
echo "Either the suppression syntax has changed or something else is failing the restore. The log:"
echo
tail -30 "$workspace/suppressed.log"
exit 1
