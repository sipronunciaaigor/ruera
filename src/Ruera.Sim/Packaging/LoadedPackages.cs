using System.Globalization;

using Ruera.Sim.Data;
using Ruera.Sim.World;

using ScenarioPackage = Ruera.Sim.Scenario.Scenario;

namespace Ruera.Sim.Packaging;

/// <summary>
/// The result of loading an ordered set of content packages (RUE-40): the
/// merged entity definitions, the maps and scenarios resolved across the set,
/// and the package-set identity for the save header (ordered <c>(id, version)</c>
/// plus the folded content hash). This is the top-level content the sim runs on.
/// </summary>
public sealed class LoadedPackages
{
    private readonly Dictionary<string, StreetGraph> _maps;
    private readonly Dictionary<string, ScenarioPackage> _scenarios;

    internal LoadedPackages(
        IReadOnlyList<(string Id, SemVer Version)> order,
        DefinitionRegistry definitions,
        Dictionary<string, StreetGraph> maps,
        Dictionary<string, ScenarioPackage> scenarios,
        ulong packageSetHash)
    {
        Order = order;
        Definitions = definitions;
        _maps = maps;
        _scenarios = scenarios;
        PackageSetHash = packageSetHash;
    }

    /// <summary>The packages in canonical load order: earlier ones are overridden by later ones.</summary>
    public IReadOnlyList<(string Id, SemVer Version)> Order { get; }

    /// <summary>Merged, validated definitions (replace-by-id in load order).</summary>
    public DefinitionRegistry Definitions { get; }

    /// <summary>Identity of the ordered package set + merged content (extends the RUE-38 bundle hash).</summary>
    public ulong PackageSetHash { get; }

    public IReadOnlyCollection<string> MapIds => _maps.Keys;

    public IReadOnlyCollection<string> ScenarioIds => _scenarios.Keys;

    public StreetGraph Map(string id) =>
        _maps.TryGetValue(id, out var map)
            ? map
            : throw new PackageLoadException(Invariant($"no map '{id}' in the loaded package set."));

    public ScenarioPackage Scenario(string id) =>
        _scenarios.TryGetValue(id, out var scenario)
            ? scenario
            : throw new PackageLoadException(Invariant($"no scenario '{id}' in the loaded package set."));

    /// <summary>
    /// Builds a fresh engine for a scenario in this package set. The scenario's
    /// map reference is already bound to a loaded map at load time (RUE-38
    /// follow-up: map ↔ MapId bind check).
    /// </summary>
    public Simulation NewSimulation(ulong seed, string scenarioId)
    {
        var scenario = Scenario(scenarioId);
        return Simulation.FromScenario(seed, scenario, Map(scenario.MapRef), Definitions);
    }

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);
}
