using System.Globalization;

using Ruera.Cli;
using Ruera.Sim;

if (args.Length > 0 && args[0] == "bench")
{
    Bench.Run(args[1..]);
    return;
}

var seed = args.Length > 0 ? ulong.Parse(args[0], CultureInfo.InvariantCulture) : 0UL;
var ticks = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 365;

var sim = new Simulation(seed);
sim.Advance(ticks);

var today = sim.Today;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"seed={seed} ticks={ticks} date={today} weekday={today.Weekday} working={sim.IsWorkingDay} hash={sim.StateHash():x16}"));
