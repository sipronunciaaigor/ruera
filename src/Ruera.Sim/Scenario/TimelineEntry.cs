namespace Ruera.Sim.Scenario;

/// <summary>
/// One scripted timeline step: an engine-owned <see cref="TimelineEffect"/>
/// triggered on a civil date (DESIGN.md §2, RUE-20). The scripted timeline is
/// deterministic and part of scenario identity — distinct from the stochastic
/// essential events (RUE-32), which draw from the Events RNG stream. The
/// trigger date is resolved to an effective tick against the scenario's epoch
/// at build time.
///
/// <c>onCondition</c> triggers (state-driven rather than date-driven) are
/// deferred until a slice event needs them (RUE-38 acceptance): the format will
/// reserve room for them without the field existing yet.
/// </summary>
public sealed record TimelineEntry(int Year, int Month, int Day, TimelineEffect Effect);
