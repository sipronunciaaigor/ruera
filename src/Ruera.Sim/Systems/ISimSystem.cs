using Ruera.Sim.Calendar;

namespace Ruera.Sim.Systems;

/// <summary>
/// One step of the fixed in-tick pipeline (DESIGN.md §2 «Risoluzione al
/// tick», RUE-6). Systems run in the explicit order declared by the engine —
/// a plain array, no DI, no reflection — and are single-threaded by rule.
/// </summary>
internal interface ISimSystem
{
    void Run(SimState state, SimCalendar calendar);
}
