$ErrorActionPreference = 'Stop'
$appDir = Join-Path $env:LOCALAPPDATA 'PersonalDownloader\App'
$exe = Join-Path $appDir 'PersonalDownloader.Desktop.exe'
$release = Join-Path $PSScriptRoot 'release\PersonalDownloader'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'PersonalDownloader.Desktop.exe')) { $release = $PSScriptRoot }
if (!(Test-Path -LiteralPath (Join-Path $release 'PersonalDownloader.Desktop.exe'))) {
    dotnet publish (Join-Path $PSScriptRoot 'Desktop\PersonalDownloader.Desktop.csproj') -c Release --no-self-contained -o $release
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null
Get-ChildItem -LiteralPath $release -File | Where-Object Extension -In '.exe','.dll','.json','.pdb' |
    Copy-Item -Destination $appDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Browser') -Destination $appDir -Recurse -Force
$protocol = 'HKCU:\Software\Classes\personaldownloader'
New-Item -Path "$protocol\shell\open\command" -Force | Out-Null
Set-Item -LiteralPath $protocol -Value 'URL:Personal Downloader'
New-ItemProperty -LiteralPath $protocol -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
Set-Item -LiteralPath "$protocol\shell\open\command" -Value ('"{0}" "%1"' -f $exe)
$menu = [Environment]::GetFolderPath('Programs')
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $menu 'Personal Downloader.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $appDir
$shortcut.Save()
Write-Output "Installed Personal Downloader. Browser extension folder: $appDir\Browser"
