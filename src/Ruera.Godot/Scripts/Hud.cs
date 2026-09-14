using System.Collections.Generic;
using System.Globalization;

using Godot;

using Ruera.Sim;

namespace Ruera.Renderer;

/// <summary>
/// Debug HUD (RUE-17, B3): clock, cash/stockpile/workers/bankrupt readout,
/// speed controls and a rolling event log (last 50). Pure staging of
/// <c>Sim.State</c>; never mutates it. Built entirely in code, like the rest
/// of Phase B (no hand-written .tscn).
/// </summary>
public partial class Hud : CanvasLayer
{
    private const int MaxLogLines = 50;

    private static readonly Dictionary<SimEventType, string> EventLabels = new()
    {
        [SimEventType.BufferOverflow] = "Sovraccarico buffer",
        [SimEventType.SanitaryViolation] = "Violazione sanitaria",
        [SimEventType.Bankruptcy] = "Bancarotta",
        [SimEventType.CarrierBreakdown] = "Guasto mezzo",
        [SimEventType.SanitaryInspection] = "Ispezione sanitaria",
        [SimEventType.TenderAnnounced] = "Bando annunciato",
        [SimEventType.MaterialSold] = "Vendita materiale",
    };

    private static readonly Dictionary<Ruera.Sim.Calendar.Weekday, string> WeekdayLabels = new()
    {
        [Ruera.Sim.Calendar.Weekday.Monday] = "lunedì",
        [Ruera.Sim.Calendar.Weekday.Tuesday] = "martedì",
        [Ruera.Sim.Calendar.Weekday.Wednesday] = "mercoledì",
        [Ruera.Sim.Calendar.Weekday.Thursday] = "giovedì",
        [Ruera.Sim.Calendar.Weekday.Friday] = "venerdì",
        [Ruera.Sim.Calendar.Weekday.Saturday] = "sabato",
        [Ruera.Sim.Calendar.Weekday.Sunday] = "domenica",
    };

    private readonly Queue<string> _logLines = new();

    private Label _dateLabel = null!;
    private Label _weekdayLabel = null!;
    private Label _workingDayLabel = null!;
    private Label _cashLabel = null!;
    private Label _stockpileLabel = null!;
    private Label _workersLabel = null!;
    private Label _bankruptLabel = null!;
    private Label _speedLabel = null!;
    private RichTextLabel _eventLog = null!;

    public void Setup(GameRoot root)
    {
        var panel = new VBoxContainer { Name = "Panel", Position = new Vector2(12, 12) };
        AddChild(panel);

        _dateLabel = AddLabel(panel);
        _weekdayLabel = AddLabel(panel);
        _workingDayLabel = AddLabel(panel);
        _cashLabel = AddLabel(panel);
        _stockpileLabel = AddLabel(panel);
        _workersLabel = AddLabel(panel);
        _bankruptLabel = AddLabel(panel);

        var speedRow = new HBoxContainer { Name = "SpeedRow" };
        panel.AddChild(speedRow);
        AddSpeedButton(speedRow, "Pausa", 0, root);
        AddSpeedButton(speedRow, "1x", 1, root);
        AddSpeedButton(speedRow, "4x", 4, root);
        AddSpeedButton(speedRow, "16x", 16, root);
        _speedLabel = AddLabel(panel);

        _eventLog = new RichTextLabel
        {
            Name = "EventLog",
            Position = new Vector2(12, 260),
            CustomMinimumSize = new Vector2(420, 260),
            ScrollFollowing = true,
            BbcodeEnabled = false,
        };
        AddChild(_eventLog);

        SetupPainter(root);
        SetupInspector(root);

        RefreshSpeed(root.Speed);
        Refresh(root.Sim);
    }

    /// <summary>Read-only entity inspector (RUE-19, B5): archetype/buffer/violations for a producer, or type/coverage/last report for a carrier.</summary>
    private void SetupInspector(GameRoot root)
    {
        var panel = new PanelContainer { Name = "InspectorPanel", Position = new Vector2(460, 200) };
        AddChild(panel);

        var label = new Label { Name = "InspectorLabel", CustomMinimumSize = new Vector2(260, 0) };
        panel.AddChild(label);

        var inspector = new Inspector { Name = "Inspector" };
        AddChild(inspector);
        inspector.Setup(root, label);
    }

    /// <summary>Coverage painting panel (RUE-30, B4): carrier picker, plan readout, Apply/Cancel.</summary>
    private void SetupPainter(GameRoot root)
    {
        var panel = new VBoxContainer { Name = "PaintPanel", Position = new Vector2(460, 12) };
        AddChild(panel);

        var carrierSelect = new OptionButton { Name = "CarrierSelect", CustomMinimumSize = new Vector2(200, 0) };
        panel.AddChild(carrierSelect);

        var planLabel = new Label { Name = "PlanLabel" };
        panel.AddChild(planLabel);

        var buttonRow = new HBoxContainer { Name = "ApplyCancelRow" };
        panel.AddChild(buttonRow);
        var applyButton = new Button { Text = "Applica" };
        buttonRow.AddChild(applyButton);
        var cancelButton = new Button { Text = "Annulla" };
        buttonRow.AddChild(cancelButton);

        var painter = new Painter { Name = "Painter" };
        AddChild(painter);
        painter.Setup(root, carrierSelect, planLabel, applyButton, cancelButton);
    }

    private static Label AddLabel(Node parent)
    {
        var label = new Label();
        parent.AddChild(label);
        return label;
    }

    private static void AddSpeedButton(Node parent, string text, int speed, GameRoot root)
    {
        var button = new Button { Text = text };
        button.Pressed += () => root.SetSpeed(speed);
        parent.AddChild(button);
    }

    public void RefreshSpeed(int speed) => _speedLabel.Text = $"Velocità: {(speed == 0 ? "pausa" : $"{speed}x")}";

    /// <summary>Called on every <c>TickResolved</c> — labels from live state, plus this tick's new log lines.</summary>
    public void Refresh(Simulation? sim)
    {
        if (sim is null)
            return;

        var state = sim.State;
        var today = sim.Today;

        _dateLabel.Text = $"Data: {today}";
        _weekdayLabel.Text = $"Giorno: {WeekdayLabels.GetValueOrDefault(today.Weekday, today.Weekday.ToString())}";
        _workingDayLabel.Text = $"Lavorativo: {(sim.IsWorkingDay ? "sì" : "no")}";
        _cashLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Cassa: {state.CashCents / 100.0:F2} lire");
        _stockpileLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"Magazzino: {state.StockpileGrams / 1000.0:F1} kg — Smistato: {state.SortedGrams / 1000.0:F1} kg");
        _workersLabel.Text = $"Operai: {state.Workers.Count}";
        _bankruptLabel.Text = $"Bancarotta: {(state.Bankrupt ? "sì" : "no")}";

        foreach (var simEvent in state.LastTickEvents)
        {
            var date = sim.Calendar.DateAt(simEvent.Tick);
            var label = EventLabels.GetValueOrDefault(simEvent.Type, simEvent.Type.ToString());
            _logLines.Enqueue($"{date} — {label}");
            while (_logLines.Count > MaxLogLines)
                _logLines.Dequeue();
        }

        _eventLog.Text = string.Join('\n', _logLines);
    }
}
