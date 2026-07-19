using Ruera.Sim.Data;

namespace Ruera.Sim.Tests;

public class DefinitionTests
{
    private static string SliceDataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "data", "definitions");

    private const string MinimalWasteJson = """
        { "formatVersion": 1, "wasteTypes": [ { "id": "base:mixed", "name": "Mixed", "baseSaleCentsPerKg": 2 } ] }
        """;

    private const string MinimalProducersJson = """
        {
          "formatVersion": 1,
          "archetypes": [
            {
              "id": "base:condo-small", "name": "Small condo",
              "bufferGrams": 120000, "maxSanitaryIntervalTicks": 7, "contractCentsPerMonth": 3000,
              "production": [ { "waste": "base:mixed", "gramsPerTick": 12000 } ]
            }
          ]
        }
        """;

    private static string CarriersJson(string carrierEntries) => $$"""
        { "formatVersion": 1, "carriers": [ {{carrierEntries}} ] }
        """;

    private const string GerlaEntry = """
        {
          "id": "base:gerla", "name": "Gerla",
          "capacityGrams": 25000, "fillMinutes": 6, "emptyMinutes": 4, "metersPerMinute": 55,
          "purchaseCents": 800, "maintenanceCentsPerDay": 0, "crew": 1, "availableFromYear": 1880
        }
        """;

    [Fact]
    public void CommittedSliceDefinitions_LoadAndMatchToyMapArchetypes()
    {
        var registry = DefinitionLoader.LoadFromDirectory(SliceDataDirectory);

        Assert.Equal(5, registry.Carriers.Count);
        Assert.Equal(25_000, registry.Carrier("base:gerla").CapacityGrams);
        Assert.Equal(1925, registry.Carrier("base:camion").AvailableFromYear);
        Assert.Equal(2, registry.Archetype("base:condo-large").MaxSanitaryIntervalTicks);

        // The toy map (RUE-9) references exactly these archetype ids.
        foreach (var id in (string[])["base:condo-small", "base:condo-large", "base:shop", "base:factory"])
            Assert.True(registry.TryGetProducerArchetype(id, out _), $"missing archetype '{id}'");
    }

    [Fact]
    public void NewCarrierType_IsAddedViaDataFileOnly()
    {
        // AC RUE-12: no sim code changes — just another entry in carriers.json.
        var directory = Directory.CreateTempSubdirectory("ruera-defs-").FullName;
        try
        {
            var compactorEntry = """
                {
                  "id": "base:autocompattatore", "name": "Autocompattatore",
                  "capacityGrams": 8000000, "fillMinutes": 10, "emptyMinutes": 25, "metersPerMinute": 250,
                  "purchaseCents": 2500000, "maintenanceCentsPerDay": 900, "crew": 2, "availableFromYear": 1955
                }
                """;
            File.WriteAllText(Path.Combine(directory, "carriers.json"), CarriersJson(GerlaEntry + "," + compactorEntry));
            File.WriteAllText(Path.Combine(directory, "waste.json"), MinimalWasteJson);
            File.WriteAllText(Path.Combine(directory, "producers.json"), MinimalProducersJson);

            var registry = DefinitionLoader.LoadFromDirectory(directory);

            var compactor = registry.Carrier("base:autocompattatore");
            Assert.Equal(8_000_000, compactor.Capacity.Value);
            Assert.Equal(2, registry.Carriers.Count);
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
        var camion = registry.Carrier("base:camion"); // available from 1925

        Assert.False(camion.IsAvailable(1924));
        Assert.True(camion.IsAvailable(1925));
        Assert.True(camion.IsAvailable(1920, anticipatedYears: 5)); // R&D hook (DESIGN.md §7)
        Assert.False(camion.IsAvailable(1920, anticipatedYears: 4));
    }

    [Fact]
    public void UnknownField_FailsWithClearError()
    {
        var badCarrier = GerlaEntry.Replace("\"capacityGrams\"", "\"capacityGramz\"");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(badCarrier), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("carriers.json", exception.Message);
    }

    [Fact]
    public void MissingRequiredField_FailsWithClearError()
    {
        var withoutCapacity = GerlaEntry.Replace("\"capacityGrams\": 25000,", "");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(withoutCapacity), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("carriers.json", exception.Message);
    }

    [Fact]
    public void BadRange_FailsNamingTheOffendingId()
    {
        var zeroCapacity = GerlaEntry.Replace("\"capacityGrams\": 25000", "\"capacityGrams\": 0");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(zeroCapacity), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("base:gerla", exception.Message);
        Assert.Contains("capacityGrams", exception.Message);
    }

    [Fact]
    public void DuplicateId_Fails()
    {
        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(GerlaEntry + "," + GerlaEntry), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("duplicate id 'base:gerla'", exception.Message);
    }

    [Fact]
    public void ProductionReferencingUnknownWaste_Fails()
    {
        var badProducers = MinimalProducersJson.Replace("\"waste\": \"base:mixed\"", "\"waste\": \"base:plastica\"");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(GerlaEntry), MinimalWasteJson, badProducers));

        Assert.Contains("base:plastica", exception.Message);
        Assert.Contains("base:condo-small", exception.Message);
    }

    [Fact]
    public void UnNamespacedId_IsRejected()
    {
        // Moddability day-one rule (DESIGN.md §2): ids are package:name.
        var bare = GerlaEntry.Replace("base:gerla", "gerla");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(bare), MinimalWasteJson, MinimalProducersJson));

        Assert.Contains("package:name", exception.Message);
    }

    [Fact]
    public void UnsupportedFormatVersion_Fails()
    {
        var futureVersion = MinimalWasteJson.Replace("\"formatVersion\": 1", "\"formatVersion\": 99");

        var exception = Assert.Throws<DefinitionLoadException>(() =>
            DefinitionLoader.Load(CarriersJson(GerlaEntry), futureVersion, MinimalProducersJson));

        Assert.Contains("formatVersion 99", exception.Message);
    }

    [Fact]
    public void UnknownId_LookupFailsWithKnownIdsListed()
    {
        var registry = DefinitionLoader.Load(CarriersJson(GerlaEntry), MinimalWasteJson, MinimalProducersJson);

        var exception = Assert.Throws<KeyNotFoundException>(() => registry.Carrier("base:zeppelin"));

        Assert.Contains("base:zeppelin", exception.Message);
        Assert.Contains("base:gerla", exception.Message);
    }

    [Fact]
    public void Registry_ListsAreSortedByOrdinalId()
    {
        var registry = DefinitionLoader.LoadFromDirectory(SliceDataDirectory);

        var ids = registry.Carriers.Select(v => v.Id).ToArray();
        var sorted = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(sorted, ids);
    }
}
