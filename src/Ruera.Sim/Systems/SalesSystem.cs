using Ruera.Sim.Calendar;

namespace Ruera.Sim.Systems;

/// <summary>
/// Step 6 of the in-tick order (DESIGN.md §2, §8 «vendita materiali:
/// immediata», RUE-45): sells the whole sorted stockpile the moment it is
/// sorted, at the base price of the scenario's <c>stockpileWaste</c> fraction.
/// </summary>
internal sealed class SalesSystem : ISimSystem
{
    public void Run(SimState state, SimCalendar calendar)
    {
        var sortedGrams = state.SortedGrams;
        if (sortedGrams == 0)
            return;

        var price = state.Definitions!.Waste(state.Economy.StockpileWaste).BaseSaleCentsPerKg;
        var cents = IntMath.MulDiv(sortedGrams, price, 1000); // grams -> kg, floor

        state.CashCents = checked(state.CashCents + cents);
        state.SortedGrams = 0;
        state.Emit(new SimEvent(state.Tick, SimEventType.MaterialSold, 0, cents));
    }
}
