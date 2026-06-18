param(
    [string]$SongLibraryPath = "..\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json",
    [string]$OutputPath = $SongLibraryPath,
    [int]$DelayMilliseconds = 220,
    [int]$MaxPages = 0,
    [int]$MaxAddedWithoutForce = 500,
    [switch]$DryRun,
    [switch]$ForceLargeChanges
)

$ErrorActionPreference = "Stop"
$scoreSaberCategory = 1

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

function Get-Characteristic {
    param([string]$GameMode)

    if ($GameMode.StartsWith("Solo")) {
        return $GameMode.Substring(4)
    }

    return $GameMode
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

    $difficulty = Get-SongProperty $song "difficulty"
    $key = "$($characteristic.ToUpperInvariant())|$difficulty|$($hash.ToUpperInvariant())"
    $songsByInternalID[$key] = $song
}
$initialKeyCount = $songsByInternalID.Count

$client = [System.Net.WebClient]::new()
$client.Encoding = [System.Text.Encoding]::UTF8

$firstPage = $client.DownloadString("https://scoresaber.com/api/leaderboards?ranked=true&page=1") | ConvertFrom-Json
$total = [int]$firstPage.metadata.total
$itemsPerPage = [int]$firstPage.metadata.itemsPerPage
$availablePages = [int][Math]::Ceiling($total / $itemsPerPage)
$pages = $availablePages
$isPartialFetch = $false
if ($MaxPages -gt 0 -and $MaxPages -lt $pages) {
    $pages = $MaxPages
    $isPartialFetch = $true
}
$leaderboards = [System.Collections.Generic.List[object]]::new()

foreach ($leaderboard in @($firstPage.leaderboards)) {
    $leaderboards.Add($leaderboard)
}

Write-Host "ScoreSaber ranked: page 1 / $pages, entries: $($leaderboards.Count) / $total"

for ($page = 2; $page -le $pages; $page++) {
    $pageData = $client.DownloadString("https://scoresaber.com/api/leaderboards?ranked=true&page=$page") | ConvertFrom-Json
    foreach ($leaderboard in @($pageData.leaderboards)) {
        $leaderboards.Add($leaderboard)
    }

    if ($page % 25 -eq 0 -or $page -eq $pages) {
        Write-Host "ScoreSaber ranked: page $page / $pages, entries: $($leaderboards.Count) / $total"
    }

    Start-Sleep -Milliseconds $DelayMilliseconds
}

$rankedScoreSaberIDs = @{}
foreach ($leaderboard in $leaderboards) {
    $rankedScoreSaberIDs["$($leaderboard.id)"] = $true
}

$added = 0
$updated = 0
$deranked = 0
$matched = 0
$missingSamples = [System.Collections.Generic.List[string]]::new()

if ($isPartialFetch) {
    Write-Host "Partial fetch requested with MaxPages=$MaxPages. Derank detection is skipped."
}
else {
    foreach ($song in $songs) {
        $scoreSaberID = Get-SongProperty $song "scoreSaberID"
        if ([string]::IsNullOrEmpty($scoreSaberID) -or $rankedScoreSaberIDs.ContainsKey("$scoreSaberID")) {
            continue
        }

        $songCategory = [int](Get-SongProperty $song "songCategory")
        if (($songCategory -band $scoreSaberCategory) -ne 0) {
            Set-SongProperty $song "songCategory" ($songCategory -band (-bnot $scoreSaberCategory))
            Set-SongProperty $song "starScoreSaber" 0.0
            $deranked++
        }
    }
}

foreach ($leaderboard in $leaderboards) {
    $characteristic = Get-Characteristic $leaderboard.difficulty.gameMode
    $difficulty = "$($leaderboard.difficulty.difficulty)"
    $hash = $leaderboard.songHash.ToUpperInvariant()
    $key = "$($characteristic.ToUpperInvariant())|$difficulty|$hash"

    if (!$songsByInternalID.ContainsKey($key)) {
        if ($missingSamples.Count -lt 10) {
            $missingSamples.Add($key)
        }

        $song = [pscustomobject][ordered]@{
            name = $leaderboard.songName
            scoreSaberID = "$($leaderboard.id)"
            hash = $hash
            difficulty = $difficulty
            characteristic = $characteristic
            songCategory = $scoreSaberCategory
            starScoreSaber = [double]$leaderboard.stars
        }
        $songs.Add($song)
        $songsByInternalID[$key] = $song
        $added++
        continue
    }

    $song = $songsByInternalID[$key]
    $matched++

    $changed = $false
    foreach ($assignment in @(
        @{ Name = "name"; Value = $leaderboard.songName },
        @{ Name = "scoreSaberID"; Value = "$($leaderboard.id)" },
        @{ Name = "hash"; Value = $hash },
        @{ Name = "difficulty"; Value = $difficulty },
        @{ Name = "characteristic"; Value = $characteristic },
        @{ Name = "starScoreSaber"; Value = [double]$leaderboard.stars }
    )) {
        $current = Get-SongProperty $song $assignment.Name
        if ("$current" -ne "$($assignment.Value)") {
            Set-SongProperty $song $assignment.Name $assignment.Value
            $changed = $true
        }
    }

    $songCategory = [int](Get-SongProperty $song "songCategory")
    if (($songCategory -band $scoreSaberCategory) -eq 0) {
        Set-SongProperty $song "songCategory" ($songCategory -bor $scoreSaberCategory)
        $changed = $true
    }

    if ($changed) {
        $updated++
    }
}

$songs = $songs | Where-Object { [int](Get-SongProperty $_ "songCategory") -ne 0 }

Write-Host "Initial songs: $initialSongCount. Existing keys: $initialKeyCount. Matched: $matched. Added: $added. Updated: $updated. Deranked: $deranked. Total songs after merge: $($songs.Count)."
if ($missingSamples.Count -gt 0) {
    Write-Host "First missing ScoreSaber keys:"
    foreach ($sample in $missingSamples) {
        Write-Host "  $sample"
    }
}

if ($DryRun) {
    Write-Host "Dry run requested. No files were written."
    exit 0
}

if ($isPartialFetch) {
    throw "MaxPages is intended for dry runs only. Re-run without -MaxPages to write a complete ScoreSaber merge."
}

if (!$ForceLargeChanges -and $added -gt $MaxAddedWithoutForce) {
    throw "Refusing to write suspicious ScoreSaber merge: added $added songs, which is above MaxAddedWithoutForce=$MaxAddedWithoutForce. Re-run with -ForceLargeChanges only after checking the input file and script output."
}

if (!$ForceLargeChanges -and $songs.Count -lt [Math]::Floor($initialSongCount * 0.9)) {
    throw "Refusing to write suspicious ScoreSaber merge: song count would shrink from $initialSongCount to $($songs.Count). Re-run with -ForceLargeChanges only after checking the input file and script output."
}

$tempPath = "$resolvedOutputPath.tmp"
$songs | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tempPath -Encoding UTF8
Move-Item -LiteralPath $tempPath -Destination $resolvedOutputPath -Force

Write-Host "Done. Initial songs: $initialSongCount. Existing keys: $initialKeyCount. Matched: $matched. Added: $added. Updated: $updated. Deranked: $deranked. Total songs: $($songs.Count)."
