namespace Ruera.Sim;

/// <summary>
/// Scenario configuration for the essential events (DESIGN.md §12 «Ritmo di
/// gioco», §16 step 5). Immutable config like the calendar: not hashed, but
/// part of the game's identity — replay and load must receive the same
/// settings (folds into the scenario package with RUE-36). Null settings on
/// the Simulation = events disabled.
/// </summary>
public sealed record EventSettings(
    int BreakdownChanceBps,
    long RepairTicks,
    int RepairCostBpsOfPurchase,
    int InspectionChanceBps,
    long InspectionFineCents,
    int TenderChanceBps,
    long TenderDeadlineTicks)
{
    /// <summary>Vertical-slice defaults: placeholder balancing, tuned via headless runs later.</summary>
    public static EventSettings Default { get; } = new(
        BreakdownChanceBps: 150,        // ~1.5% per working day per vehicle
        RepairTicks: 5,
        RepairCostBpsOfPurchase: 500,   // repair = 5% of purchase price
        InspectionChanceBps: 100,
        InspectionFineCents: 2_000,     // per violating producer, on top of daily fines
        TenderChanceBps: 50,
        TenderDeadlineTicks: 30);
}
