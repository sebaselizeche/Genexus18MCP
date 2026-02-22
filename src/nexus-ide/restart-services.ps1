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
Write-Host "Rebuilding Worker..."
dotnet build "C:\Projetos\GenexusMCP\src\GxMcp.Worker\GxMcp.Worker.csproj" --configuration Debug

# Start the Gateway
$gatewayPath = "C:\Projetos\GenexusMCP\src\GxMcp.Gateway\bin\Debug\net8.0\GxMcp.Gateway.exe"
if (Test-Path $gatewayPath) {
    Write-Host "Starting Gateway..."
    Start-Process $gatewayPath -WindowStyle Normal
} else {
    Write-Error "Gateway not found at $gatewayPath"
}

# Wait for HTTP server to be ready
Start-Sleep -Seconds 3
