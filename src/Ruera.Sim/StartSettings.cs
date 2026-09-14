namespace Ruera.Sim;

/// <summary>
/// Scenario configuration for the starting endowment (DESIGN.md §2 «Scenario
/// e timeline storica», RUE-43): cash and trained workers on day zero.
/// Applied once at <see cref="SimState"/> construction — not part of mutable
/// state, so it isn't hashed there (it folds into the scenario hash instead,
/// like <see cref="EventSettings"/>).
/// </summary>
public sealed record StartSettings(long CashCents, int Workers)
{
    /// <summary>Vertical-slice defaults: the endowment hardcoded before RUE-43.</summary>
    public static StartSettings Default { get; } = new(CashCents: 500_000, Workers: 4); // 5 000 lire, 4 trained workers
}
