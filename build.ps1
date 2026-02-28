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
Write-Host "   > Building Gateway (Release)..."
$gwProj = "src\GxMcp.Gateway\GxMcp.Gateway.csproj"
$tempGw = Join-Path $publishDir "temp_gw"
dotnet publish $gwProj -c Release --nologo -o $tempGw
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Gateway publish failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

if (Test-Path $tempGw) {
    Copy-Item "$tempGw\*" "$publishDir" -Force -Recurse
    Remove-Item $tempGw -Recurse -Force
}

Write-Host "   > Building Gateway (Debug)..."
dotnet build $gwProj -c Debug --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Gateway debug build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Build Worker (.NET Framework 4.8)
Write-Host "   > Building Worker (Release)..."
dotnet build "src\GxMcp.Worker\GxMcp.Worker.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Worker build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "   > Building Worker (Debug)..."
dotnet build "src\GxMcp.Worker\GxMcp.Worker.csproj" -c Debug --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Worker debug build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 4. Copy Worker Binaries to Publish
$workerPublishDir = Join-Path $publishDir "worker"
if (-not (Test-Path $workerPublishDir)) { New-Item -Path $workerPublishDir -ItemType Directory }

# Copy Release Worker to Publish
$workerBinRelease = Join-Path "src" "GxMcp.Worker" | Join-Path -ChildPath "bin\Release"
if (-not (Test-Path $workerBinRelease)) {
    $workerBinRelease = Join-Path "src" "GxMcp.Worker" | Join-Path -ChildPath "bin\x86\Release"
}

if (Test-Path $workerBinRelease) {
    Write-Host "   > Deploying Release Worker binaries to $workerPublishDir..."
    Get-ChildItem -Path "$workerBinRelease\*" -Recurse | Copy-Item -Destination "$workerPublishDir" -Recurse -Force
}

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

Write-Host "`n✅ Build Complete!" -ForegroundColor Green
Write-Host "   - Output: $publishDir"
Write-Host "   - Worker: $publishDir\worker\GxMcp.Worker.exe"
Write-Host "   - Gateway: $publishDir\GxMcp.Gateway.exe"

# 7. Deploy to Extension Backend (for live development)
$extBackendDir = Join-Path $root "src\nexus-ide\backend"
Write-Host "`n🚀 Deploying to Extension Backend: $extBackendDir" -ForegroundColor Cyan
if (-not (Test-Path $extBackendDir)) { New-Item -Path $extBackendDir -ItemType Directory }
Copy-Item "$publishDir\*" -Destination "$extBackendDir" -Recurse -Force

