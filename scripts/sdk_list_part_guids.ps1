
Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Architecture.Common.dll"
Add-Type -Path "C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Genexus.Common.dll"

$targets = @(
    "RulesPart", "EventsPart", "WebFormPart", "WinFormPart", "VariablesPart", "HelpPart", "DocumentationPart", "StructurePart"
)

Write-Host "--- Common GeneXus Part GUIDs ---"
$partsAsm = [System.Reflection.Assembly]::LoadFrom("C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Genexus.Common.dll")
foreach ($tname in $targets) {
    $type = $partsAsm.GetTypes() | Where-Object { $_.Name -eq $tname }
    if ($type) {
        $id = [Guid]::Empty
        $f = $type.GetField("TypeGuid", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
        if ($f) { $id = $f.GetValue($null) }
        else {
            $p = $type.GetProperty("TypeGuid", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
            if ($p) { $id = $p.GetValue($null, $null) }
        }
        
        if ($id -ne [Guid]::Empty) {
            Write-Host "$($tname): $($id)"
        }
    }
}
