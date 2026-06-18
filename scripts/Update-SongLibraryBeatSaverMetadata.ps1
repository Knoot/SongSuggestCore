param(
    [string]$SongLibraryPath = "..\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json",
    [string]$OutputPath = $SongLibraryPath,
    [int]$DelayMilliseconds = 80,
    [switch]$OnlyMissing,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Get-SongProperty {
    param($Song, [string]$Name)

    $property = $Song.PSObject.Properties[$Name]
    if ($null -ne $property) {
        return $property.Value
    }

    return $null
}

function Set-SongProperty {
    param($Song, [string]$Name, $Value)

    $property = $Song.PSObject.Properties[$Name]
    if ($null -ne $property) {
        $property.Value = $Value
    }
    else {
        $Song | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    }
}

function Get-DifficultyValue {
    param([string]$Difficulty)

    if ([string]::IsNullOrEmpty($Difficulty)) {
        return "0"
    }

    switch ($Difficulty.ToLowerInvariant()) {
        "easy" { return "1" }
        "normal" { return "3" }
        "hard" { return "5" }
        "expert" { return "7" }
        "expertplus" { return "9" }
        "expert+" { return "9" }
        "expert_plus" { return "9" }
        "1" { return "1" }
        "3" { return "3" }
        "5" { return "5" }
        "7" { return "7" }
        "9" { return "9" }
        default { return "0" }
    }
}

function Has-BeatSaverMetadata {
    param($Song)

    return $null -ne (Get-SongProperty $Song "njs") -and
        $null -ne (Get-SongProperty $Song "nps") -and
        $null -ne (Get-SongProperty $Song "seconds")
}

$resolvedPath = Resolve-Path -LiteralPath $SongLibraryPath
$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$songs = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
$groups = $songs | Group-Object -Property hash
$client = [System.Net.WebClient]::new()
$client.Encoding = [System.Text.Encoding]::UTF8

$hashIndex = 0
$updatedSongs = 0
$failedHashes = 0
$skippedHashes = 0
$matchedSongs = 0
$missingDiffs = 0
$failedSamples = [System.Collections.Generic.List[string]]::new()
$missingDiffSamples = [System.Collections.Generic.List[string]]::new()

foreach ($group in $groups) {
    $hashIndex++

    if ($OnlyMissing -and (@($group.Group) | Where-Object { -not (Has-BeatSaverMetadata $_) }).Count -eq 0) {
        $skippedHashes++
        continue
    }

    $hash = $group.Name.ToLowerInvariant()

    if ($hashIndex -eq 1 -or $hashIndex % 100 -eq 0) {
        Write-Host "BeatSaver metadata: $hashIndex / $($groups.Count), updated songs: $updatedSongs, failed hashes: $failedHashes, skipped hashes: $skippedHashes"
    }

    try {
        $json = $client.DownloadString("https://api.beatsaver.com/maps/hash/$hash")
        $map = $json | ConvertFrom-Json
    }
    catch {
        if ($failedSamples.Count -lt 20) {
            $failedSamples.Add("$hash - $($_.Exception.Message)")
        }

        $failedHashes++
        Start-Sleep -Milliseconds $DelayMilliseconds
        continue
    }

    $diffsByKey = @{}
    foreach ($version in @($map.versions)) {
        if ($null -eq $version.hash -or $version.hash.ToLowerInvariant() -ne $hash) {
            continue
        }

        foreach ($diff in @($version.diffs)) {
            $difficultyValue = Get-DifficultyValue $diff.difficulty
            $key = "$($diff.characteristic.ToUpperInvariant())|$difficultyValue"
            $diffsByKey[$key] = $diff
        }
    }

    foreach ($song in $group.Group) {
        $characteristic = Get-SongProperty $song "characteristic"
        if ([string]::IsNullOrEmpty($characteristic)) {
            $characteristic = "Standard"
            Set-SongProperty $song "characteristic" $characteristic
        }

        $difficulty = Get-SongProperty $song "difficulty"
        $key = "$($characteristic.ToUpperInvariant())|$difficulty"
        if (!$diffsByKey.ContainsKey($key)) {
            if ($missingDiffSamples.Count -lt 20) {
                $missingDiffSamples.Add("$hash $key")
            }

            $missingDiffs++
            continue
        }

        $matchedSongs++
        $diff = $diffsByKey[$key]
        $changed = $false

        if ((Get-SongProperty $song "beatSaverID") -ne $map.id) {
            Set-SongProperty $song "beatSaverID" $map.id
            $changed = $true
        }

        foreach ($property in @("njs", "nps", "seconds")) {
            $value = [double](Get-SongProperty $diff $property)
            $current = Get-SongProperty $song $property
            if ($null -eq $current -or [double]$current -ne $value) {
                Set-SongProperty $song $property $value
                $changed = $true
            }
        }

        if ($changed) {
            $updatedSongs++
        }
    }

    Start-Sleep -Milliseconds $DelayMilliseconds
}

Write-Host "BeatSaver metadata complete. Songs: $($songs.Count). Hashes: $($groups.Count). Matched songs: $matchedSongs. Updated songs: $updatedSongs. Missing diffs: $missingDiffs. Failed hashes: $failedHashes. Skipped hashes: $skippedHashes."
if ($failedSamples.Count -gt 0) {
    Write-Host "First failed BeatSaver hashes:"
    foreach ($sample in $failedSamples) {
        Write-Host "  $sample"
    }
}
if ($missingDiffSamples.Count -gt 0) {
    Write-Host "First missing BeatSaver diffs:"
    foreach ($sample in $missingDiffSamples) {
        Write-Host "  $sample"
    }
}

if ($DryRun) {
    Write-Host "Dry run requested. No files were written."
    exit 0
}

$tempPath = "$resolvedOutputPath.tmp"
$songs | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tempPath -Encoding UTF8
Move-Item -LiteralPath $tempPath -Destination $resolvedOutputPath -Force
Write-Host "Done. Updated songs: $updatedSongs. Failed hashes: $failedHashes. Skipped hashes: $skippedHashes."
