namespace Ruera.Sim.Data;

/// <summary>
/// One package's parsed, intrinsically-validated definitions before the
/// cross-package merge (RUE-40). Archetype→waste references are NOT yet
/// resolved here — that happens against the merged waste set so a mod producer
/// can reference a base waste type.
/// </summary>
internal sealed record DefinitionFragment(
    List<CarrierDefinition> Carriers,
    List<WasteDefinition> WasteTypes,
    List<ProducerArchetype> Archetypes);
