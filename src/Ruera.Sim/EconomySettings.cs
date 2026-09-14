namespace Ruera.Sim;

/// <summary>
/// Scenario configuration for the economic cadences (DESIGN.md §2 «Cadenze
/// economiche», RUE-43): wage, fines, delivery/training delays, shift budget
/// and (RUE-45) the sorting/sales parameters. Immutable config like the
/// calendar: not hashed on <see cref="SimState"/>, but part of the game's
/// identity — replay and load must receive the same settings (folds into the
/// scenario hash via <see cref="Scenario.Scenario"/>).
/// </summary>
public sealed record EconomySettings(
    long DailyWageCents,
    long FineCentsPerViolation,
    long DeliveryDelayTicks,
    long TrainingTicks,
    long ShiftMinutes,
    long SortingGramsPerWorkerDay,
    string StockpileWaste)
{
    /// <summary>Vertical-slice defaults: the constants hardcoded before RUE-43/45.</summary>
    public static EconomySettings Default { get; } = new(
        DailyWageCents: 300,        // 3 lire/day laborer
        FineCentsPerViolation: 500, // 5 lire per violation-tick
        DeliveryDelayTicks: 5,
        TrainingTicks: 10,
        ShiftMinutes: 480,          // collection shift 2-10 (DESIGN.md §2)
        SortingGramsPerWorkerDay: 400_000,
        StockpileWaste: "base:mixed"); // the slice's single undifferentiated fraction (DESIGN.md §3)
}
