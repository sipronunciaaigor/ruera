using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Dismisses a worker. Applied at tick open (RUE-6): the roster shrinks before
/// the day plan, so crew gating reflects it that same tick. Wages already
/// accrued to date stay due at the next Saturday close (they are not per-worker
/// — see <see cref="SimState.RemoveWorker"/>); only future accrual drops.
/// </summary>
public sealed record FireWorkerCommand(int WorkerId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.FireWorker;

    internal override CommandValidation Validate(SimState state) =>
        state.TryGetWorker(WorkerId, out _)
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown worker {WorkerId}."));

    internal override void Apply(SimState state) => state.RemoveWorker(WorkerId);

    internal override void WritePayload(BinaryWriter writer) => writer.Write(WorkerId);

    internal static FireWorkerCommand ReadPayload(BinaryReader reader) => new(reader.ReadInt32());
}
