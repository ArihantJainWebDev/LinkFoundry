$ErrorActionPreference = 'Stop'
$releaseDir = Join-Path $PSScriptRoot 'release'
$bundle = Join-Path $releaseDir 'LinkFoundry'
dotnet publish (Join-Path $PSScriptRoot 'Desktop\PersonalDownloader.Desktop.csproj') -c Release -r win-x64 --no-self-contained -p:NuGetAudit=false -o $bundle
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in 'Install.ps1','README.md','LICENSE','Browser') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $bundle -Recurse -Force
}
$archive = Join-Path $releaseDir 'LinkFoundry-Windows-x64.zip'
Compress-Archive -LiteralPath $bundle -DestinationPath $archive -Force
Write-Output $archive
