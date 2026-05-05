using Meridian.Abstractions;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class EfCoreSymbolClassifier
{
    private const string EfCoreNamespace = "Microsoft.EntityFrameworkCore";
    private const string DbContextMetadataName = "DbContext";
    private const string DbSetMetadataName = "DbSet`1";

    public string ClassifyType(INamedTypeSymbol typeSymbol)
    {
        return IsDbContextType(typeSymbol) ? GraphNodeKinds.DbContext : GraphNodeKinds.Type;
    }

    public bool IsDbContextType(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (IsEfCoreType(current, DbContextMetadataName))
            {
                return true;
            }
        }

        return false;
    }

    public INamedTypeSymbol? TryGetDbContextType(ITypeSymbol? typeSymbol)
    {
        return TryNormalizeNamedType(typeSymbol) is { } namedType && IsDbContextType(namedType)
            ? namedType
            : null;
    }

    public INamedTypeSymbol? TryGetDbSetEntityType(ITypeSymbol? typeSymbol)
    {
        if (TryNormalizeNamedType(typeSymbol) is not { } namedType ||
            !IsDbSetType(namedType) ||
            namedType.TypeArguments.Length != 1)
        {
            return null;
        }

        return TryNormalizeNamedType(namedType.TypeArguments[0]);
    }

    private static bool IsDbSetType(INamedTypeSymbol typeSymbol)
    {
        return IsEfCoreType(typeSymbol.OriginalDefinition, DbSetMetadataName);
    }

    private static INamedTypeSymbol? TryNormalizeNamedType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType ||
            namedType.TypeKind is TypeKind.Error or TypeKind.Dynamic ||
            namedType.SpecialType == SpecialType.System_Object)
        {
            return null;
        }

        return namedType;
    }

    private static bool IsEfCoreType(INamedTypeSymbol typeSymbol, string metadataName)
    {
        return typeSymbol.MetadataName.Equals(metadataName, StringComparison.Ordinal) &&
            typeSymbol.ContainingNamespace.ToDisplayString().Equals(EfCoreNamespace, StringComparison.Ordinal);
    }
}
