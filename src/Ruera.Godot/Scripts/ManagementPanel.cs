using System;
using System.Globalization;
using System.Linq;

using Godot;

using Ruera.Sim.Commands;

namespace Ruera.Renderer;

/// <summary>
/// Buy a carrier / hire a worker (RUE-53, B8): fabri's first playtest —
/// "I can't build a depot, hire new people, buy new means of transport...
/// nothing is fun yet" — these commands already existed in <c>Ruera.Sim</c>
/// (RUE-6/RUE-14) but had no UI. The type picker era-gates on the in-sim
/// calendar year (DESIGN.md §7) by filtering out not-yet-available types,
/// rather than rendering them disabled. Buy is the real purchase (cash +
/// delivery delay via <see cref="BuyCarrierCommand"/>); the scenario-bootstrap
/// <c>AddCarrierCommand</c> (free, instant) stays code-only — exposing it to
/// the player would bypass the economy DESIGN.md §7 is built around.
/// </summary>
public partial class ManagementPanel : Node
{
    private GameRoot _root = null!;
    private OptionButton _carrierTypeSelect = null!;
    private Label _statusLabel = null!;
    private string[] _availableTypeIds = [];

    public void Setup(GameRoot root, OptionButton carrierTypeSelect, Button buyButton, Button hireButton, Label statusLabel)
    {
        _root = root;
        _carrierTypeSelect = carrierTypeSelect;
        _statusLabel = statusLabel;

        buyButton.Pressed += BuyCarrier;
        hireButton.Pressed += HireWorker;

        root.TickResolved += RefreshCarrierTypes;
        RefreshCarrierTypes();
    }

    private void RefreshCarrierTypes()
    {
        var sim = _root.Sim;
        if (sim is null || sim.State.Definitions is null)
            return;

        var year = sim.Today.Year;
        var available = sim.State.Definitions.Carriers.Where(d => d.IsAvailable(year)).ToArray();

        var previouslySelected = _carrierTypeSelect.Selected >= 0 && _carrierTypeSelect.Selected < _availableTypeIds.Length
            ? _availableTypeIds[_carrierTypeSelect.Selected]
            : null;

        _carrierTypeSelect.Clear();
        _availableTypeIds = [.. available.Select(d => d.Id)];
        foreach (var definition in available)
        {
            _carrierTypeSelect.AddItem(string.Create(CultureInfo.InvariantCulture,
                $"{definition.Name} — {definition.PurchaseCents / 100.0:F2} lire"));
        }

        if (available.Length == 0)
            return;

        var index = Array.IndexOf(_availableTypeIds, previouslySelected);
        _carrierTypeSelect.Select(index >= 0 ? index : 0);
    }

    private void BuyCarrier()
    {
        var sim = _root.Sim;
        if (sim is null || _carrierTypeSelect.Selected < 0 || _carrierTypeSelect.Selected >= _availableTypeIds.Length)
            return;

        try
        {
            sim.Submit(new BuyCarrierCommand(_availableTypeIds[_carrierTypeSelect.Selected]));
            _statusLabel.Text = "Ordinato: consegna a breve.";
        }
        catch (ArgumentException ex)
        {
            _statusLabel.Text = ex.Message;
        }
    }

    private void HireWorker()
    {
        var sim = _root.Sim;
        if (sim is null)
            return;

        sim.Submit(new HireWorkerCommand());
        _statusLabel.Text = "Assunto: operativo dopo il periodo di formazione.";
    }
}
