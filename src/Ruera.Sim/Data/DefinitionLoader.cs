using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruera.Sim.Data;

/// <summary>Raised when definition files are malformed, incomplete, or inconsistent.</summary>
public sealed class DefinitionLoadException(string message) : Exception(message);

/// <summary>
/// Strict loader for the entity definition files (vehicles.json, waste.json,
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
            ReadFile(directory, "vehicles.json"),
            ReadFile(directory, "waste.json"),
            ReadFile(directory, "producers.json"));
    }

    public static DefinitionRegistry Load(string vehiclesJson, string wasteJson, string producersJson)
    {
        var vehicles = Parse<VehiclesFile>(vehiclesJson, "vehicles.json");
        var waste = Parse<WasteFile>(wasteJson, "waste.json");
        var producers = Parse<ProducersFile>(producersJson, "producers.json");

        ValidateVehicles(vehicles.Vehicles);
        ValidateWaste(waste.WasteTypes);
        ValidateArchetypes(producers.Archetypes, waste.WasteTypes);

        return new DefinitionRegistry(
            [.. vehicles.Vehicles],
            [.. waste.WasteTypes],
            [.. producers.Archetypes]);
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

    private static void ValidateVehicles(List<VehicleDefinition> vehicles)
    {
        RequireUniqueIds(vehicles.Select(v => v.Id), "vehicles.json");
        foreach (var vehicle in vehicles)
        {
            Require(!string.IsNullOrWhiteSpace(vehicle.Id), "vehicles.json", vehicle.Id, "id must not be empty");
            Require(!string.IsNullOrWhiteSpace(vehicle.Name), "vehicles.json", vehicle.Id, "name must not be empty");
            Require(vehicle.CapacityGrams > 0, "vehicles.json", vehicle.Id, Invariant($"capacityGrams must be > 0 (was {vehicle.CapacityGrams})"));
            Require(vehicle.FillMinutes >= 0, "vehicles.json", vehicle.Id, Invariant($"fillMinutes must be >= 0 (was {vehicle.FillMinutes})"));
            Require(vehicle.EmptyMinutes >= 0, "vehicles.json", vehicle.Id, Invariant($"emptyMinutes must be >= 0 (was {vehicle.EmptyMinutes})"));
            Require(vehicle.MetersPerMinute > 0, "vehicles.json", vehicle.Id, Invariant($"metersPerMinute must be > 0 (was {vehicle.MetersPerMinute})"));
            Require(vehicle.PurchaseCents >= 0, "vehicles.json", vehicle.Id, Invariant($"purchaseCents must be >= 0 (was {vehicle.PurchaseCents})"));
            Require(vehicle.MaintenanceCentsPerDay >= 0, "vehicles.json", vehicle.Id, Invariant($"maintenanceCentsPerDay must be >= 0 (was {vehicle.MaintenanceCentsPerDay})"));
            Require(vehicle.Crew >= 1, "vehicles.json", vehicle.Id, Invariant($"crew must be >= 1 (was {vehicle.Crew})"));
            Require(vehicle.AvailableFromYear is >= 1500 and <= 3000, "vehicles.json", vehicle.Id, Invariant($"availableFromYear must be within 1500-3000 (was {vehicle.AvailableFromYear})"));
        }
    }

    private static void ValidateWaste(List<WasteDefinition> wasteTypes)
    {
        RequireUniqueIds(wasteTypes.Select(w => w.Id), "waste.json");
        foreach (var waste in wasteTypes)
        {
            Require(!string.IsNullOrWhiteSpace(waste.Id), "waste.json", waste.Id, "id must not be empty");
            Require(!string.IsNullOrWhiteSpace(waste.Name), "waste.json", waste.Id, "name must not be empty");
            Require(waste.BaseSaleCentsPerKg >= 0, "waste.json", waste.Id, Invariant($"baseSaleCentsPerKg must be >= 0 (was {waste.BaseSaleCentsPerKg})"));
        }
    }

    private static void ValidateArchetypes(List<ProducerArchetype> archetypes, List<WasteDefinition> wasteTypes)
    {
        RequireUniqueIds(archetypes.Select(a => a.Id), "producers.json");
        var wasteIds = wasteTypes.Select(w => w.Id).ToArray();
        foreach (var archetype in archetypes)
        {
            Require(!string.IsNullOrWhiteSpace(archetype.Id), "producers.json", archetype.Id, "id must not be empty");
            Require(!string.IsNullOrWhiteSpace(archetype.Name), "producers.json", archetype.Id, "name must not be empty");
            Require(archetype.BufferGrams > 0, "producers.json", archetype.Id, Invariant($"bufferGrams must be > 0 (was {archetype.BufferGrams})"));
            Require(archetype.MaxSanitaryIntervalTicks >= 1, "producers.json", archetype.Id, Invariant($"maxSanitaryIntervalTicks must be >= 1 (was {archetype.MaxSanitaryIntervalTicks})"));
            Require(archetype.ContractCentsPerMonth >= 0, "producers.json", archetype.Id, Invariant($"contractCentsPerMonth must be >= 0 (was {archetype.ContractCentsPerMonth})"));
            Require(archetype.Production.Count > 0, "producers.json", archetype.Id, "production must not be empty");

            var seenWaste = new List<string>();
            foreach (var production in archetype.Production)
            {
                Require(wasteIds.Contains(production.Waste, StringComparer.Ordinal), "producers.json", archetype.Id,
                    Invariant($"production references unknown waste type '{production.Waste}'"));
                Require(!seenWaste.Contains(production.Waste, StringComparer.Ordinal), "producers.json", archetype.Id,
                    Invariant($"production lists waste type '{production.Waste}' more than once"));
                Require(production.GramsPerTick >= 0, "producers.json", archetype.Id,
                    Invariant($"gramsPerTick must be >= 0 (was {production.GramsPerTick})"));
                seenWaste.Add(production.Waste);
            }
        }
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

    private sealed class VehiclesFile : IDefinitionFile
    {
        public required int FormatVersion { get; init; }

        public required List<VehicleDefinition> Vehicles { get; init; }
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
