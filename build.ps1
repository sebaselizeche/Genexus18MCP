# GeneXus MCP - Build & Deploy Script
# ==========================================

$ErrorActionPreference = "Continue" # Don't stop on Stop-Process errors
$root = $PSScriptRoot
$publishDir = Join-Path $root "publish"

Write-Host "🚧 Preparing Build..." -ForegroundColor Cyan

# 0. Stop running processes
Write-Host "   > Stopping running processes..."
Stop-Process -Name GxMcp.Worker -ErrorAction SilentlyContinue
Stop-Process -Name GxMcp.Gateway -ErrorAction SilentlyContinue

$ErrorActionPreference = "Stop"

# 1. Clean Publish Directory
if (Test-Path $publishDir) {
    Write-Host "   > Cleaning publish directory (preserving logs)..."
    # Preserve logs and worker config
    Get-ChildItem -Path "$publishDir\*" -Exclude "worker_log.txt", "mcp_debug.log", "*.log", "GxMcp.Worker.exe.config" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -Path $publishDir -ItemType Directory
}

Write-Host "🚧 Building Solutions..." -ForegroundColor Cyan

# 2. Build Gateway (.NET 8)
Write-Host "   > Building Gateway..."
$gwProj = "src\GxMcp.Gateway\GxMcp.Gateway.csproj"
$tempGw = Join-Path $publishDir "temp_gw"
# Normal publish (multi-file) is more reliable for dependencies
dotnet publish $gwProj -c Release --nologo -o $tempGw
if (Test-Path $tempGw) {
    Write-Host "   > Deploying Gateway files from: $tempGw"
    Copy-Item "$tempGw\*" "$publishDir" -Force -Recurse
    Remove-Item $tempGw -Recurse -Force
} else {
    Write-Error "Gateway publish failed to create output in $tempGw"
}

# 3. Build Worker (.NET Framework 4.8)
Write-Host "   > Building Worker..."
dotnet build "src\GxMcp.Worker\GxMcp.Worker.csproj" -c Release --nologo

# 4. Copy Worker Binaries to Publish
$workerPublishDir = Join-Path $publishDir "worker"
if (-not (Test-Path $workerPublishDir)) { New-Item -Path $workerPublishDir -ItemType Directory }

# Find the actual output directory (might be bin\Release or bin\Release\net48)
$workerBin = Join-Path "src" "GxMcp.Worker"
$workerBin = Join-Path $workerBin "bin"
$workerBin = Join-Path $workerBin "Release"
if (-not (Test-Path $workerBin)) {
    $workerBin = Join-Path "src" "GxMcp.Worker"
    $workerBin = Join-Path $workerBin "bin"
    $workerBin = Join-Path $workerBin "x86"
    $workerBin = Join-Path $workerBin "Release"
}

Write-Host "   > Deploying Worker binaries from $workerBin to $workerPublishDir..."
# DO NOT exclude the .config file, it's required for .NET Framework apps!
Get-ChildItem -Path "$workerBin\*" -Recurse | Copy-Item -Destination "$workerPublishDir" -Recurse -Force

# 4.1 Copy GeneXus Definitions (Crucial for SDK)
$gxPath = "C:\Program Files (x86)\GeneXus\GeneXus18"
if (Test-Path "$gxPath\Definitions") {
    Write-Host "   > Copying GeneXus Definitions..."
    if (-not (Test-Path "$workerPublishDir\Definitions")) {
        Copy-Item "$gxPath\Definitions" -Destination "$workerPublishDir\Definitions" -Recurse -Force
    }
}

# 5. Copy Config Template if missing
if (-not (Test-Path "$publishDir\config.json")) {
    if (Test-Path "$root\config.json") {
        Write-Host "   > Copying existing config.json from root..."
        Copy-Item "$root\config.json" -Destination "$publishDir\config.json"
    } else {
        Write-Host "   > Creating default config.json..."
        $defaultConfig = @{
            GeneXus = @{
                InstallationPath = "C:\\Program Files (x86)\\GeneXus\\GeneXus18"
                WorkerExecutable = "$publishDir\\worker\\GxMcp.Worker.exe"
            }
            Server = @{
                HttpPort = 5000
                McpStdio = $true
            }
            Logging = @{
                Level = "Debug"
                Path = "logs"
            }
            Environment = @{
                KBPath = "C:\\KBs\\academicoLocal"
            }
        } | ConvertTo-Json -Depth 4
        Set-Content "$publishDir\config.json" $defaultConfig
    }
}

# 6. Generate start_mcp.bat (Launcher for Platform)
Write-Host "   > Generating start_mcp.bat..."
$batContent = "@echo off`r`ncd /d ""%~dp0""`r`nGxMcp.Gateway.exe`r`n"
Set-Content -Path "$publishDir\start_mcp.bat" -Value $batContent -Encoding Ascii

Write-Host "✅ Build Complete!" -ForegroundColor Green
Write-Host "   - Output: $publishDir"
Write-Host "   - Worker: $publishDir\GxMcp.Worker.exe"
Write-Host "   - Gateway: $publishDir\GxMcp.Gateway.exe"
