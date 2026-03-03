# Kill existing processes
try { 
    taskkill /F /IM GxMcp.Gateway.exe /T 2>$null 
    taskkill /F /IM GxMcp.Worker.exe /T 2>$null
    Write-Host "Services cleaned up."
} catch { 
    Write-Host "No services to clean." 
}

# Give a small delay
Start-Sleep -Seconds 1

# Rebuild Worker to ensure latest logic
Write-Host "Rebuilding Worker (Release)..."
dotnet build "C:\Projetos\GenexusMCP\src\GxMcp.Worker\GxMcp.Worker.csproj" --configuration Release

# Rebuild Gateway
Write-Host "Rebuilding Gateway (Release)..."
dotnet build "C:\Projetos\GenexusMCP\src\GxMcp.Gateway\GxMcp.Gateway.csproj" --configuration Release

# Start the Gateway
$gatewayPath = "C:\Projetos\GenexusMCP\src\GxMcp.Gateway\bin\Release\net8.0\GxMcp.Gateway.exe"
if (Test-Path $gatewayPath) {
    Write-Host "Starting Gateway (Release)..."
    Start-Process $gatewayPath -WindowStyle Normal
} else {
    Write-Error "Gateway not found at $gatewayPath"
}

# Wait for HTTP server to be ready
Start-Sleep -Seconds 3
