param(
    [string]$TargetName = "ProcArqCandUniGra",
    [string]$KBPath = "C:\KBs\academicoLocal"
)

$gxPath = "C:\Program Files (x86)\GeneXus\GeneXus18"
[Reflection.Assembly]::LoadFrom("$gxPath\Artech.Genexus.Common.dll") | Out-Null
[Reflection.Assembly]::LoadFrom("$gxPath\Artech.Architecture.Common.dll") | Out-Null
[Reflection.Assembly]::LoadFrom("$gxPath\Artech.Common.dll") | Out-Null
[Reflection.Assembly]::LoadFrom("$gxPath\Connector.dll") | Out-Null

# Setup Context
$ctxType = [Artech.Architecture.Common.Services.ContextService]
$ctxType::Initialize()

# Initialize Model Objects
$initType = [Artech.Genexus.Common.KBModelObjectsInitializer]
$initType::Initialize()

# Open KB
Write-Host "Opening KB: $KBPath"
$options = New-Object Artech.Architecture.Common.Objects.KnowledgeBase+OpenOptions($KBPath)
$kb = [Artech.Architecture.Common.Objects.KnowledgeBase]::Open($options)

if (-not $kb) { Write-Error "Failed to open KB"; exit }

# Find Object
Write-Host "Finding Object: $TargetName"
$obj = $kb.DesignModel.Objects.GetByName($null, $null, $TargetName) | Select-Object -First 1

if (-not $obj) { Write-Error "Object not found"; $kb.Close(); exit }

Write-Host "Object: $($obj.Name) ($($obj.TypeDescriptor.Name))"

Write-Host "`n--- References FROM this object (Calls) ---" -ForegroundColor Yellow
$refsFrom = $obj.GetReferences()
foreach ($r in $refsFrom) {
    $target = $kb.DesignModel.Objects.Get($r.To)
    Write-Host "  -> $($target.Name) ($($target.TypeDescriptor.Name))"
}

Write-Host "`n--- References TO this object (Used By) ---" -ForegroundColor Green
$refsTo = $obj.GetReferencesTo()
foreach ($r in $refsTo) {
    $source = $kb.DesignModel.Objects.Get($r.From)
    Write-Host "  <- $($source.Name) ($($source.TypeDescriptor.Name))"
}

$kb.Close()
