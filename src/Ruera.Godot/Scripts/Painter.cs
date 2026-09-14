using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;

using Ruera.Sim;
using Ruera.Sim.Commands;

namespace Ruera.Renderer;

/// <summary>
/// Coverage painting (RUE-30, DESIGN.md §4): pick a carrier, click edges to
/// toggle them into a pending set, see the budget colour and planned legs
/// live via <c>Sim.PlanTour</c> — the same pessimistic query the read-only
/// preview API exposes (RUE-19) — then Apply (exactly one
/// <c>SetCoverageCommand</c>) or Cancel (revert to the committed coverage).
/// Never submits a command per click.
/// </summary>
public partial class Painter : Node
{
    private GameRoot _root = null!;
    private OptionButton _carrierSelect = null!;
    private Label _planLabel = null!;

    private int _selectedCarrierId;
    private readonly HashSet<int> _pending = [];

    public void Setup(GameRoot root, OptionButton carrierSelect, Label planLabel, Button applyButton, Button cancelButton)
    {
        _root = root;
        _carrierSelect = carrierSelect;
        _planLabel = planLabel;

        _carrierSelect.ItemSelected += _ => SelectCarrier();
        applyButton.Pressed += Apply;
        cancelButton.Pressed += Cancel;

        root.World!.EdgeClicked += ToggleEdge;
        root.TickResolved += RefreshCarrierList;
        RefreshCarrierList();
    }

    private void RefreshCarrierList()
    {
        var sim = _root.Sim;
        if (sim is null)
            return;

        var previouslySelected = _selectedCarrierId;
        _carrierSelect.Clear();
        foreach (var carrier in sim.State.Carriers)
            _carrierSelect.AddItem($"#{carrier.Id} {carrier.TypeId}");

        if (sim.State.Carriers.Count == 0)
            return;

        var index = 0;
        for (var i = 0; i < sim.State.Carriers.Count; i++)
        {
            if (sim.State.Carriers[i].Id == previouslySelected)
            {
                index = i;
                break;
            }
        }

        _carrierSelect.Select(index);
        SelectCarrier();
    }

    private void SelectCarrier()
    {
        var sim = _root.Sim;
        if (sim is null || _carrierSelect.ItemCount == 0)
            return;

        var carrier = sim.State.Carriers[_carrierSelect.Selected];
        _selectedCarrierId = carrier.Id;
        _pending.Clear();
        foreach (var edgeId in carrier.CoverageEdges)
            _pending.Add(edgeId);
        RepaintAndReplan();
    }

    private void ToggleEdge(int edgeId)
    {
        if (_selectedCarrierId == 0)
            return;
        if (!_pending.Remove(edgeId))
            _pending.Add(edgeId);
        RepaintAndReplan();
    }

    private void RepaintAndReplan()
    {
        var sim = _root.Sim;
        var world = _root.World;
        // The TryGetCarrier check guards a stale id after a Load (B6) onto a
        // save with fewer carriers — RefreshCarrierList reselects a valid one
        // on the next tick regardless.
        if (sim is null || world is null || _selectedCarrierId == 0 || !sim.State.TryGetCarrier(_selectedCarrierId, out _))
            return;

        var pendingArray = _pending.OrderBy(id => id).ToArray();

        world.ClearEdgeHighlights();
        world.ClearArrows();

        if (pendingArray.Length == 0)
        {
            _planLabel.Text = "0 / — min";
            return;
        }

        var plan = sim.PlanTour(_selectedCarrierId, pendingArray);

        // Green (0% of budget) -> red (>= 100%), DESIGN.md §4.
        var bps = plan.BudgetUsedBps;
        var hue = (1f / 3f) * (1f - Mathf.Clamp(bps / 10_000f, 0f, 1f));
        var color = Color.FromHsv(hue, 0.75f, 0.9f);
        foreach (var edgeId in pendingArray)
            world.HighlightEdge(edgeId, color);
        world.ShowPlan(plan);

        _planLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"{plan.Total.Value} / {plan.Budget.Value} min — viaggi {plan.Trips} — {bps / 100.0:F0}%");
    }

    private void Apply()
    {
        if (_root.Sim is null || _selectedCarrierId == 0 || !_root.Sim.State.TryGetCarrier(_selectedCarrierId, out _))
            return;
        _root.Sim.Submit(new SetCoverageCommand(_selectedCarrierId, [.. _pending.OrderBy(id => id)]));
        _planLabel.Text += "  (applicato al prossimo tick)";
    }

    private void Cancel() => SelectCarrier(); // resets pending back to the carrier's committed coverage
}
