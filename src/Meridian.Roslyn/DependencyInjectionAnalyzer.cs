using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class DependencyInjectionAnalyzer
{
    private static readonly HashSet<string> RegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddScoped",
        "AddSingleton",
        "AddTransient"
    };

    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public DependencyInjectionAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public void AnalyzeConstructorInjection(TypeDeclarationAnalysisResult typeResult, GraphBuilder graph)
    {
        var typeSymbol = typeResult.Symbol;
        var typeNode = typeResult.Node;
        if (typeSymbol.TypeKind != TypeKind.Class || typeSymbol.IsRecord)
        {
            return;
        }

        foreach (var constructor in SelectConstructors(typeSymbol))
        {
            var constructorLocation = _sourceFilter.FirstAnalyzableSourceLocation(constructor);
            foreach (var parameter in constructor.Parameters.OrderBy(parameter => parameter.Ordinal))
            {
                if (parameter.Type is not INamedTypeSymbol dependencyType ||
                    !IsInjectableDependencyType(dependencyType) ||
                    !_sourceFilter.HasAnalyzableSourceLocation(dependencyType))
                {
                    continue;
                }

                var dependencyNode = _graphFactory.CreateTypeNode(dependencyType);
                graph.AddNode(dependencyNode);
                graph.AddEdge(new GraphEdge
                {
                    Source = typeNode.Id,
                    Target = dependencyNode.Id,
                    Relation = GraphRelations.Injects,
                    Confidence = ConfidenceLevels.Extracted,
                    ConfidenceScore = 1.0,
                    Evidence = _graphFactory.CreateEvidence(
                        _sourceFilter.FirstAnalyzableSourceLocation(parameter, constructorLocation),
                        constructor.ToDisplayString(SymbolDisplay.MethodFormat),
                        $"Constructor parameter '{parameter.Name}' injects '{dependencyNode.Symbol}'.")
                });
            }
        }
    }

    public void AnalyzeRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var methodName = InvocationSymbolResolver.GetInvokedMethodName(invocation);
        if (methodName is null || !RegistrationMethods.Contains(methodName))
        {
            return;
        }

        var methodSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (methodSymbol is null || !IsDependencyInjectionRegistrationMethod(methodSymbol))
        {
            return;
        }

        var registrationTypes = ResolveRegistrationTypes(methodSymbol, semanticModel, invocation, cancellationToken);
        if (registrationTypes is null)
        {
            return;
        }

        var (serviceType, implementationType) = registrationTypes.Value;
        if (!_sourceFilter.HasAnalyzableSourceLocation(serviceType) || !_sourceFilter.HasAnalyzableSourceLocation(implementationType))
        {
            return;
        }

        var serviceNode = _graphFactory.CreateTypeNode(serviceType);
        var implementationNode = _graphFactory.CreateTypeNode(implementationType);
        graph.AddNode(serviceNode);
        graph.AddNode(implementationNode);

        graph.AddEdge(new GraphEdge
        {
            Source = serviceNode.Id,
            Target = implementationNode.Id,
            Relation = GraphRelations.RegisteredAs,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                invocation.GetLocation(),
                (semanticModel.GetEnclosingSymbol(invocation.SpanStart, cancellationToken) as IMethodSymbol)?.ToDisplayString(SymbolDisplay.MethodFormat),
                $"Roslyn resolved DI registration '{methodName}' from '{serviceNode.Symbol}' to '{implementationNode.Symbol}'."),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["lifetime"] = RegistrationLifetime(methodName)
            }
        });
    }

    private IEnumerable<IMethodSymbol> SelectConstructors(INamedTypeSymbol typeSymbol)
    {
        var constructors = typeSymbol.Constructors
            .Where(constructor => !constructor.IsImplicitlyDeclared &&
                !constructor.IsStatic &&
                constructor.Parameters.Length > 0 &&
                _sourceFilter.HasAnalyzableSourceLocation(constructor))
            .OrderBy(constructor => constructor.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal)
            .ToArray();

        if (constructors.Length <= 1)
        {
            return constructors;
        }

        var markedConstructors = constructors
            .Where(IsActivatorUtilitiesConstructor)
            .ToArray();

        return markedConstructors.Length == 1 ? markedConstructors : [];
    }

    private static (INamedTypeSymbol Service, INamedTypeSymbol Implementation)? ResolveRegistrationTypes(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var typeArguments = InvocationSymbolResolver.ResolveGenericTypeArguments(methodSymbol, semanticModel, invocation, cancellationToken);
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return typeArguments.Count switch
            {
                1 => (typeArguments[0], typeArguments[0]),
                >= 2 => (typeArguments[0], typeArguments[1]),
                _ => null
            };
        }

        if (typeArguments.Count == 0 || invocation.ArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        var factoryExpression = invocation.ArgumentList.Arguments[0].Expression;
        return ResolveFactoryImplementationType(factoryExpression, semanticModel, cancellationToken) is { } implementationType
            ? (typeArguments[0], implementationType)
            : null;
    }

    private static INamedTypeSymbol? ResolveFactoryImplementationType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        var serviceProviderParameter = ResolveSingleLambdaParameter(lambda, semanticModel, cancellationToken);
        return lambda.Body switch
        {
            ExpressionSyntax bodyExpression => TryResolveFactoryResultType(bodyExpression, semanticModel, serviceProviderParameter, cancellationToken),
            BlockSyntax block => ResolveSingleReturnFactoryResultType(block, semanticModel, serviceProviderParameter, cancellationToken),
            _ => null
        };
    }

    private static INamedTypeSymbol? ResolveSingleReturnFactoryResultType(
        BlockSyntax block,
        SemanticModel semanticModel,
        IParameterSymbol? serviceProviderParameter,
        CancellationToken cancellationToken)
    {
        if (block.Statements.Count == 0 ||
            block.Statements[^1] is not ReturnStatementSyntax { Expression: { } returnExpression })
        {
            return null;
        }

        if (block.Statements.Take(block.Statements.Count - 1).Any(static statement =>
            statement is not LocalDeclarationStatementSyntax and not ExpressionStatementSyntax and not EmptyStatementSyntax))
        {
            return null;
        }

        return TryResolveFactoryResultType(returnExpression, semanticModel, serviceProviderParameter, cancellationToken);
    }

    private static INamedTypeSymbol? TryResolveFactoryResultType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol? serviceProviderParameter,
        CancellationToken cancellationToken)
    {
        return TryResolveObjectCreationType(expression, semanticModel, cancellationToken) ??
            TryResolveGetRequiredServiceType(expression, semanticModel, serviceProviderParameter, cancellationToken);
    }

    private static INamedTypeSymbol? TryResolveObjectCreationType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not ObjectCreationExpressionSyntax and not ImplicitObjectCreationExpressionSyntax)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return TryNormalizeRegistrationType(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    private static INamedTypeSymbol? TryResolveGetRequiredServiceType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol? serviceProviderParameter,
        CancellationToken cancellationToken)
    {
        if (expression is not InvocationExpressionSyntax invocation || serviceProviderParameter is null)
        {
            return null;
        }

        var methodSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (methodSymbol is null ||
            !IsGetRequiredServiceMethod(methodSymbol) ||
            !UsesLambdaServiceProviderParameter(invocation, semanticModel, serviceProviderParameter, cancellationToken))
        {
            return null;
        }

        var typeArguments = InvocationSymbolResolver.ResolveGenericTypeArguments(methodSymbol, semanticModel, invocation, cancellationToken);
        if (typeArguments.Count != 1)
        {
            return null;
        }

        var implementationType = TryNormalizeRegistrationType(typeArguments[0]);
        return implementationType is { TypeKind: TypeKind.Class, IsAbstract: false }
            ? implementationType
            : null;
    }

    private static bool UsesLambdaServiceProviderParameter(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        IParameterSymbol serviceProviderParameter,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            ResolvesToParameter(memberAccess.Expression, semanticModel, serviceProviderParameter, cancellationToken))
        {
            return true;
        }

        return invocation.ArgumentList.Arguments.Count > 0 &&
            ResolvesToParameter(invocation.ArgumentList.Arguments[0].Expression, semanticModel, serviceProviderParameter, cancellationToken);
    }

    private static bool ResolvesToParameter(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol parameterSymbol,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is IParameterSymbol symbol &&
            SymbolEqualityComparer.Default.Equals(symbol, parameterSymbol);
    }

    private static IParameterSymbol? ResolveSingleLambdaParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var parameter = lambda switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda when parenthesizedLambda.ParameterList.Parameters.Count == 1 => parenthesizedLambda.ParameterList.Parameters[0],
            _ => null
        };

        return parameter is null
            ? null
            : semanticModel.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol;
    }

    private static INamedTypeSymbol? TryNormalizeRegistrationType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType ||
            namedType.TypeKind is TypeKind.Error or TypeKind.Dynamic ||
            namedType.SpecialType == SpecialType.System_Object)
        {
            return null;
        }

        return namedType;
    }

    private static bool IsDependencyInjectionRegistrationMethod(IMethodSymbol methodSymbol)
    {
        if (!RegistrationMethods.Contains(methodSymbol.Name))
        {
            return false;
        }

        if (methodSymbol.ContainingNamespace.ToDisplayString().Equals("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
        {
            return true;
        }

        return methodSymbol.Parameters.Any(static parameter =>
            parameter.Type.Name == "IServiceCollection" &&
            parameter.Type.ContainingNamespace.ToDisplayString().Equals("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal));
    }

    private static bool IsActivatorUtilitiesConstructor(IMethodSymbol constructor)
    {
        return constructor.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute" &&
            attribute.AttributeClass.ContainingNamespace.ToDisplayString().Equals("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal));
    }

    private static bool IsGetRequiredServiceMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Name.Equals("GetRequiredService", StringComparison.Ordinal) &&
            methodSymbol.ContainingType.Name.Equals("ServiceProviderServiceExtensions", StringComparison.Ordinal) &&
            methodSymbol.ContainingNamespace.ToDisplayString().Equals("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal);
    }

    private static string RegistrationLifetime(string methodName)
    {
        return methodName switch
        {
            "AddScoped" => "scoped",
            "AddSingleton" => "singleton",
            "AddTransient" => "transient",
            _ => "unknown"
        };
    }

    private static bool IsInjectableDependencyType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.TypeKind is TypeKind.Class or TypeKind.Interface && !typeSymbol.IsRecord;
    }
}
