using System;
using SongLibraryNS;

namespace AccSaberReloadedJson
{
    public class AccSaberReloadedLastUpdated
    {
        public DateTime refreshTime { get; set; }
    }

    public class AccSaberReloadedRankedSong
    {
        public string songName { get; set; }
        public string categoryCode { get; set; }
        public double complexity { get; set; }
        public string difficulty { get; set; }
        public string songHash { get; set; }
        public string blLeaderboardId { get; set; }
        public string ssLeaderboardId { get; set; }

        public SongID internalID { get; set; }
    }
}