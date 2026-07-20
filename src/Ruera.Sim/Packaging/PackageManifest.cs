using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruera.Sim.Packaging;

/// <summary>Raised when a package manifest or the package set is malformed or inconsistent.</summary>
public sealed class PackageLoadException(string message) : Exception(message);

/// <summary>A dependency on another package, with a minimum acceptable version.</summary>
public sealed record PackageDependency(string Id, SemVer MinVersion);

/// <summary>
/// A content package's manifest (package.json — DESIGN.md §2 «Formato pacchetti
/// mod», RUE-36/RUE-40). The <see cref="Id"/> is the package's namespace: every
/// content id it ships must be <c>id:name</c>. Strict parse, like the other
/// loaders: unknown fields and missing required fields fail with a named error.
/// </summary>
public sealed record PackageManifest(
    string Id,
    string Name,
    SemVer Version,
    SemVer GameVersion,
    IReadOnlyList<PackageDependency> Dependencies)
{
    private const int SupportedFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static PackageManifest Load(string json, string sourceName = "package.json")
    {
        ManifestFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ManifestFile>(json, Options);
        }
        catch (JsonException exception)
        {
            throw Fail(sourceName, exception.Message);
        }

        if (file is null)
            throw Fail(sourceName, "file is empty");
        if (file.FormatVersion != SupportedFormatVersion)
            throw Fail(sourceName, FormattableString.Invariant(
                $"unsupported formatVersion {file.FormatVersion} (supported: {SupportedFormatVersion})"));

        if (string.IsNullOrWhiteSpace(file.Id))
            throw Fail(sourceName, "id must not be empty");
        // The manifest id is a namespace token, not a namespaced content id: no colon.
        if (file.Id!.Contains(':', StringComparison.Ordinal) || file.Id.Any(char.IsWhiteSpace))
            throw Fail(sourceName, FormattableString.Invariant(
                $"id '{file.Id}' must be a bare namespace token (no ':' and no whitespace, e.g. 'base')"));
        if (string.IsNullOrWhiteSpace(file.Name))
            throw Fail(sourceName, "name must not be empty");
        if (!SemVer.TryParse(file.Version, out var version))
            throw Fail(sourceName, FormattableString.Invariant($"version '{file.Version}' is not a valid semver (major.minor.patch)"));
        if (!SemVer.TryParse(file.GameVersion, out var gameVersion))
            throw Fail(sourceName, FormattableString.Invariant($"gameVersion '{file.GameVersion}' is not a valid semver (major.minor.patch)"));

        var dependencies = new List<PackageDependency>();
        foreach (var dependency in file.Dependencies ?? [])
        {
            if (string.IsNullOrWhiteSpace(dependency.Id))
                throw Fail(sourceName, "dependency id must not be empty");
            if (string.Equals(dependency.Id, file.Id, StringComparison.Ordinal))
                throw Fail(sourceName, FormattableString.Invariant($"package '{file.Id}' cannot depend on itself"));
            if (dependencies.Any(d => string.Equals(d.Id, dependency.Id, StringComparison.Ordinal)))
                throw Fail(sourceName, FormattableString.Invariant($"duplicate dependency '{dependency.Id}'"));
            if (!SemVer.TryParse(dependency.MinVersion, out var minVersion))
                throw Fail(sourceName, FormattableString.Invariant(
                    $"dependency '{dependency.Id}': minVersion '{dependency.MinVersion}' is not a valid semver"));
            dependencies.Add(new PackageDependency(dependency.Id!, minVersion));
        }

        return new PackageManifest(file.Id, file.Name!, version, gameVersion, dependencies);
    }

    private static PackageLoadException Fail(string sourceName, string message) =>
        new(FormattableString.Invariant($"{sourceName}: {message}."));

    private sealed class ManifestFile
    {
        public required int FormatVersion { get; init; }

        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Version { get; init; }

        public string? GameVersion { get; init; }

        public List<DependencyDto>? Dependencies { get; init; }
    }

    private sealed class DependencyDto
    {
        public string? Id { get; init; }

        public string? MinVersion { get; init; }
    }
}
