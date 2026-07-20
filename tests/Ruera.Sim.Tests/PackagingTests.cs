using Ruera.Sim.Packaging;
using Ruera.Sim.Persistence;

namespace Ruera.Sim.Tests;

/// <summary>
/// RUE-40: multi-package content loader — manifest, deterministic topological
/// load order, whole-entity replace-by-id override, cross-reference over the
/// merged set, and the package-set hash.
/// </summary>
public class PackagingTests
{
    private static string BasePackagesRoot =>
        Path.Combine(AppContext.BaseDirectory, "data", "packages");

    [Fact]
    public void BasePackage_Loads_MergesAndResolvesScenario()
    {
        var loaded = ContentLoader.LoadFromDirectory(BasePackagesRoot);

        Assert.Equal([("base", new SemVer(1, 0, 0))], loaded.Order);
        Assert.Equal(5, loaded.Definitions.Carriers.Count);
        Assert.Equal(25_000, loaded.Definitions.Carrier("base:gerla").CapacityGrams);
        Assert.Contains("base:toy", loaded.MapIds);
        Assert.Contains("base:milano-1880", loaded.ScenarioIds);

        // End-to-end: a scenario in the set builds a runnable sim (map bound).
        var sim = loaded.NewSimulation(0UL, "base:milano-1880");
        sim.Advance(30);
        Assert.Equal(1880, sim.Today.Year);
    }

    [Fact]
    public void PackageSetHash_IsStable_AcrossLoads()
    {
        var a = ContentLoader.LoadFromDirectory(BasePackagesRoot);
        var b = ContentLoader.LoadFromDirectory(BasePackagesRoot);

        Assert.Equal(a.PackageSetHash, b.PackageSetHash);
    }

    [Fact]
    public void SecondPackage_AddsAndOverrides_MergesInLoadOrder()
    {
        Run(root =>
        {
            CreatePackage(root, "core", "1.0.0", [],
                carriers: Carriers(Carrier("core:cart", 1000)),
                waste: Waste(WasteType("core:mixed")),
                producers: Producers(Archetype("core:condo", "core:mixed")));
            // Depends on core, so it may override core:cart (cross-namespace) and add its own.
            CreatePackage(root, "mod", "1.0.0", [("core", "1.0.0")],
                carriers: Carriers(Carrier("core:cart", 9999), Carrier("mod:truck", 5000)));

            var loaded = ContentLoader.LoadFromDirectory(root);

            Assert.Equal(["core", "mod"], loaded.Order.Select(p => p.Id));
            Assert.Equal(9999, loaded.Definitions.Carrier("core:cart").CapacityGrams); // overridden, last wins
            Assert.Equal(5000, loaded.Definitions.Carrier("mod:truck").CapacityGrams);  // added
            Assert.True(loaded.Definitions.TryGetProducerArchetype("core:condo", out _)); // inherited from core
        });
    }

    [Fact]
    public void LoadOrder_IsDeterministic_DependenciesFirstThenIdTiebreak()
    {
        Run(root =>
        {
            // c and b are independent (tiebreak by id → b before c); a depends on both.
            CreatePackage(root, "b", "1.0.0", [], waste: Waste(WasteType("b:w")));
            CreatePackage(root, "c", "1.0.0", [], waste: Waste(WasteType("c:w")));
            CreatePackage(root, "a", "1.0.0", [("b", "1.0.0"), ("c", "1.0.0")], waste: Waste(WasteType("a:w")));

            var loaded = ContentLoader.LoadFromDirectory(root);

            Assert.Equal(["b", "c", "a"], loaded.Order.Select(p => p.Id));
        });
    }

    [Fact]
    public void CrossNamespaceOverride_WithoutDependency_IsRejected()
    {
        Run(root =>
        {
            CreatePackage(root, "core", "1.0.0", [], carriers: Carriers(Carrier("core:cart", 1000)));
            CreatePackage(root, "rogue", "1.0.0", [], carriers: Carriers(Carrier("core:cart", 9999)));

            var exception = Assert.Throws<PackageLoadException>(() => ContentLoader.LoadFromDirectory(root));
            Assert.Contains("neither owns nor depends on", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MissingDependency_IsRejected()
    {
        Run(root =>
        {
            CreatePackage(root, "mod", "1.0.0", [("ghost", "1.0.0")], waste: Waste(WasteType("mod:w")));

            var exception = Assert.Throws<PackageLoadException>(() => ContentLoader.LoadFromDirectory(root));
            Assert.Contains("missing package 'ghost'", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void UnmetDependencyVersion_IsRejected()
    {
        Run(root =>
        {
            CreatePackage(root, "core", "1.0.0", [], waste: Waste(WasteType("core:w")));
            CreatePackage(root, "mod", "1.0.0", [("core", "2.0.0")], waste: Waste(WasteType("mod:w")));

            var exception = Assert.Throws<PackageLoadException>(() => ContentLoader.LoadFromDirectory(root));
            Assert.Contains(">= 2.0.0", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DependencyCycle_IsRejected()
    {
        Run(root =>
        {
            CreatePackage(root, "x", "1.0.0", [("y", "1.0.0")], waste: Waste(WasteType("x:w")));
            CreatePackage(root, "y", "1.0.0", [("x", "1.0.0")], waste: Waste(WasteType("y:w")));

            var exception = Assert.Throws<PackageLoadException>(() => ContentLoader.LoadFromDirectory(root));
            Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ModProducer_MayReferenceBaseWaste_AcrossPackages()
    {
        Run(root =>
        {
            CreatePackage(root, "core", "1.0.0", [], waste: Waste(WasteType("core:mixed")));
            // Archetype in mod references a waste type owned by core — resolves over the merged set.
            CreatePackage(root, "mod", "1.0.0", [("core", "1.0.0")],
                producers: Producers(Archetype("mod:condo", "core:mixed")));

            var loaded = ContentLoader.LoadFromDirectory(root);

            Assert.True(loaded.Definitions.TryGetProducerArchetype("mod:condo", out _));
        });
    }

    [Fact]
    public void BadManifestSemver_IsRejected()
    {
        var exception = Assert.Throws<PackageLoadException>(() => PackageManifest.Load(
            """{ "formatVersion": 1, "id": "base", "name": "Base", "version": "1.x", "gameVersion": "0.1.0", "dependencies": [] }"""));
        Assert.Contains("not a valid semver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestIdWithColon_IsRejected()
    {
        var exception = Assert.Throws<PackageLoadException>(() => PackageManifest.Load(
            """{ "formatVersion": 1, "id": "base:x", "name": "Base", "version": "1.0.0", "gameVersion": "0.1.0", "dependencies": [] }"""));
        Assert.Contains("bare namespace token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndLoad_ThroughPackageSet_RoundTrips()
    {
        var loaded = ContentLoader.LoadFromDirectory(BasePackagesRoot);
        var sim = loaded.NewSimulation(5UL, "base:milano-1880");
        sim.Advance(120);
        var expected = sim.StateHash();

        var restored = loaded.LoadSave(SaveSystem.Save(sim), "base:milano-1880");

        Assert.Equal(expected, restored.StateHash());
    }

    [Fact]
    public void LoadSave_WithDifferentPackageVersion_FailsNamingThePackage()
    {
        Run(root =>
        {
            // A save made from base 1.0.0…
            var original = ContentLoader.LoadFromDirectory(BasePackagesRoot);
            var sim = original.NewSimulation(3UL, "base:milano-1880");
            sim.Advance(20);
            var bytes = SaveSystem.Save(sim);

            // …cannot be loaded against the same package bumped to 1.0.1.
            var bumped = Path.Combine(root, "base");
            CopyDirectory(Path.Combine(BasePackagesRoot, "base"), bumped);
            var manifest = Path.Combine(bumped, "package.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"1.0.0\"", "\"1.0.1\"", StringComparison.Ordinal));

            var loaded = ContentLoader.LoadFromDirectory(root);
            var exception = Assert.Throws<SaveLoadException>(() => loaded.LoadSave(bytes, "base:milano-1880"));
            Assert.Contains("base", exception.Message, StringComparison.Ordinal);
            Assert.Contains("1.0.0", exception.Message, StringComparison.Ordinal);
        });
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), overwrite: true);
    }

    private static void Run(Action<string> test)
    {
        var root = Directory.CreateTempSubdirectory("ruera-pkg-").FullName;
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreatePackage(string root, string id, string version, (string Id, string Min)[] deps,
        string? carriers = null, string? waste = null, string? producers = null)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), Manifest(id, version, deps));
        if (carriers is null && waste is null && producers is null)
            return;

        var definitions = Path.Combine(dir, "definitions");
        Directory.CreateDirectory(definitions);
        if (carriers is not null)
            File.WriteAllText(Path.Combine(definitions, "carriers.json"), carriers);
        if (waste is not null)
            File.WriteAllText(Path.Combine(definitions, "waste.json"), waste);
        if (producers is not null)
            File.WriteAllText(Path.Combine(definitions, "producers.json"), producers);
    }

    private static string Manifest(string id, string version, (string Id, string Min)[] deps)
    {
        var depsJson = string.Join(",", deps.Select(d => $$"""{ "id": "{{d.Id}}", "minVersion": "{{d.Min}}" }"""));
        return $$"""{ "formatVersion": 1, "id": "{{id}}", "name": "{{id}}", "version": "{{version}}", "gameVersion": "0.1.0", "dependencies": [ {{depsJson}} ] }""";
    }

    private static string Carriers(params string[] entries) =>
        $$"""{ "formatVersion": 1, "carriers": [ {{string.Join(",", entries)}} ] }""";

    private static string Carrier(string id, long capacity) =>
        $$"""{ "id": "{{id}}", "name": "{{id}}", "capacityGrams": {{capacity}}, "fillMinutes": 5, "emptyMinutes": 5, "metersPerMinute": 60, "purchaseCents": 100, "maintenanceCentsPerDay": 0, "crew": 1, "availableFromYear": 1880 }""";

    private static string Waste(params string[] entries) =>
        $$"""{ "formatVersion": 1, "wasteTypes": [ {{string.Join(",", entries)}} ] }""";

    private static string WasteType(string id) =>
        $$"""{ "id": "{{id}}", "name": "{{id}}", "baseSaleCentsPerKg": 1 }""";

    private static string Producers(params string[] entries) =>
        $$"""{ "formatVersion": 1, "archetypes": [ {{string.Join(",", entries)}} ] }""";

    private static string Archetype(string id, string waste) =>
        $$"""{ "id": "{{id}}", "name": "{{id}}", "bufferGrams": 1000, "maxSanitaryIntervalTicks": 7, "contractCentsPerMonth": 100, "production": [ { "waste": "{{waste}}", "gramsPerTick": 10 } ] }""";
}
