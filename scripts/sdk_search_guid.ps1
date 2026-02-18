
param(
    [Parameter(Mandatory=$true)]
    [string]$TargetGuid
)

Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Architecture.Common.dll"
Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Genexus.Common.dll"

Write-Host "Searching for owners of GUID: $TargetGuid..."

$assemblies = @(
    "Artech.Architecture.Common.dll",
    "Artech.Genexus.Common.dll"
)

foreach ($asm in $assemblies) {
    Write-Host "Checking $asm..."
    $types = [System.Reflection.Assembly]::LoadFrom("C:\Program Files (x86)\GeneXus\GeneXus18\$asm").GetTypes()
    foreach ($type in $types) {
        # Check Static Fields
        $fields = $type.GetFields([System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        foreach ($field in $fields) {
            try {
                $val = $field.GetValue($null)
                if ($val -ne $null -and $val.ToString() -eq $TargetGuid) {
                    Write-Host "[FIELD] Type: $($type.FullName), Name: $($field.Name)"
                }
            } catch {}
        }
        
        # Check Static Properties
        $props = $type.GetProperties([System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        foreach ($prop in $props) {
            try {
                $val = $prop.GetValue($null)
                if ($val -ne $null -and $val.ToString() -eq $TargetGuid) {
                    Write-Host "[PROP] Type: $($type.FullName), Name: $($prop.Name)"
                }
            } catch {}
        }
    }
}
