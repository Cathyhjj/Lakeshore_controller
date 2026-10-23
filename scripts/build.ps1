[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$lakeRoot = Split-Path $PSScriptRoot -Parent
$lakeCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $lakeCompiler)) {
    $lakeCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $lakeCompiler)) { throw 'Install .NET Framework 4.7.2 or later on Windows.' }
$lakeOutput = Join-Path $lakeRoot ('artifacts\bin\' + $Configuration)
New-Item -ItemType Directory -Path $lakeOutput -Force | Out-Null
$lakeSources = @(Get-ChildItem -LiteralPath (Join-Path $lakeRoot 'src\LakeShoreControl') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$lakeSources += Join-Path $lakeRoot 'tests\SelfTests.cs'
$lakeOptions = @('/nologo', '/target:winexe', '/platform:anycpu', '/codepage:65001', '/langversion:5', '/warn:4')
if ($Configuration -eq 'Release') { $lakeOptions += '/optimize+' } else { $lakeOptions += @('/optimize-', '/debug+', '/define:DEBUG;TRACE') }
$lakeReferences = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Windows.Forms.DataVisualization.dll', 'System.Web.Extensions.dll') | ForEach-Object { '/reference:' + $_ }
$lakeExecutable = Join-Path $lakeOutput 'LakeShoreControl.exe'
& $lakeCompiler $lakeOptions (('/out:') + $lakeExecutable) $lakeReferences $lakeSources
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Copy-Item -LiteralPath (Join-Path $lakeRoot 'src\LakeShoreControl\App.config') -Destination ($lakeExecutable + '.config') -Force
New-Item -ItemType Directory -Path (Join-Path $lakeOutput 'recipes') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $lakeRoot 'recipes') | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $lakeOutput 'recipes') -Recurse -Force }
Write-Output ('Built: ' + $lakeExecutable)
