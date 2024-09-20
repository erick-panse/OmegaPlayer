﻿using System;

namespace OmegaPlayer.Models
{
    public class UserActivity
    {
        public int UserActivityID { get; set; }
        public int TrackID { get; set; }
        public int ProfileID { get; set; }
        public DateTime ActivityTime { get; set; }
        public string ActivityType { get; set; }
    }
}