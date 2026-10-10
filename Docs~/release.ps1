# Builds the release zip of AvatarDoc: everything the package needs, without the
# internal docs and without the tests. Run it from anywhere:
#   powershell -ExecutionPolicy Bypass -File "<this file>"
# The zip lands in <project>\Releases and is dropped into a project's Packages folder as it is.

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $PSScriptRoot          # the package folder, e.g. ...\Packages\dev.docme.avatar-doctor
# The Unity project folder: the first folder above the package that holds both Assets and ProjectSettings.
$project = (Get-Item $src).Parent
while ($project -and -not ((Test-Path (Join-Path $project.FullName "Assets")) -and (Test-Path (Join-Path $project.FullName "ProjectSettings")))) { $project = $project.Parent }
if (-not $project) { throw "No Unity project folder found above $src" }
$project = $project.FullName
$id = (Get-Content "$src\package.json" | ConvertFrom-Json).name
$version = (Get-Content "$src\package.json" | ConvertFrom-Json).version
$stage = Join-Path $env:TEMP "avatardoc-release\$id"
$releases = Join-Path $project "Releases"

if (Test-Path (Split-Path $stage)) { Remove-Item -Recurse -Force (Split-Path $stage) }
New-Item -ItemType Directory -Force $stage | Out-Null
robocopy $src $stage /E /XD "Docs~" "Tests" ".git" /XF ".gitignore" /NFL /NDL /NJH /NJS | Out-Null
Remove-Item -Force "$stage\Tests.meta" -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $releases | Out-Null
$zip = Join-Path $releases "$id-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host ("{0}  {1:N0} KB" -f $zip, ((Get-Item $zip).Length / 1KB))
