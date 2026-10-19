param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    foreach ($required in @('LICENSE', 'packages.lock.json', 'licenses/NAudio-MIT.txt', 'licenses/WDL.txt')) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Required release file missing: $required" }
    }
    [xml]$project = Get-Content MidiMixer.csproj -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'Invalid release version' }
    function Invoke-Dotnet([string[]]$Arguments) {
        & $Dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
    }
    Invoke-Dotnet @('restore', '--locked-mode')
    Invoke-Dotnet @('build', '-c', 'Release', '--no-restore', '-p:ContinuousIntegrationBuild=true')
    Invoke-Dotnet @('bin/Release/net10.0-windows/MidiMixer.dll', '--core-test')

    # Always use a fresh directory. Never replace a running installation or reuse
    # a publish folder that might contain personal diagnostic reports.
    $buildName = 'release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $releaseRoot = Join-Path $projectRoot "artifacts/$buildName"
    $packageName = "MidiMixer-$version-win-x64"
    $publishRoot = Join-Path $releaseRoot $packageName
    New-Item -ItemType Directory -Path $publishRoot | Out-Null
    Invoke-Dotnet @('publish', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:ContinuousIntegrationBuild=true', '-o', $publishRoot)

    # Read exact runtime package versions chosen by the SDK, not the newest
    # package found in a user's cache.
    $assets = Get-Content obj/project.assets.json -Raw | ConvertFrom-Json
    $runtimePackages = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies |
        Where-Object name -in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64'))
    if ($runtimePackages.Count -ne 2) { throw 'Could not identify both runtime packages' }
    $runtimeNotes = Join-Path $publishRoot 'licenses/runtime'
    New-Item -ItemType Directory -Path $runtimeNotes -Force | Out-Null
    foreach ($package in $runtimePackages) {
        $runtimeVersion = ([regex]::Match($package.version, '\d+\.\d+\.\d+')).Value
        $relative = $package.name.ToLowerInvariant() + '/' + $runtimeVersion
        $packageFolder = @($assets.packageFolders.PSObject.Properties.Name |
            ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
        if (-not $packageFolder) { throw "Runtime package missing: $relative" }
        $notices = @(Get-ChildItem -LiteralPath $packageFolder -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\..*)?$' })
        if (-not ($notices | Where-Object Name -like 'LICENSE*')) { throw "Runtime license missing: $relative" }
        if ($package.name -eq 'Microsoft.NETCore.App.Runtime.win-x64' -and -not ($notices | Where-Object Name -like 'THIRD-PARTY*')) { throw 'Runtime third-party notices missing' }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $runtimeNotes ($package.name + '-' + $notice.Name))
        }
    }
    $runtimePackages | ConvertTo-Json | Set-Content (Join-Path $runtimeNotes 'runtime-packages.json') -Encoding utf8
    Copy-Item -LiteralPath docs -Destination $publishRoot -Recurse
    Copy-Item -LiteralPath CONTRIBUTING.md -Destination $publishRoot

    $forbidden = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Where-Object {
        $_.Name -match '^(settings|virtual-mixer)\.json|test\.(json|txt|png)$|^cable-devices\.|\.pfx$|\.p12$|\.sys$|\.inf$|^VBCABLE_'
    })
    if ($forbidden.Count) { throw 'Personal/test/driver files detected in package' }
    $zipPath = Join-Path $releaseRoot ($packageName + '.zip')
    Compress-Archive -LiteralPath $publishRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $packageName.zip" | Set-Content (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Prepared locally: $zipPath"
    Write-Output "SHA256: $hash"
    Write-Output 'Nothing was uploaded or published.'
}
finally { Pop-Location }
