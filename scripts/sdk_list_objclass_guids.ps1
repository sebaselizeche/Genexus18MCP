
Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Architecture.Common.dll"
Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Genexus.Common.dll"

$ocType = [Artech.Genexus.Common.ObjClass]
Write-Host "--- GeneXus Object Type GUIDs (ObjClass) ---"
$ocType.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static) | Where-Object { $_.FieldType -eq [System.Guid] } | Sort-Object Name | ForEach-Object {
    Write-Host "$($_.Name): $($_.GetValue($null))"
}
