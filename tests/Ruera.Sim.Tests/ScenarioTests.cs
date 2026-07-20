using Ruera.Sim.Calendar;
using Ruera.Sim.Data;
using Ruera.Sim.Persistence;
using Ruera.Sim.Scenario;
using Ruera.Sim.World;

using ScenarioModel = Ruera.Sim.Scenario.Scenario;

namespace Ruera.Sim.Tests;

/// <summary>
/// RUE-38: data-driven scenario loader + timeline effect engine, and the RUE-39
/// hard time cap. Verifies the committed base:milano-1880 scenario reproduces
/// the old hardcoded calendar, that SetCalendar timeline effects apply
/// deterministically and survive save/load, and that scenario identity covers
/// the timeline.
/// </summary>
public class ScenarioTests
{
    private static readonly string BaseScenarioPath =
        Path.Combine(AppContext.BaseDirectory, "data", "scenarios", "base-milano-1880", "scenario.json");

    private static StreetGraph ToyGraph() =>
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "maps", "toy.map.json"), SliceDefinitions());

    private static DefinitionRegistry SliceDefinitions() =>
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "definitions"));

    [Fact]
    public void BaseScenario_LoadsAndReproducesHardcodedCalendar()
    {
        var scenario = ScenarioLoader.LoadFromFile(BaseScenarioPath);

        Assert.Equal("base:milano-1880", scenario.Id);
        Assert.Equal("base:toy", scenario.MapRef);

        var loaded = scenario.BuildCalendar();
        var reference = SimCalendar.Milano1880();

        // 50+ years: identical dates and working-day rule, tick for tick.
        for (long tick = 0; tick < 22_000; tick++)
        {
            Assert.Equal(reference.DateAt(tick), loaded.DateAt(tick));
            Assert.Equal(reference.IsWorkingDay(tick), loaded.IsWorkingDay(tick));
        }
    }

    [Fact]
    public void BaseScenario_WorldRun_MatchesHardcodedSetupTrajectory()
    {
        // A world run where the calendar actually drives behaviour: the loaded
        // base scenario (calendar + Default events) must be trajectory-identical
        // to the old hardcoded setup (Milano1880 calendar + Default events).
        var scenario = ScenarioLoader.LoadFromFile(BaseScenarioPath);
        var graph = ToyGraph();
        var definitions = SliceDefinitions();

        var loaded = Simulation.FromScenario(9UL, scenario, graph, definitions);
        var reference = new Simulation(9UL, graph, definitions, EventSettings.Default); // hardcoded Milano1880

        loaded.Advance(1_200);
        reference.Advance(1_200);

        Assert.Equal(reference.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void SetCalendar_AddHoliday_AppliesOnlyFromItsDate()
    {
        // A holiday (1 May) added from 1881; the calendar must change only on
        // 1 May dates in 1881 and later, and nothing else.
        var withTimeline = ScenarioLoader.Load(ScenarioJson(
            """
            { "onYear": 1881, "onMonth": 5, "onDay": 1,
              "effect": { "type": "setCalendar", "addHoliday": { "month": 5, "day": 1, "name": "Festa dei lavoratori" } } }
            """));
        var withoutTimeline = ScenarioLoader.Load(ScenarioJson(""));

        var amended = withTimeline.BuildCalendar();
        var basis = withoutTimeline.BuildCalendar();

        var triggerTick = basis.TickOf(1881, 5, 1);
        var differences = 0;
        for (long tick = 0; tick < basis.TickOf(1885, 1, 1); tick++)
        {
            if (amended.IsWorkingDay(tick) == basis.IsWorkingDay(tick))
                continue;

            differences++;
            var date = amended.DateAt(tick);
            Assert.Equal((5, 1), (date.Month, date.Day)); // only 1 May is touched
            Assert.True(tick >= triggerTick);              // and only from the trigger date on
            Assert.True(basis.IsWorkingDay(tick));         // it removed a working day
            Assert.False(amended.IsWorkingDay(tick));      // by making it a holiday
        }

        Assert.True(differences >= 1, "the SetCalendar holiday should change at least one day");
    }

    [Fact]
    public void SetCalendar_IsDeterministic_SameScenarioSameCalendar()
    {
        var json = ScenarioJson(
            """
            { "onYear": 1890, "onMonth": 5, "onDay": 1,
              "effect": { "type": "setCalendar", "addRestDay": "saturday" } }
            """);

        var a = ScenarioLoader.Load(json).BuildCalendar();
        var b = ScenarioLoader.Load(json).BuildCalendar();

        for (long tick = 0; tick < 5_000; tick++)
            Assert.Equal(a.IsWorkingDay(tick), b.IsWorkingDay(tick));
    }

    [Fact]
    public void SetCalendar_SurvivesSaveAndLoad()
    {
        var scenario = ScenarioLoader.Load(ScenarioJson(
            """
            { "onYear": 1881, "onMonth": 5, "onDay": 1,
              "effect": { "type": "setCalendar", "addHoliday": { "month": 5, "day": 1, "name": "Festa dei lavoratori" } } }
            """));
        var graph = ToyGraph();
        var definitions = SliceDefinitions();

        var sim = Simulation.FromScenario(7UL, scenario, graph, definitions);
        sim.Advance(500); // past 1881-05-01
        var expected = sim.StateHash();

        var bytes = SaveSystem.Save(sim);
        var restored = SaveSystem.Load(bytes, graph, definitions, scenario: scenario);

        Assert.Equal(expected, restored.StateHash());
        restored.Advance(400);
        sim.Advance(400);
        Assert.Equal(sim.StateHash(), restored.StateHash()); // replay-stable past the amendment
    }

    [Fact]
    public void ModdingTimeline_ChangesScenarioHash()
    {
        var plain = ScenarioLoader.Load(ScenarioJson(""));
        var modded = ScenarioLoader.Load(ScenarioJson(
            """
            { "onYear": 1897, "onMonth": 5, "onDay": 1,
              "effect": { "type": "setCalendar", "addHoliday": { "month": 5, "day": 1, "name": "Primo Maggio" } } }
            """));

        Assert.NotEqual(plain.ContentHash(), modded.ContentHash());

        var graph = ToyGraph();
        var definitions = SliceDefinitions();
        Assert.NotEqual(
            ScenarioHash.Compute(plain, graph, definitions),
            ScenarioHash.Compute(modded, graph, definitions));
    }

    [Fact]
    public void UnknownEffectType_IsRejected()
    {
        var exception = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(ScenarioJson(
            """{ "onYear": 1900, "onMonth": 1, "onDay": 1, "effect": { "type": "teleport" } }""")));

        Assert.Contains("unknown timeline effect type 'teleport'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservedEffectType_IsRejectedAsReserved()
    {
        var exception = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(ScenarioJson(
            """{ "onYear": 1900, "onMonth": 1, "onDay": 1, "effect": { "type": "growWorld" } }""")));

        Assert.Contains("reserved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        // The format is closed: onCondition and friends are reserved, not silently
        // accepted. An undeclared field fails the load.
        var exception = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(ScenarioJson(
            """{ "onYear": 1900, "onMonth": 1, "onDay": 1, "onCondition": "bankrupt", "effect": { "type": "setCalendar", "addRestDay": "saturday" } }""")));

        Assert.Contains("scenario.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HardCap_RefusesToAdvancePast12345()
    {
        // Epoch two days before the cap: 12345-12-30. Two ticks resolve 12-30 and
        // 12-31; the third would land on 12346-01-01 and must be refused (RUE-39).
        var calendar = new SimCalendar(SimCalendar.MaxYear, 12, 30, []);
        var sim = new Simulation(0UL, calendar);

        sim.Advance(2); // 12345-12-30, 12345-12-31 — fine

        var exception = Assert.Throws<InvalidOperationException>(() => sim.Advance(1));
        Assert.Contains("12345", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalEnd_IsParsedButNotEngineEnforced()
    {
        // A declared end is scenario data (part of identity), not a wall: the
        // engine still advances past it — only the hard cap stops it.
        var scenario = ScenarioLoader.Load(ScenarioJson("", """, "end": { "year": 1885, "month": 12, "day": 31 }"""));

        Assert.Equal((1885, 12, 31), scenario.End);

        var calendar = scenario.BuildCalendar();
        var sim = new Simulation(0UL, calendar);
        sim.Advance((int)calendar.TickOf(1890, 1, 1)); // advance well past the declared 1885 end
        Assert.Equal(1890, sim.Today.Year);            // engine ignored the end; only the hard cap stops it
    }

    private static string ScenarioJson(string timelineEntry, string extra = "")
    {
        var timeline = string.IsNullOrWhiteSpace(timelineEntry) ? "" : timelineEntry;
        return $$"""
        {
          "formatVersion": 1,
          "id": "base:milano-1880",
          "name": "Test scenario",
          "map": "base:toy",
          "calendar": {
            "epochYear": 1880,
            "epochMonth": 1,
            "epochDay": 1,
            "restDays": ["sunday"],
            "holidays": [
              { "month": 12, "day": 25, "name": "Natale" }
            ]
          },
          "timeline": [{{timeline}}]{{extra}}
        }
        """;
    }
}
