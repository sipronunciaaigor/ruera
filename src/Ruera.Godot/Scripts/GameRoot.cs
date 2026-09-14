using System.IO;

using Godot;

using Ruera.Sim;
using Ruera.Sim.Commands;
using Ruera.Sim.Packaging;

namespace Ruera.Renderer;

/// <summary>
/// Root of the single Main.tscn scene (RUE-17, B1): loads content and builds
/// the sim the rest of the game reads and drives. The sim never depends on
/// Godot (DESIGN.md §5/§6) — this script is the one place they meet.
/// </summary>
public partial class GameRoot : Node3D
{
    private const string ScenarioId = "base:milano-1880";
    private const ulong Seed = 42UL;

    public Simulation? Sim { get; private set; }

    public WorldView? World { get; private set; }

    public override void _Ready()
    {
        var packagesPath = Path.GetFullPath(
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "data", "packages"));
        if (!Directory.Exists(packagesPath))
        {
            GD.PushError($"Ruera: packages directory not found at '{packagesPath}'.");
            return;
        }

        var packages = ContentLoader.LoadFromDirectory(packagesPath);
        Sim = packages.NewSimulation(Seed, ScenarioId);

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

        GD.Print($"Ruera: loaded {ScenarioId}, seed {Seed}, today {Sim.Today}.");
    }
}
