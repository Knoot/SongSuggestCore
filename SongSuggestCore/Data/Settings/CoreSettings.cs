using System.IO;

namespace Settings
{
    public class CoreSettings
    {
        public string UserID { get; set; } = "-1";
        public FilePathSettings FilePathSettings { get; set; }
        public TextWriter Log { get; set; } = null;
        public bool UseScoreSaberLeaderboard { get; set; } = true;
        public bool UpdateScoreSaberLeaderboard { get; set; } = true;
        public bool UseAccSaberLeaderboard { get; set; } = true;
        public bool UpdateAccSaberLeaderboard { get; set; } = true;
        public bool UseBeatLeaderLeaderboard { get; set; } = true;
        public bool UpdateBeatLeaderLeaderboard { get; set; } = true;
        // Startup only; callers such as the game client must initialize Core on a worker thread.
        public System.TimeSpan BeatLeaderStartupCheckDelay { get; set; } = System.TimeSpan.Zero;
        // Called on the thread performing the operation; UI clients should only publish the text here.
        public System.Action<string> StatusChanged { get; set; }
        //Removes plays with Score Saber shared songs only in the Beat Leader score data.
        public bool FilterScoreSaberBiasInBeatLeader { get; set; } = true;
        //Allows a client to increase web requests to limits, should only be used if not using other web requests during run.
        public bool MaxWebRequests { get; set; } = false;
        public bool ValidateCacheFilesOnInitialize { get; set; } = true;
        public bool LoadRuntimeDataOnInitialize { get; set; } = true;
    }
}
