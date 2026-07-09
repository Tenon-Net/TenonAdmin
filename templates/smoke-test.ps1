#!/usr/bin/env pwsh
# TenonAdmin template smoke test: pack core -> local feed -> pack template -> install -> scaffold -> build.
# Proves the generated tenon-app compiles against the real kernel packages.
# Usage (from repo root): pwsh templates/smoke-test.ps1
# ASCII-only on purpose: Windows PowerShell 5.1 mis-decodes UTF-8-no-BOM .ps1 under non-Latin locales.
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path "$PSScriptRoot/..").Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("tenon-tmpl-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$feed = Join-Path $work 'feed'
$out  = Join-Path $work 'Probe'
$ver  = '0.0.1-preview'
New-Item -ItemType Directory -Force -Path $feed | Out-Null

function Check($msg) { if ($LASTEXITCODE -ne 0) { throw "FAILED: $msg (exit $LASTEXITCODE)" } }

try {
    Write-Host "== pack core packages -> $feed"
    dotnet pack "$repo/backend/TenonAdmin.slnx" -c Release -o $feed -p:Version=$ver; Check 'pack core'

    Write-Host "== pack template package -> $feed"
    dotnet pack "$repo/templates/TenonAdmin.Templates.csproj" -c Release -o $feed -p:Version=$ver; Check 'pack template'

    Write-Host "== install template"
    dotnet new install (Join-Path $feed "TenonAdmin.Templates.$ver.nupkg") --force; Check 'template install'

    try {
        Write-Host "== scaffold tenon-app -> $out"
        dotnet new tenon-app -n Probe -o $out --TenonPkgVersion $ver --skipRestore true; Check 'dotnet new'

        # Point restore at the local feed + nuget.org via nuget.config (more robust than restore -s <url>, consistent across shells).
        @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Out-File -FilePath (Join-Path $out 'nuget.config') -Encoding utf8

        Write-Host "== build generated project (restore + build)"
        dotnet build $out -c Release; Check 'build'

        Write-Host "`n[OK] smoke passed: generated tenon-app compiles against the real kernel."
    }
    finally {
        dotnet new uninstall TenonAdmin.Templates 2>$null | Out-Null
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
