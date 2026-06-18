param(
    [string]$SongLibraryPath = "..\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json",
    [string]$OutputPath = $SongLibraryPath,
    [int]$DelayMilliseconds = 220,
    [int]$MaxAddedWithoutForce = 500,
    [switch]$DryRun,
    [switch]$ForceLargeChanges
)

$ErrorActionPreference = "Stop"
$accSaberTrueCategory = 2
$accSaberStandardCategory = 4
$accSaberTechCategory = 8
$allAccSaberCategories = $accSaberTrueCategory -bor $accSaberStandardCategory -bor $accSaberTechCategory

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

function Get-AccSaberCategory {
    param([string]$CategoryCode)

    switch ($CategoryCode) {
        "true_acc" { return $accSaberTrueCategory }
        "standard_acc" { return $accSaberStandardCategory }
        "tech_acc" { return $accSaberTechCategory }
        default { return 0 }
    }
}

function Add-SongCategory {
    param($Song, [int]$Category)

    $songCategory = [int](Get-SongProperty $Song "songCategory")
    if (($songCategory -band $Category) -eq 0) {
        Set-SongProperty $Song "songCategory" ($songCategory -bor $Category)
        return $true
    }

    return $false
}

function Remove-SongCategory {
    param($Song, [int]$Category)

    $songCategory = [int](Get-SongProperty $Song "songCategory")
    $newCategory = $songCategory -band (-bnot $Category)
    if ($newCategory -ne $songCategory) {
        Set-SongProperty $Song "songCategory" $newCategory
        return $true
    }

    return $false
}

$resolvedPath = Resolve-Path -LiteralPath $SongLibraryPath
$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$songs = [System.Collections.Generic.List[object]]::new()
$loadedSongs = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
foreach ($song in $loadedSongs) {
    $songs.Add($song)
}

$initialSongCount = $songs.Count
$songsByInternalID = @{}
foreach ($song in $songs) {
    $characteristic = Get-SongProperty $song "characteristic"
    if ([string]::IsNullOrEmpty($characteristic)) {
        $characteristic = "Standard"
        Set-SongProperty $song "characteristic" $characteristic
    }

    $hash = Get-SongProperty $song "hash"
    if ([string]::IsNullOrEmpty($hash)) {
        continue
    }

    $difficulty = Get-DifficultyValue (Get-SongProperty $song "difficulty")
    Set-SongProperty $song "difficulty" $difficulty
    $key = "$($characteristic.ToUpperInvariant())|$difficulty|$($hash.ToUpperInvariant())"
    $songsByInternalID[$key] = $song
}
$initialKeyCount = $songsByInternalID.Count

$removed = 0
foreach ($song in $songs) {
    if (Remove-SongCategory $song $allAccSaberCategories) {
        Set-SongProperty $song "complexityAccSaber" 0.0
        $removed++
    }
}

$client = [System.Net.WebClient]::new()
$client.Encoding = [System.Text.Encoding]::UTF8

Write-Host "AccSaber Reloaded ranked songs: requesting all difficulties"
$rankedSongs = $client.DownloadString("https://api.accsaberreloaded.com/v1/maps/difficulties/all") | ConvertFrom-Json
$rankedSongs = @($rankedSongs)
Write-Host "AccSaber Reloaded ranked songs received: $($rankedSongs.Count)"

Start-Sleep -Milliseconds $DelayMilliseconds

$matched = 0
$added = 0
$updated = 0
$skipped = 0
$missingSamples = [System.Collections.Generic.List[string]]::new()

foreach ($rankedSong in $rankedSongs) {
    $category = Get-AccSaberCategory $rankedSong.categoryCode
    if ($category -eq 0 -or [string]::IsNullOrEmpty($rankedSong.songHash)) {
        $skipped++
        continue
    }

    $characteristic = "Standard"
    $difficulty = Get-DifficultyValue $rankedSong.difficulty
    if ($difficulty -eq "0") {
        $skipped++
        continue
    }

    $hash = $rankedSong.songHash.ToUpperInvariant()
    $key = "$($characteristic.ToUpperInvariant())|$difficulty|$hash"

    if (!$songsByInternalID.ContainsKey($key)) {
        if ($missingSamples.Count -lt 10) {
            $missingSamples.Add($key)
        }

        $song = [pscustomobject][ordered]@{
            name = $rankedSong.songName
            scoreSaberID = "$($rankedSong.ssLeaderboardId)"
            beatLeaderID = "$($rankedSong.blLeaderboardId)"
            hash = $hash
            difficulty = $difficulty
            characteristic = $characteristic
            songCategory = 0
            starScoreSaber = 0.0
            starBeatLeader = 0.0
            starAccBeatLeader = 0.0
            starPassBeatLeader = 0.0
            starTechBeatLeader = 0.0
            complexityAccSaber = 0.0
            complexityAutoBalancer = 0.0
        }
        $songs.Add($song)
        $songsByInternalID[$key] = $song
        $added++
    }
    else {
        $song = $songsByInternalID[$key]
        $matched++
    }

    $changed = $false
    foreach ($assignment in @(
        @{ Name = "name"; Value = $rankedSong.songName },
        @{ Name = "scoreSaberID"; Value = "$($rankedSong.ssLeaderboardId)" },
        @{ Name = "beatLeaderID"; Value = "$($rankedSong.blLeaderboardId)" },
        @{ Name = "hash"; Value = $hash },
        @{ Name = "difficulty"; Value = $difficulty },
        @{ Name = "characteristic"; Value = $characteristic },
        @{ Name = "complexityAccSaber"; Value = [double]$rankedSong.complexity }
    )) {
        $current = Get-SongProperty $song $assignment.Name
        if ("$current" -ne "$($assignment.Value)") {
            Set-SongProperty $song $assignment.Name $assignment.Value
            $changed = $true
        }
    }

    if (Add-SongCategory $song $category) {
        $changed = $true
    }

    if ($changed) {
        $updated++
    }
}

$songs = $songs | Where-Object { [int](Get-SongProperty $_ "songCategory") -ne 0 }

Write-Host "Initial songs: $initialSongCount. Existing keys: $initialKeyCount. Matched: $matched. Added: $added. Updated: $updated. Removed old AccSaber categories: $removed. Skipped: $skipped. Total songs after merge: $($songs.Count)."
if ($missingSamples.Count -gt 0) {
    Write-Host "First missing AccSaber keys:"
    foreach ($sample in $missingSamples) {
        Write-Host "  $sample"
    }
}

if ($DryRun) {
    Write-Host "Dry run requested. No files were written."
    exit 0
}

if (!$ForceLargeChanges -and $added -gt $MaxAddedWithoutForce) {
    throw "Refusing to write suspicious AccSaber merge: added $added songs, which is above MaxAddedWithoutForce=$MaxAddedWithoutForce. Re-run with -ForceLargeChanges only after checking the input file and script output."
}

if (!$ForceLargeChanges -and $songs.Count -lt [Math]::Floor($initialSongCount * 0.9)) {
    throw "Refusing to write suspicious AccSaber merge: song count would shrink from $initialSongCount to $($songs.Count). Re-run with -ForceLargeChanges only after checking the input file and script output."
}

$tempPath = "$resolvedOutputPath.tmp"
$songs | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tempPath -Encoding UTF8
Move-Item -LiteralPath $tempPath -Destination $resolvedOutputPath -Force

Write-Host "Done. Initial songs: $initialSongCount. Existing keys: $initialKeyCount. Matched: $matched. Added: $added. Updated: $updated. Removed old AccSaber categories: $removed. Skipped: $skipped. Total songs: $($songs.Count)."
