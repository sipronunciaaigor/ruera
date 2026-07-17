using System.Reflection;

namespace Ruera.Sim.Tests;

/// <summary>
/// Reflection-based determinism enforcement (RUE-11): the compile-time layer
/// is BannedApiAnalyzers; this layer guarantees no floating-point or decimal
/// state can ever appear in the sim assembly (DESIGN.md §2, rule 1).
/// </summary>
public class ArchitectureTests
{
    private static readonly Type[] BannedNumericTypes = [typeof(float), typeof(double), typeof(decimal)];

    [Fact]
    public void SimAssembly_HasNoFloatDoubleOrDecimalFields()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(Simulation).Assembly.GetTypes())
        {
            const BindingFlags allDeclared = BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.Instance | BindingFlags.Static |
                                             BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(allDeclared))
            {
                if (UsesBannedNumeric(field.FieldType))
                    offenders.Add($"{type.FullName}.{field.Name} : {field.FieldType}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Floating-point/decimal state is banned in Ruera.Sim (DESIGN.md §2):\n" + string.Join("\n", offenders));
    }

    private static bool UsesBannedNumeric(Type type)
    {
        if (BannedNumericTypes.Contains(type))
            return true;
        if (type.HasElementType && type.GetElementType() is { } element)
            return UsesBannedNumeric(element);
        if (type.IsGenericType)
            return type.GetGenericArguments().Any(UsesBannedNumeric);
        return false;
    }
}
