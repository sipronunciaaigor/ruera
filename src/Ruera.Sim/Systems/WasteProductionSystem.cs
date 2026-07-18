using Ruera.Sim.Calendar;

namespace Ruera.Sim.Systems;

/// <summary>
/// Step 3 of the in-tick order: producers accumulate waste every tick — also
/// on rest days, which is exactly why the Monday after Sunday is heavy
/// (DESIGN.md §3).
/// </summary>
internal sealed class WasteProductionSystem : ISimSystem
{
    public void Run(SimState state, SimCalendar calendar)
    {
        foreach (var producer in state.Producers) // id order
        {
            foreach (var production in producer.Archetype.Production)
                producer.BufferGrams = checked(producer.BufferGrams + production.GramsPerTick);
        }
    }
}
