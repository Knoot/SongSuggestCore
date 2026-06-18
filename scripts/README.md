# InitialData update scripts

Run these from `W:\BeatSaber\SmartSongSuggest` in a normal PowerShell window, not from the VSCode integrated terminal.

## 1. Update ScoreSaber ranked songs

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryScoreSaber.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  *> .\scoresaber-update.log
```

This reads all current ScoreSaber ranked leaderboard pages and merges them into `SongLibrary.json`.
For the current initial library, `Added` should be small. If it reports thousands of added songs, stop and restore the file before continuing; the script now refuses to write more than 500 additions unless `-ForceLargeChanges` is passed.

## 2. Update BeatLeader ranked songs

Check first:

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryBeatLeader.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  -DryRun `
  *> .\beatleader-dryrun.log
```

Write after checking the dry run:

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryBeatLeader.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  *> .\beatleader-update.log
```

This rebuilds the BeatLeader category from `https://api.beatleader.com/songsuggest/songs?after_time=-1`.

## 3. Update AccSaber Reloaded ranked songs

Check first:

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryAccSaber.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  -DryRun `
  *> .\accsaber-dryrun.log
```

Write after checking the dry run:

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryAccSaber.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  *> .\accsaber-update.log
```

This rebuilds the AccSaber True, Standard, and Tech categories from `https://api.accsaberreloaded.com/v1/maps/difficulties/all`.

## 4. Add BeatSaver map metadata

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryBeatSaverMetadata.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  *> .\beatsaver-metadata-update.log
```

This adds `beatSaverID`, `njs`, `nps`, and `seconds` from BeatSaver `versions[].diffs[]`.

For later incremental runs, use `-OnlyMissing`:

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-SongLibraryBeatSaverMetadata.ps1 `
  -SongLibraryPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json `
  -OnlyMissing `
  *> .\beatsaver-metadata-update.log
```

Run this after ScoreSaber, BeatLeader, and AccSaber are already merged.

## 5. Bump metadata after a successful data update

```powershell
powershell -ExecutionPolicy Bypass -File .\SongSuggestCore\scripts\Update-InitialDataFilesMeta.ps1 `
  -FilesMetaPath .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\Files.meta `
  -SongLibraryVersion 62.0
```

Use a new major `songLibraryVersion` when the library content/format changes.

## Quick checks

```powershell
$songs = Get-Content .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\SongLibrary.json -Raw | ConvertFrom-Json
$songs.Count
($songs | Where-Object { $_.PSObject.Properties['njs'] }).Count
($songs | Where-Object { $_.PSObject.Properties['nps'] }).Count
($songs | Where-Object { $_.PSObject.Properties['seconds'] }).Count
```

`Top10KPlayers.json` is not refreshed by these scripts. The current upstream `Files.meta` already matches the local `Top10KPlayers.json` version. A true top-player refresh uses `Top10kRefresh` and requires a long ScoreSaber crawl over thousands of player score pages.
