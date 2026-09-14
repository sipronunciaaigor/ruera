using System.Globalization;

using Ruera.Sim;
using Ruera.Sim.Commands;
using Ruera.Sim.Packaging;

namespace Ruera.Cli;

/// <summary>
/// Headless play harness (RUE-46, DESIGN.md §2 «test automatici di
/// bilanciamento»): scripts a fixed fleet strategy over N years and prints a
/// monthly ledger, so balance changes can be judged without Godot. Promotes
/// the scratch probe used for the 2026-09-14 engine assessment (PLAN.md
/// Appendix A) to a first-class command.
///
/// Usage: <c>Ruera.Cli play [--packages dir] [--scenario id] [--seed n] [--years n] [--strategy name]</c>
/// </summary>
internal static class Play
{
    private static readonly string[] StrategyNames = ["none", "gerle", "mixed", "two-navazze"];

    public static void Run(string[] args)
    {
        var packagesDir = Arg(args, "--packages", "data/packages");
        var scenarioId = Arg(args, "--scenario", "base:milano-1880");
        var seed = ulong.Parse(Arg(args, "--seed", "42"), CultureInfo.InvariantCulture);
        var years = int.Parse(Arg(args, "--years", "1"), CultureInfo.InvariantCulture);
        var strategy = Arg(args, "--strategy", "two-navazze");

        if (!Array.Exists(StrategyNames, s => s == strategy))
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture,
                $"unknown strategy '{strategy}'. Known: {string.Join(", ", StrategyNames)}."));

        var packages = ContentLoader.LoadFromDirectory(Path.GetFullPath(packagesDir));
        var sim = packages.NewSimulation(seed, scenarioId);
        RunStrategy(sim, strategy);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"play scenario={scenarioId} seed={seed} years={years} strategy={strategy}"));

        long fines = 0, overflow = 0, sanitary = 0, breakdowns = 0, inspections = 0, tenders = 0, soldCents = 0;
        var lastCash = sim.State.CashCents;
        var lastMonth = sim.Today.Month;
        for (var day = 0; day < 365 * years; day++)
        {
            sim.Advance(1);
            foreach (var simEvent in sim.State.LastTickEvents)
            {
                switch (simEvent.Type)
                {
                    case SimEventType.BufferOverflow: overflow++; fines += 500; break;
                    case SimEventType.SanitaryViolation: sanitary++; fines += 500; break;
                    case SimEventType.CarrierBreakdown: breakdowns++; break;
                    case SimEventType.SanitaryInspection: inspections++; fines += simEvent.Data; break;
                    case SimEventType.TenderAnnounced: tenders++; break;
                    case SimEventType.MaterialSold: soldCents += simEvent.Data; break;
                    case SimEventType.Bankruptcy:
                        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  !!! BANKRUPT at {sim.Today}"));
                        break;
                }
            }

            if (sim.Today.Month != lastMonth)
            {
                var state = sim.State;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{sim.Today}: cash={state.CashCents / 100.0,9:F2} (d{(state.CashCents - lastCash) / 100.0,8:F2}) " +
                    $"stockpile={state.StockpileGrams / 1000,6} kg sorted={state.SortedGrams / 1000,4} kg sold={soldCents / 100.0,8:F2} " +
                    $"viol(over/san)={overflow}/{sanitary} fines={fines / 100.0:F0} brk={breakdowns} insp={inspections} tenders={tenders}"));
                lastCash = state.CashCents;
                lastMonth = sim.Today.Month;
            }
        }

        var final = sim.State;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"end {sim.Today}: cash={final.CashCents / 100.0:F2} lire bankrupt={final.Bankrupt} " +
            $"stockpile={final.StockpileGrams / 1000} kg sold={soldCents / 100.0:F2} hash={sim.StateHash():x16}"));
        foreach (var producer in final.Producers)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  producer {producer.Id} {producer.Archetype.Id} edge={producer.EdgeId} " +
                $"buffer={producer.BufferGrams / 1000} kg (max {producer.Archetype.BufferGrams / 1000}) " +
                $"contract={producer.HasContract} violations={producer.ViolationCount} lastCollected={producer.LastCollectedTick}"));
        }
    }

    /// <summary>
    /// Signs every producer under contract, then buys/hires the strategy's
    /// fleet and paints coverage once deliveries/training land (tick 6).
    /// Coverage splits the producer edges round-robin across the fleet.
    /// </summary>
    private static void RunStrategy(Simulation sim, string strategy)
    {
        var state = sim.State;
        foreach (var producer in state.Producers)
            sim.Submit(new SignContractCommand(producer.Id));

        int[] allProducerEdges = [.. state.Producers.Select(p => p.EdgeId).Distinct().OrderBy(e => e)];

        switch (strategy)
        {
            case "none":
                break;
            case "gerle":
                for (var i = 0; i < 4; i++)
                    sim.Submit(new AddCarrierCommand("base:gerla"));
                break;
            case "mixed":
                sim.Submit(new BuyCarrierCommand("base:navazza"));
                sim.Submit(new AddCarrierCommand("base:carretto"));
                break;
            case "two-navazze":
                sim.Submit(new BuyCarrierCommand("base:navazza"));
                sim.Submit(new BuyCarrierCommand("base:navazza"));
                break;
        }

        sim.Advance(6); // purchase deliveries + hire training land

        var carrierCount = state.Carriers.Count;
        for (var carrierId = 1; carrierId <= carrierCount; carrierId++)
        {
            var coverage = allProducerEdges.Where((_, i) => i % carrierCount == carrierId - 1).ToArray();
            sim.Submit(new SetCoverageCommand(carrierId, coverage));
        }
    }

    private static string Arg(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        }

        return fallback;
    }
}
