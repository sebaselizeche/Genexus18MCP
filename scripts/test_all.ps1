# GeneXus MCP & Nexus IDE - Comprehensive Test Runner

Write-Host "--- [1/3] Compiling Project ---" -ForegroundColor Cyan
.\build.ps1
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

Write-Host "`n--- [2/3] Running MCP Internal Unit Tests ---" -ForegroundColor Cyan
$gwProcess = Start-Process -FilePath "C:\Projetos\GenexusMCP\publish\GxMcp.Gateway.exe" -WindowStyle Hidden -PassThru
Write-Host "Waiting for Gateway & Worker to initialize (SDK load)..."
Start-Sleep -Seconds 15

try {
    $response = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/command" -Body '{"jsonrpc":"2.0", "id": 1, "method": "tools/call", "params": {"name": "genexus_self_test", "arguments": {}}}' -ContentType "application/json" -TimeoutSec 60
    
    if ($response.error) {
        Write-Host "Gateway Error: $($response.error.message)" -ForegroundColor Red
    } else {
        $testResults = $response.result.content[0].text | ConvertFrom-Json
        if ($testResults.status -eq "Success" -or $testResults.status -eq "Partial") {
            Write-Host "MCP Self-Test: $($testResults.status) ✅" -ForegroundColor Green
            $testResults.tests | ForEach-Object {
                $icon = if ($_.status -eq "Pass") { "✅" } else { "❌" }
                Write-Host "  $icon $($_.name): $($_.status) ($($_.message))"
            }
        } else {
            Write-Host "MCP Self-Test: FAILED ❌" -ForegroundColor Red
            Write-Host ($testResults | ConvertTo-Json)
        }
    }
} catch {
    Write-Host "Error connecting to Gateway for tests: $_" -ForegroundColor Red
} finally {
    Stop-Process -Id $gwProcess.Id -Force -ErrorAction SilentlyContinue
}

Write-Host "`n--- [3/3] Running Nexus IDE UI Tests ---" -ForegroundColor Cyan
cd src/nexus-ide
npm test
cd ../..

Write-Host "`n--- Test Cycle Complete ---" -ForegroundColor Cyan
