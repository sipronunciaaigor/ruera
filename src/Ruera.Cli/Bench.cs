using System.Diagnostics;
using System.Globalization;
using System.Text;

using Ruera.Sim;
using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Cli;

/// <summary>
/// Headless allocation/throughput benchmark for the per-tick system pipeline
/// (RUE-37, DESIGN.md §2 «50 anni in secondi»). Builds a synthetic world far
/// larger than the toy map, warms up, then measures bytes allocated and wall
/// time over a run of working days. Prints the final state hash so before/after
/// runs can confirm behaviour is unchanged.
///
/// Usage: <c>Ruera.Cli bench [gridW] [gridH] [carriers] [ticks]</c>
/// </summary>
internal static class Bench
{
    public static void Run(string[] args)
    {
        var gridW = Arg(args, 0, 8);
        var gridH = Arg(args, 1, 8);
        var carriers = Arg(args, 2, 16);
        var ticks = Arg(args, 3, 1000);

        var definitions = DefinitionLoader.Load(CarriersJson, WasteJson, ProducersJson);
        var (mapJson, edgeCount, producerCount) = BuildMap(gridW, gridH);
        var graph = MapLoader.Load(mapJson, definitions);

        var sim = new Simulation(1UL, graph, definitions);

        // Fleet + crew: one worker per carrier (crew gating needs trained crew).
        for (var i = 0; i < carriers; i++)
        {
            sim.Submit(new AddCarrierCommand("bench:cart"));
            sim.Submit(new HireWorkerCommand());
        }

        sim.Advance(1); // materialize carriers + workers at tick open

        var span = Math.Max(4, edgeCount / carriers * 2);
        for (var carrierId = 1; carrierId <= carriers; carrierId++)
        {
            var coverage = new int[span];
            for (var j = 0; j < span; j++)
                coverage[j] = (carrierId * 7 + j) % edgeCount + 1;
            sim.Submit(new SetCoverageCommand(carrierId, coverage));
        }

        sim.Advance(15); // past the ~10-tick training delay; reach steady state

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        sim.Advance(ticks);
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"bench grid={gridW}x{gridH} edges={edgeCount} producers={producerCount} carriers={carriers} ticks={ticks}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  alloc={allocated / 1024.0 / 1024.0:F2} MiB  alloc/tick={(double)allocated / ticks:F0} B  time={elapsedMs:F1} ms  us/tick={elapsedMs * 1000.0 / ticks:F1}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  hash={sim.StateHash():x16}"));
    }

    private static int Arg(string[] args, int index, int fallback) =>
        index < args.Length && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static (string Json, int EdgeCount, int ProducerCount) BuildMap(int width, int height)
    {
        var nodes = new StringBuilder();
        var edges = new StringBuilder();
        var producers = new StringBuilder();

        int NodeId(int row, int column) => row * width + column + 1;

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (nodes.Length > 0)
                    nodes.Append(',');
                nodes.Append(string.Create(CultureInfo.InvariantCulture,
                    $"{{\"id\":{NodeId(row, column)},\"x\":{column * 100},\"y\":{row * 100}}}"));
            }
        }

        var edgeId = 0;
        var producerId = 0;
        void AddEdge(int from, int to)
        {
            edgeId++;
            if (edges.Length > 0)
                edges.Append(',');
            edges.Append(string.Create(CultureInfo.InvariantCulture,
                $"{{\"id\":{edgeId},\"from\":{from},\"to\":{to},\"lengthMeters\":100}}"));
            // A producer on every other edge keeps collection work substantial.
            if (edgeId % 2 == 0)
            {
                producerId++;
                if (producers.Length > 0)
                    producers.Append(',');
                producers.Append(string.Create(CultureInfo.InvariantCulture,
                    $"{{\"id\":{producerId},\"edge\":{edgeId},\"archetype\":\"bench:src\"}}"));
            }
        }

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (column + 1 < width)
                    AddEdge(NodeId(row, column), NodeId(row, column + 1));
                if (row + 1 < height)
                    AddEdge(NodeId(row, column), NodeId(row + 1, column));
            }
        }

        var json = string.Create(CultureInfo.InvariantCulture,
            $"{{\"formatVersion\":1,\"id\":\"bench:grid\",\"name\":\"Bench grid\",\"nodes\":[{nodes}],\"edges\":[{edges}],\"depots\":[{{\"id\":1,\"node\":1}}],\"producers\":[{producers}]}}");
        return (json, edgeId, producerId);
    }

    private const string CarriersJson = """
        { "formatVersion": 1, "carriers": [
          { "id": "bench:cart", "name": "Cart", "capacityGrams": 200000, "fillMinutes": 2, "emptyMinutes": 5,
            "metersPerMinute": 80, "purchaseCents": 100, "maintenanceCentsPerDay": 0, "crew": 1, "availableFromYear": 1880 }
        ] }
        """;

    private const string WasteJson = """
        { "formatVersion": 1, "wasteTypes": [ { "id": "bench:mixed", "name": "Mixed", "baseSaleCentsPerKg": 1 } ] }
        """;

    private const string ProducersJson = """
        { "formatVersion": 1, "archetypes": [
          { "id": "bench:src", "name": "Source", "bufferGrams": 500000, "maxSanitaryIntervalTicks": 7,
            "contractCentsPerMonth": 0, "production": [ { "waste": "bench:mixed", "gramsPerTick": 5000 } ] }
        ] }
        """;
}
