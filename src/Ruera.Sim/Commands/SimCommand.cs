namespace Ruera.Sim.Commands;

/// <summary>Outcome of a command precondition check.</summary>
public readonly record struct CommandValidation(bool IsValid, string? Reason)
{
    public static CommandValidation Valid { get; } = new(true, null);

    public static CommandValidation Invalid(string reason) => new(false, reason);
}

/// <summary>
/// Base of all input commands. Every mutation of sim state enters through a
/// command applied at the opening of a tick (DESIGN.md §2 «Risoluzione al
/// tick»): game = initial state + seed + command stream. The hierarchy is
/// closed to this assembly (internal members): commands are engine-defined.
/// </summary>
public abstract record SimCommand
{
    /// <summary>Stable wire identifier for serialization.</summary>
    public abstract CommandTypeId TypeId { get; }

    /// <summary>
    /// Precondition check against read-only state. Runs at submission for
    /// caller feedback and again at application as the authority: a command
    /// that turned invalid by its day is skipped deterministically.
    /// </summary>
    internal abstract CommandValidation Validate(SimState state);

    internal abstract void Apply(SimState state);

    internal abstract void WritePayload(BinaryWriter writer);
}
