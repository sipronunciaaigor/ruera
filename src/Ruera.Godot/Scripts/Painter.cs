using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;

using Ruera.Sim;
using Ruera.Sim.Calendar;
using Ruera.Sim.Commands;

namespace Ruera.Renderer;

/// <summary>
/// Coverage painting (RUE-30, DESIGN.md §4): pick a carrier, click edges to
/// toggle them into a pending set, see the budget colour and planned legs
/// live via <c>Sim.PlanTour</c> — the same pessimistic query the read-only
/// preview API exposes (RUE-19) — then Apply (exactly one
/// <c>SetCoverageCommand</c>) or Cancel (revert to the committed coverage).
/// Never submits a command per click.
///
/// RUE-53/B8 adds service lines (DESIGN.md §4, <see cref="RouteTemplate"/>):
/// save the currently painted selection as a named line, then assign any
/// carrier to an existing line without repainting — fabri's playtest: "I
/// can't even assign the carrier to an existing line". A line only feeds a
/// carrier's tour when that carrier has no direct painted coverage
/// (DayPlanSystem's override rule), so Apply/direct painting still wins.
/// Schedule editing (which weekdays a line runs) is out of B8's scope: lines
/// created here always run every day.
/// </summary>
public partial class Painter : Node
{
    private static readonly byte AllDaysMask = RouteTemplate.Mask(
        Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday, Weekday.Saturday, Weekday.Sunday);

    private GameRoot _root = null!;
    private OptionButton _carrierSelect = null!;
    private Label _planLabel = null!;
    private LineEdit _templateNameInput = null!;
    private OptionButton _templateSelect = null!;
    private Label _templateStatusLabel = null!;

    private int _selectedCarrierId;
    private readonly HashSet<int> _pending = [];
    private int[] _templateIds = [];

    public void Setup(GameRoot root, OptionButton carrierSelect, Label planLabel, Button applyButton, Button cancelButton,
        LineEdit templateNameInput, Button createTemplateButton, OptionButton templateSelect, Button assignTemplateButton,
        Label templateStatusLabel)
    {
        _root = root;
        _carrierSelect = carrierSelect;
        _planLabel = planLabel;
        _templateNameInput = templateNameInput;
        _templateSelect = templateSelect;
        _templateStatusLabel = templateStatusLabel;

        _carrierSelect.ItemSelected += _ => SelectCarrier();
        applyButton.Pressed += Apply;
        cancelButton.Pressed += Cancel;
        createTemplateButton.Pressed += CreateTemplate;
        assignTemplateButton.Pressed += AssignToTemplate;

        root.World!.EdgeClicked += ToggleEdge;
        root.TickResolved += RefreshCarrierList;
        root.TickResolved += RefreshTemplateList;
        RefreshCarrierList();
        RefreshTemplateList();
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

    private void RefreshTemplateList()
    {
        var sim = _root.Sim;
        if (sim is null)
            return;

        var previouslySelected = _templateSelect.Selected >= 0 && _templateSelect.Selected < _templateIds.Length
            ? _templateIds[_templateSelect.Selected]
            : (int?)null;

        _templateSelect.Clear();
        _templateIds = [.. sim.State.Templates.Select(t => t.Id)];
        foreach (var template in sim.State.Templates)
            _templateSelect.AddItem($"{template.Name} (#{template.Id}, {template.AssignedCarriers.Count} mezzi)");

        if (_templateIds.Length == 0)
            return;

        var index = previouslySelected is { } id ? Array.IndexOf(_templateIds, id) : -1;
        _templateSelect.Select(index >= 0 ? index : 0);
    }

    private void CreateTemplate()
    {
        var sim = _root.Sim;
        if (sim is null)
            return;
        if (_pending.Count == 0)
        {
            _templateStatusLabel.Text = "Dipingi almeno un arco prima di salvare una linea.";
            return;
        }

        var name = _templateNameInput.Text.Trim();
        if (name.Length == 0)
            name = string.Create(CultureInfo.InvariantCulture, $"Linea {sim.State.Templates.Count + 1}");

        try
        {
            sim.Submit(new CreateRouteTemplateCommand(name, [.. _pending.OrderBy(id => id)], AllDaysMask));
            _templateStatusLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Linea '{name}' creata.");
            _templateNameInput.Text = "";
        }
        catch (ArgumentException ex)
        {
            _templateStatusLabel.Text = ex.Message;
        }
    }

    private void AssignToTemplate()
    {
        var sim = _root.Sim;
        if (sim is null || _selectedCarrierId == 0 || _templateSelect.Selected < 0 || _templateSelect.Selected >= _templateIds.Length)
            return;

        var templateId = _templateIds[_templateSelect.Selected];
        var template = sim.State.Template(templateId);
        var carrierIds = template.AssignedCarriers.Append(_selectedCarrierId).Distinct().ToArray();

        try
        {
            sim.Submit(new SetTemplateCarriersCommand(templateId, carrierIds));
            _templateStatusLabel.Text = string.Create(CultureInfo.InvariantCulture,
                $"Mezzo #{_selectedCarrierId} assegnato a '{template.Name}'.");
        }
        catch (ArgumentException ex)
        {
            _templateStatusLabel.Text = ex.Message;
        }
    }
}
