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

## BeatLeader PPv4

Run once before packaging the PPv4 release:

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --beatleader --large-song-library-update
```

This downloads the noMods population and complete per-map PPv4 inputs. The first
run can take tens of minutes. Progress is logged as `PPv4 map metadata: N/total`.
Pending metadata is checkpointed every 20 maps for the same server revision;
repeat the same command after a network failure. `--force-beatleader` discards
checkpoint reuse and rebuilds everything. A changed server revision requires a
new consistent snapshot; never override a snapshot/rating mismatch.

The persistent output contains `BeatLeaderDataset.meta` and immutable generations
in `BeatLeaderData/<generation>/` (ratings, population, and source songs). The
manifest is committed last and contains SHA-256 hashes. A derived
`BeatLeaderLeaderboard.json` is also generated for the existing recommendation
algorithm. These files are no longer stored in the tool's temporary directory.
Do not copy only Files.meta or only the ratings file. The release script packages
the manifest and its complete generation and refuses missing/corrupt PPv4 data.

The first successful PPv4 export automatically uses a major song-library bump.
Later runs use the requested normal version policy. This does not refresh
ScoreSaber Top10K or change its version.

Existing BL user PP caches are invalidated once per model/context/dataset revision
and downloaded transactionally in noMods. Local accuracy/history is preserved.
All new Local/Session PP calculations run offline with the installed map metadata;
no PPv3 formula or fallback exists. Missing required data produces a visible error
instead of a silently reduced playlist. Old local empty-modifier records retain
their historical assumed-noMods meaning; new records capture actual modifiers.

## Regression Checks

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.Tests -c Release
```

Add `-- --live` to additionally verify the difficulty DTO and PP values against
the current BeatLeader API. This uses network requests; the default run is offline.

The API may report tiny negative `sliderPercentage` residuals for zero-slider
maps (for example, `-2.792416E-08` on `3db71x91`). Values down to `-1E-6` are
accepted unchanged for formula parity; larger negative values, missing fields,
NaN and infinity are rejected. Invalid metadata is diagnosed separately from
snapshot/live rating differences, which remain a hard error.

The dependency-free executable test project exits nonzero on failure. Fixed raw
API fixtures cover normal, short acc, speed, and tech maps at 90/95/97/98/99%.
The numerical tolerance is max(0.01 PP, official PP * 0.0001). Tests also exercise
offline Local/Session scoring, cache transactions, timestamps and file integrity.
Live production parity, a complete dataset export and in-game smoke tests are
separate release gates, not replaced by the offline suite.
