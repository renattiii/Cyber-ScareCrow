param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$hostSource = Join-Path $root "src\CyberScarecrow.Host\host.cpp"
$hostOutputDirectory = Join-Path $root "src\CyberScarecrow.App\host"
$hostOutput = Join-Path $hostOutputDirectory "CyberScarecrow.Host.exe"
$iconPath = Join-Path $root "src\CyberScarecrow.App\assets\scarecrow.ico"
$projectPath = Join-Path $root "src\CyberScarecrow.App\CyberScarecrow.App.csproj"
$distPath = Join-Path $root "dist"

Write-Host "CyberScarecrow build" -ForegroundColor Yellow
Write-Host "-------------------"

if (-not (Test-Path $iconPath)) {
    Write-Host "Generating application icon..."
    & (Join-Path $root "tools\make-icon.ps1")
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe was not found. Install Visual Studio Build Tools with the C++ workload."
}

$vsPath = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($vsPath)) {
    throw "Visual Studio C++ build tools were not found."
}
$vsPath = $vsPath.Trim()

$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) {
    throw "vcvars64.bat was not found at $vcvars"
}

New-Item -ItemType Directory -Force -Path $hostOutputDirectory | Out-Null

Write-Host "Building the native decoy host..."
$compileCommand = "call `"$vcvars`" >nul && cd /d `"$hostOutputDirectory`" && cl /nologo /O1 /MT /EHsc /std:c++17 `"$hostSource`" /link /SUBSYSTEM:WINDOWS user32.lib"
cmd.exe /c $compileCommand
if ($LASTEXITCODE -ne 0) {
    throw "The decoy host failed to compile (exit code $LASTEXITCODE)."
}

$compiledHost = Join-Path $hostOutputDirectory "host.exe"
if (-not (Test-Path $compiledHost)) {
    throw "The compiler did not produce host.exe."
}

Move-Item $compiledHost $hostOutput -Force
Remove-Item (Join-Path $hostOutputDirectory "host.obj") -Force -ErrorAction SilentlyContinue

Write-Host "Building the WinForms app..."
if (Test-Path $distPath) {
    Remove-Item $distPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $distPath | Out-Null

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -o $distPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit code $LASTEXITCODE)."
}

$appExe = Join-Path $distPath "CyberScarecrow.exe"
$hostExe = Join-Path $distPath "CyberScarecrow.Host.exe"
if (-not (Test-Path $appExe)) {
    throw "The app executable was not produced."
}
if (-not (Test-Path $hostExe)) {
    throw "The decoy host was not copied into the output."
}

Write-Host ""
Write-Host "Built:" -ForegroundColor Green
Write-Host "  $appExe"
Write-Host "  $hostExe"
Write-Host ""
Write-Host "Run CyberScarecrow.exe, pick your decoys, and flip the switch." -ForegroundColor Green
