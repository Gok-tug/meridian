using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class EfCoreAnalyzer
{
    private static readonly HashSet<string> QueryMethods = new(StringComparer.Ordinal)
    {
        "All",
        "Any",
        "AsEnumerable",
        "AsNoTracking",
        "AsQueryable",
        "Count",
        "Find",
        "FindAsync",
        "First",
        "FirstOrDefault",
        "Include",
        "LongCount",
        "OrderBy",
        "OrderByDescending",
        "Select",
        "Single",
        "SingleOrDefault",
        "ThenBy",
        "ThenByDescending",
        "ToArray",
        "ToArrayAsync",
        "ToList",
        "ToListAsync",
        "Where"
    };

    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;
    private readonly EfCoreSymbolClassifier _classifier;

    public EfCoreAnalyzer(
        RoslynSourceFilter sourceFilter,
        RoslynGraphFactory graphFactory,
        EfCoreSymbolClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(classifier);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
        _classifier = classifier;
    }

    public void AnalyzeType(TypeDeclarationAnalysisResult typeResult, GraphBuilder graph)
    {
        if (!_classifier.IsDbContextType(typeResult.Symbol))
        {
            return;
        }

        foreach (var property in typeResult.Symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsImplicitlyDeclared && _sourceFilter.HasAnalyzableSourceLocation(property))
            .OrderBy(property => property.ToDisplayString(SymbolDisplay.TypeFormat), StringComparer.Ordinal))
        {
            var entityType = _classifier.TryGetDbSetEntityType(property.Type);
            if (entityType is null || !_sourceFilter.HasAnalyzableSourceLocation(entityType))
            {
                continue;
            }

            var entityNode = _graphFactory.CreateTypeNode(entityType);
            graph.AddNode(entityNode);
            graph.AddEdge(new GraphEdge
            {
                Source = typeResult.Node.Id,
                Target = entityNode.Id,
                Relation = GraphRelations.Contains,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = _graphFactory.CreateEvidence(
                    _sourceFilter.FirstAnalyzableSourceLocation(property),
                    typeResult.Node.Symbol,
                    $"EF Core DbSet property '{property.Name}' exposes entity '{entityNode.Symbol}'.")
            });
        }
    }

    public void AnalyzeMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        if (IsDbContextSetInvocationAccess(memberAccess) || IsInsideNameofExpression(memberAccess))
        {
            return;
        }

        var sourceMethod = GetSourceMethod(memberAccess, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
        var dbSetAccess = ResolveDbSetMemberAccess(symbol);
        if (dbSetAccess is null)
        {
            return;
        }

        EmitDbContextUse(sourceMethod, dbSetAccess.Value.DbContextType, memberAccess.GetLocation(), memberAccess.ToString(), graph);
    }

    public void AnalyzeConditionalAccess(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var sourceMethod = GetSourceMethod(conditionalAccess, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
        {
            var symbol = semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol;
            if (ResolveDbSetMemberAccess(symbol) is { } dbSetAccess)
            {
                EmitDbContextUse(sourceMethod, dbSetAccess.DbContextType, conditionalAccess.GetLocation(), conditionalAccess.ToString(), graph);
            }

            return;
        }

        if (conditionalAccess.WhenNotNull is not InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } invocation)
        {
            return;
        }

        var methodSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (methodSymbol is null || !methodSymbol.Name.Equals("Set", StringComparison.Ordinal))
        {
            return;
        }

        if (methodSymbol.ContainingType is null || !_classifier.IsDbContextType(methodSymbol.ContainingType))
        {
            return;
        }

        var typeArguments = InvocationSymbolResolver.ResolveGenericTypeArguments(methodSymbol, semanticModel, invocation, cancellationToken);
        if (typeArguments.Count != 1 || !_sourceFilter.HasAnalyzableSourceLocation(typeArguments[0]))
        {
            return;
        }

        var receiverTypeInfo = semanticModel.GetTypeInfo(conditionalAccess.Expression, cancellationToken);
        var receiverType = _classifier.TryGetDbContextType(receiverTypeInfo.Type ?? receiverTypeInfo.ConvertedType) ?? methodSymbol.ContainingType;
        EmitDbContextUse(sourceMethod, receiverType, conditionalAccess.GetLocation(), binding.ToString(), graph);
    }

    public void AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var sourceMethod = GetSourceMethod(invocation, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        var methodName = InvocationSymbolResolver.GetInvokedMethodName(invocation);
        if (methodName is null)
        {
            return;
        }

        var methodSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (methodName.Equals("Set", StringComparison.Ordinal) && methodSymbol?.ContainingType is { } containingType && _classifier.IsDbContextType(containingType))
        {
            var typeArguments = InvocationSymbolResolver.ResolveGenericTypeArguments(methodSymbol, semanticModel, invocation, cancellationToken);
            if (typeArguments.Count == 1 && _sourceFilter.HasAnalyzableSourceLocation(typeArguments[0]))
            {
                var receiverType = TryResolveReceiverDbContextType(invocation, semanticModel, cancellationToken) ??
                    TryResolveImplicitReceiverDbContextType(sourceMethod) ??
                    containingType;
                EmitDbContextUse(sourceMethod, receiverType, invocation.GetLocation(), invocation.Expression.ToString(), graph);
            }

            return;
        }

        if (!QueryMethods.Contains(methodName))
        {
            return;
        }

        var entityType = TryResolveDbSetReceiverEntityType(invocation, semanticModel, cancellationToken);
        if (entityType is null)
        {
            return;
        }

        EmitEntityQuery(sourceMethod, entityType, invocation.GetLocation(), invocation.ToString(), graph);
    }

    private (INamedTypeSymbol DbContextType, INamedTypeSymbol EntityType)? ResolveDbSetMemberAccess(ISymbol? symbol)
    {
        var memberType = symbol switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };

        if (memberType is null || symbol?.ContainingType is not { } containingType || !_classifier.IsDbContextType(containingType))
        {
            return null;
        }

        var entityType = _classifier.TryGetDbSetEntityType(memberType);
        return entityType is not null && _sourceFilter.HasAnalyzableSourceLocation(entityType)
            ? (containingType, entityType)
            : null;
    }

    private INamedTypeSymbol? TryResolveDbSetReceiverEntityType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        var entityType = _classifier.TryGetDbSetEntityType(typeInfo.Type ?? typeInfo.ConvertedType);
        return entityType is not null && _sourceFilter.HasAnalyzableSourceLocation(entityType) ? entityType : null;
    }

    private INamedTypeSymbol? TryResolveReceiverDbContextType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        return _classifier.TryGetDbContextType(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    private INamedTypeSymbol? TryResolveImplicitReceiverDbContextType(IMethodSymbol sourceMethod)
    {
        return sourceMethod.ContainingType is { } containingType && _classifier.IsDbContextType(containingType)
            ? containingType
            : null;
    }

    private IMethodSymbol? GetSourceMethod(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sourceMethod = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken) as IMethodSymbol;
        return sourceMethod is not null && _sourceFilter.HasAnalyzableSourceLocation(sourceMethod)
            ? sourceMethod
            : null;
    }

    private void EmitDbContextUse(
        IMethodSymbol sourceMethod,
        INamedTypeSymbol dbContextType,
        Location location,
        string expression,
        GraphBuilder graph)
    {
        if (!_sourceFilter.HasAnalyzableSourceLocation(dbContextType))
        {
            return;
        }

        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var dbContextNode = _graphFactory.CreateTypeNode(dbContextType);
        graph.AddNode(sourceNode);
        graph.AddNode(dbContextNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = dbContextNode.Id,
            Relation = GraphRelations.Uses,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                location,
                sourceNode.Symbol,
                $"Roslyn resolved EF Core DbContext use '{expression}' to '{dbContextNode.Symbol}'.")
        });
    }

    private void EmitEntityQuery(
        IMethodSymbol sourceMethod,
        INamedTypeSymbol entityType,
        Location location,
        string expression,
        GraphBuilder graph)
    {
        if (!_sourceFilter.HasAnalyzableSourceLocation(entityType))
        {
            return;
        }

        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var entityNode = _graphFactory.CreateTypeNode(entityType);
        graph.AddNode(sourceNode);
        graph.AddNode(entityNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = entityNode.Id,
            Relation = GraphRelations.Queries,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                location,
                sourceNode.Symbol,
                $"Roslyn resolved EF Core query '{expression}' to entity '{entityNode.Symbol}'.")
        });
    }

    private static bool IsDbContextSetInvocationAccess(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name switch
        {
            GenericNameSyntax { Identifier.ValueText: "Set" } => memberAccess.Parent is InvocationExpressionSyntax invocation &&
                invocation.Expression == memberAccess,
            IdentifierNameSyntax { Identifier.ValueText: "Set" } => memberAccess.Parent is InvocationExpressionSyntax invocation &&
                invocation.Expression == memberAccess,
            _ => false
        };
    }

    private static bool IsInsideNameofExpression(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax invocation &&
                InvocationSymbolResolver.GetInvokedMethodName(invocation)?.Equals("nameof", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }
}
