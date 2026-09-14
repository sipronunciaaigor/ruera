namespace Ruera.Sim;

/// <summary>
/// One arc of a planned tour: travel from wherever the carrier was to
/// <see cref="EdgeId"/> and across it (DESIGN.md §4 «giro con frecce»).
/// <see cref="NodePath"/> is the node-by-node route (cold path, via
/// <c>StreetGraph.ShortestPath</c>) for the renderer to draw. <see cref="ArrivalMinute"/>
/// is the running tour-budget minute at which the carrier reaches
/// <see cref="EdgeId"/>'s far end, before any stop time on it.
/// <see cref="ReturnToDepot"/> marks a leg immediately followed by a trip back
/// to the depot (a multi-trip refill, RUE-44, or the tour's final return).
/// </summary>
public sealed record TourLeg(int EdgeId, IReadOnlyList<int> NodePath, long ArrivalMinute, int Stops, bool ReturnToDepot);

/// <summary>
/// Read-only tour plan for a carrier (RUE-19 direction, DESIGN.md §3 «l'ispezione
/// passa dalle query di lettura»): the same pessimistic simulation the UI preview
/// already ran (DESIGN.md §4), now exposing every leg instead of only the total.
/// <see cref="Total"/> is what <c>Simulation.PreviewTour</c> used to return alone.
/// </summary>
public sealed record TourPlan(int CarrierId, IReadOnlyList<TourLeg> Legs, int Trips, Minutes Total, Minutes Budget, long BudgetUsedBps);
