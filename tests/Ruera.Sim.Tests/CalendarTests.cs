using Ruera.Sim.Calendar;

namespace Ruera.Sim.Tests;

public class CalendarTests
{
    [Fact]
    public void Tick0_IsEpoch()
    {
        var date = SimCalendar.Milano1880().DateAt(0);

        Assert.Equal(new SimDate(1880, 1, 1, Weekday.Thursday), date);
    }

    [Fact]
    public void MatchesDotNetGregorian_OverSixtyYears()
    {
        // Cross-check our integer calendar against an independent
        // implementation (DateTime is fine in tests; it is banned in the sim).
        var calendar = SimCalendar.Milano1880();
        var epoch = new DateTime(1880, 1, 1);

        for (long tick = 0; tick < 22_000; tick++)
        {
            var expected = epoch.AddDays(tick);
            var actual = calendar.DateAt(tick);

            Assert.Equal(expected.Year, actual.Year);
            Assert.Equal(expected.Month, actual.Month);
            Assert.Equal(expected.Day, actual.Day);
            Assert.Equal(((int)expected.DayOfWeek + 6) % 7, (int)actual.Weekday);
        }
    }

    [Fact]
    public void LeapYears_FollowGregorianRules()
    {
        var calendar = SimCalendar.Milano1880();

        // 1880 is a leap year: tick 59 (Jan has 31 days, Feb 28 is tick 58) is Feb 29.
        var leapDay = calendar.DateAt(59);
        Assert.Equal((1880, 2, 29), (leapDay.Year, leapDay.Month, leapDay.Day));

        // 1900 is NOT a leap year (divisible by 100, not by 400): Feb 28 + 1 = Mar 1.
        var feb28Of1900 = (new DateTime(1900, 2, 28) - new DateTime(1880, 1, 1)).Days;
        var next = calendar.DateAt(feb28Of1900 + 1);
        Assert.Equal((1900, 3, 1), (next.Year, next.Month, next.Day));
    }

    [Fact]
    public void Sundays_AreNotWorkingDays_SaturdaysAre()
    {
        var calendar = SimCalendar.Milano1880();

        // 1880-01-01 is Thursday: tick 2 = Saturday, tick 3 = Sunday.
        Assert.True(calendar.IsWorkingDay(2));  // six-day working week
        Assert.False(calendar.IsWorkingDay(3));
    }

    [Fact]
    public void FixedHolidays_AreNotWorkingDays()
    {
        var calendar = SimCalendar.Milano1880();

        var natale = (new DateTime(1880, 12, 25) - new DateTime(1880, 1, 1)).Days;
        var santAmbrogio = (new DateTime(1881, 12, 7) - new DateTime(1880, 1, 1)).Days;

        Assert.True(calendar.IsHoliday(calendar.DateAt(natale)));
        Assert.False(calendar.IsWorkingDay(natale));
        Assert.False(calendar.IsWorkingDay(santAmbrogio));
    }

    [Fact]
    public void ATick_KnowsItsDate_ThroughTheSimulation()
    {
        var sim = new Simulation(0);

        sim.Advance(31);

        Assert.Equal(new SimDate(1880, 2, 1, Weekday.Sunday), sim.Today);
        Assert.False(sim.IsWorkingDay);
    }

    [Fact]
    public void SimDate_FormatsInvariant()
    {
        Assert.Equal("1880-01-01", SimCalendar.Milano1880().DateAt(0).ToString());
    }
}
