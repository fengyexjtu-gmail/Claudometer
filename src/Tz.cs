using System;

namespace TokenMeter
{
    /// <summary>
    /// Display timezone. Follows the machine by default; a fixed zone can be chosen instead so the
    /// window times don't move when a laptop travels.
    /// </summary>
    public static class Tz
    {
        /// <summary>Sentinel id: use whatever zone Windows is set to.</summary>
        public const string LocalId = "local";
        public const string DefaultId = LocalId;

        /// <summary>
        /// The short list the settings dialog offers before "More time zones..." - one well-known
        /// zone per major region, as Windows ids (the dialog shows Windows' localized names).
        /// </summary>
        public static readonly string[] CommonIds =
        {
            "Pacific Standard Time", "Mountain Standard Time", "Central Standard Time",
            "Eastern Standard Time", "UTC", "GMT Standard Time", "W. Europe Standard Time",
            "Russian Standard Time", "India Standard Time", "China Standard Time",
            "Singapore Standard Time", "Tokyo Standard Time", "AUS Eastern Standard Time",
        };

        private static TimeZoneInfo _zone;
        private static string _id;

        public static void Use(string id)
        {
            _id = string.IsNullOrEmpty(id) ? DefaultId : id;
            _zone = null;
        }

        public static TimeZoneInfo Zone
        {
            get
            {
                if (_zone != null) return _zone;
                if (_id == null || _id == LocalId) return _zone = TimeZoneInfo.Local;
                try { _zone = TimeZoneInfo.FindSystemTimeZoneById(_id); }
                catch (Exception) { _zone = TimeZoneInfo.Local; }   // a zone this machine doesn't know
                return _zone;
            }
        }

        public static string Id { get { return Zone.Id; } }

        /// <summary>Converts a UTC instant to the display zone.</summary>
        public static DateTime Show(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            try { return TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(), Zone); }
            catch (Exception) { return utc.ToLocalTime(); }
        }

        /// <summary>Converts a wall-clock time in the display zone back to UTC.</summary>
        public static DateTime ToUtc(DateTime displayTime)
        {
            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(displayTime, DateTimeKind.Unspecified), Zone);
            }
            catch (Exception) { return displayTime.ToUniversalTime(); }
        }

        /// <summary>The zone's current offset, e.g. "UTC+8" or "UTC+5:30" (DST-aware).</summary>
        public static string Offset(TimeZoneInfo z)
        {
            TimeSpan off = z.GetUtcOffset(DateTime.UtcNow);
            string sign = off < TimeSpan.Zero ? "-" : "+";
            string hours = Math.Abs(off.Hours).ToString();
            if (off.Minutes != 0) hours += ":" + Math.Abs(off.Minutes).ToString("00");
            return "UTC" + sign + hours;
        }
    }
}
