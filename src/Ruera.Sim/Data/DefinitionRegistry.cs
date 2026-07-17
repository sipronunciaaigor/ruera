using System.Globalization;

namespace Ruera.Sim.Data;

/// <summary>
/// Validated, immutable set of entity definitions the sim runs on. Collections
/// are sorted by ordinal id (deterministic iteration, DESIGN.md §2 rule 5);
/// lookups use binary search — no hash codes anywhere.
/// </summary>
public sealed class DefinitionRegistry
{
    private readonly VehicleDefinition[] _vehicles;
    private readonly WasteDefinition[] _wasteTypes;
    private readonly ProducerArchetype[] _producerArchetypes;
    private readonly string[] _vehicleIds;
    private readonly string[] _wasteIds;
    private readonly string[] _archetypeIds;

    internal DefinitionRegistry(
        VehicleDefinition[] vehicles,
        WasteDefinition[] wasteTypes,
        ProducerArchetype[] producerArchetypes)
    {
        Array.Sort(vehicles, static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        Array.Sort(wasteTypes, static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        Array.Sort(producerArchetypes, static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        _vehicles = vehicles;
        _wasteTypes = wasteTypes;
        _producerArchetypes = producerArchetypes;
        _vehicleIds = [.. vehicles.Select(v => v.Id)];
        _wasteIds = [.. wasteTypes.Select(w => w.Id)];
        _archetypeIds = [.. producerArchetypes.Select(a => a.Id)];
    }

    /// <summary>All vehicle types, sorted by id.</summary>
    public IReadOnlyList<VehicleDefinition> Vehicles => _vehicles;

    /// <summary>All waste fractions, sorted by id.</summary>
    public IReadOnlyList<WasteDefinition> WasteTypes => _wasteTypes;

    /// <summary>All producer archetypes, sorted by id.</summary>
    public IReadOnlyList<ProducerArchetype> ProducerArchetypes => _producerArchetypes;

    public VehicleDefinition Vehicle(string id) => Get(_vehicles, _vehicleIds, id, "vehicle");

    public WasteDefinition Waste(string id) => Get(_wasteTypes, _wasteIds, id, "waste type");

    public ProducerArchetype Archetype(string id) => Get(_producerArchetypes, _archetypeIds, id, "producer archetype");

    public bool TryGetVehicle(string id, out VehicleDefinition definition) => TryGet(_vehicles, _vehicleIds, id, out definition);

    public bool TryGetWaste(string id, out WasteDefinition definition) => TryGet(_wasteTypes, _wasteIds, id, out definition);

    public bool TryGetProducerArchetype(string id, out ProducerArchetype definition) => TryGet(_producerArchetypes, _archetypeIds, id, out definition);

    private static T Get<T>(T[] items, string[] ids, string id, string kind)
    {
        return TryGet(items, ids, id, out var definition)
            ? definition
            : throw new KeyNotFoundException(string.Create(CultureInfo.InvariantCulture,
                $"Unknown {kind} '{id}'. Known ids: {string.Join(", ", ids)}."));
    }

    private static bool TryGet<T>(T[] items, string[] ids, string id, out T definition)
    {
        var index = Array.BinarySearch(ids, id, StringComparer.Ordinal);
        if (index >= 0)
        {
            definition = items[index];
            return true;
        }

        definition = default!;
        return false;
    }
}
