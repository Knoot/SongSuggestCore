param(
    [string]$SongLibraryPath = "..\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json",
    [string]$OutputPath = $SongLibraryPath,
    [long]$AfterTime = -1,
    [int]$DelayMilliseconds = 220,
    [int]$MaxAddedWithoutForce = 500,
    [switch]$DryRun,
    [switch]$ForceLargeChanges
)

$ErrorActionPreference = "Stop"
$beatLeaderCategory = 16

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
    if (($songCategory -band $Category) -ne 0) {
        Set-SongProperty $Song "songCategory" ($songCategory -band (-bnot $Category))
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
    if (Remove-SongCategory $song $beatLeaderCategory) {
        Set-SongProperty $song "starBeatLeader" 0.0
        Set-SongProperty $song "starAccBeatLeader" 0.0
        Set-SongProperty $song "starPassBeatLeader" 0.0
        Set-SongProperty $song "starTechBeatLeader" 0.0
        Set-SongProperty $song "beatLeaderID" $null
        $removed++
    }
}

$client = [System.Net.WebClient]::new()
$client.Encoding = [System.Text.Encoding]::UTF8

Write-Host "BeatLeader ranked songs: requesting after_time=$AfterTime"
$rankedSongs = $client.DownloadString("https://api.beatleader.com/songsuggest/songs?after_time=$AfterTime") | ConvertFrom-Json
$rankedSongs = @($rankedSongs | Where-Object { $_.mode -eq "Standard" -or $_.mode -eq "OneSaber" })
Write-Host "BeatLeader ranked songs received: $($rankedSongs.Count)"

Start-Sleep -Milliseconds $DelayMilliseconds

$matched = 0
$added = 0
$updated = 0
$skipped = 0
$missingSamples = [System.Collections.Generic.List[string]]::new()

foreach ($rankedSong in $rankedSongs) {
    if ([string]::IsNullOrEmpty($rankedSong.hash) -or [string]::IsNullOrEmpty($rankedSong.mode)) {
        $skipped++
        continue
    }

    $characteristic = $rankedSong.mode
    $difficulty = Get-DifficultyValue $rankedSong.difficulty
    if ($difficulty -eq "0") {
        $skipped++
        continue
    }

    $hash = $rankedSong.hash.ToUpperInvariant()
    $key = "$($characteristic.ToUpperInvariant())|$difficulty|$hash"

    if (!$songsByInternalID.ContainsKey($key)) {
        if ($missingSamples.Count -lt 10) {
            $missingSamples.Add($key)
        }

        $song = [pscustomobject][ordered]@{
            name = $rankedSong.name
            scoreSaberID = $null
            beatLeaderID = "$($rankedSong.id)"
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
        @{ Name = "name"; Value = $rankedSong.name },
        @{ Name = "beatLeaderID"; Value = "$($rankedSong.id)" },
        @{ Name = "hash"; Value = $hash },
        @{ Name = "difficulty"; Value = $difficulty },
        @{ Name = "characteristic"; Value = $characteristic },
        @{ Name = "starBeatLeader"; Value = [double]$rankedSong.stars },
        @{ Name = "starAccBeatLeader"; Value = [double]$rankedSong.accRating },
        @{ Name = "starPassBeatLeader"; Value = [double]$rankedSong.passRating },
        @{ Name = "starTechBeatLeader"; Value = [double]$rankedSong.techRating }
    )) {
        $current = Get-SongProperty $song $assignment.Name
        if ("$current" -ne "$($assignment.Value)") {
            Set-SongProperty $song $assignment.Name $assignment.Value
            $changed = $true
        }
    }

    if ([double]$rankedSong.stars -gt 0) {
        if (Add-SongCategory $song $beatLeaderCategory) {
            $changed = $true
        }
    }
    else {
        if (Remove-SongCategory $song $beatLeaderCategory) {
            $changed = $true
        }
    }

    if ($changed) {
        $updated++
    }
}

$songs = $songs | Where-Object { [int](Get-SongProperty $_ "songCategory") -ne 0 }

Write-Host "Initial songs: $initialSongCount. Existing keys: $initialKeyCount. Matched: $matched. Added: $added. Updated: $updated. Removed old BeatLeader category: $removed. Skipped: $skipped. Total songs after merge: $($songs.Count)."
if ($missingSamples.Count -gt 0) {
    Write-Host "First missing BeatLeader keys:"
    foreach ($sample in $missingSamples) {
        Write-Host "  $sample"
    }
}

if ($DryRun) {
    Write-Host "Dry run requested. No files were written."
    exit 0
}

if (!$ForceLargeChanges -and $added -gt $MaxAddedWithoutForce) {
    throw "Refusing to write suspicious BeatLeader merge: added $added songs, which is above MaxAddedWithoutForce=$MaxAddedWithoutForce. Re-run with -ForceLargeChanges only after checking the input file and script output."
}

if (!$ForceLargeChanges -and $songs.Count -lt [Math]::Floor($initialSongCount * 0.9)) {
    throw "Refusing to write suspicious BeatLeader merge: song count would shrink from $initialSongCount to $($songs.Count). Re-run with -ForceLargeChanges only after checking the input file and script output."
}

$tempPath = "$resolvedOutputPath.tmp"
$songs | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tempPath -Encoding UTF8
Move-Item -LiteralPath $tempPath -Destination $resolvedOutputPath -Force

Write-Host "Done. Initial songs: $initialSongCount. Existing keys: $initialKeyCount. Matched: $matched. Added: $added. Updated: $updated. Removed old BeatLeader category: $removed. Skipped: $skipped. Total songs: $($songs.Count)."
