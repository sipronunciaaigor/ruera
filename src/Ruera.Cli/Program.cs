using Ruera.Sim;

var seed = args.Length > 0 ? ulong.Parse(args[0]) : 0UL;
var ticks = args.Length > 1 ? int.Parse(args[1]) : 365;

var sim = new Simulation(seed);
sim.Advance(ticks);

Console.WriteLine($"seed={seed} ticks={ticks} hash={sim.StateHash():x16}");
