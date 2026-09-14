using System.Collections.Generic;
using System.Globalization;

using Godot;

using Ruera.Sim;

namespace Ruera.Renderer;

/// <summary>
/// Debug HUD (RUE-17 B3, RUE-52 B7): clock, cash/stockpile/workers/bankrupt
/// readout, speed controls and a rolling event log (last 50). Pure staging
/// of <c>Sim.State</c>; never mutates it. Built entirely in code, like the
/// rest of Phase B (no hand-written .tscn). Every group sits in a
/// <see cref="PanelContainer"/> (B7: fabri's playtest found bare text
/// floating directly over the 3D scene hard to read).
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
        // All top-row panels flow left to right inside one HBoxContainer
        // instead of guessed fixed pixel offsets (RUE-53/B8 fix): guessed
        // offsets kept landing panels on top of the toy map's nodes, which
        // fills nearly the whole viewport — fabri's playtest, again, after
        // B8 added the Inspector/Management column. A container lays out
        // children by their real measured size, so it can never overlap
        // itself regardless of how tall any one panel's content gets.
        var topRow = new HBoxContainer { Name = "TopRow", Position = new Vector2(12, 12) };
        topRow.AddThemeConstantOverride("separation", 12);
        AddChild(topRow);

        SetupStatus(root, topRow);
        SetupPainter(root, topRow);

        var rightColumn = new VBoxContainer { Name = "RightColumn" };
        rightColumn.AddThemeConstantOverride("separation", 12);
        topRow.AddChild(rightColumn);
        SetupInspector(root, rightColumn);
        SetupManagement(root, rightColumn);

        SetupEventLog();

        RefreshSpeed(root.Speed);
        Refresh(root.Sim);
    }

    /// <summary>Clock, cash/stockpile/workers/bankrupt readout, speed controls, save/load (RUE-17, B3).</summary>
    private void SetupStatus(GameRoot root, Node parent)
    {
        AddPanel(parent, out var body);

        _dateLabel = AddLabel(body);
        _weekdayLabel = AddLabel(body);
        _workingDayLabel = AddLabel(body);
        _cashLabel = AddLabel(body);
        _stockpileLabel = AddLabel(body);
        _workersLabel = AddLabel(body);
        _bankruptLabel = AddLabel(body);

        var speedRow = new HBoxContainer { Name = "SpeedRow" };
        body.AddChild(speedRow);
        AddSpeedButton(speedRow, "Pausa", 0, root);
        AddSpeedButton(speedRow, "1x", 1, root);
        AddSpeedButton(speedRow, "4x", 4, root);
        AddSpeedButton(speedRow, "16x", 16, root);
        _speedLabel = AddLabel(body);

        var saveLoadRow = new HBoxContainer { Name = "SaveLoadRow" };
        body.AddChild(saveLoadRow);
        var saveButton = new Button { Text = "Salva" };
        saveButton.Pressed += root.Save;
        saveLoadRow.AddChild(saveButton);
        var loadButton = new Button { Text = "Carica" };
        loadButton.Pressed += root.Load;
        saveLoadRow.AddChild(loadButton);
    }

    /// <summary>
    /// Anchored to the bottom-left corner (B7) instead of a fixed top-left
    /// pixel offset, so it never sits over the map viewport regardless of
    /// window size — fabri's playtest: the log was overlaying the grid.
    /// </summary>
    private void SetupEventLog()
    {
        const float width = 380f, height = 220f, margin = 12f;
        AddPanel(Control.LayoutPreset.BottomLeft, new Vector2(margin, -height - margin), out var body);

        _eventLog = new RichTextLabel
        {
            Name = "EventLog",
            CustomMinimumSize = new Vector2(width, height),
            ScrollFollowing = true,
            BbcodeEnabled = false,
        };
        body.AddChild(_eventLog);
    }

    /// <summary>
    /// Read-only entity inspector (RUE-19, B5): archetype/buffer/violations for
    /// a producer, or type/coverage/last report for a carrier. RUE-53/B8 adds
    /// the one exception — a "Firma contratto" button when an uncontracted
    /// producer is selected.
    /// </summary>
    private void SetupInspector(GameRoot root, Node parent)
    {
        AddPanel(parent, out var body);

        var label = new Label { Name = "InspectorLabel", CustomMinimumSize = new Vector2(260, 0) };
        body.AddChild(label);

        var signContractButton = new Button { Name = "SignContractButton", Text = "Firma contratto" };
        body.AddChild(signContractButton);

        var inspector = new Inspector { Name = "Inspector" };
        AddChild(inspector);
        inspector.Setup(root, label, signContractButton);
    }

    /// <summary>
    /// Coverage painting panel (RUE-30, B4): carrier picker, plan readout,
    /// Apply/Cancel. RUE-53/B8 adds service lines: save the painted selection
    /// as a named line, and assign any carrier to an existing line.
    /// </summary>
    private void SetupPainter(GameRoot root, Node parent)
    {
        AddPanel(parent, out var body);

        var carrierSelect = new OptionButton { Name = "CarrierSelect", CustomMinimumSize = new Vector2(200, 0) };
        body.AddChild(carrierSelect);

        var planLabel = new Label { Name = "PlanLabel" };
        body.AddChild(planLabel);

        var buttonRow = new HBoxContainer { Name = "ApplyCancelRow" };
        body.AddChild(buttonRow);
        var applyButton = new Button { Text = "Applica" };
        buttonRow.AddChild(applyButton);
        var cancelButton = new Button { Text = "Annulla" };
        buttonRow.AddChild(cancelButton);

        var templateNameInput = new LineEdit { Name = "TemplateNameInput", PlaceholderText = "Nome linea", CustomMinimumSize = new Vector2(200, 0) };
        body.AddChild(templateNameInput);

        var templateCreateRow = new HBoxContainer { Name = "TemplateCreateRow" };
        body.AddChild(templateCreateRow);
        var createTemplateButton = new Button { Text = "Crea linea da selezione" };
        templateCreateRow.AddChild(createTemplateButton);

        var templateAssignRow = new HBoxContainer { Name = "TemplateAssignRow" };
        body.AddChild(templateAssignRow);
        var templateSelect = new OptionButton { Name = "TemplateSelect", CustomMinimumSize = new Vector2(200, 0) };
        templateAssignRow.AddChild(templateSelect);
        var assignTemplateButton = new Button { Text = "Assegna mezzo" };
        templateAssignRow.AddChild(assignTemplateButton);

        var templateStatusLabel = new Label { Name = "TemplateStatusLabel" };
        body.AddChild(templateStatusLabel);

        var painter = new Painter { Name = "Painter" };
        AddChild(painter);
        painter.Setup(root, carrierSelect, planLabel, applyButton, cancelButton,
            templateNameInput, createTemplateButton, templateSelect, assignTemplateButton, templateStatusLabel);
    }

    /// <summary>Buy a carrier / hire a worker (RUE-53, B8) — commands that pre-date Phase B but had no UI.</summary>
    private void SetupManagement(GameRoot root, Node parent)
    {
        AddPanel(parent, out var body);

        var carrierTypeSelect = new OptionButton { Name = "CarrierTypeSelect", CustomMinimumSize = new Vector2(220, 0) };
        body.AddChild(carrierTypeSelect);

        var buyButton = new Button { Text = "Compra mezzo" };
        body.AddChild(buyButton);

        var hireButton = new Button { Text = "Assumi operaio" };
        body.AddChild(hireButton);

        var statusLabel = new Label { Name = "ManagementStatusLabel" };
        body.AddChild(statusLabel);

        var management = new ManagementPanel { Name = "ManagementPanel" };
        AddChild(management);
        management.Setup(root, carrierTypeSelect, buyButton, hireButton, statusLabel);
    }

    /// <summary>A semi-transparent background panel anchored by a Control preset + offset from the anchor point (B7) — used only by the event log, which sits outside the flowed top row on purpose.</summary>
    private PanelContainer AddPanel(Control.LayoutPreset anchorPreset, Vector2 offsetFromAnchor, out VBoxContainer body)
    {
        var panel = AddPanel(this, out body);
        panel.SetAnchorsPreset(anchorPreset);
        panel.Position = offsetFromAnchor;
        return panel;
    }

    /// <summary>
    /// A semi-transparent background panel with a VBoxContainer body (B7),
    /// parented wherever the caller needs it laid out — a flow container
    /// (B8) for the top-row panels, or directly on the Hud for anchored ones.
    /// </summary>
    private static PanelContainer AddPanel(Node parent, out VBoxContainer body)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.55f),
            ContentMarginLeft = 8f,
            ContentMarginRight = 8f,
            ContentMarginTop = 6f,
            ContentMarginBottom = 6f,
        };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(panel);

        body = new VBoxContainer();
        panel.AddChild(body);
        return panel;
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
            _logLines.Enqueue($"{date} — {label}{EntitySuffix(state, simEvent.Type, simEvent.EntityId)}");
            while (_logLines.Count > MaxLogLines)
                _logLines.Dequeue();
        }

        _eventLog.Text = string.Join('\n', _logLines);
    }

    /// <summary>
    /// Names the producer/carrier an event is about (B7: fabri's playtest —
    /// "the sanitary violation doesn't state what's wrong"). EntityId is 0
    /// (company-wide) for Bankruptcy/SanitaryInspection/MaterialSold, which
    /// stay unsuffixed. Ids here always resolve — the sim only ever emits
    /// these events against entities that exist at that tick.
    /// </summary>
    private static string EntitySuffix(SimState state, SimEventType type, int entityId) => type switch
    {
        SimEventType.BufferOverflow or SimEventType.SanitaryViolation or SimEventType.TenderAnnounced =>
            $" ({state.Producer(entityId).Archetype.Name} #{entityId})",
        SimEventType.CarrierBreakdown => $" ({state.Carrier(entityId).TypeId} #{entityId})",
        _ => "",
    };
}
