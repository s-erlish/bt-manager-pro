<#
.SYNOPSIS
    Builds Bluetooth Manager Pro into a single .exe.

.DESCRIPTION
    Needs nothing but the .NET 8 SDK — no Visual Studio, no NuGet packages.

.PARAMETER SelfContained
    Bundles the .NET runtime into the executable (~70 MB) so it runs on a machine
    without the .NET 8 Desktop Runtime installed. Without it the build is
    framework-dependent (~25 MB) and requires that runtime.

.PARAMETER Runtime
    Target RID. win-x64 by default; pass win-arm64 for ARM devices.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$project = Join-Path $root 'src\BluetoothManagerPro\BluetoothManagerPro.csproj'
$output = Join-Path $root "publish\$Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

Write-Host "Publishing $Runtime (self-contained: $($SelfContained.IsPresent))..." -ForegroundColor Cyan

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained $($SelfContained.IsPresent.ToString().ToLower()) `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $output 'BluetoothManagerPro.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Ready: $exe ($size MB)" -ForegroundColor Green
