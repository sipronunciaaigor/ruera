using System.IO;

using Godot;

using Ruera.Sim;
using Ruera.Sim.Commands;
using Ruera.Sim.Packaging;
using Ruera.Sim.Persistence;

namespace Ruera.Renderer;

/// <summary>
/// Root of the single Main.tscn scene (RUE-17, B1): loads content and builds
/// the sim the rest of the game reads and drives. The sim never depends on
/// Godot (DESIGN.md §5/§6) — this script is the one place they meet.
/// </summary>
public partial class GameRoot : Node3D
{
    /// <summary>Emitted right after <see cref="Simulation.Advance"/> resolves one tick.</summary>
    [Signal]
    public delegate void TickResolvedEventHandler();

    private const string ScenarioId = "base:milano-1880";
    private const ulong Seed = 42UL;
    private const string SaveSlotPath = "user://slot1.ruera";

    /// <summary>Real seconds per tick at Speed = 1 — rendering-only pace (DESIGN.md §2); never affects sim results.</summary>
    private const double SecondsPerTick = 1.0;

    private double _accumulatedSeconds;
    private int _lastActiveSpeed = 1;
    private LoadedPackages? _packages;

    public Simulation? Sim { get; private set; }

    public WorldView? World { get; private set; }

    public Hud? Hud { get; private set; }

    /// <summary>0 = paused, otherwise ticks-per-real-second multiplier (1/4/16).</summary>
    public int Speed { get; private set; } = 1;

    public override void _Ready()
    {
        var packagesPath = Path.GetFullPath(
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "data", "packages"));
        if (!Directory.Exists(packagesPath))
        {
            GD.PushError($"Ruera: packages directory not found at '{packagesPath}'.");
            return;
        }

        _packages = ContentLoader.LoadFromDirectory(packagesPath);
        Sim = _packages.NewSimulation(Seed, ScenarioId);

        Sim.Submit(new AddCarrierCommand("base:navazza"));
        Sim.Submit(new AddCarrierCommand("base:navazza"));
        foreach (var producer in Sim.State.Producers)
            Sim.Submit(new SignContractCommand(producer.Id));

        World = new WorldView { Name = "World" };
        AddChild(World);
        World.Build(Sim);

        var cameraRig = new CameraRig { Name = "CameraRig" };
        AddChild(cameraRig);
        cameraRig.Setup(World.Center);

        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            Rotation = new Vector3(Mathf.DegToRad(-60), Mathf.DegToRad(30), 0),
        });

        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.55f),
                AmbientLightEnergy = 0.6f,
            },
        });

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.Setup(this);
        TickResolved += () => Hud!.Refresh(Sim);
        TickResolved += () => World!.Refresh(Sim!.State);
        TickResolved += () => World!.PlayCarrierRoutes(Sim!, SecondsPerTick / Speed);

        GD.Print($"Ruera: loaded {ScenarioId}, seed {Seed}, today {Sim.Today}.");
    }

    public override void _Process(double delta)
    {
        if (Sim is null || Speed == 0)
            return;

        _accumulatedSeconds += delta * Speed;
        while (_accumulatedSeconds >= SecondsPerTick)
        {
            _accumulatedSeconds -= SecondsPerTick;
            Sim.Advance(1);
            EmitSignal(SignalName.TickResolved);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;

        switch (key.Keycode)
        {
            case Key.Space: SetSpeed(Speed == 0 ? _lastActiveSpeed : 0); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(4); break;
            case Key.Key3: SetSpeed(16); break;
            default: return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Speeds 0/1/4/16 (DESIGN.md §2: rendering pace only, never affects sim results).</summary>
    public void SetSpeed(int speed)
    {
        if (Speed != 0)
            _lastActiveSpeed = Speed;
        Speed = speed;
        Hud?.RefreshSpeed(Speed);
    }

    /// <summary>B6: writes SaveSystem.Save(Sim) to user://slot1.ruera.</summary>
    public void Save()
    {
        if (Sim is null)
            return;

        var bytes = SaveSystem.Save(Sim);
        using var file = Godot.FileAccess.Open(SaveSlotPath, Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushError($"Ruera: could not open '{SaveSlotPath}' for writing ({Godot.FileAccess.GetOpenError()}).");
            return;
        }

        file.StoreBuffer(bytes);
        GD.Print($"Ruera: saved to {SaveSlotPath}.");
    }

    /// <summary>
    /// B6: loads user://slot1.ruera via LoadedPackages.LoadSave, replaces
    /// Sim and rebuilds World in place (same instance — see WorldView.Build)
    /// so Painter/Inspector's event subscriptions stay valid.
    /// </summary>
    public void Load()
    {
        if (_packages is null || World is null)
            return;

        if (!Godot.FileAccess.FileExists(SaveSlotPath))
        {
            GD.PushWarning($"Ruera: no save file at '{SaveSlotPath}'.");
            return;
        }

        using var file = Godot.FileAccess.Open(SaveSlotPath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushError($"Ruera: could not open '{SaveSlotPath}' for reading ({Godot.FileAccess.GetOpenError()}).");
            return;
        }

        var bytes = file.GetBuffer((long)file.GetLength());
        Sim = _packages.LoadSave(bytes, ScenarioId);

        World.Build(Sim);
        World.Refresh(Sim.State);
        Hud!.Refresh(Sim);
        GD.Print($"Ruera: loaded {SaveSlotPath}, today {Sim.Today}.");
    }
}
