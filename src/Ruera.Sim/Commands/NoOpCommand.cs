namespace Ruera.Sim.Commands;

/// <summary>Does nothing; timestamps a tick in the log and exercises the pipeline in tests.</summary>
public sealed record NoOpCommand : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.NoOp;

    internal override CommandValidation Validate(SimState state) => CommandValidation.Valid;

    internal override void Apply(SimState state)
    {
    }

    internal override void WritePayload(BinaryWriter writer)
    {
    }

    internal static NoOpCommand ReadPayload(BinaryReader reader) => new();
}
