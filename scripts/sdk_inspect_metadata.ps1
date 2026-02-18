
Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Genexus.Common.dll"

Write-Host "--- eDBType Enum Values (Data Types) ---"
[Artech.Genexus.Common.eDBType].GetEnumValues() | ForEach-Object {
    Write-Host $_
}

Write-Host "`n--- Attribute Properties (SDK Reference) ---"
$attType = [Artech.Genexus.Common.Objects.Attribute]
$attType.GetProperties() | Select-Object Name, PropertyType | Sort-Object Name | ForEach-Object {
    Write-Host "$($_.Name) ($($_.PropertyType))"
}
