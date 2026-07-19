using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.World;

namespace Ruera.Sim.Persistence;

/// <summary>
/// Content identity of the loaded scenario (map + entity definitions), stored
/// in the save header (RUE-8): different data = different game, the same
/// logic SimVersion applies to code. Deterministic: all collections are
/// sorted by construction.
/// </summary>
public static class ScenarioHash
{
    public static ulong Compute(StreetGraph graph, DefinitionRegistry definitions)
    {
        var hasher = Fnv1a64.Create();
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

        return hasher.Hash;
    }
}
