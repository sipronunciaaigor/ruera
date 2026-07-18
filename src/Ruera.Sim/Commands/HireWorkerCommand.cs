namespace Ruera.Sim.Commands;

/// <summary>
/// Hires one worker. In payroll from the first Saturday; crews nothing for the
/// ~10 training ticks (DESIGN.md §2 «Ritardi realistici»).
/// </summary>
public sealed record HireWorkerCommand : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.HireWorker;

    internal override CommandValidation Validate(SimState state) => CommandValidation.Valid;

    internal override void Apply(SimState state) => state.AddWorker(state.Tick);

    internal override void WritePayload(BinaryWriter writer)
    {
    }

    internal static HireWorkerCommand ReadPayload(BinaryReader reader) => new();
}
