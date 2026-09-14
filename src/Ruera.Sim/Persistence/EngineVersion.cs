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
    /// v1 = RUE-11 core, v2 = RUE-16 world state, v3 = RUE-14 economy,
    /// v4 = RUE-31 route templates, v5 = RUE-32 events, v6 = RUE-44 multi-trip
    /// day plan (a carrier that fills up mid-tour now returns to the depot
    /// and heads back out instead of stopping for the day, changing how much
    /// gets collected on any world with a graph). RUE-38/RUE-42/RUE-43 did not
    /// bump: each reproduces the prior trajectory exactly, so golden hashes
    /// were unchanged.
    /// </summary>
    public const int SimVersion = 6;

    /// <summary>
    /// Bumped when the canonical snapshot layout changes shape. Same schema +
    /// different SimVersion still loads from a snapshot (replay frozen);
    /// different schema invalidates the save (pre-1.0: no migrations).
    /// </summary>
    public const int StateSchemaVersion = 5;
}
