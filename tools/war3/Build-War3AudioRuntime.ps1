[CmdletBinding()]
param(
    [string]$SourceCatalog = "D:\Godot\war3_assets\exports\audio_catalog",
    [string]$SourceAudio = "D:\Godot\war3_assets\godot_export\audio",
    [string]$ProjectRoot = "",
    [ValidateSet("human-core")]
    [string]$Profile = "human-core"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
$CatalogOutput = Join-Path $ProjectRoot "assets\warcraft3\classic\data\audio_catalog"
$RuntimeOutput = Join-Path $ProjectRoot "assets\generated\warcraft3_audio"

function Reset-GeneratedDirectory {
    param([string]$Path, [string]$ExpectedLeaf)
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $full -Leaf) -ne $ExpectedLeaf) {
        throw "Refusing to reset unexpected output directory: $full"
    }
    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
    New-Item -ItemType Directory -Path $full -Force | Out-Null
}

if (-not (Test-Path -LiteralPath (Join-Path $SourceCatalog "manifest.json"))) {
    throw "Audio catalog manifest not found: $SourceCatalog"
}
if (-not (Test-Path -LiteralPath $SourceAudio)) {
    throw "Audio source directory not found: $SourceAudio"
}

$manifest = Get-Content -LiteralPath (Join-Path $SourceCatalog "manifest.json") -Raw |
    ConvertFrom-Json
if ($manifest.schema -ne "war3-audio-catalog-manifest/v1") {
    throw "Unsupported audio catalog schema: $($manifest.schema)"
}

Reset-GeneratedDirectory $CatalogOutput "audio_catalog"
Copy-Item -Path (Join-Path $SourceCatalog "*") -Destination $CatalogOutput `
    -Recurse -Force
Reset-GeneratedDirectory $RuntimeOutput "warcraft3_audio"

$cueIndex = @{}
foreach ($entry in $manifest.cues) { $cueIndex[$entry.id] = $entry }
$selectedCueIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)

function Add-CueReferences {
    param($Value)
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        if ($cueIndex.ContainsKey($Value)) { [void]$selectedCueIds.Add($Value) }
        return
    }
    if ($Value -is [Collections.IEnumerable] -and
        $Value -isnot [Management.Automation.PSCustomObject]) {
        foreach ($item in $Value) { Add-CueReferences $item }
        return
    }
    foreach ($property in $Value.PSObject.Properties) {
        Add-CueReferences $property.Value
    }
}

function Get-ReferenceLabel {
    param($Value)
    if ($null -eq $Value) { return "" }
    if ($Value -is [string]) { return $Value }
    foreach ($name in @("label", "id", "value")) {
        $property = $Value.PSObject.Properties[$name]
        if ($null -ne $property -and
            -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }
    return ""
}

$humanCoreObjectIds = @(
    "hhou", "hbar", "htow", "hbla", "halt", "hlum", "hars", "harm",
    "hgra", "hvlt", "hgtw",
    "hfoo", "hrif", "hpea", "hkni", "hmpr", "hsor", "hspt", "hmtm",
    "hgyr", "hmtt", "hgry", "hdhw", "Hamg", "Hmkg", "Hpal", "Hblm"
)

$bindingById = @{}
foreach ($entry in $manifest.bindings.units) { $bindingById[$entry.id] = $entry }
$abilityBindingById = @{}
foreach ($entry in $manifest.bindings.abilities) {
    $abilityBindingById[$entry.id] = $entry
}
$playableAbilityIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$modelMetadataPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$unitDataRoot = Join-Path $ProjectRoot `
    "assets\warcraft3\classic\data\unit_editor_data"
$unitDataById = @{}
$unitDataManifestPath = Join-Path $unitDataRoot "manifest.json"
if (Test-Path -LiteralPath $unitDataManifestPath) {
    $unitDataManifest = Get-Content -LiteralPath $unitDataManifestPath -Raw |
        ConvertFrom-Json
    foreach ($entry in $unitDataManifest.units) { $unitDataById[$entry.id] = $entry }
}
foreach ($objectId in $humanCoreObjectIds) {
    if (-not $bindingById.ContainsKey($objectId)) {
        Write-Warning "No audio binding for playable object $objectId"
        continue
    }
    $binding = Get-Content -LiteralPath (
        Join-Path $SourceCatalog $bindingById[$objectId].path) -Raw |
        ConvertFrom-Json
    Add-CueReferences $binding
    $voiceSet = $binding.references.voiceSet
    if ($null -ne $voiceSet -and -not [string]::IsNullOrWhiteSpace($voiceSet.id)) {
        $voicePath = Join-Path $SourceCatalog $voiceSet.path
        if (Test-Path -LiteralPath $voicePath) {
            Add-CueReferences (Get-Content -LiteralPath $voicePath -Raw |
                ConvertFrom-Json)
        }
        $deathCue = "$($voiceSet.id)Death"
        if ($cueIndex.ContainsKey($deathCue)) { [void]$selectedCueIds.Add($deathCue) }
    }
    if ($unitDataById.ContainsKey($objectId)) {
        $unitData = Get-Content -LiteralPath (
            Join-Path $unitDataRoot $unitDataById[$objectId].path) -Raw |
            ConvertFrom-Json
        foreach ($abilityId in $unitData.summary.abilities) {
            if (-not [string]::IsNullOrWhiteSpace($abilityId)) {
                [void]$playableAbilityIds.Add($abilityId)
            }
        }
        foreach ($section in $unitData.editor.PSObject.Properties) {
            foreach ($fieldName in @("abilList", "heroAbilList")) {
                $field = $section.Value.PSObject.Properties[$fieldName]
                if ($null -eq $field) { continue }
                foreach ($abilityId in ([string]$field.Value).Split(',')) {
                    $abilityId = $abilityId.Trim()
                    if (-not [string]::IsNullOrWhiteSpace($abilityId) -and
                        $abilityId -ne "_") {
                        [void]$playableAbilityIds.Add($abilityId)
                    }
                }
            }
        }
        foreach ($asset in $unitData.assets.PSObject.Properties) {
            $godotPath = [string]$asset.Value.godotPath
            if ([string]::IsNullOrWhiteSpace($godotPath)) { continue }
            $normalized = $godotPath.Replace('\', '/')
            if (-not $normalized.StartsWith(
                    "models/", [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $metadata = "metadata/" + $normalized.Substring(7)
            $metadata = [Text.RegularExpressions.Regex]::Replace(
                $metadata, "\.glb$", ".war3.json",
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)
            [void]$modelMetadataPaths.Add($metadata)
        }
    }
}

$runtimeAbilityEntries = [Collections.Generic.List[object]]::new()
foreach ($abilityId in ($playableAbilityIds | Sort-Object)) {
    if (-not $abilityBindingById.ContainsKey($abilityId)) {
        Write-Warning "No audio ability binding for playable ability $abilityId"
        continue
    }
    $entry = $abilityBindingById[$abilityId]
    $binding = Get-Content -LiteralPath (
        Join-Path $SourceCatalog $entry.path) -Raw | ConvertFrom-Json
    Add-CueReferences $binding.references
    [void]$runtimeAbilityEntries.Add([ordered]@{
        id = $abilityId
        effect = Get-ReferenceLabel $binding.references.effect
        loopedEffect = Get-ReferenceLabel $binding.references.loopedEffect
    })
}

$animationEventByCode = @{}
$animationMapPath = Join-Path $SourceCatalog "animation_event_map.json"
if (Test-Path -LiteralPath $animationMapPath) {
    $animationMap = Get-Content -LiteralPath $animationMapPath -Raw |
        ConvertFrom-Json
    foreach ($entry in $animationMap.events) {
        $animationEventByCode[$entry.eventCode] = $entry
    }
}
$runtimeAnimationEntries = [Collections.Generic.List[object]]::new()
$selectedAnimationCodes = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($relativeMetadata in ($modelMetadataPaths | Sort-Object)) {
    $metadataPath = Join-Path (
        Join-Path $ProjectRoot "assets\warcraft3\classic") $relativeMetadata
    if (-not (Test-Path -LiteralPath $metadataPath)) { continue }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    foreach ($eventObject in $metadata.eventObjects) {
        $name = [string]$eventObject.Name
        if ($name.Length -lt 8 -or
            -not $name.StartsWith("SNDX", [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $eventCode = $name.Substring(4, 4).ToUpperInvariant()
        if (-not $selectedAnimationCodes.Add($eventCode) -or
            -not $animationEventByCode.ContainsKey($eventCode)) {
            continue
        }
        $eventBinding = $animationEventByCode[$eventCode]
        Add-CueReferences $eventBinding.sound
        [void]$runtimeAnimationEntries.Add([ordered]@{
            eventCode = $eventCode
            cue = Get-ReferenceLabel $eventBinding.sound
        })
    }
}

foreach ($cueId in @(
    "InterfaceClick", "InterfaceError", "ErrorMessage",
    "RallyPointPlace", "WayPoint",
    "PlaceBuildingDefault", "ConstructingBuildingDefault",
    "JobDoneSoundHuman",
    "ResearchCompleteHuman", "UpgradeCompleteHuman",
    "QuestNew", "Hint", "Warning")) {
    if ($cueIndex.ContainsKey($cueId)) { [void]$selectedCueIds.Add($cueId) }
}

$copiedFiles = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$runtimeCueEntries = [Collections.Generic.List[object]]::new()
$runtimeBytes = 0L
foreach ($cueId in ($selectedCueIds | Sort-Object)) {
    $indexEntry = $cueIndex[$cueId]
    $cuePath = Join-Path $SourceCatalog $indexEntry.path
    $cue = Get-Content -LiteralPath $cuePath -Raw | ConvertFrom-Json
    $resourcePaths = [Collections.Generic.List[string]]::new()
    foreach ($source in $cue.normalized.sources) {
        if (-not $source.exists -or [string]::IsNullOrWhiteSpace($source.resolvedPath)) {
            continue
        }
        $relative = $source.resolvedPath.Replace('/', '\').TrimStart('\')
        if (-not $copiedFiles.Add($relative)) {
            [void]$resourcePaths.Add($source.resolvedPath.Replace('\', '/'))
            continue
        }
        $sourcePath = Join-Path $SourceAudio $relative
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "Resolved cue source is missing from audio overlay: $relative"
        }
        $destination = Join-Path $RuntimeOutput $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) `
            -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
        $runtimeBytes += (Get-Item -LiteralPath $destination).Length
        [void]$resourcePaths.Add($source.resolvedPath.Replace('\', '/'))
    }
    [void]$runtimeCueEntries.Add([ordered]@{
        id = $cueId
        category = $cue.category
        resources = $resourcePaths.ToArray()
    })
}

$musicEntries = [Collections.Generic.List[object]]::new()
$assetCatalog = Get-Content -LiteralPath (Join-Path $SourceCatalog "assets.json") `
    -Raw | ConvertFrom-Json
$humanMusicNames = @("Human1.mp3", "Human2.mp3", "Human3.mp3", "HumanX1.mp3")
foreach ($asset in $assetCatalog.assets) {
    if ($asset.inferredCategory -ne "music" -or
        $humanMusicNames -notcontains (Split-Path $asset.virtualPath -Leaf)) {
        continue
    }
    $relative = $asset.virtualPath.Replace('/', '\').TrimStart('\')
    $resourcePath = $asset.virtualPath.Replace('\', '/')
    if ($copiedFiles.Add($relative)) {
        $sourcePath = Join-Path $SourceAudio $relative
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "Music source is missing from audio overlay: $relative"
        }
        $destination = Join-Path $RuntimeOutput $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) `
            -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
        $runtimeBytes += (Get-Item -LiteralPath $destination).Length
    }
    [void]$musicEntries.Add([ordered]@{
        id = [IO.Path]::GetFileNameWithoutExtension($relative)
        resource = $resourcePath
        race = "human"
    })
}

$runtimeManifest = [ordered]@{
    schema = "war3-audio-runtime-pack/v1"
    generatedAt = [DateTime]::UtcNow.ToString("o")
    profile = $Profile
    sourceCatalogSchema = $manifest.schema
    sourceCatalogGeneratedAt = $manifest.generatedAt
    statistics = [ordered]@{
        cueCount = $runtimeCueEntries.Count
        abilityBindingCount = $runtimeAbilityEntries.Count
        animationEventCount = $runtimeAnimationEntries.Count
        audioFileCount = $copiedFiles.Count
        bytes = $runtimeBytes
    }
    objectIds = $humanCoreObjectIds
    abilities = $runtimeAbilityEntries.ToArray()
    animationEvents = $runtimeAnimationEntries.ToArray()
    cues = $runtimeCueEntries.ToArray()
    music = $musicEntries.ToArray()
}
$runtimeManifest | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $RuntimeOutput "runtime_manifest.json") `
        -Encoding UTF8

Write-Host "WAR3_AUDIO_RUNTIME_OK profile=$Profile cues=$($runtimeCueEntries.Count) abilities=$($runtimeAbilityEntries.Count) animation_events=$($runtimeAnimationEntries.Count) music=$($musicEntries.Count) files=$($copiedFiles.Count) bytes=$runtimeBytes"
Write-Host "Catalog: $CatalogOutput"
Write-Host "Runtime: $RuntimeOutput"
Write-Host "Next: godot --headless --editor --path `"$ProjectRoot`" --quit-after 600"
