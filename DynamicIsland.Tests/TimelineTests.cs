namespace DynamicIsland.Tests;

public class TimelineTests
{
    static readonly DateTimeOffset Reported = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Duration = TimeSpan.FromMinutes(4);

    [Fact]
    public void Playing_adds_time_since_report()
    {
        TimeSpan now = Timeline.Current(TimeSpan.FromSeconds(30), Reported, Reported.AddSeconds(5), playing: true, Duration);
        Assert.Equal(TimeSpan.FromSeconds(35), now);
    }

    [Fact]
    public void Paused_keeps_reported_position()
    {
        TimeSpan now = Timeline.Current(TimeSpan.FromSeconds(30), Reported, Reported.AddSeconds(5), playing: false, Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), now);
    }

    [Fact]
    public void Stale_report_is_clamped_to_duration()
    {
        TimeSpan now = Timeline.Current(TimeSpan.FromMinutes(3), Reported, Reported.AddMinutes(10), playing: true, Duration);
        Assert.Equal(Duration, now);
    }

    [Theory]
    [InlineData(0.25, 60)]
    [InlineData(-0.5, 0)]   // dragged past the left edge
    [InlineData(1.7, 240)]  // dragged past the right edge
    public void Seek_fraction_maps_to_clamped_position(double fraction, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Timeline.AtFraction(fraction, Duration));

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Formats_like_a_player(int seconds, string expected) =>
        Assert.Equal(expected, Timeline.Format(TimeSpan.FromSeconds(seconds)));
}
