using Ruera.Sim.Calendar;

namespace Ruera.Sim.Systems;

/// <summary>
/// Closing accounting (step 7 of the in-tick order) on the RUE-6 cadences:
/// wages accrue per worked day and are paid at Saturday close; condo contracts
/// pay on the first tick of the month (previous month's fee); routine
/// maintenance is a daily rate per owned vehicle; fines post at the tick the
/// violation is assessed; purchased vehicles materialize at their scheduled
/// delivery tick. Bankruptcy latches the first time cash closes below zero.
/// </summary>
internal sealed class EconomySystem : ISimSystem
{
    // Scenario constants — scenario data eventually (RUE-20).
    internal const long DailyWageCents = 300;        // 3 lire/day laborer
    internal const long FineCentsPerViolation = 500; // 5 lire per violation-tick
    internal const long DeliveryDelayTicks = 5;
    internal const long TrainingTicks = 10;

    public void Run(SimState state, SimCalendar calendar)
    {
        state.DeliverDue(); // scheduled future-tick effects: the checkpoint is a tick

        var date = calendar.DateAt(state.Tick);

        if (calendar.IsWorkingDay(state.Tick))
            state.WageAccruedCents = checked(state.WageAccruedCents + DailyWageCents * state.Workers.Count);
        if (date.Weekday == Weekday.Saturday)
        {
            state.CashCents = checked(state.CashCents - state.WageAccruedCents);
            state.WageAccruedCents = 0;
        }

        if (date.Day == 1 && state.Tick > 0)
        {
            foreach (var producer in state.Producers) // id order
            {
                if (producer.HasContract)
                    state.CashCents = checked(state.CashCents + producer.Archetype.ContractCentsPerMonth);
            }
        }

        foreach (var vehicle in state.Vehicles) // id order
            state.CashCents = checked(state.CashCents - vehicle.Definition.MaintenanceCentsPerDay);

        foreach (var simEvent in state.LastTickEvents)
        {
            if (simEvent.Type is SimEventType.BufferOverflow or SimEventType.SanitaryViolation)
                state.CashCents = checked(state.CashCents - FineCentsPerViolation);
        }

        if (state.CashCents < 0 && !state.Bankrupt)
        {
            state.Bankrupt = true;
            state.Emit(new SimEvent(state.Tick, SimEventType.Bankruptcy, 0));
        }
    }
}
