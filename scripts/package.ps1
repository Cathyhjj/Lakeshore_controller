[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$lakeRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'test.ps1') -Configuration Release
$lakeArtifacts = Join-Path $lakeRoot 'artifacts'
$lakeStaging = Join-Path $lakeArtifacts ('package-' + [Guid]::NewGuid().ToString('N'))
$lakePackage = Join-Path $lakeStaging 'LakeShoreControl'
New-Item -ItemType Directory -Path $lakePackage -Force | Out-Null
$lakeBin = Join-Path $lakeArtifacts 'bin\Release'
# Explicit allowlist excludes any logs or measurements created beside the app.
foreach ($lakeName in @('LakeShoreControl.exe', 'LakeShoreControl.exe.config', 'recipes')) {
    Copy-Item -LiteralPath (Join-Path $lakeBin $lakeName) -Destination $lakePackage -Recurse
}
Copy-Item -LiteralPath (Join-Path $lakeRoot 'docs\user-guide-en.txt') -Destination (Join-Path $lakePackage 'README_EN.txt')
Copy-Item -LiteralPath (Join-Path $lakeRoot 'docs\user-guide-zh.md') -Destination (Join-Path $lakePackage 'user-guide-zh.md')
$lakeZip = Join-Path $lakeArtifacts 'LakeShoreControl.zip'
Compress-Archive -LiteralPath $lakePackage -DestinationPath $lakeZip -Force
Write-Output ('Packaged: ' + $lakeZip)
