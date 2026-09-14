using System;
using System.Globalization;
using System.Linq;

using Godot;

using Ruera.Sim.Commands;

namespace Ruera.Renderer;

/// <summary>
/// Read-only inspector panel (RUE-19, B5): clicking a producer box or
/// carrier sphere shows its live state. Selection = emissive highlight on
/// the Mesh child (WorldView.SetProducerSelected/SetCarrierSelected); never
/// mutates <c>Sim.State</c> (DESIGN.md §3: inspection is a read query) except
/// for the one action button RUE-53/B8 adds — signing a contract with the
/// selected, uncontracted producer (fabri's playtest: "I can't ... sign
/// contracts").
/// </summary>
public partial class Inspector : Node
{
    private GameRoot _root = null!;
    private Label _label = null!;
    private Button _signContractButton = null!;

    private int? _selectedProducerId;
    private int? _selectedCarrierId;

    public void Setup(GameRoot root, Label label, Button signContractButton)
    {
        _root = root;
        _label = label;
        _signContractButton = signContractButton;

        _signContractButton.Visible = false;
        _signContractButton.Pressed += SignContract;

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
            _signContractButton.Visible = !producer.HasContract;
        }
        else if (_selectedCarrierId is { } carrierId)
        {
            _signContractButton.Visible = false;
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
            _signContractButton.Visible = false;
            _label.Text = "";
        }
    }

    private void SignContract()
    {
        if (_root.Sim is null || _selectedProducerId is not { } producerId)
            return;

        try
        {
            _root.Sim.Submit(new SignContractCommand(producerId));
            _signContractButton.Visible = false;
        }
        catch (ArgumentException)
        {
            // Producer went under contract (or was removed) between the last
            // refresh and this click; the next Refresh() reconciles the button.
        }
    }
}
