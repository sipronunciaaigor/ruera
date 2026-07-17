using System.Globalization;

using Ruera.Sim;

var seed = args.Length > 0 ? ulong.Parse(args[0], CultureInfo.InvariantCulture) : 0UL;
var ticks = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 365;

var sim = new Simulation(seed);
sim.Advance(ticks);

var today = sim.Today;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"seed={seed} ticks={ticks} date={today} weekday={today.Weekday} working={sim.IsWorkingDay} hash={sim.StateHash():x16}"));
