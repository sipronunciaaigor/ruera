using Ruera.Sim.Calendar;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Systems;

/// <summary>
/// Step 4 of the in-tick order: the working day as a capacity problem solved
/// inside the tick (DESIGN.md §2, §4). Each vehicle with painted coverage gets
/// a deterministic greedy tour (nearest-neighbor from the depot, deliberately
/// suboptimal), consuming a minutes budget: travel + stops + depot round-trip.
/// Overlap rule: the first vehicle empties, later ones pass through paying
/// travel only. Effects are written at tick close; the rendered day is staging.
/// </summary>
internal sealed class DayPlanSystem : ISimSystem
{
    /// <summary>Collection shift 2–10 = 480 minutes (DESIGN.md §2). Scenario data eventually.</summary>
    internal const long ShiftMinutes = 480;

    public void Run(SimState state, SimCalendar calendar)
    {
        if (state.Graph is null || !calendar.IsWorkingDay(state.Tick))
            return; // production continues on rest days; collection does not

        // Service lines scheduled for today's weekday (DESIGN.md §4), id order.
        var weekday = calendar.DateAt(state.Tick).Weekday;
        var activeLines = new List<RouteTemplate>();
        foreach (var template in state.Templates) // id order
        {
            if (template.IsActiveOn(weekday))
                activeLines.Add(template);
        }

        var lineCollected = new long[activeLines.Count];
        var lineDispatched = new int[activeLines.Count];
        var lineServed = new List<int>[activeLines.Count];
        for (var i = 0; i < activeLines.Count; i++)
            lineServed[i] = [];

        // Crew gating: vehicles staff up in id order from productive workers
        // only — trainees cost wages but crew nothing (DESIGN.md §2).
        var crewAvailable = 0;
        foreach (var worker in state.Workers)
        {
            if (state.Tick - worker.HiredTick >= EconomySystem.TrainingTicks)
                crewAvailable++;
        }

        foreach (var vehicle in state.Vehicles) // id order: deterministic
        {
            // Direct painted coverage is the override (DESIGN.md §4);
            // otherwise the union of today's assigned lines, in line id order.
            int[] coverage;
            var attributed = new List<int>(); // indices into activeLines
            if (vehicle.CoverageArray.Length > 0)
            {
                coverage = vehicle.CoverageArray;
            }
            else
            {
                var union = new List<int>();
                for (var i = 0; i < activeLines.Count; i++)
                {
                    if (Array.BinarySearch(activeLines[i].AssignedArray, vehicle.Id) < 0)
                        continue;
                    attributed.Add(i);
                    union.AddRange(activeLines[i].EdgeArray);
                }

                coverage = [.. union.Distinct().OrderBy(id => id)];
            }

            if (coverage.Length == 0 || vehicle.Definition.Crew > crewAvailable)
                continue;
            crewAvailable -= vehicle.Definition.Crew;
            foreach (var lineIndex in attributed)
                lineDispatched[lineIndex]++;
            ExecuteTour(state, vehicle, coverage, activeLines, attributed, lineCollected, lineServed);
        }

        for (var i = 0; i < activeLines.Count; i++)
        {
            state.ReportLine(new LineReport(activeLines[i].Id, lineDispatched[i], lineCollected[i],
                [.. lineServed[i].Distinct().OrderBy(id => id)]));
        }
    }

    private static void ExecuteTour(SimState state, VehicleState vehicle, int[] coverageEdges,
        List<RouteTemplate> activeLines, List<int> attributed, long[] lineCollected, List<int>[] lineServed)
    {
        var graph = state.Graph!;
        var definition = vehicle.Definition;
        var depotNode = graph.Depots[0].Node;

        var remaining = new List<int>(coverageEdges); // sorted: ties pick lowest id
        var served = new List<int>();
        var current = depotNode;
        long used = 0, collected = 0;
        var capacityLeft = definition.CapacityGrams;
        var leftDepot = false;

        while (remaining.Count > 0 && capacityLeft > 0)
        {
            var (edge, approach, entry) = NearestEdge(graph, current, remaining);
            var exit = entry == edge.From ? edge.To : edge.From;
            var travel = IntMath.DivCeil(approach + edge.LengthMeters, definition.MetersPerMinute);

            var stops = 0;
            foreach (var producer in state.Producers)
            {
                if (producer.EdgeId == edge.Id && producer.BufferGrams > 0)
                    stops++; // emptied producers cost nothing: overlap benefit, execution only
            }

            var stopMinutes = stops * (long)definition.FillMinutes;
            var returnHome = IntMath.DivCeil(graph.Distance(exit, depotNode).Value, definition.MetersPerMinute)
                             + definition.EmptyMinutes;
            if (used + travel + stopMinutes + returnHome > ShiftMinutes)
                break; // infeasible: head home, the rest stays unserved

            used += travel + stopMinutes;
            foreach (var producer in state.Producers) // id order
            {
                if (producer.EdgeId != edge.Id || producer.BufferGrams == 0 || capacityLeft == 0)
                    continue;
                var take = Math.Min(producer.BufferGrams, capacityLeft);
                producer.BufferGrams -= take;
                producer.LastCollectedTick = state.Tick;
                capacityLeft -= take;
                collected += take;
                served.Add(producer.Id);
                foreach (var lineIndex in attributed)
                {
                    if (Array.BinarySearch(activeLines[lineIndex].EdgeArray, edge.Id) >= 0)
                    {
                        lineCollected[lineIndex] += take;
                        lineServed[lineIndex].Add(producer.Id);
                        break; // shared edges attribute to the lowest line id
                    }
                }
            }

            leftDepot = true;
            current = exit;
            remaining.Remove(edge.Id);
        }

        if (leftDepot)
        {
            used += IntMath.DivCeil(graph.Distance(current, depotNode).Value, definition.MetersPerMinute);
            if (collected > 0)
                used += definition.EmptyMinutes;
        }

        state.StockpileGrams = checked(state.StockpileGrams + collected);
        state.Report(new DayPlanReport(vehicle.Id, used, collected, served));
    }

    /// <summary>
    /// Pessimistic preview for the UI (DESIGN.md §4): full cost of the painted
    /// coverage — every producer on it costs a stop regardless of buffers,
    /// other vehicles or capacity. Overlap savings exist only in execution;
    /// estimates are pessimistic, reality can only be better.
    /// </summary>
    public static Minutes Preview(SimState state, VehicleState vehicle) =>
        Preview(state, vehicle.Definition, vehicle.CoverageArray);

    /// <summary>Same estimate for a tentative coverage set (the UI previews while painting).</summary>
    public static Minutes Preview(SimState state, VehicleDefinition definition, IReadOnlyList<int> coverage)
    {
        var graph = state.Graph
                    ?? throw new InvalidOperationException("Preview requires a world (street graph).");
        var depotNode = graph.Depots[0].Node;

        var remaining = coverage.Distinct().OrderBy(id => id).ToList();
        var current = depotNode;
        long total = 0;
        while (remaining.Count > 0)
        {
            var (edge, approach, entry) = NearestEdge(graph, current, remaining);
            total += IntMath.DivCeil(approach + edge.LengthMeters, definition.MetersPerMinute);
            foreach (var producer in state.Producers)
            {
                if (producer.EdgeId == edge.Id)
                    total += definition.FillMinutes;
            }

            current = entry == edge.From ? edge.To : edge.From;
            remaining.Remove(edge.Id);
        }

        if (current != depotNode || coverage.Count > 0)
        {
            total += IntMath.DivCeil(graph.Distance(current, depotNode).Value, definition.MetersPerMinute);
            total += definition.EmptyMinutes;
        }

        return new Minutes(total);
    }

    private static (MapEdge Edge, long Approach, int Entry) NearestEdge(StreetGraph graph, int fromNode, List<int> candidateIds)
    {
        MapEdge? best = null;
        long bestDistance = long.MaxValue;
        var bestEntry = 0;
        foreach (var edgeId in candidateIds) // ascending: ties keep the lowest edge id
        {
            var edge = graph.Edge(edgeId);
            var toFrom = graph.Distance(fromNode, edge.From).Value;
            var toTo = graph.Distance(fromNode, edge.To).Value;
            var (distance, entry) = toFrom <= toTo ? (toFrom, edge.From) : (toTo, edge.To);
            if (distance < bestDistance)
            {
                best = edge;
                bestDistance = distance;
                bestEntry = entry;
            }
        }

        return (best!, bestDistance, bestEntry);
    }
}
