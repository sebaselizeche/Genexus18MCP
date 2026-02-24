# Nexus IDE - Extension Packaging Script
# ==========================================

$root = $PSScriptRoot
$rootBase = Split-Path $root -Parent
$ideDir = Join-Path $rootBase "src\nexus-ide"
$backendDest = Join-Path $ideDir "backend"

Write-Host "🚧 Starting Extension Packaging..." -ForegroundColor Cyan

# 1. Build the Backend
Write-Host "   > Building Backend Services..."
Push-Location $rootBase
.\build.ps1
Pop-Location

# 2. Bundle Backend into Extension
Write-Host "   > Bundling Backend into Extension..."
if (Test-Path $backendDest) { Remove-Item $backendDest -Recurse -Force }
New-Item -Path $backendDest -ItemType Directory

$publishDir = Join-Path $rootBase "publish"
Copy-Item "$publishDir\*" -Destination $backendDest -Recurse -Force

# 3. Compile Extension
Write-Host "   > Compiling Extension..."
Push-Location $ideDir
npm run compile

# 4. Package Extension (.vsix)
Write-Host "   > Creating .vsix package..."
# Check if vsce is installed
if (-not (Get-Command vsce -ErrorAction SilentlyContinue)) {
    Write-Host "   > installing @vscode/vsce globally..."
    npm install -g @vscode/vsce
}

vsce package --out ../../release/nexus-ide.vsix

Pop-Location

Write-Host "✅ Extension Packaged!" -ForegroundColor Green
Write-Host "   - Output: release/nexus-ide.vsix"
