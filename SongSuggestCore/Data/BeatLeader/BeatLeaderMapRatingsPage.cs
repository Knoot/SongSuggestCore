using System.Collections.Generic;

namespace BeatLeader
{
    public sealed class BeatLeaderMapRatingsPage
    {
        public int Page { get; set; }
        public int ItemsPerPage { get; set; }
        public int Total { get; set; }
        public List<BeatLeaderMapRating> Ratings { get; set; }
    }
}
