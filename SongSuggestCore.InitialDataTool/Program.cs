using Actions;
using Data;
using LinkedData;
using Settings;
using SongSuggestNS;

var options = InitialDataToolOptions.Parse(args);
if (options.ShowHelp)
{
    InitialDataToolOptions.PrintHelp();
    return 0;
}

if (!Directory.Exists(options.InitialDataDir))
{
    Console.Error.WriteLine($"InitialData directory not found: {options.InitialDataDir}");
    return 1;
}

var cacheDir = Path.Combine(Path.GetTempPath(), "SongSuggestCore.InitialDataTool", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cacheDir);

try
{
    SongSuggest.Log = Console.Out;

    var paths = new FilePathSettings(options.InitialDataDir)
    {
        songLibraryPath = "",
        top10kPlayersPath = "",
        filesDataPath = "",
        activePlayerDataPath = cacheDir,
        bannedSongsPath = cacheDir,
        likedSongsPath = cacheDir,
        lastSuggestionsPath = cacheDir,
        rankedData = cacheDir,
        playlistPath = cacheDir
    };

    var core = new SongSuggest(new CoreSettings
    {
        FilePathSettings = paths,
        UserID = "-1",
        UseScoreSaberLeaderboard = false,
        UpdateScoreSaberLeaderboard = false,
        UseAccSaberLeaderboard = false,
        UpdateAccSaberLeaderboard = false,
        UseBeatLeaderLeaderboard = false,
        UpdateBeatLeaderLeaderboard = false,
        ValidateCacheFilesOnInitialize = false,
        LoadRuntimeDataOnInitialize = false,
        MaxWebRequests = options.MaxWebRequests
    });

    if (options.UpdateScoreSaberSongs)
    {
        core.UpdateScoreSaberRankedSongLibrary();
    }

    if (options.UpdateScoreSaberTop10k)
    {
        var refresh = new Top10kRefresh { songSuggest = core };
        Top10kPlayers fullInfoPlayers = null;
        refresh.ComparativeBestTop10kPlayerDataPuller(ref fullInfoPlayers, options.LargeTop10kUpdate);
    }

    core.RefreshInitialData(
        updateScoreSaberSongs: false,
        updateBeatLeader: options.UpdateBeatLeader,
        updateAccSaber: options.UpdateAccSaber,
        updateBeatSaverMetadata: options.UpdateBeatSaverMetadata,
        onlyMissingBeatSaverMetadata: options.OnlyMissingBeatSaverMetadata,
        maxBeatSaverMetadataSongs: options.MaxBeatSaverMetadataSongs);

    if (options.BumpSongLibraryVersion)
    {
        InitialDataToolHelpers.BumpVersion(core, FilesMetaType.SongLibraryVersion, options.LargeSongLibraryUpdate);
    }

    Console.WriteLine("InitialData update completed.");
    return 0;
}
finally
{
    try
    {
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
    }
    catch
    {
        Console.Error.WriteLine($"Warning: failed to delete temporary cache directory: {cacheDir}");
    }
}

internal sealed class InitialDataToolOptions
{
    public string InitialDataDir { get; private set; } = Path.GetFullPath(Path.Combine(
        Environment.CurrentDirectory,
        "SmartSongSuggest",
        "TaohSongSuggest",
        "Configuration",
        "InitialData"));

    public bool UpdateScoreSaberSongs { get; private set; }
    public bool UpdateScoreSaberTop10k { get; private set; }
    public bool UpdateBeatLeader { get; private set; }
    public bool UpdateAccSaber { get; private set; }
    public bool UpdateBeatSaverMetadata { get; private set; }
    public bool OnlyMissingBeatSaverMetadata { get; private set; } = true;
    public int MaxBeatSaverMetadataSongs { get; private set; }
    public bool LargeTop10kUpdate { get; private set; }
    public bool LargeSongLibraryUpdate { get; private set; }
    public bool BumpSongLibraryVersionOnly { get; private set; }
    public bool BumpSongLibraryVersion =>
        UpdateScoreSaberSongs ||
        UpdateBeatLeader ||
        UpdateAccSaber ||
        UpdateBeatSaverMetadata ||
        BumpSongLibraryVersionOnly;
    public bool MaxWebRequests { get; private set; }
    public bool ShowHelp { get; private set; }

    public static InitialDataToolOptions Parse(string[] args)
    {
        var options = new InitialDataToolOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--initial-data-dir":
                    options.InitialDataDir = Path.GetFullPath(ReadValue(args, ref i, arg));
                    break;
                case "--scoresaber-songs":
                    options.UpdateScoreSaberSongs = true;
                    break;
                case "--scoresaber-top10k":
                    options.UpdateScoreSaberTop10k = true;
                    break;
                case "--beatleader":
                    options.UpdateBeatLeader = true;
                    break;
                case "--accsaber":
                    options.UpdateAccSaber = true;
                    break;
                case "--beatsaver-metadata":
                    options.UpdateBeatSaverMetadata = true;
                    break;
                case "--beatsaver-all":
                    options.OnlyMissingBeatSaverMetadata = false;
                    break;
                case "--beatsaver-max":
                    options.MaxBeatSaverMetadataSongs = int.Parse(ReadValue(args, ref i, arg));
                    break;
                case "--large-top10k-update":
                    options.LargeTop10kUpdate = true;
                    break;
                case "--large-song-library-update":
                    options.LargeSongLibraryUpdate = true;
                    break;
                case "--bump-song-library-version":
                    options.BumpSongLibraryVersionOnly = true;
                    break;
                case "--max-web-requests":
                    options.MaxWebRequests = true;
                    break;
                case "--all-song-data":
                    options.UpdateScoreSaberSongs = true;
                    options.UpdateBeatLeader = true;
                    options.UpdateAccSaber = true;
                    options.UpdateBeatSaverMetadata = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
SongSuggestCore InitialData update tool

Options:
  --initial-data-dir <path>    InitialData directory. Defaults to SmartSongSuggest/TaohSongSuggest/Configuration/InitialData under current directory.
  --scoresaber-songs           Refresh ScoreSaber ranked songs in SongLibrary.json.
  --scoresaber-top10k          Refresh Top10KPlayers.json from ScoreSaber. This is very long.
  --large-top10k-update        Bump major versions during --scoresaber-top10k.
  --bump-song-library-version  Bump minor songLibraryVersion without refreshing data.
  --large-song-library-update  Bump major songLibraryVersion for song data updates. Default is minor.
  --beatleader                 Refresh BeatLeader leaderboard and SongLibrary.json data.
  --accsaber                   Refresh AccSaber leaderboard and SongLibrary.json data.
  --beatsaver-metadata         Refresh BeatSaver beatSaverID/njs/nps/seconds metadata.
  --beatsaver-all              Refresh all BeatSaver metadata, not only missing rows.
  --beatsaver-max <count>      Limit BeatSaver metadata rows for test runs.
  --all-song-data              Refresh ScoreSaber songs, BeatLeader, AccSaber, and missing BeatSaver metadata.
  --max-web-requests           Use Core full-throttle mode.
""");
    }

    private static string ReadValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {name}");
        }

        index++;
        return args[index];
    }
}

internal static class InitialDataToolHelpers
{
    public static void BumpVersion(SongSuggest core, FilesMetaType type, bool major)
    {
        var filesMeta = core.fileHandler.LoadFilesMeta();
        if (major)
        {
            filesMeta.UpdateMajor(type);
        }
        else
        {
            filesMeta.UpdateMinor(type);
        }

        core.fileHandler.SaveFilesMeta(filesMeta);
        core.filesMeta = filesMeta;
    }
}
