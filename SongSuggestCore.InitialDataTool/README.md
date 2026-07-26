# SongSuggestCore InitialData Tool

Updates SmartSongSuggest bundled `InitialData` through SongSuggestCore C# refresh code.

Run from the shared workspace root:

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --all-song-data
```

Song data updates bump the minor `songLibraryVersion` by default. Add `--large-song-library-update` when the update should bump the major version.
Use `--bump-song-library-version` only when you need to bump `songLibraryVersion` without refreshing data.

Useful targeted runs:

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --scoresaber-songs

dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --beatleader --accsaber

dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --beatsaver-metadata --beatsaver-max 100
```

`--scoresaber-top10k` refreshes `Top10KPlayers.json` from ScoreSaber and is intentionally separate because it performs a long crawl over player score pages.

Run it separately:

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --scoresaber-top10k
```

Use `--large-top10k-update` with `--scoresaber-top10k` when the refreshed top-player data should bump the major `top10kVersion` and `songLibraryVersion`.
