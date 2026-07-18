namespace Ruera.Sim.Persistence;

/// <summary>
/// The engine's determinism labels (DESIGN.md §2 «Save e replay», RUE-8).
/// </summary>
public static class EngineVersion
{
    /// <summary>
    /// Bumped on every change that alters the state-hash trajectory for the
    /// same seed + commands — i.e. whenever golden hashes are consciously
    /// updated. Replays and ghosts require an exact match.
    /// v1 = RUE-11 core, v2 = RUE-16 world state, v3 = RUE-14 economy.
    /// </summary>
    public const int SimVersion = 3;

    /// <summary>
    /// Bumped when the canonical snapshot layout changes shape. Same schema +
    /// different SimVersion still loads from a snapshot (replay frozen);
    /// different schema invalidates the save (pre-1.0: no migrations).
    /// </summary>
    public const int StateSchemaVersion = 3;
}
