using Ruera.Sim.Calendar;
using Ruera.Sim.Rng;

namespace Ruera.Sim.Systems;

/// <summary>
/// Step 2 of the in-tick order (calendar/events): the micro-sollecitazioni of
/// DESIGN.md §12 — breakdowns, inspections, tenders — drawn from the dedicated
/// Events RNG stream so other systems' sequences never shift. Runs only when
/// the scenario provides <see cref="EventSettings"/>; costs post at the
/// assessment tick (RUE-6 cadence).
/// </summary>
internal sealed class EventsSystem : ISimSystem
{
    public void Run(SimState state, SimCalendar calendar)
    {
        var settings = state.Events;
        if (settings is null || state.Graph is null || !calendar.IsWorkingDay(state.Tick))
            return;

        var rng = state.Rng(RngStreamId.Events);

        foreach (var carrier in state.Carriers) // id order: deterministic
        {
            if (state.Tick < carrier.OutOfServiceUntilTick)
                continue; // already in the workshop
            if (!Chance(rng, settings.BreakdownChanceBps))
                continue;

            carrier.OutOfServiceUntilTick = state.Tick + settings.RepairTicks;
            var repairCost = IntMath.ApplyBps(carrier.Definition.PurchaseCents, settings.RepairCostBpsOfPurchase);
            state.CashCents = checked(state.CashCents - repairCost); // extraordinary repair: posted at the breakdown tick
            state.Emit(new SimEvent(state.Tick, SimEventType.CarrierBreakdown, carrier.Id, carrier.OutOfServiceUntilTick));
        }

        if (Chance(rng, settings.InspectionChanceBps))
        {
            long finesCharged = 0;
            foreach (var producer in state.Producers) // id order
            {
                var overBuffer = producer.BufferGrams > producer.Archetype.BufferGrams;
                var overInterval = state.Tick - producer.LastCollectedTick > producer.Archetype.MaxSanitaryIntervalTicks;
                if (overBuffer || overInterval)
                    finesCharged = checked(finesCharged + settings.InspectionFineCents);
            }

            state.CashCents = checked(state.CashCents - finesCharged);
            state.Emit(new SimEvent(state.Tick, SimEventType.SanitaryInspection, 0, finesCharged));
        }

        if (Chance(rng, settings.TenderChanceBps))
        {
            var unsigned = new List<int>();
            foreach (var producer in state.Producers) // id order
            {
                if (!producer.HasContract)
                    unsigned.Add(producer.Id);
            }

            if (unsigned.Count > 0)
            {
                // Announcement feed for the UI; the mechanical gate (tender-only
                // contracts) arrives with the crime/reputation chain (DESIGN.md §9).
                var producerId = unsigned[(int)rng.NextInt64(0, unsigned.Count)];
                state.Emit(new SimEvent(state.Tick, SimEventType.TenderAnnounced, producerId,
                    state.Tick + settings.TenderDeadlineTicks));
            }
        }
    }

    private static bool Chance(IDeterministicRng rng, int chanceBps) =>
        chanceBps > 0 && rng.NextInt64(0, IntMath.BasisPointScale) < chanceBps;
}
