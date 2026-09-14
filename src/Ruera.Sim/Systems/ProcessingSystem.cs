using Ruera.Sim.Calendar;

namespace Ruera.Sim.Systems;

/// <summary>
/// Step 5 of the in-tick order (DESIGN.md §2, §8 «lavorazione in azienda»,
/// RUE-45): idle trained workers sort the company stockpile into
/// <see cref="SimState.SortedGrams"/>, ready for <see cref="SalesSystem"/> in
/// the same tick. Slice = single undifferentiated fraction: per-fraction
/// stock is the 1980 extension.
/// </summary>
internal sealed class ProcessingSystem : ISimSystem
{
    public void Run(SimState state, SimCalendar calendar)
    {
        if (state.Graph is null || !calendar.IsWorkingDay(state.Tick))
            return; // no company premises, or nobody works today: nothing to sort

        var idleWorkers = 0;
        foreach (var worker in state.Workers)
        {
            if (state.Tick - worker.HiredTick >= state.Economy.TrainingTicks)
                idleWorkers++;
        }

        idleWorkers -= state.CrewUsedToday;
        if (idleWorkers <= 0)
            return;

        var capacity = idleWorkers * state.Economy.SortingGramsPerWorkerDay;
        var sorted = Math.Min(state.StockpileGrams, capacity);
        if (sorted <= 0)
            return;

        state.StockpileGrams -= sorted;
        state.SortedGrams = checked(state.SortedGrams + sorted);
    }
}
