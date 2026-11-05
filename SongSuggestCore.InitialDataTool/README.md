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

## BeatLeader PPv4.1

Core 2.5.0 / UI 2.3.0 use model `ppv4.1-2026-11-05`, pinned to RatingAPI
`48cc07c61dd57a99d872c20b3df9085ffe9ba494` and website
`12415e959374f1813120fd67b9889ee3054e715e`. The October PPv4 model is obsolete;
updating only the ratings without updating Core is not compatible.
The model change rejects old dataset manifests and pending checkpoints even if
their server timestamp matches. Old BL user PP requires a successful full refresh;
Local accuracy, modifiers and dates, and SS/Acc caches are preserved.

Run once before packaging the PPv4 release:

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.InitialDataTool -- `
  --initial-data-dir .\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData `
  --beatleader --large-song-library-update
```

This downloads the noMods population and complete PPv4 inputs using ranked
leaderboard pages of 100 difficulties (about 40 requests for 3968 difficulties,
instead of one request per difficulty). Progress includes the page number.
Page numbers, totals, lengths, duplicate IDs, required inputs and snapshot ratings
are validated before commit. At most five missing difficulties may use individual
requests; a larger gap aborts the update instead of starting a mass fallback.
Pending metadata is saved after each page. An interrupted partial traversal restarts
at page one to avoid skipping records if ordering changed; a fully traversed
checkpoint can be reused for the same server revision. Old partial checkpoints
are not reused. `--force-beatleader` rebuilds everything. A changed server revision
requires a new consistent snapshot; never override a snapshot/rating mismatch.

The game client runs the existing update during background initialization, no
earlier than 10 seconds after SmartSongSuggest initialization starts. It still
applies the update in that launch; there is no next-session activation rule.
The Unity main thread does not wait on the delay, but SmartSongSuggest readiness
still waits for initialization. This delay does not apply to InitialDataTool.

The persistent output contains `BeatLeaderDataset.meta` and immutable generations
in `BeatLeaderData/<generation>/` (ratings, population, and source songs). The
manifest is committed last and contains SHA-256 hashes. A derived
`BeatLeaderLeaderboard.json` is also generated for the existing recommendation
algorithm. These files are no longer stored in the tool's temporary directory.
Do not copy only Files.meta or only the ratings file. The release script packages
the manifest and its complete generation and refuses missing/corrupt PPv4 data.
Before commit, the updater compares computed stars and official PP on reference
maps and samples top/middle/bottom supported population profiles. Each sampled
profile must contain unchanged results agreeing with its snapshot, and its live
results must agree with the local formula. Improvements after the snapshot are
allowed, but no matching reference results aborts the update. This is a sample
check, not a full audit of every population score.

The first successful export of a new PP model automatically uses a major song-library bump.
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

The active fixtures are `Fixtures/ppv41-official.json`: eight maps including
OneSaber, 72 official values at 0/90/95/96/97/98/99/99.5/100%, with capture times
and pinned source revisions. The October `ppv4-official.json` and
`ppv4-slider-noise.json` remain historical references, not current expectations.
Refresh fixtures only against a verified compatible production formula:

```powershell
dotnet run --project .\SongSuggestCore\SongSuggestCore.Tests -c Release -- `
  --capture-fixtures .\SongSuggestCore\SongSuggestCore.Tests\Fixtures\ppv41-official.json
```

The recorder refuses formula/API differences and rating changes during capture.
