using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.Persistence;
using Ruera.Sim.Scenario;
using Ruera.Sim.World;

using ScenarioPackage = Ruera.Sim.Scenario.Scenario;

namespace Ruera.Sim.Packaging;

/// <summary>
/// Loads an ordered set of content packages and merges them into one runnable
/// content set (RUE-36/RUE-40). A package is a folder with a
/// <c>package.json</c> manifest plus content by convention
/// (<c>definitions/</c>, <c>maps/</c>, <c>scenarios/</c>). Load order is a
/// deterministic topological sort of the dependency DAG (stable tiebreak by
/// package id); override is whole-entity replace by id, last-writer-wins.
/// </summary>
public static class ContentLoader
{
    public static LoadedPackages LoadFromDirectory(string packagesRoot)
    {
        if (!Directory.Exists(packagesRoot))
            throw new PackageLoadException(Invariant($"packages root not found: '{packagesRoot}'."));

        var discovered = new List<DiscoveredPackage>();
        foreach (var dir in Directory.GetDirectories(packagesRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(dir, "package.json");
            if (!File.Exists(manifestPath))
                continue;
            var manifest = PackageManifest.Load(File.ReadAllText(manifestPath),
                Invariant($"{Path.GetFileName(dir)}/package.json"));
            discovered.Add(new DiscoveredPackage(manifest, dir));
        }

        if (discovered.Count == 0)
            throw new PackageLoadException(Invariant(
                $"no packages found under '{packagesRoot}' (a package is a folder with a package.json)."));

        return Load(discovered);
    }

    private static LoadedPackages Load(List<DiscoveredPackage> discovered)
    {
        var byId = new Dictionary<string, DiscoveredPackage>(StringComparer.Ordinal);
        foreach (var pkg in discovered)
        {
            if (!byId.TryAdd(pkg.Manifest.Id, pkg))
                throw new PackageLoadException(Invariant($"duplicate package id '{pkg.Manifest.Id}'."));
        }

        foreach (var pkg in discovered)
        {
            foreach (var dependency in pkg.Manifest.Dependencies)
            {
                if (!byId.TryGetValue(dependency.Id, out var target))
                    throw new PackageLoadException(Invariant(
                        $"package '{pkg.Manifest.Id}' requires missing package '{dependency.Id}'."));
                if (target.Manifest.Version < dependency.MinVersion)
                    throw new PackageLoadException(Invariant(
                        $"package '{pkg.Manifest.Id}' requires '{dependency.Id}' >= {dependency.MinVersion}, but {target.Manifest.Version} is present."));
            }
        }

        var ordered = TopologicalOrder(discovered, byId);

        // Merge definitions: replace-by-id, last-writer-wins in load order. The
        // archetype->waste cross-reference is resolved once over the merged set.
        var carriers = new Dictionary<string, CarrierDefinition>(StringComparer.Ordinal);
        var waste = new Dictionary<string, WasteDefinition>(StringComparer.Ordinal);
        var archetypes = new Dictionary<string, ProducerArchetype>(StringComparer.Ordinal);
        foreach (var pkg in ordered)
        {
            var fragment = LoadFragment(pkg);
            var allowed = AllowedNamespaces(pkg.Manifest);
            foreach (var carrier in fragment.Carriers)
            {
                RequireNamespace(carrier.Id, allowed, pkg.Manifest.Id, "carrier");
                carriers[carrier.Id] = carrier;
            }

            foreach (var wasteType in fragment.WasteTypes)
            {
                RequireNamespace(wasteType.Id, allowed, pkg.Manifest.Id, "waste type");
                waste[wasteType.Id] = wasteType;
            }

            foreach (var archetype in fragment.Archetypes)
            {
                RequireNamespace(archetype.Id, allowed, pkg.Manifest.Id, "producer archetype");
                archetypes[archetype.Id] = archetype;
            }
        }

        var definitions = DefinitionLoader.BuildRegistry([.. carriers.Values], [.. waste.Values], [.. archetypes.Values]);

        // Maps are validated against the merged registry (producer archetype refs
        // may cross packages) and merged by MapId.
        var maps = new Dictionary<string, StreetGraph>(StringComparer.Ordinal);
        foreach (var pkg in ordered)
        {
            var allowed = AllowedNamespaces(pkg.Manifest);
            foreach (var mapPath in EnumerateFiles(pkg.Directory, "maps", "*.map.json"))
            {
                var graph = MapLoader.LoadFromFile(mapPath, definitions);
                RequireNamespace(graph.MapId, allowed, pkg.Manifest.Id, "map");
                maps[graph.MapId] = graph;
            }
        }

        // Scenarios last: every map is now known, so the map-ref bind check
        // (RUE-38 follow-up) resolves against the full merged map set.
        var scenarios = new Dictionary<string, ScenarioPackage>(StringComparer.Ordinal);
        foreach (var pkg in ordered)
        {
            var allowed = AllowedNamespaces(pkg.Manifest);
            foreach (var scenarioPath in EnumerateScenarioFiles(pkg.Directory))
            {
                var scenario = ScenarioLoader.LoadFromFile(scenarioPath);
                RequireNamespace(scenario.Id, allowed, pkg.Manifest.Id, "scenario");
                if (!maps.ContainsKey(scenario.MapRef))
                    throw new PackageLoadException(Invariant(
                        $"scenario '{scenario.Id}' references map '{scenario.MapRef}', which no loaded package provides."));
                scenarios[scenario.Id] = scenario;
            }
        }

        var order = ordered.Select(p => (p.Manifest.Id, p.Manifest.Version)).ToArray();
        var hash = ComputePackageSetHash(ordered, definitions, maps, scenarios);
        return new LoadedPackages(order, definitions, maps, scenarios, hash);
    }

    /// <summary>
    /// Deterministic load order: dependencies before dependents, ties broken by
    /// package id (ordinal). Never depends on filesystem or clock order. Places
    /// one package per step (the smallest ready id) for a fully canonical order.
    /// </summary>
    private static List<DiscoveredPackage> TopologicalOrder(
        List<DiscoveredPackage> packages, Dictionary<string, DiscoveredPackage> byId)
    {
        var remaining = new HashSet<string>(packages.Select(p => p.Manifest.Id), StringComparer.Ordinal);
        var result = new List<DiscoveredPackage>(packages.Count);
        while (remaining.Count > 0)
        {
            string? next = null;
            foreach (var id in remaining.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (byId[id].Manifest.Dependencies.All(d => !remaining.Contains(d.Id)))
                {
                    next = id;
                    break;
                }
            }

            if (next is null)
                throw new PackageLoadException(Invariant(
                    $"dependency cycle among packages: {string.Join(", ", remaining.OrderBy(x => x, StringComparer.Ordinal))}."));

            result.Add(byId[next]);
            remaining.Remove(next);
        }

        return result;
    }

    private static ulong ComputePackageSetHash(
        List<DiscoveredPackage> ordered,
        DefinitionRegistry definitions,
        Dictionary<string, StreetGraph> maps,
        Dictionary<string, ScenarioPackage> scenarios)
    {
        var hasher = Fnv1a64.Create();
        hasher.Add(ordered.Count);
        foreach (var pkg in ordered) // canonical load order
        {
            hasher.Add(pkg.Manifest.Id);
            hasher.Add(pkg.Manifest.Version.Major);
            hasher.Add(pkg.Manifest.Version.Minor);
            hasher.Add(pkg.Manifest.Version.Patch);
        }

        ScenarioHash.AddDefinitions(ref hasher, definitions);

        hasher.Add(maps.Count);
        foreach (var mapId in maps.Keys.OrderBy(k => k, StringComparer.Ordinal))
            ScenarioHash.AddGraph(ref hasher, maps[mapId]);

        hasher.Add(scenarios.Count);
        foreach (var scenarioId in scenarios.Keys.OrderBy(k => k, StringComparer.Ordinal))
            scenarios[scenarioId].AddToHash(ref hasher);

        return hasher.Hash;
    }

    private static DefinitionFragment LoadFragment(DiscoveredPackage pkg)
    {
        var directory = Path.Combine(pkg.Directory, "definitions");
        return DefinitionLoader.LoadFragment(
            ReadIfExists(Path.Combine(directory, "carriers.json")),
            ReadIfExists(Path.Combine(directory, "waste.json")),
            ReadIfExists(Path.Combine(directory, "producers.json")));
    }

    private static HashSet<string> AllowedNamespaces(PackageManifest manifest)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { manifest.Id };
        foreach (var dependency in manifest.Dependencies)
            allowed.Add(dependency.Id);
        return allowed;
    }

    private static void RequireNamespace(string contentId, HashSet<string> allowed, string packageId, string kind)
    {
        var colon = contentId.IndexOf(':');
        var ns = colon > 0 ? contentId[..colon] : contentId;
        if (!allowed.Contains(ns))
            throw new PackageLoadException(Invariant(
                $"package '{packageId}' declares {kind} '{contentId}' in namespace '{ns}', which it neither owns nor depends on."));
    }

    private static IEnumerable<string> EnumerateFiles(string packageDir, string subDirectory, string pattern)
    {
        var directory = Path.Combine(packageDir, subDirectory);
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern).OrderBy(f => f, StringComparer.Ordinal)
            : [];
    }

    private static IEnumerable<string> EnumerateScenarioFiles(string packageDir)
    {
        var directory = Path.Combine(packageDir, "scenarios");
        if (!Directory.Exists(directory))
            return [];
        return Directory.GetDirectories(directory)
            .OrderBy(d => d, StringComparer.Ordinal)
            .Select(d => Path.Combine(d, "scenario.json"))
            .Where(File.Exists);
    }

    private static string? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);

    private sealed record DiscoveredPackage(PackageManifest Manifest, string Directory);
}
