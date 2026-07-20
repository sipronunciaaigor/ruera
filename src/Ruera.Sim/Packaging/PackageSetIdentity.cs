namespace Ruera.Sim.Packaging;

/// <summary>
/// The identity of the ordered package set a game was loaded from (RUE-40),
/// carried into the save header (RUE-8/RUE-18 successor): the packages in
/// canonical load order with their versions, plus the folded content hash.
/// A load can compare the ordered <c>(id, version)</c> list element-wise to
/// name precisely which package is missing or at the wrong version.
/// </summary>
public sealed record PackageSetIdentity(IReadOnlyList<(string Id, SemVer Version)> Packages, ulong Hash);
