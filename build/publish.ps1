<#
.SYNOPSIS
    Builds the two release downloads into artifacts/publish:
      DeepCoolLite.exe                - needs the .NET 10 Desktop Runtime
      DeepCoolLite-SelfContained.exe  - includes .NET, runs anywhere (Windows 10/11 x64)
.PARAMETER Version
    Version to stamp into the exes (e.g. 1.0.0). Defaults to <Version> from Directory.Build.props.
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\publish'
$project = Join-Path $root 'src\DeepCoolLite.Settings\DeepCoolLite.Settings.csproj'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$flavours = @(
    @{ SelfContained = 'false'; Name = 'DeepCoolLite.exe' },
    @{ SelfContained = 'true';  Name = 'DeepCoolLite-SelfContained.exe' }
)

foreach ($f in $flavours) {
    $dir = Join-Path $out "tmp-$($f.SelfContained)"
    $args = @('publish', $project, '-c', 'Release', "-p:SelfContained=$($f.SelfContained)", '-o', $dir, '-nologo')
    if ($Version) { $args += "-p:Version=$Version" }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (SelfContained=$($f.SelfContained))" }

    Move-Item (Join-Path $dir 'DeepCoolLite.exe') (Join-Path $out $f.Name)
    Remove-Item $dir -Recurse -Force
}

Get-ChildItem $out | ForEach-Object { '{0,-34} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
