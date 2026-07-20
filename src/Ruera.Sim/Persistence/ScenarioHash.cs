using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.World;

namespace Ruera.Sim.Persistence;

/// <summary>
/// Content identity of the loaded scenario, stored in the save header (RUE-8):
/// different data = different game, the same logic SimVersion applies to code.
/// Deterministic: all collections are sorted or in declared order by
/// construction. The whole-bundle overload (RUE-38) folds the scenario config
/// — calendar, timeline, events, declared end — in with the map and entity
/// definitions, so modding any of them (e.g. a timeline entry) changes the
/// scenario hash.
/// </summary>
public static class ScenarioHash
{
    /// <summary>Map + entity definitions identity (pre-scenario-package callers, and the basis of the bundle hash).</summary>
    public static ulong Compute(StreetGraph graph, DefinitionRegistry definitions)
    {
        var hasher = Fnv1a64.Create();
        AddContent(ref hasher, graph, definitions);
        return hasher.Hash;
    }

    /// <summary>Whole-bundle identity (RUE-38): scenario config first, then map + definitions.</summary>
    public static ulong Compute(Scenario.Scenario scenario, StreetGraph graph, DefinitionRegistry definitions)
    {
        var hasher = Fnv1a64.Create();
        scenario.AddToHash(ref hasher);
        AddContent(ref hasher, graph, definitions);
        return hasher.Hash;
    }

    private static void AddContent(ref Fnv1a64 hasher, StreetGraph graph, DefinitionRegistry definitions)
    {
        AddGraph(ref hasher, graph);
        AddDefinitions(ref hasher, definitions);
    }

    /// <summary>Feeds one map's canonical fields into a hasher (reused by the package-set hash, RUE-40).</summary>
    internal static void AddGraph(ref Fnv1a64 hasher, StreetGraph graph)
    {
        hasher.Add(graph.MapId);
        hasher.Add(graph.Nodes.Count);
        foreach (var node in graph.Nodes)
        {
            hasher.Add(node.Id);
            hasher.Add(node.X);
            hasher.Add(node.Y);
        }

        hasher.Add(graph.Edges.Count);
        foreach (var edge in graph.Edges)
        {
            hasher.Add(edge.Id);
            hasher.Add(edge.From);
            hasher.Add(edge.To);
            hasher.Add(edge.LengthMeters);
        }

        hasher.Add(graph.Depots.Count);
        foreach (var depot in graph.Depots)
        {
            hasher.Add(depot.Id);
            hasher.Add(depot.Node);
        }

        hasher.Add(graph.Producers.Count);
        foreach (var producer in graph.Producers)
        {
            hasher.Add(producer.Id);
            hasher.Add(producer.Edge);
            hasher.Add(producer.Archetype);
        }
    }

    /// <summary>Feeds the merged entity definitions into a hasher (reused by the package-set hash, RUE-40).</summary>
    internal static void AddDefinitions(ref Fnv1a64 hasher, DefinitionRegistry definitions)
    {
        hasher.Add(definitions.Carriers.Count);
        foreach (var carrier in definitions.Carriers)
        {
            hasher.Add(carrier.Id);
            hasher.Add(carrier.Name);
            hasher.Add(carrier.CapacityGrams);
            hasher.Add(carrier.FillMinutes);
            hasher.Add(carrier.EmptyMinutes);
            hasher.Add(carrier.MetersPerMinute);
            hasher.Add(carrier.PurchaseCents);
            hasher.Add(carrier.MaintenanceCentsPerDay);
            hasher.Add(carrier.Crew);
            hasher.Add(carrier.AvailableFromYear);
        }

        hasher.Add(definitions.WasteTypes.Count);
        foreach (var waste in definitions.WasteTypes)
        {
            hasher.Add(waste.Id);
            hasher.Add(waste.Name);
            hasher.Add(waste.BaseSaleCentsPerKg);
        }

        hasher.Add(definitions.ProducerArchetypes.Count);
        foreach (var archetype in definitions.ProducerArchetypes)
        {
            hasher.Add(archetype.Id);
            hasher.Add(archetype.Name);
            hasher.Add(archetype.BufferGrams);
            hasher.Add(archetype.MaxSanitaryIntervalTicks);
            hasher.Add(archetype.ContractCentsPerMonth);
            hasher.Add(archetype.Production.Count);
            foreach (var production in archetype.Production)
            {
                hasher.Add(production.Waste);
                hasher.Add(production.GramsPerTick);
            }
        }
    }
}
