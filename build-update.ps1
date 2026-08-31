param(
    [string]$FromVersion = '1.2.0',
    [string]$NuGetPackages = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'Shutdown-WinUI3-Source\src\Shutdown\Shutdown.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$project.Project.PropertyGroup.Version
$artifactRoot = Join-Path $projectRoot 'artifacts'
$baseDir = Join-Path $artifactRoot "Shutdown-Tray-v$FromVersion-win-x64"
$latestDir = Join-Path $artifactRoot "Shutdown-Tray-v$version-win-x64"
$staging = Join-Path $artifactRoot "Shutdown-Tray-v$version-update-staging"
$archive = Join-Path $artifactRoot "Shutdown-Tray-v$version-update-from-v$FromVersion-win-x64.zip"

if (-not (Test-Path -LiteralPath $baseDir)) { throw "Base folder not found: $baseDir" }

& (Join-Path $projectRoot 'build-local.ps1') -NuGetPackages $NuGetPackages
if ($LASTEXITCODE -ne 0) { throw 'Full build failed.' }

if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
$changed = 0
foreach ($file in Get-ChildItem -LiteralPath $latestDir -File -Recurse | Where-Object { $_.Name -ne 'SHA256SUMS.txt' }) {
    $relative = [IO.Path]::GetRelativePath($latestDir, $file.FullName)
    $before = Join-Path $baseDir $relative
    $same = (Test-Path -LiteralPath $before) -and
        ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $before -Algorithm SHA256).Hash)
    if ($same) { continue }
    $destination = Join-Path $staging $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $changed++
}

$notes = @(
    "Shutdown Tray $version update from $FromVersion",
    '',
    '1. Exit Shutdown Tray from its tray menu.',
    '2. Extract this archive directly into the folder of the full version, replacing files.',
    '3. Start Shutdown.exe again.',
    '',
    "Changed files: $changed",
    'settings.json is not included and your settings are preserved.'
)
[IO.File]::WriteAllLines((Join-Path $staging 'UPDATE-README.txt'), $notes, [Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -CompressionLevel Optimal
Get-Item -LiteralPath $archive | Select-Object FullName, Length
Get-FileHash -LiteralPath $archive -Algorithm SHA256
"Verified $changed changed application files."
