$ErrorActionPreference = 'Stop'
$releaseDir = Join-Path $PSScriptRoot 'release'
$bundle = Join-Path $releaseDir 'PersonalDownloader'
dotnet publish (Join-Path $PSScriptRoot 'Desktop\PersonalDownloader.Desktop.csproj') -c Release --no-self-contained -p:NuGetAudit=false -o $bundle
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in 'Install.ps1','README.md','Browser') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $bundle -Recurse -Force
}
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$archive = Join-Path $releaseDir "PersonalDownloader-Windows-$arch.zip"
Compress-Archive -LiteralPath $bundle -DestinationPath $archive -Force
Write-Output $archive
