using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal static class CommunityToolkitMvvmGeneratedMemberResolver
{
    private const string ComponentModelNamespace = "CommunityToolkit.Mvvm.ComponentModel";
    private const string InputNamespace = "CommunityToolkit.Mvvm.Input";

    public static bool HasObservablePropertyAttribute(IFieldSymbol fieldSymbol)
    {
        return HasAttribute(fieldSymbol, "ObservablePropertyAttribute", ComponentModelNamespace);
    }

    public static bool HasRelayCommandAttribute(IMethodSymbol methodSymbol)
    {
        return HasAttribute(methodSymbol, "RelayCommandAttribute", InputNamespace);
    }

    public static string? GeneratedPropertyName(string fieldName)
    {
        var trimmed = fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName[2..] : fieldName.TrimStart('_');
        return trimmed.Length == 0 ? null : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    public static string GeneratedCommandName(string methodName)
    {
        var baseName = methodName.EndsWith("Async", StringComparison.Ordinal) ? methodName[..^"Async".Length] : methodName;
        if (baseName.StartsWith("On", StringComparison.Ordinal) && baseName.Length > 2 && char.IsUpper(baseName[2]))
        {
            baseName = baseName[2..];
        }

        return baseName + "Command";
    }

    public static bool IsAsyncCommand(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ReturnType is INamedTypeSymbol returnType &&
            returnType.Name is "Task" or "ValueTask" &&
            returnType.ContainingNamespace.ToDisplayString().Equals("System.Threading.Tasks", StringComparison.Ordinal);
    }

    public static IFieldSymbol? FindObservablePropertyBackingField(INamedTypeSymbol typeSymbol, string propertyName, RoslynSourceFilter sourceFilter)
    {
        foreach (var type in SelfAndBaseTypes(typeSymbol))
        {
            var field = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => !field.IsImplicitlyDeclared &&
                    sourceFilter.HasAnalyzableSourceLocation(field) &&
                    HasObservablePropertyAttribute(field))
                .OrderBy(field => field.ToDisplayString(SymbolDisplay.MemberFormat), StringComparer.Ordinal)
                .FirstOrDefault(field => GeneratedPropertyName(field.Name)?.Equals(propertyName, StringComparison.Ordinal) == true);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    public static IMethodSymbol? FindRelayCommandMethod(INamedTypeSymbol typeSymbol, string commandName, RoslynSourceFilter sourceFilter)
    {
        foreach (var type in SelfAndBaseTypes(typeSymbol))
        {
            var method = type.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary &&
                    !method.IsImplicitlyDeclared &&
                    sourceFilter.HasAnalyzableSourceLocation(method) &&
                    HasRelayCommandAttribute(method))
                .OrderBy(method => method.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal)
                .FirstOrDefault(method => GeneratedCommandName(method.Name).Equals(commandName, StringComparison.Ordinal));
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName, string namespaceName)
    {
        var shortName = metadataName.EndsWith("Attribute", StringComparison.Ordinal)
            ? metadataName[..^9]
            : metadataName;
        return symbol.GetAttributes().Any(attribute =>
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
            {
                return false;
            }

            var nameMatches = attributeClass.Name.Equals(metadataName, StringComparison.Ordinal) ||
                attributeClass.Name.Equals(shortName, StringComparison.Ordinal);
            return nameMatches && attributeClass.ContainingNamespace.ToDisplayString().Equals(namespaceName, StringComparison.Ordinal);
        });
    }
}
