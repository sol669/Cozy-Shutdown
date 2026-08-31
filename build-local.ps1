param([string]$NuGetPackages = '')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'Shutdown-WinUI3-Source\src\Shutdown\Shutdown.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$project.Project.PropertyGroup.Version
$artifactRoot = Join-Path $projectRoot 'artifacts'
$folderName = "Shutdown-Tray-v$version-win-x64"
$publishDir = Join-Path $artifactRoot $folderName
$archivePath = Join-Path $artifactRoot "Shutdown-Tray-v$version-full-win-x64.zip"
$argsList = @('publish', $projectFile, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:Platform=x64', '-p:NuGetAudit=false', '-o', $publishDir)
if ($NuGetPackages) { $argsList += "-p:RestorePackagesPath=$NuGetPackages" }
& dotnet @argsList
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
& dotnet run --project (Join-Path $projectRoot 'tests\Shutdown.PolicyTests.csproj') -c Release -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Policy tests failed.' }

foreach ($required in @('Shutdown.exe', 'Shutdown.dll', 'coreclr.dll', 'hostfxr.dll', 'Microsoft.UI.Xaml.dll',
    'Assets\ShutdownTrey.ico', 'Assets\tray_rdp_white.ico', 'Assets\tray_rdp_black.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) { throw "Incomplete autonomous build: $required" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README-FULL.txt') -Destination (Join-Path $publishDir 'README.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publishDir 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot "CHANGELOG-$version.md") -Destination (Join-Path $publishDir 'CHANGELOG.md')

$entries = @(Get-ChildItem -LiteralPath $publishDir -File -Recurse | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($publishDir, $_.FullName).Replace('\', '/')
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
})
[IO.File]::WriteAllLines((Join-Path $publishDir 'SHA256SUMS.txt'), $entries, [Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath $publishDir -DestinationPath $archivePath -CompressionLevel Optimal -Force

# Read back every archived file and compare it to the baseline for later small updates.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($line in $entries) {
        $expected = $line.Substring(0, 64)
        $relative = $line.Substring(66)
        $entry = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq "$folderName/$relative" } | Select-Object -First 1
        if (-not $entry) { throw "Missing archive entry: $relative" }
        $stream = $entry.Open()
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $actual = [Convert]::ToHexString($hash.ComputeHash($stream)).ToLowerInvariant() }
        finally { $hash.Dispose(); $stream.Dispose() }
        if ($actual -ne $expected) { throw "Archive checksum mismatch: $relative" }
    }
}
finally { $zip.Dispose() }
Get-Item -LiteralPath $archivePath | Select-Object FullName, Length
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
"Verified $($entries.Count) packaged files."
