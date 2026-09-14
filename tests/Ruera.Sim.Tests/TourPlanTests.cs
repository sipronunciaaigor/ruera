using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// RUE-19 direction (A6): <c>Simulation.PlanTour</c> exposes the same
/// pessimistic simulation <c>PreviewTour</c> already ran, leg by leg. Toy-map
/// facts as in DayPlanTests.cs: depot at node 1; edge 2 = nodes 2-3 (producer 1,
/// condo-small); edge 5 = nodes 6-7 (producer 2, condo-large, buffer cap 300 000 g).
/// </summary>
public class TourPlanTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "definitions"));

    private static Simulation WithCarrier(string type, int[] coverage)
    {
        var sim = new Simulation(1, Graph, Definitions);
        sim.Submit(new AddCarrierCommand(type));
        sim.Advance(1); // resolves the Jan 1 holiday: production only, carrier materializes
        sim.Submit(new SetCoverageCommand(1, coverage));
        return sim;
    }

    [Fact]
    public void PlanTour_SingleTrip_MatchesHandComputedLegAndBps()
    {
        // Same setup/arithmetic as DayPlanTests.ExecutedTour_MatchesHandComputedPlan:
        // travel ceil(600/90)=7, fill 12, return ceil(600/90)=7, empty 15 -> 41 min.
        var sim = WithCarrier("base:navazza", [2]);

        var plan = sim.PlanTour(1, [2]);

        Assert.Equal(1, plan.CarrierId);
        Assert.Equal(1, plan.Trips);
        Assert.Equal(41, plan.Total.Value);
        Assert.Equal(480, plan.Budget.Value);
        Assert.Equal(854, plan.BudgetUsedBps); // floor(41 x 10 000 / 480) = floor(410 000 / 480) = 854

        var leg = Assert.Single(plan.Legs);
        Assert.Equal(2, leg.EdgeId);
        Assert.Equal(7, leg.ArrivalMinute); // travel only, before the fill stop
        Assert.Equal(1, leg.Stops);
        Assert.True(leg.ReturnToDepot);
        Assert.Equal([1, 2, 3], leg.NodePath); // depot -> entry (edge.From=2) -> exit (edge.To=3)
    }

    [Fact]
    public void PlanTour_MultiTrip_TwelveIdenticalLegsAllReturningToDepot()
    {
        // Pessimistic buffer = condo-large's archetype cap (300 000 g), evenly
        // divisible by the gerla's 25 000 g basket: exactly 12 identical trips,
        // each ending in a depot return (the top-of-loop refill for the first
        // 11, the tour's own final return for the 12th) -- see
        // DayPlanTests.Preview_MultiTrip_UsesArchetypeCapAsPessimisticBufferAndExceedsBudget
        // for the 528-minute total.
        var sim = WithCarrier("base:gerla", [5]);

        var plan = sim.PlanTour(1, [5]);

        Assert.Equal(12, plan.Trips);
        Assert.Equal(12, plan.Legs.Count);
        Assert.Equal(528, plan.Total.Value);
        Assert.Equal(11_000, plan.BudgetUsedBps); // floor(528 x 10 000 / 480) = 11 000 exactly: over budget, as a preview may be

        foreach (var leg in plan.Legs)
        {
            Assert.Equal(5, leg.EdgeId);
            Assert.Equal(1, leg.Stops);
            Assert.True(leg.ReturnToDepot); // every trip empties the basket exactly (300 000 / 25 000 = 12)
        }

        Assert.Equal(17, plan.Legs[0].ArrivalMinute); // first trip departs from the depot: ceil(900/55) = 17
    }

    [Fact]
    public void PreviewTour_StillMatchesPlanTourTotal()
    {
        // PreviewTour is now a thin wrapper over PlanTour (one code path).
        var sim = WithCarrier("base:gerla", [5]);

        Assert.Equal(sim.PlanTour(1, [5]).Total, sim.PreviewTour(1, [5]));

        sim.Advance(1); // commits the painted coverage
        Assert.Equal(sim.PlanTour(1).Total, sim.PreviewTour(1));
    }
}
