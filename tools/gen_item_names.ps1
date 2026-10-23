# Generates ItemNames.json (TweakDB id -> record name) for every record in ItemClasses.json, and reports how many
# inventory items of the given saves it covers.
param([string]$BinDir, [string]$OutFile, [string]$Saves)

$ErrorActionPreference = 'Stop'
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($s, $e)
    $p = Join-Path $BinDir ((New-Object System.Reflection.AssemblyName($e.Name)).Name + '.dll')
    if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
    return $null
})
Set-Location $BinDir
foreach ($n in 'WolvenKit.Core', 'WolvenKit.Common', 'WolvenKit.RED4', 'CP2077SaveEditor.Core') {
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $BinDir "$n.dll"))
}
$names = [CP2077SaveEditor.GameData]::TweakDbStrings
$classes = [CP2077SaveEditor.Utils.ResourceHelper]::ItemClasses

$map = [ordered]@{}
$missing = 0
foreach ($id in ($classes.Keys | Sort-Object)) {
    $name = $names.GetString([uint64]$id)
    if ($name) { $map["$id"] = $name } else { $missing++ }
}
# plus every other item record ("Items.X", no sub-field), e.g. quest items and shards
$extra = 0
foreach ($kv in $names.GetAll()) {
    $n = $kv.Value
    if ($n -and $n.StartsWith('Items.') -and $n.IndexOf('.', 6) -lt 0 -and -not $map.Contains("$($kv.Key)")) { $map["$($kv.Key)"] = $n; $extra++ }
}
"extra item records: $extra"
$json = $map | ConvertTo-Json -Compress
[IO.File]::WriteAllText($OutFile, $json, [Text.UTF8Encoding]::new($false))
"names: $($map.Count) resolved, $missing unresolved, $([math]::Round((Get-Item $OutFile).Length / 1KB)) KB"
($classes.Values | Group-Object Type | Sort-Object Count -Descending | Select-Object -First 25 | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', '

foreach ($save in ($Saves -split ";")) {
    $r = [WolvenKit.RED4.Save.IO.CyberpunkSaveReader]::new([IO.File]::OpenRead($save))
    $sf = $null; [void]$r.ReadFile([ref]$sf); $r.Dispose()
    $h = [CP2077SaveEditor.SaveFileHelper]::new(); $h.SaveFile = $sf
    $all = 0; $known = 0; $unknownNames = @()
    foreach ($sub in $h.GetInventoriesContainer().SubInventories) {
        foreach ($it in $sub.Items) {
            $all++
            $id = [uint64]$it.ItemInfo.ItemId.Id
            if ($map.Contains("$id")) { $known++ } else { $unknownNames += $names.GetString($id) }
        }
    }
    "$(Split-Path (Split-Path $save) -Leaf): $known / $all inventory items covered; not covered e.g.: " + (($unknownNames | Select-Object -Unique -First 12) -join ', ')
}
