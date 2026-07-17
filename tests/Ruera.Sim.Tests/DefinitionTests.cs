using Ruera.Sim.Data;

namespace Ruera.Sim.Tests;

public class DefinitionTests
{
    private static string SliceDataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "data", "definitions");

    private const string MinimalWasteJson = """
        { "formatVersion": 1, "wasteTypes": [ { "id": "mixed", "name": "Mixed", "baseSaleCentsPerKg": 2 } ] }
        """;

    private const string MinimalProducersJson = """
        {
          "formatVersion": 1,
          "archetypes": [
            {
              "id": "condo-small", "name": "Small condo",
              "bufferGrams": 120000, "maxSanitaryIntervalTicks": 7,
              "production": [ { "waste": "mixed", "gramsPerTick": 12000 } ]
            }
          ]
        }
        """;

    private static string VehiclesJson(string vehicleEntries) => $$"""
        { "formatVersion": 1, "vehicles": [ {{vehicleEntries}} ] }
        """;

    private const string GerlaEntry = """
        {
          "id": "gerla", "name": "Gerla",
          "capacityGrams": 25000, "fillMinutes": 6, "emptyMinutes": 4, "metersPerMinute": 55,
          "purchaseCents": 800, "maintenanceCentsPerDay": 0, "crew": 1, "availableFromYear": 1880
        }
        """;

    [Fact]
    public void CommittedSliceDefinitions_LoadAndMatchToyMapArchetypes()
    {
        var registry = DefinitionLoader.LoadFromDirectory(SliceDataDirectory);

        Assert.Equal(5, registry.Vehicles.Count);
        Assert.Equal(25_000, registry.Vehicle("gerla").CapacityGrams);
        Assert.Equal(1925, registry.Vehicle("camion").AvailableFromYear);
        Assert.Equal(2, registry.Archetype("condo-large").MaxSanitaryIntervalTicks);

        // The toy map (RUE-9) references exactly these archetype ids.
        foreach (var id in (string[])["condo-small", "condo-large", "shop", "factory"])
            Assert.True(registry.TryGetProducerArchetype(id, out _), $"missing archetype '{id}'");
    }

    [Fact]
    public void NewVehicleType_IsAddedViaDataFileOnly()
    {
        // AC RUE-12: no sim code changes — just another entry in vehicles.json.
        var directory = Directory.CreateTempSubdirectory("ruera-defs-").FullName;
        try
        {
            var compactorEntry = """
                {
                  "id": "autocompattatore", "name": "Autocompattatore",
                  "capacityGrams": 8000000, "fillMinutes": 10, "emptyMinutes": 25, "metersPerMinute": 250,
                  "purchaseCents": 2500000, "maintenanceCentsPerDay": 900, "crew": 2, "availableFromYear": 1955
                }
                """;
            File.WriteAllText(Path.Combine(directory, "vehicles.json"), VehiclesJson(GerlaEntry + "," + compactorEntry));
            File.WriteAllText(Path.Combine(directory, "waste.json"), MinimalWasteJson);
            File.WriteAllText(Path.Combine(directory, "producers.json"), MinimalProducersJson);

            var registry = DefinitionLoader.LoadFromDirectory(directory);

            var compactor = registry.Vehicle("autocompattatore");
            Assert.Equal(8_000_000, compactor.Capacity.Value);
            Assert.Equal(2, registry.Vehicles.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EraGating_UsesAvailabilityYearAndRndAnticipation()
    {
        var registry = DefinitionLoader.LoadFromDirectory(SliceDataDirectory);
        var camion = registry.Vehicle("camion"); // available from 1925

        Assert.False(camion.IsAvailable(1924));
        Assert.True(camion.IsAvailable(1925));
        Assert.True(camion.IsAvailable(1920, anticipatedYears: 5)); // R&D hook (DESIGN.md §7)
        Assert.False(camion.IsAvailable(1920, anticipatedYears: 4));
    }

    [Fact]
    public void UnknownField_FailsWithClearError()
    {
        var badVehicle = GerlaEntry.Replace("\"capacityGrams\"", "\"capacityGramz\"");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(VehiclesJson(badVehicle), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("vehicles.json", exception.Message);
    }

    [Fact]
    public void MissingRequiredField_FailsWithClearError()
    {
        var withoutCapacity = GerlaEntry.Replace("\"capacityGrams\": 25000,", "");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(VehiclesJson(withoutCapacity), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("vehicles.json", exception.Message);
    }

    [Fact]
    public void BadRange_FailsNamingTheOffendingId()
    {
        var zeroCapacity = GerlaEntry.Replace("\"capacityGrams\": 25000", "\"capacityGrams\": 0");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(VehiclesJson(zeroCapacity), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("gerla", exception.Message);
        Assert.Contains("capacityGrams", exception.Message);
    }

    [Fact]
    public void DuplicateId_Fails()
    {
        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(VehiclesJson(GerlaEntry + "," + GerlaEntry), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("duplicate id 'gerla'", exception.Message);
    }

    [Fact]
    public void ProductionReferencingUnknownWaste_Fails()
    {
        var badProducers = MinimalProducersJson.Replace("\"waste\": \"mixed\"", "\"waste\": \"plastica\"");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(VehiclesJson(GerlaEntry), MinimalWasteJson, badProducers));

        Assert.Contains("plastica", exception.Message);
        Assert.Contains("condo-small", exception.Message);
    }

    [Fact]
    public void UnsupportedFormatVersion_Fails()
    {
        var futureVersion = MinimalWasteJson.Replace("\"formatVersion\": 1", "\"formatVersion\": 99");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(VehiclesJson(GerlaEntry), futureVersion, MinimalProducersJson));

        Assert.Contains("formatVersion 99", exception.Message);
    }

    [Fact]
    public void UnknownId_LookupFailsWithKnownIdsListed()
    {
        var registry = DefinitionLoader.Load(VehiclesJson(GerlaEntry), MinimalWasteJson, MinimalProducersJson);

        var exception = Assert.Throws<KeyNotFoundException>(() => registry.Vehicle("zeppelin"));

        Assert.Contains("zeppelin", exception.Message);
        Assert.Contains("gerla", exception.Message);
    }

    [Fact]
    public void Registry_ListsAreSortedByOrdinalId()
    {
        var registry = DefinitionLoader.LoadFromDirectory(SliceDataDirectory);

        var ids = registry.Vehicles.Select(v => v.Id).ToArray();
        var sorted = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(sorted, ids);
    }
}
