namespace BeatSaverJson
{
    public class BeatSaverDiff
    {
        public double njs { get; set; }
        public double nps { get; set; }
        public double seconds { get; set; }
        public string characteristic { get; set; }
        public string difficulty { get; set; }
    }

    public class BeatSaverVersion
    {
        public string hash { get; set; }
        public BeatSaverDiff[] diffs { get; set; }
    }

    public class BeatSaverSongInfo
    {
        public string id { get; set; }
        public BeatSaverVersion[] versions { get; set; }
    }
}
