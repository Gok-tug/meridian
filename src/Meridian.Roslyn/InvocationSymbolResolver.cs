using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal static class InvocationSymbolResolver
{
    public static IMethodSymbol? ResolveTargetMethod(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            return methodSymbol.ReducedFrom ?? methodSymbol;
        }

        if (symbolInfo.CandidateSymbols.Length == 1 && symbolInfo.CandidateSymbols[0] is IMethodSymbol candidate)
        {
            return candidate.ReducedFrom ?? candidate;
        }

        return null;
    }

    public static IReadOnlyList<INamedTypeSymbol> ResolveGenericTypeArguments(
        IMethodSymbol? methodSymbol,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (methodSymbol is not null && methodSymbol.TypeArguments.Length > 0)
        {
            var methodTypeArguments = methodSymbol.TypeArguments
                .OfType<INamedTypeSymbol>()
                .ToArray();
            if (methodTypeArguments.Length == methodSymbol.TypeArguments.Length)
            {
                return methodTypeArguments;
            }
        }

        var syntaxTypeArguments = new List<INamedTypeSymbol>();
        foreach (var typeSyntax in GetGenericTypeArgumentSyntaxes(invocation))
        {
            if (semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type is INamedTypeSymbol typeSymbol)
            {
                syntaxTypeArguments.Add(typeSymbol);
            }
        }

        return syntaxTypeArguments;
    }

    public static string? GetInvokedMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifierName } => identifierName.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            _ => null
        };
    }

    private static IEnumerable<TypeSyntax> GetGenericTypeArgumentSyntaxes(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax memberGenericName })
        {
            return memberGenericName.TypeArgumentList.Arguments;
        }

        if (invocation.Expression is GenericNameSyntax genericName)
        {
            return genericName.TypeArgumentList.Arguments;
        }

        return Array.Empty<TypeSyntax>();
    }
}
