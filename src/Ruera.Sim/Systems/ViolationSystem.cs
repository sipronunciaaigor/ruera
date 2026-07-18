using Ruera.Sim.Calendar;

namespace Ruera.Sim.Systems;

/// <summary>
/// Closing checks (step 7 of the in-tick order): buffer overflows and sanitary
/// intervals (DESIGN.md §3). Emits an event and increments the producer's
/// violation tally for every tick a condition holds — fines land with RUE-14.
/// </summary>
internal sealed class ViolationSystem : ISimSystem
{
    public void Run(SimState state, SimCalendar calendar)
    {
        foreach (var producer in state.Producers) // id order
        {
            if (producer.BufferGrams > producer.Archetype.BufferGrams)
            {
                state.Emit(new SimEvent(state.Tick, SimEventType.BufferOverflow, producer.Id));
                producer.ViolationCount++;
            }

            if (state.Tick - producer.LastCollectedTick > producer.Archetype.MaxSanitaryIntervalTicks)
            {
                state.Emit(new SimEvent(state.Tick, SimEventType.SanitaryViolation, producer.Id));
                producer.ViolationCount++;
            }
        }
    }
}
