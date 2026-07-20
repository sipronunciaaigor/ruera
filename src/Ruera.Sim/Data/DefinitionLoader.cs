using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruera.Sim.Data;

/// <summary>Raised when definition files are malformed, incomplete, or inconsistent.</summary>
public sealed class DefinitionLoadException(string message) : Exception(message);

/// <summary>
/// Strict loader for the entity definition files (carriers.json, waste.json,
/// producers.json — DESIGN.md §2 «Dati, non codice»). JSON is culture-free by
/// spec and quantities are integers in the files (§2 rule 8). Unknown fields,
/// missing required fields, bad ranges and dangling references all fail the
/// load with an error that names the file and the offending id.
/// </summary>
public static class DefinitionLoader
{
    private const int SupportedFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static DefinitionRegistry LoadFromDirectory(string directory)
    {
        return Load(
            ReadFile(directory, "carriers.json"),
            ReadFile(directory, "waste.json"),
            ReadFile(directory, "producers.json"));
    }

    public static DefinitionRegistry Load(string carriersJson, string wasteJson, string producersJson)
    {
        var fragment = LoadFragment(carriersJson, wasteJson, producersJson);
        return BuildRegistry(fragment.Carriers, fragment.WasteTypes, fragment.Archetypes);
    }

    /// <summary>
    /// Parses and intrinsically validates one package's definition files, but
    /// defers the archetype→waste cross-reference (RUE-40): that resolves against
    /// the merged set across packages, so a mod producer may reference a base
    /// waste. Missing files yield empty lists — a package need not ship all three.
    /// </summary>
    internal static DefinitionFragment LoadFragment(string? carriersJson, string? wasteJson, string? producersJson)
    {
        var carriers = carriersJson is null ? [] : Parse<CarriersFile>(carriersJson, "carriers.json").Carriers;
        var waste = wasteJson is null ? [] : Parse<WasteFile>(wasteJson, "waste.json").WasteTypes;
        var archetypes = producersJson is null ? [] : Parse<ProducersFile>(producersJson, "producers.json").Archetypes;

        ValidateCarriers(carriers);
        ValidateWaste(waste);
        ValidateArchetypesIntrinsic(archetypes);

        return new DefinitionFragment(carriers, waste, archetypes);
    }

    /// <summary>
    /// Validates archetype→waste references over a (possibly cross-package
    /// merged) waste set and builds the immutable registry. The intrinsic
    /// per-entity checks are assumed already done by <see cref="LoadFragment"/>.
    /// </summary>
    internal static DefinitionRegistry BuildRegistry(
        IReadOnlyList<CarrierDefinition> carriers,
        IReadOnlyList<WasteDefinition> wasteTypes,
        IReadOnlyList<ProducerArchetype> archetypes)
    {
        ValidateArchetypeWasteRefs(archetypes, [.. wasteTypes.Select(w => w.Id)]);
        return new DefinitionRegistry([.. carriers], [.. wasteTypes], [.. archetypes]);
    }

    private static string ReadFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            throw new DefinitionLoadException(Invariant($"{fileName}: file not found in '{directory}'."));
        return File.ReadAllText(path);
    }

    private static TFile Parse<TFile>(string json, string fileName)
        where TFile : IDefinitionFile
    {
        TFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TFile>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new DefinitionLoadException(Invariant($"{fileName}: {exception.Message}"));
        }

        if (file is null)
            throw new DefinitionLoadException(Invariant($"{fileName}: file is empty."));
        if (file.FormatVersion != SupportedFormatVersion)
            throw new DefinitionLoadException(Invariant(
                $"{fileName}: unsupported formatVersion {file.FormatVersion} (supported: {SupportedFormatVersion})."));
        return file;
    }

    private static void ValidateCarriers(List<CarrierDefinition> carriers)
    {
        RequireUniqueIds(carriers.Select(v => v.Id), "carriers.json");
        foreach (var carrier in carriers)
        {
            Require(!string.IsNullOrWhiteSpace(carrier.Id), "carriers.json", carrier.Id, "id must not be empty");
            RequireNamespacedId(carrier.Id, "carriers.json");
            Require(!string.IsNullOrWhiteSpace(carrier.Name), "carriers.json", carrier.Id, "name must not be empty");
            Require(carrier.CapacityGrams > 0, "carriers.json", carrier.Id, Invariant($"capacityGrams must be > 0 (was {carrier.CapacityGrams})"));
            Require(carrier.FillMinutes >= 0, "carriers.json", carrier.Id, Invariant($"fillMinutes must be >= 0 (was {carrier.FillMinutes})"));
            Require(carrier.EmptyMinutes >= 0, "carriers.json", carrier.Id, Invariant($"emptyMinutes must be >= 0 (was {carrier.EmptyMinutes})"));
            Require(carrier.MetersPerMinute > 0, "carriers.json", carrier.Id, Invariant($"metersPerMinute must be > 0 (was {carrier.MetersPerMinute})"));
            Require(carrier.PurchaseCents >= 0, "carriers.json", carrier.Id, Invariant($"purchaseCents must be >= 0 (was {carrier.PurchaseCents})"));
            Require(carrier.MaintenanceCentsPerDay >= 0, "carriers.json", carrier.Id, Invariant($"maintenanceCentsPerDay must be >= 0 (was {carrier.MaintenanceCentsPerDay})"));
            Require(carrier.Crew >= 1, "carriers.json", carrier.Id, Invariant($"crew must be >= 1 (was {carrier.Crew})"));
            Require(carrier.AvailableFromYear is >= 1500 and <= 3000, "carriers.json", carrier.Id, Invariant($"availableFromYear must be within 1500-3000 (was {carrier.AvailableFromYear})"));
        }
    }

    private static void ValidateWaste(List<WasteDefinition> wasteTypes)
    {
        RequireUniqueIds(wasteTypes.Select(w => w.Id), "waste.json");
        foreach (var waste in wasteTypes)
        {
            Require(!string.IsNullOrWhiteSpace(waste.Id), "waste.json", waste.Id, "id must not be empty");
            RequireNamespacedId(waste.Id, "waste.json");
            Require(!string.IsNullOrWhiteSpace(waste.Name), "waste.json", waste.Id, "name must not be empty");
            Require(waste.BaseSaleCentsPerKg >= 0, "waste.json", waste.Id, Invariant($"baseSaleCentsPerKg must be >= 0 (was {waste.BaseSaleCentsPerKg})"));
        }
    }

    private static void ValidateArchetypesIntrinsic(List<ProducerArchetype> archetypes)
    {
        RequireUniqueIds(archetypes.Select(a => a.Id), "producers.json");
        foreach (var archetype in archetypes)
        {
            Require(!string.IsNullOrWhiteSpace(archetype.Id), "producers.json", archetype.Id, "id must not be empty");
            RequireNamespacedId(archetype.Id, "producers.json");
            Require(!string.IsNullOrWhiteSpace(archetype.Name), "producers.json", archetype.Id, "name must not be empty");
            Require(archetype.BufferGrams > 0, "producers.json", archetype.Id, Invariant($"bufferGrams must be > 0 (was {archetype.BufferGrams})"));
            Require(archetype.MaxSanitaryIntervalTicks >= 1, "producers.json", archetype.Id, Invariant($"maxSanitaryIntervalTicks must be >= 1 (was {archetype.MaxSanitaryIntervalTicks})"));
            Require(archetype.ContractCentsPerMonth >= 0, "producers.json", archetype.Id, Invariant($"contractCentsPerMonth must be >= 0 (was {archetype.ContractCentsPerMonth})"));
            Require(archetype.Production.Count > 0, "producers.json", archetype.Id, "production must not be empty");

            var seenWaste = new List<string>();
            foreach (var production in archetype.Production)
            {
                Require(!seenWaste.Contains(production.Waste, StringComparer.Ordinal), "producers.json", archetype.Id,
                    Invariant($"production lists waste type '{production.Waste}' more than once"));
                Require(production.GramsPerTick >= 0, "producers.json", archetype.Id,
                    Invariant($"gramsPerTick must be >= 0 (was {production.GramsPerTick})"));
                seenWaste.Add(production.Waste);
            }
        }
    }

    /// <summary>The archetype→waste cross-reference, over the merged waste set (RUE-40).</summary>
    private static void ValidateArchetypeWasteRefs(IReadOnlyList<ProducerArchetype> archetypes, string[] wasteIds)
    {
        foreach (var archetype in archetypes)
        {
            foreach (var production in archetype.Production)
            {
                Require(wasteIds.Contains(production.Waste, StringComparer.Ordinal), "producers.json", archetype.Id,
                    Invariant($"production references unknown waste type '{production.Waste}'"));
            }
        }
    }

    /// <summary>Moddability day-one rule (DESIGN.md §2 «Moddabilità»): every content id is package:name.</summary>
    private static void RequireNamespacedId(string id, string fileName)
    {
        var colon = id.IndexOf(':');
        if (colon <= 0 || colon != id.LastIndexOf(':') || colon == id.Length - 1)
            throw new DefinitionLoadException(Invariant(
                $"{fileName}: id '{id}' must be namespaced as 'package:name' (e.g. 'base:gerla')."));
    }

    private static void RequireUniqueIds(IEnumerable<string> ids, string fileName)
    {
        var seen = new List<string>();
        foreach (var id in ids)
        {
            if (seen.Contains(id, StringComparer.Ordinal))
                throw new DefinitionLoadException(Invariant($"{fileName}: duplicate id '{id}'."));
            seen.Add(id);
        }
    }

    private static void Require(bool condition, string fileName, string id, string message)
    {
        if (!condition)
            throw new DefinitionLoadException(Invariant($"{fileName}: '{id}': {message}."));
    }

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);

    private interface IDefinitionFile
    {
        int FormatVersion { get; }
    }

    private sealed class CarriersFile : IDefinitionFile
    {
        public required int FormatVersion { get; init; }

        public required List<CarrierDefinition> Carriers { get; init; }
    }

    private sealed class WasteFile : IDefinitionFile
    {
        public required int FormatVersion { get; init; }

        public required List<WasteDefinition> WasteTypes { get; init; }
    }

    private sealed class ProducersFile : IDefinitionFile
    {
        public required int FormatVersion { get; init; }

        public required List<ProducerArchetype> Archetypes { get; init; }
    }
}
