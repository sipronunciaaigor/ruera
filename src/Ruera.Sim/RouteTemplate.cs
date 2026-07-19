using Ruera.Sim.Calendar;

namespace Ruera.Sim;

/// <summary>
/// A «linea di servizio» (DESIGN.md §4): a named tour template — an edge set
/// scheduled on the weekly pattern and assigned to carriers ("Giro Navigli:
/// camion 3 e 7, lun/gio"). Per-carrier direct coverage remains the override.
/// The line is also the per-route reporting unit.
/// </summary>
public sealed class RouteTemplate
{
    private int[] _edges;
    private int[] _assignedCarriers = [];

    internal RouteTemplate(int id, string name, int[] sortedEdges, byte weekdayMask)
    {
        Id = id;
        Name = name;
        _edges = sortedEdges;
        WeekdayMask = weekdayMask;
    }

    public int Id { get; }

    public string Name { get; internal set; }

    /// <summary>Bit per <see cref="Weekday"/> (Monday = bit 0 … Sunday = bit 6). Rest days never collect regardless.</summary>
    public byte WeekdayMask { get; internal set; }

    /// <summary>Covered street edges, sorted ascending (canonical form).</summary>
    public IReadOnlyList<int> Edges => _edges;

    /// <summary>Assigned carrier ids, sorted ascending.</summary>
    public IReadOnlyList<int> AssignedCarriers => _assignedCarriers;

    internal int[] EdgeArray => _edges;

    internal int[] AssignedArray => _assignedCarriers;

    internal void Update(string name, int[] sortedEdges, byte weekdayMask)
    {
        Name = name;
        _edges = sortedEdges;
        WeekdayMask = weekdayMask;
    }

    internal void SetAssignedCarriers(int[] sortedCarrierIds) => _assignedCarriers = sortedCarrierIds;

    public bool IsActiveOn(Weekday weekday) => (WeekdayMask & (1 << (int)weekday)) != 0;

    /// <summary>Convenience for building masks: Mask(Weekday.Monday, Weekday.Thursday).</summary>
    public static byte Mask(params Weekday[] days)
    {
        byte mask = 0;
        foreach (var day in days)
            mask |= (byte)(1 << (int)day);
        return mask;
    }
}

/// <summary>What one service line achieved in the last resolved day (per-line totals, DESIGN.md §4).</summary>
public sealed record LineReport(int TemplateId, int CarriersDispatched, long CollectedGrams, IReadOnlyList<int> ServedProducerIds);
