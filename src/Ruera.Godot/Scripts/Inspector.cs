using System.Globalization;
using System.Linq;

using Godot;

namespace Ruera.Renderer;

/// <summary>
/// Read-only inspector panel (RUE-19, B5): clicking a producer box or
/// carrier sphere shows its live state. Selection = emissive highlight on
/// the Mesh child (WorldView.SetProducerSelected/SetCarrierSelected); never
/// mutates <c>Sim.State</c> (DESIGN.md §3: inspection is a read query).
/// </summary>
public partial class Inspector : Node
{
    private GameRoot _root = null!;
    private Label _label = null!;

    private int? _selectedProducerId;
    private int? _selectedCarrierId;

    public void Setup(GameRoot root, Label label)
    {
        _root = root;
        _label = label;

        var world = root.World!;
        world.ProducerClicked += SelectProducer;
        world.CarrierClicked += SelectCarrier;
        root.TickResolved += Refresh;
    }

    private void SelectProducer(int producerId)
    {
        ClearSelection();
        _selectedProducerId = producerId;
        _root.World!.SetProducerSelected(producerId, true);
        Refresh();
    }

    private void SelectCarrier(int carrierId)
    {
        ClearSelection();
        _selectedCarrierId = carrierId;
        _root.World!.SetCarrierSelected(carrierId, true);
        Refresh();
    }

    private void ClearSelection()
    {
        if (_selectedProducerId is { } producerId)
            _root.World!.SetProducerSelected(producerId, false);
        if (_selectedCarrierId is { } carrierId)
            _root.World!.SetCarrierSelected(carrierId, false);
        _selectedProducerId = null;
        _selectedCarrierId = null;
    }

    private void Refresh()
    {
        var sim = _root.Sim;
        if (sim is null)
            return;

        if (_selectedProducerId is { } producerId)
        {
            var producer = sim.State.Producer(producerId);
            _label.Text = string.Create(CultureInfo.InvariantCulture,
                $"Produttore #{producer.Id} — {producer.Archetype.Name}\n" +
                $"Buffer: {producer.BufferGrams / 1000.0:F1} / {producer.Archetype.BufferGrams / 1000.0:F1} kg\n" +
                $"Ultima raccolta: {sim.Calendar.DateAt(producer.LastCollectedTick)}\n" +
                $"Violazioni: {producer.ViolationCount}\n" +
                $"Contratto: {(producer.HasContract ? "sì" : "no")}");
        }
        else if (_selectedCarrierId is { } carrierId)
        {
            if (!sim.State.TryGetCarrier(carrierId, out var carrier))
            {
                // Stale after a Load (B6) onto a save with fewer carriers.
                _selectedCarrierId = null;
                _label.Text = "";
                return;
            }

            var report = sim.State.LastDayReports.FirstOrDefault(r => r.CarrierId == carrierId);
            var reportText = report is null
                ? "nessun giro oggi"
                : string.Create(CultureInfo.InvariantCulture,
                    $"{report.MinutesUsed} min, {report.CollectedGrams / 1000.0:F1} kg, " +
                    $"{report.Trips} viaggi, {report.ServedProducerIds.Count} produttori serviti");
            _label.Text = string.Create(CultureInfo.InvariantCulture,
                $"Mezzo #{carrier.Id} — {carrier.TypeId}\n" +
                $"Copertura: {carrier.CoverageEdges.Count} archi\n" +
                $"Ultimo giro: {reportText}");
        }
        else
        {
            _label.Text = "";
        }
    }
}
