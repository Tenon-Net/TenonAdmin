#!/usr/bin/env pwsh
# TenonAdmin template smoke test: pack core -> local feed -> pack template -> install -> scaffold -> build.
# Proves the generated tenon-app compiles against the real kernel packages.
# Usage (from repo root): pwsh templates/smoke-test.ps1 [-Version <ver>]
# ASCII-only on purpose: Windows PowerShell 5.1 mis-decodes UTF-8-no-BOM .ps1 under non-Latin locales.
param(
    # Default is a version that exists ONLY in the local feed, never on nuget.org. This is load-bearing:
    # the packed template must reference exactly what we just packed. If it still carries a stale hardcoded
    # version, restore reaches for nuget.org, finds nothing, and this test fails -- which is the point
    # (a tagged release must not ship a template pinned to a version the tag never published).
    # The release workflow passes the real tag version instead; same proof, against the actual artifacts.
    [string]$Version = '9.9.9-smoke'
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path "$PSScriptRoot/..").Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("tenon-tmpl-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$feed = Join-Path $work 'feed'
$out  = Join-Path $work 'Probe'
$ver  = $Version
New-Item -ItemType Directory -Force -Path $feed | Out-Null

# Restore into a throwaway global-packages folder, i.e. simulate a clean machine.
# Without this the test is only as honest as the dev box: a TenonAdmin left in the real global cache by an
# earlier run satisfies the restore, and a template pinned to an unpublished version still comes up green.
$env:NUGET_PACKAGES = Join-Path $work 'nuget'

# The template's automatic restore runs before the generated project can carry its own NuGet.config.
# Put the isolated feed configuration in the parent so the post-action discovers it naturally.
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Out-File -FilePath (Join-Path $work 'NuGet.config') -Encoding utf8

function Check($msg) { if ($LASTEXITCODE -ne 0) { throw "FAILED: $msg (exit $LASTEXITCODE)" } }

try {
    Write-Host "== pack core packages -> $feed"
    dotnet pack "$repo/backend/TenonAdmin.slnx" -c Release -o $feed -p:Version=$ver; Check 'pack core'

    Write-Host "== pack template package -> $feed"
    dotnet pack "$repo/templates/TenonAdmin.Templates.csproj" -c Release -o $feed -p:Version=$ver; Check 'pack template'

    Write-Host "== install template"
    dotnet new install (Join-Path $feed "TenonAdmin.Templates.$ver.nupkg") --force; Check 'template install'

    try {
        # No --TenonPkgVersion or --skipRestore on purpose: this is the consumer's first command, verbatim.
        # Passing it would paper over the packaged default, which is exactly the thing that has to be right.
        Write-Host "== scaffold tenon-app -> $out (template default version + automatic restore)"
        dotnet new tenon-app -n Probe -o $out; Check 'dotnet new + automatic restore'

        $assets = Join-Path $out 'obj/project.assets.json'
        if (-not (Test-Path $assets)) {
            throw 'FAILED: template restore post-action did not create obj/project.assets.json.'
        }

        # The generated project must pin exactly what we just packed. Without this assertion the test is
        # near-useless: a PackageReference Version is a MINIMUM, so a stale version silently floats up to
        # whatever the feed has (NU1603) and everything looks green -- while the shipped template references
        # a version that was never published. Consumers on TreatWarningsAsErrors / lock files / exact-version
        # policies are the ones who eat it.
        $csproj = Get-Content (Join-Path $out 'Probe.csproj') -Raw
        if ($csproj -notmatch [regex]::Escape("Version=`"$ver`"")) {
            throw "FAILED: generated project does not reference the packed version $ver. The template default was not stamped at pack time. csproj says: $(($csproj | Select-String 'PackageReference.*TenonAdmin' -AllMatches).Matches.Value -join ', ')"
        }

        # -warnaserror:NU1603 is the general net: restore must find the exact version, never float to it.
        Write-Host "== build generated project without restoring again (exact version required)"
        dotnet build $out -c Release --no-restore -warnaserror:NU1603; Check 'build'

        Write-Host "`n[OK] smoke passed: generated tenon-app compiles against the real kernel."
    }
    finally {
        dotnet new uninstall TenonAdmin.Templates 2>$null | Out-Null
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
