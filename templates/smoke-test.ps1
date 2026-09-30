#!/usr/bin/env pwsh
# TenonAdmin template smoke test: pack core -> local feed -> pack template -> install -> scaffold -> build.
# Proves the generated tenon-app compiles against the real kernel packages, in both shapes: the default
# scaffold and the optional '--integration' one (third-party integration package + Integrations/ samples).
# Usage (from repo root): pwsh templates/smoke-test.ps1 [-Version <ver>]
# ASCII-only on purpose: Windows PowerShell 5.1 mis-decodes UTF-8-no-BOM .ps1 under non-Latin locales.
param(
    # Default is a version that exists ONLY in the local feed, never on nuget.org. This is load-bearing:
    # the packed template must reference exactly what we just packed. If it still carries a stale hardcoded
    # version, restore reaches for nuget.org, finds nothing, and this test fails -- which is the point
    # (a tagged release must not ship a template pinned to a version the tag never published).
    # The release workflow passes the real tag version instead; same proof, against the actual artifacts.
    [string]$Version = '9.9.9-smoke',
    # Local runs only: when 5100 is already taken (a MinimalHost dev server, say), serve the generated hosts on
    # another port. The launch profile still applies (Development, CodeFirst on); only the listen URL changes.
    # CI keeps 5100 so the consumer's first 'dotnet run' is exercised verbatim.
    [int]$Port = 5100
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path "$PSScriptRoot/..").Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("tenon-tmpl-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$feed = Join-Path $work 'feed'
# Hyphenated on purpose. 'dotnet new' sanitizes "-" to "_" when substituting the name into file CONTENT,
# but renames FILES with the literal, so the two disagree for any hyphenated name -- a common convention
# (tenon-example itself). A non-hyphenated scaffold name cannot see that class of bug at all; it shipped
# a broken generated Dockerfile once precisely because this test used 'Probe'.
$name = 'probe-app'
$sanitized = $name -replace '-', '_'
$out  = Join-Path $work $name
$itgName = 'probe-itg'
$itgOut  = Join-Path $work $itgName
$ver  = $Version
$base = "http://localhost:$Port"
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

# HTTP status of a GET, without throwing on 4xx/5xx (works on Windows PowerShell 5.1 and PowerShell 7).
function Get-HttpStatus([string]$uri) {
    try { return [int](Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 5).StatusCode }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { return 0 } }
}

function Invoke-Json([string]$method, [string]$uri, $body, $headers = @{}) {
    Invoke-RestMethod -Method $method -Uri $uri -Headers $headers -ContentType 'application/json' `
        -Body ($body | ConvertTo-Json -Depth 8 -Compress) -TimeoutSec 10
}

function Assert-OpenValidation([string]$uri, [string]$apiKey, [string]$title) {
    try {
        Invoke-Json 'Post' $uri @{ title = $title } @{ 'X-Api-Key' = $apiKey } | Out-Null
        throw "FAILED: invalid sample-doc title length $($title.Length) was accepted."
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
        $raw = $_.ErrorDetails.Message
        if (-not $raw -and $_.Exception.Response.Content) {
            $raw = $_.Exception.Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
        $body = $raw | ConvertFrom-Json
        if ($status -ne 400 -or $body.code -ne 49007) {
            throw "FAILED: invalid sample-doc title length $($title.Length) returned HTTP $status code $($body.code), expected HTTP 400 code 49007."
        }
    }
}

# Compiling is not the advertised promise -- "dotnet run and it works" is. A green build hid a real
# regression once: the template shipped no launch profile, so dotnet run resolved to Production,
# CodeFirst auto-DDL is off there by design, and the consumer's first command died on missing seed
# tables. Run it verbatim (no env override), require a live /health, then run the optional probes
# against the live host.
function Test-GeneratedHost([string]$projectDir, [string]$processName, [scriptblock]$probe) {
    Write-Host "== run $processName verbatim (dotnet run must reach /health)"
    # Both scaffolds listen on the same port. If anything still answers there, /health below would be served
    # by that other process and every probe would test the wrong host.
    if ((Get-HttpStatus "$base/health") -ne 0) {
        throw "FAILED: $base is still served by another process before starting $processName (pass -Port to use a free port locally)."
    }
    $runLog = Join-Path $work "$processName.run.log"
    $errLog = Join-Path $work "$processName.run.err.log"
    $runArgs = @('run', '--project', $projectDir, '-c', 'Release', '--no-build')
    if ($Port -ne 5100) { $runArgs += @('--', '--urls', $base) }
    $proc = Start-Process dotnet -PassThru -NoNewWindow -ArgumentList $runArgs `
        -RedirectStandardOutput $runLog -RedirectStandardError $errLog
    try {
        $healthy = $false
        foreach ($i in 1..60) {
            if ($proc.HasExited) { break }
            try {
                $r = Invoke-WebRequest -Uri "$base/health" -UseBasicParsing -TimeoutSec 5
                if ($r.StatusCode -eq 200) { $healthy = $true; break }
            } catch { Start-Sleep -Seconds 2 }
        }
        if (-not $healthy) {
            $tail = (Get-Content $runLog, $errLog -ErrorAction SilentlyContinue | Select-Object -Last 40) -join "`n"
            throw "FAILED: generated $processName did not serve /health from a plain 'dotnet run'.`n$tail"
        }
        if ($probe) {
            try { & $probe }
            catch {
                $tail = (Get-Content $runLog, $errLog -ErrorAction SilentlyContinue | Select-Object -Last 40) -join "`n"
                throw "$($_.Exception.Message)`n$tail"
            }
        }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        # 'dotnet run' launches the app as a child; killing the parent orphans it (and the port).
        # The scaffold lives under a unique temp root, so filter by executable path instead of killing every
        # unrelated process that happens to share the generated app name.
        $projectRoot = [System.IO.Path]::GetFullPath($projectDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and [System.IO.Path]::GetFullPath($_.Path).StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)
        } | Stop-Process -Force -ErrorAction SilentlyContinue
        foreach ($i in 1..15) {
            if ((Get-HttpStatus "$base/health") -eq 0) { break }
            Start-Sleep -Seconds 1
        }
    }
}

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
        dotnet new tenon-app -n $name -o $out; Check 'dotnet new + automatic restore'

        $assets = Join-Path $out 'obj/project.assets.json'
        if (-not (Test-Path $assets)) {
            throw 'FAILED: template restore post-action did not create obj/project.assets.json.'
        }

        # The generated project must pin exactly what we just packed. Without this assertion the test is
        # near-useless: a PackageReference Version is a MINIMUM, so a stale version silently floats up to
        # whatever the feed has (NU1603) and everything looks green -- while the shipped template references
        # a version that was never published. Consumers on TreatWarningsAsErrors / lock files / exact-version
        # policies are the ones who eat it.
        $csproj = Get-Content (Join-Path $out "$name.csproj") -Raw
        if ($csproj -notmatch [regex]::Escape("Version=`"$ver`"")) {
            throw "FAILED: generated project does not reference the packed version $ver. The template default was not stamped at pack time. csproj says: $(($csproj | Select-String 'PackageReference.*TenonAdmin' -AllMatches).Matches.Value -join ', ')"
        }

        # The integration module is opt-in: the default scaffold must not carry its package or its samples.
        if ($csproj -match 'TenonAdmin\.Integration' -or (Test-Path (Join-Path $out 'Integrations'))) {
            throw 'FAILED: the default scaffold references TenonAdmin.Integration or contains Integrations/; the module must stay opt-in (--integration).'
        }

        # The generated Dockerfile is never built here (no docker in CI for this job), so a hyphenated
        # scaffold name alone would still not see the bug that shipped in 0.3.2: content substitution wrote
        # probe_app.csproj / probe_app.dll into the Dockerfile while the real file is probe-app.csproj, and
        # docker build died for every consumer using a hyphenated name. Assert statically instead --
        # no sanitized-name literal anywhere, and any literal .csproj it names must actually exist.
        $dockerfile = Join-Path $out 'Dockerfile'
        if (Test-Path $dockerfile) {
            $df = Get-Content $dockerfile -Raw
            # Guard on the names actually differing. With a hyphen-free name the sanitized form IS the real
            # name, so an unguarded check fires on the legitimate substitution and reports the nonsense
            # "contains 'Probe' but the files use 'Probe'". (Hit while proving this assertion reddens.)
            if ($sanitized -ne $name -and $df -match [regex]::Escape($sanitized)) {
                throw "FAILED: generated Dockerfile contains the sanitized project name '$sanitized', but the scaffolded files use '$name'. Name substitution disagrees with file renaming; docker build would fail for any hyphenated project name."
            }
            foreach ($m in [regex]::Matches($df, '[\w.-]+\.csproj')) {
                if (-not (Test-Path (Join-Path $out $m.Value))) {
                    throw "FAILED: generated Dockerfile references '$($m.Value)', which does not exist in the scaffolded project. Use a *.csproj glob instead of a project-name literal."
                }
            }
        }

        # -warnaserror:NU1603 is the general net: restore must find the exact version, never float to it.
        Write-Host "== build generated project without restoring again (exact version required)"
        dotnet build $out -c Release --no-restore -warnaserror:NU1603; Check 'build'
        Test-GeneratedHost $out $name $null

        # --integration adds a second package (TenonAdmin.Integration) that the default scaffold never touches,
        # plus consumer code compiled against it. Same bar: exact packed version, build, and a live host whose
        # startup validators accept the generated open endpoints, outbound target and delivery adapter.
        Write-Host "== scaffold tenon-app --integration -> $itgOut"
        dotnet new tenon-app -n $itgName -o $itgOut --integration; Check 'dotnet new --integration + automatic restore'
        $itgCsproj = Get-Content (Join-Path $itgOut "$itgName.csproj") -Raw
        if ($itgCsproj -notmatch [regex]::Escape("Include=`"TenonAdmin.Integration`" Version=`"$ver`"")) {
            throw "FAILED: the --integration scaffold does not reference TenonAdmin.Integration $ver. csproj says: $(($itgCsproj | Select-String 'PackageReference.*TenonAdmin[^/]*' -AllMatches).Matches.Value -join ', ')"
        }
        if (-not (Test-Path (Join-Path $itgOut 'Integrations/SampleDocSyncAdapter.cs'))) {
            throw 'FAILED: the --integration scaffold did not generate the Integrations/ samples.'
        }
        Write-Host "== build --integration project without restoring again (exact version required)"
        dotnet build $itgOut -c Release --no-restore -warnaserror:NU1603; Check 'build (--integration)'
        $oldAdminPassword = $env:TenonAdmin__Seed__AdminPassword
        $env:TenonAdmin__Seed__AdminPassword = 'TemplateSmoke@123'
        try {
            Test-GeneratedHost $itgOut $itgName {
                # Without a credential the generated open endpoint must answer 401 from the open-app scheme;
                # a 404 would mean the Integrations/ controllers or the module's routes were never mapped.
                $status = Get-HttpStatus "$base/api/open/v1/sample-docs"
                if ($status -ne 401) {
                    throw "FAILED: GET /api/open/v1/sample-docs without X-Api-Key returned $status, expected 401."
                }

                # Exercise generated input validation over HTTP after a real app/grant/scope/credential setup.
                $login = Invoke-Json 'Post' "$base/api/v1/auth/login" @{ account = 'superAdmin'; password = 'TemplateSmoke@123' }
                $auth = @{ Authorization = "Bearer $($login.data.accessToken)" }
                $app = Invoke-Json 'Post' "$base/api/v1/integration/app" `
                    @{ code = 'template-smoke'; name = 'Template smoke'; enabled = $true; ownerOrgId = 1 } $auth
                $appId = $app.data
                $catalog = Invoke-RestMethod -Method Get -Uri "$base/api/v1/integration/catalog/endpoints" -Headers $auth -TimeoutSec 10
                $permission = ($catalog.data | Where-Object { $_.httpMethod -eq 'POST' -and $_.route -eq 'api/open/v1/sample-docs' }).permission
                if (-not $permission) { throw 'FAILED: generated POST sample-doc endpoint is absent from the open API catalog.' }
                Invoke-Json 'Put' "$base/api/v1/integration/app/$appId/grants" @{ permissions = @($permission) } $auth | Out-Null
                Invoke-Json 'Put' "$base/api/v1/integration/app/$appId/scopes" `
                    @{ bindings = @(@{ scopeKey = 'org'; allValues = $true; values = @() }) } $auth | Out-Null
                $issued = Invoke-Json 'Post' "$base/api/v1/integration/app/$appId/credentials" @{} $auth
                Assert-OpenValidation "$base/api/open/v1/sample-docs" $issued.data.apiKey ''
                Assert-OpenValidation "$base/api/open/v1/sample-docs" $issued.data.apiKey ('x' * 129)
            }
        }
        finally {
            if ($null -eq $oldAdminPassword) { Remove-Item Env:TenonAdmin__Seed__AdminPassword -ErrorAction SilentlyContinue }
            else { $env:TenonAdmin__Seed__AdminPassword = $oldAdminPassword }
        }

        Write-Host "`n[OK] smoke passed: generated tenon-app (default and --integration) compiles and runs against the real kernel."
    }
    finally {
        dotnet new uninstall TenonAdmin.Templates 2>$null | Out-Null
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
