param(
    [Parameter(Mandatory=$false, Position=0)]
    [string]$TypeName,

    [Parameter(Mandatory=$false)]
    [string]$DllName = "Artech.Genexus.Common.dll",

    [switch]$Methods,
    [switch]$Properties,
    [switch]$Fields,
    [switch]$Events,
    [switch]$Full,
    [switch]$Json,
    [string]$Filter = "",
    [switch]$IncludeInherited = $true
)

$gxPath = "C:\Program Files (x86)\GeneXus\GeneXus18"
$dllPath = Join-Path $gxPath $DllName

if (-not (Test-Path $dllPath)) {
    # Try some common alternatives
    $alternatives = @("Artech.Genexus.Common.dll", "Artech.Architecture.Common.dll", "Artech.Common.dll", "Artech.FrameworkDE.dll")
    foreach ($alt in $alternatives) {
        $altPath = Join-Path $gxPath $alt
        if (Test-Path $altPath) {
            $dllPath = $altPath
            $DllName = $alt
            break
        }
    }
}

if (-not (Test-Path $dllPath)) {
    Write-Error "Could not find GeneXus DLL at $gxPath"
    return
}

try {
    $asm = [Reflection.Assembly]::LoadFrom($dllPath)
} catch {
    Write-Error "Failed to load assembly $DllName : $($_.Exception.Message)"
    return
}

if (-not $TypeName) {
    Write-Host "--- Types in $DllName (Top 100) ---" -ForegroundColor Yellow
    $types = $asm.GetExportedTypes() | Select-Object -First 100 | ForEach-Object { 
        [PSCustomObject]@{
            FullName = $_.FullName
            BaseType = $_.BaseType.Name
        }
    }
    if ($Json) { $types | ConvertTo-Json } else { $types | Format-Table -AutoSize }
    return
}

# Resolve target type
$type = $asm.GetType($TypeName)
if (-not $type) {
    $matches = $asm.GetTypes() | Where-Object { $_.FullName -like "*$TypeName*" -or $_.Name -eq $TypeName }
    if ($matches.Count -eq 0) {
        Write-Error "Type '$TypeName' not found in $DllName"
        return
    } elseif ($matches.Count -gt 1) {
        Write-Host "Multiple matches for '$TypeName':" -ForegroundColor Cyan
        $matches | ForEach-Object { Write-Host "  $($_.FullName)" }
        return
    }
    $type = $matches[0]
}

function Get-DetailedMethod($m) {
    $params = $m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }
    return [PSCustomObject]@{
        Name = $m.Name
        ReturnType = $m.ReturnType.Name
        Parameters = ($params -join ", ")
        IsVirtual = $m.IsVirtual
        IsStatic = $m.IsStatic
        DeclaringType = $m.DeclaringType.Name
    }
}

function Get-DetailedProperty($p) {
    return [PSCustomObject]@{
        Name = $p.Name
        Type = $p.PropertyType.Name
        CanRead = $p.CanRead
        CanWrite = $p.CanWrite
        DeclaringType = $p.DeclaringType.Name
    }
}

$bindingFlags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::Static
if (-not $IncludeInherited) { $bindingFlags = $bindingFlags -bor [Reflection.BindingFlags]::DeclaredOnly }

$result = [PSCustomObject]@{
    TypeName = $type.FullName
    BaseType = $type.BaseType.FullName
    Interfaces = ($type.GetInterfaces() | ForEach-Object { $_.Name }) -join ", "
    IsEnum = $type.IsEnum
}

if ($type.IsEnum) {
    $enumVals = @{}
    [Enum]::GetNames($type) | ForEach-Object { $enumVals[$_] = [int][Enum]::Parse($type, $_) }
    $result | Add-Member -MemberType NoteProperty -Name "EnumValues" -Value $enumVals
}

if ($Full -or $Properties -or (-not $Methods -and -not $Fields -and -not $Events)) {
    $props = $type.GetProperties($bindingFlags) | Where-Object { -not $Filter -or $_.Name -like "*$Filter*" } | ForEach-Object { Get-DetailedProperty $_ }
    $result | Add-Member -MemberType NoteProperty -Name "Properties" -Value $props
}

if ($Full -or $Methods) {
    # Filter out property accessors if not requested
    $meths = $type.GetMethods($bindingFlags) | 
             Where-Object { (-not $Filter -or $_.Name -like "*$Filter*") -and ($Full -or -not ($_.Name.StartsWith("get_") -or $_.Name.StartsWith("set_"))) } | 
             ForEach-Object { Get-DetailedMethod $_ }
    $result | Add-Member -MemberType NoteProperty -Name "Methods" -Value $meths
}

if ($Full -or $Fields) {
    $fieldsList = $type.GetFields($bindingFlags) | Where-Object { -not $Filter -or $_.Name -like "*$Filter*" } | ForEach-Object {
        [PSCustomObject]@{ Name = $_.Name; Type = $_.FieldType.Name; IsStatic = $_.IsStatic; Value = (try { if($_.IsStatic){$_.GetValue($null)}else{"(instance)"} } catch { "Error" }) }
    }
    $result | Add-Member -MemberType NoteProperty -Name "Fields" -Value $fieldsList
}

if ($Json) {
    $result | ConvertTo-Json -Depth 5
} else {
    Write-Host "`n=== $($result.TypeName) ===" -ForegroundColor Cyan
    Write-Host "Base: $($result.BaseType)"
    Write-Host "Interfaces: $($result.Interfaces)"
    
    if ($result.IsEnum) {
        Write-Host "`n--- Enum Values ---" -ForegroundColor Yellow
        $result.EnumValues.GetEnumerator() | Sort-Object Value | ForEach-Object { Write-Host "  $($_.Key) = $($_.Value)" }
    }

    if ($result.Properties) {
        Write-Host "`n--- Properties ---" -ForegroundColor Yellow
        $result.Properties | Format-Table -AutoSize
    }

    if ($result.Methods) {
        Write-Host "`n--- Methods ---" -ForegroundColor Yellow
        $result.Methods | Format-Table -AutoSize
    }

    if ($result.Fields) {
        Write-Host "`n--- Fields ---" -ForegroundColor Yellow
        $result.Fields | Format-Table -AutoSize
    }
}
