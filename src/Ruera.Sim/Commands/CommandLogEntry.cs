namespace Ruera.Sim.Commands;

/// <summary>A command bound to the day (tick index) at whose opening it applies.</summary>
public readonly record struct CommandLogEntry(long Day, SimCommand Command);
