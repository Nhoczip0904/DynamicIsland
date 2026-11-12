namespace DynamicIsland;

static class Timeline
{
    /// <summary>
    /// Players only report the position when it changes state, so while playing
    /// add the time since that report. Clamped to [0, duration].
    /// </summary>
    public static TimeSpan Current(TimeSpan reported, DateTimeOffset reportedAt, DateTimeOffset now, bool playing, TimeSpan duration)
    {
        TimeSpan position = playing ? reported + (now - reportedAt) : reported;
        if (position < TimeSpan.Zero) return TimeSpan.Zero;
        return position > duration ? duration : position;
    }

    public static TimeSpan AtFraction(double fraction, TimeSpan duration) => duration * Math.Clamp(fraction, 0, 1);

    public static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
