using MorganHacks.Applications.Forms;

namespace MorganHacks.Api.Tests;

public class MlhSeasonTests
{
    [Theory]
    [InlineData("2027-04-03T13:00:00Z", 2027)]
    [InlineData("2026-09-26T13:00:00Z", 2027)]
    [InlineData("2026-07-01T03:59:59Z", 2026)]
    [InlineData("2026-07-01T04:00:00Z", 2027)]
    [InlineData("2027-07-01T03:59:59Z", 2027)]
    [InlineData("2027-07-01T04:00:00Z", 2028)]
    [InlineData("2030-03-16T14:00:00Z", 2030)]
    [InlineData("2024-04-06T14:00:00Z", 2024)]
    public void Season_follows_the_event_date_in_Eastern_time(string startsAt, int season)
    {
        Assert.Equal(season, MlhSeason.For(DateTimeOffset.Parse(startsAt)));
    }

    [Fact]
    public void An_undated_event_does_not_guess_a_season()
    {
        Assert.Null(MlhSeason.For(null));
    }
}
