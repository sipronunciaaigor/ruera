using Ruera.Sim.Calendar;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Systems;

/// <summary>
/// Step 4 of the in-tick order: the working day as a capacity problem solved
/// inside the tick (DESIGN.md §2, §4). Each carrier with painted coverage gets
/// a deterministic greedy tour (nearest-neighbor from the depot, deliberately
/// suboptimal), consuming a minutes budget: travel + stops + depot round-trip.
/// Overlap rule: the first carrier empties, later ones pass through paying
/// travel only. Effects are written at tick close; the rendered day is staging.
/// </summary>
internal sealed class DayPlanSystem : ISimSystem
{
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

        // Crew gating: carriers staff up in id order from productive workers
        // only — trainees cost wages but crew nothing (DESIGN.md §2).
        var crewAvailable = 0;
        foreach (var worker in state.Workers)
        {
            if (state.Tick - worker.HiredTick >= state.Economy.TrainingTicks)
                crewAvailable++;
        }

        foreach (var carrier in state.Carriers) // id order: deterministic
        {
            if (state.Tick < carrier.OutOfServiceUntilTick)
                continue; // in the workshop (RUE-32)

            // Direct painted coverage is the override (DESIGN.md §4);
            // otherwise the union of today's assigned lines, in line id order.
            int[] coverage;
            var attributed = new List<int>(); // indices into activeLines
            if (carrier.CoverageArray.Length > 0)
            {
                coverage = carrier.CoverageArray;
            }
            else
            {
                var union = new List<int>();
                for (var i = 0; i < activeLines.Count; i++)
                {
                    if (Array.BinarySearch(activeLines[i].AssignedArray, carrier.Id) < 0)
                        continue;
                    attributed.Add(i);
                    union.AddRange(activeLines[i].EdgeArray);
                }

                coverage = [.. union.Distinct().OrderBy(id => id)];
            }

            if (coverage.Length == 0 || carrier.Definition.Crew > crewAvailable)
                continue;
            crewAvailable -= carrier.Definition.Crew;
            state.CrewUsedToday += carrier.Definition.Crew; // read by ProcessingSystem (RUE-45)
            foreach (var lineIndex in attributed)
                lineDispatched[lineIndex]++;
            ExecuteTour(state, carrier, coverage, activeLines, attributed, lineCollected, lineServed);
        }

        for (var i = 0; i < activeLines.Count; i++)
        {
            state.ReportLine(new LineReport(activeLines[i].Id, lineDispatched[i], lineCollected[i],
                [.. lineServed[i].Distinct().OrderBy(id => id)]));
        }
    }

    /// <summary>
    /// Multi-trip execution (RUE-44, DESIGN.md §2 «Viaggi multipli nel turno»):
    /// when the carrier fills up mid-route, it returns to the depot, unloads and
    /// heads back out — as long as the remaining minutes budget covers the
    /// round trip. An edge stays in <c>remaining</c> while any producer on it
    /// still has grams left after a visit, so a later trip finishes the job.
    /// Termination is guaranteed: every visit costs at least one minute.
    /// </summary>
    private static void ExecuteTour(SimState state, CarrierState carrier, int[] coverageEdges,
        List<RouteTemplate> activeLines, List<int> attributed, long[] lineCollected, List<int>[] lineServed)
    {
        var graph = state.Graph!;
        var definition = carrier.Definition;
        var depotNode = graph.Depots[0].Node;
        var shiftMinutes = state.Economy.ShiftMinutes;

        var remaining = new List<int>(coverageEdges); // sorted: ties pick lowest id
        var served = new List<int>();
        var executedLegs = new List<ExecutedLeg>();
        var current = depotNode;
        long used = 0, collected = 0;
        var capacityLeft = definition.CapacityGrams;
        var leftDepot = false;
        var trips = 0;
        var startingNewTrip = true; // the first departure is trip 1

        while (remaining.Count > 0)
        {
            if (capacityLeft == 0)
            {
                var refillReturn = IntMath.DivCeil(graph.Distance(current, depotNode).Value, definition.MetersPerMinute)
                                    + definition.EmptyMinutes;
                if (used + refillReturn > shiftMinutes)
                    break; // infeasible to head home and back out again today

                used += refillReturn;
                capacityLeft = definition.CapacityGrams;
                current = depotNode;
                startingNewTrip = true;
                if (executedLegs.Count > 0)
                    executedLegs[^1] = executedLegs[^1] with { ReturnToDepot = true };
                continue;
            }

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
            if (used + travel + stopMinutes + returnHome > shiftMinutes)
                break; // infeasible: head home, the rest stays unserved

            if (startingNewTrip)
            {
                trips++; // a new departure from the depot, either the first or after a refill
                startingNewTrip = false;
            }

            used += travel + stopMinutes;
            var edgeFullyServed = true;
            foreach (var producer in state.Producers) // id order
            {
                if (producer.EdgeId != edge.Id || producer.BufferGrams == 0)
                    continue;
                if (capacityLeft == 0)
                {
                    edgeFullyServed = false;
                    continue;
                }

                var take = Math.Min(producer.BufferGrams, capacityLeft);
                producer.BufferGrams -= take;
                producer.LastCollectedTick = state.Tick;
                capacityLeft -= take;
                collected += take;
                served.Add(producer.Id);
                if (producer.BufferGrams > 0)
                    edgeFullyServed = false; // capacity ran out mid-producer: come back for the rest
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
            executedLegs.Add(new ExecutedLeg(edge.Id, ReturnToDepot: false));
            if (edgeFullyServed)
                remaining.Remove(edge.Id);
        }

        if (leftDepot)
        {
            used += IntMath.DivCeil(graph.Distance(current, depotNode).Value, definition.MetersPerMinute);
            if (collected > 0)
                used += definition.EmptyMinutes;
            executedLegs[^1] = executedLegs[^1] with { ReturnToDepot = true }; // the tour's final return
        }

        state.StockpileGrams = checked(state.StockpileGrams + collected);
        // Multi-trip can revisit the same producer across trips: dedupe like
        // LineReport already does, so a served id is reported once per tour.
        state.Report(new DayPlanReport(carrier.Id, used, collected, [.. served.Distinct().OrderBy(id => id)], trips,
            executedLegs));
    }

    /// <summary>Pessimistic minutes-only estimate (DESIGN.md §4); see <see cref="Plan(SimState,CarrierState)"/> for the full plan.</summary>
    public static Minutes Preview(SimState state, CarrierState carrier) => Plan(state, carrier).Total;

    /// <summary>Same estimate for a tentative coverage set (the UI previews while painting).</summary>
    public static Minutes Preview(SimState state, CarrierDefinition definition, IReadOnlyList<int> coverage, int carrierId) =>
        Plan(state, definition, coverage, carrierId).Total;

    /// <summary>Builds the plan from the carrier's own committed coverage.</summary>
    public static TourPlan Plan(SimState state, CarrierState carrier) =>
        Plan(state, carrier.Definition, carrier.CoverageArray, carrier.Id);

    /// <summary>
    /// Pessimistic tour plan (DESIGN.md §4, RUE-19): full cost of the given
    /// coverage, ignoring overlaps with other carriers — overlap savings exist
    /// only in execution; estimates are pessimistic, reality can only be
    /// better. Multi-trip aware (RUE-44): each producer is assumed to carry
    /// <c>max(archetype buffer, current buffer)</c> — its declared cap, or its
    /// actual buffer if that already overflowed the cap — so the simulated
    /// depot round-trips (inserted exactly like execution) can only add time
    /// versus what will really happen; preview minutes are always >= real
    /// minutes. Unlike execution, this never stops at the shift budget: the
    /// full pessimistic total is reported even past 100% (<see cref="TourPlan.BudgetUsedBps"/>
    /// can exceed 10 000), so the UI can show it.
    /// </summary>
    public static TourPlan Plan(SimState state, CarrierDefinition definition, IReadOnlyList<int> coverage, int carrierId)
    {
        var graph = state.Graph
                    ?? throw new InvalidOperationException("Preview requires a world (street graph).");
        var depotNode = graph.Depots[0].Node;

        // Pessimistic per-producer amount, fixed up front: worst case is the
        // producer's own declared cap, unless it is already over that cap.
        var pessimisticGrams = new Dictionary<int, long>();
        foreach (var producer in state.Producers)
        {
            if (coverage.Contains(producer.EdgeId))
                pessimisticGrams[producer.Id] = Math.Max(producer.Archetype.BufferGrams, producer.BufferGrams);
        }

        var remaining = coverage.Distinct().OrderBy(id => id).ToList();
        var legs = new List<TourLeg>();
        var current = depotNode;
        long total = 0;
        var capacityLeft = definition.CapacityGrams;
        var trips = 0;
        var startingNewTrip = true;
        while (remaining.Count > 0)
        {
            if (capacityLeft == 0)
            {
                total += IntMath.DivCeil(graph.Distance(current, depotNode).Value, definition.MetersPerMinute)
                         + definition.EmptyMinutes;
                capacityLeft = definition.CapacityGrams;
                current = depotNode;
                startingNewTrip = true;
                if (legs.Count > 0)
                    legs[^1] = legs[^1] with { ReturnToDepot = true };
                continue;
            }

            var (edge, approach, entry) = NearestEdge(graph, current, remaining);
            total += IntMath.DivCeil(approach + edge.LengthMeters, definition.MetersPerMinute);
            var arrivalMinute = total;
            if (startingNewTrip)
            {
                trips++;
                startingNewTrip = false;
            }

            var stops = 0;
            var edgeFullyServed = true;
            foreach (var producer in state.Producers)
            {
                if (producer.EdgeId != edge.Id)
                    continue;
                var left = pessimisticGrams[producer.Id];
                if (left == 0)
                    continue;
                stops++;
                total += definition.FillMinutes;
                if (capacityLeft == 0)
                {
                    edgeFullyServed = false;
                    continue;
                }

                var take = Math.Min(left, capacityLeft);
                pessimisticGrams[producer.Id] = left - take;
                capacityLeft -= take;
                if (left - take > 0)
                    edgeFullyServed = false;
            }

            var exit = entry == edge.From ? edge.To : edge.From;
            var nodePath = graph.ShortestPath(current, entry);
            legs.Add(new TourLeg(edge.Id, [.. nodePath, exit], arrivalMinute, stops, ReturnToDepot: false));
            current = exit;
            if (edgeFullyServed)
                remaining.Remove(edge.Id);
        }

        if (current != depotNode || coverage.Count > 0)
        {
            total += IntMath.DivCeil(graph.Distance(current, depotNode).Value, definition.MetersPerMinute);
            total += definition.EmptyMinutes;
            if (legs.Count > 0)
                legs[^1] = legs[^1] with { ReturnToDepot = true };
        }

        var budget = state.Economy.ShiftMinutes;
        var budgetUsedBps = IntMath.MulDiv(total, 10_000, budget);
        return new TourPlan(carrierId, legs, trips, new Minutes(total), new Minutes(budget), budgetUsedBps);
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
