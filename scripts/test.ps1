[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$lakeRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
$lakeResults = Join-Path $lakeRoot 'artifacts\test-results'
New-Item -ItemType Directory -Path $lakeResults -Force | Out-Null
$lakeReport = Join-Path $lakeResults ('self-tests-' + $Configuration + '.txt')
$lakeExecutable = Join-Path $lakeRoot ('artifacts\bin\' + $Configuration + '\LakeShoreControl.exe')
# The --self-test entry point runs before GUI/serial connection initialization.
$lakeProcess = Start-Process -FilePath $lakeExecutable -ArgumentList @('--self-test', ('"{0}"' -f $lakeReport)) -WindowStyle Hidden -Wait -PassThru
if (Test-Path -LiteralPath $lakeReport) { Get-Content -LiteralPath $lakeReport }
if ($lakeProcess.ExitCode -ne 0) { throw ('Self tests failed. See ' + $lakeReport) }
