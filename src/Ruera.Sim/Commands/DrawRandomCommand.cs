using System.Globalization;

using Ruera.Sim.Rng;

namespace Ruera.Sim.Commands;

/// <summary>
/// Debug command: draws one value from a per-system RNG stream, perturbing
/// downstream evolution. Real game commands (buy carrier, hire, paint
/// coverage, sign contract, …) land with their systems (RUE-12/14/16); this
/// one exists so the command pipeline is exercisable before they do.
/// </summary>
public sealed record DrawRandomCommand(RngStreamId Stream) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.DrawRandom;

    internal override CommandValidation Validate(SimState state) =>
        Enum.IsDefined(Stream)
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown RNG stream {(ulong)Stream}."));

    internal override void Apply(SimState state) => state.Rng(Stream).NextUInt64();

    internal override void WritePayload(BinaryWriter writer) => writer.Write((ulong)Stream);

    internal static DrawRandomCommand ReadPayload(BinaryReader reader) => new((RngStreamId)reader.ReadUInt64());
}
