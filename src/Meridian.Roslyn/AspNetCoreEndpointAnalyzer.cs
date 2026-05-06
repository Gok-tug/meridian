using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class AspNetCoreEndpointAnalyzer
{
    private static readonly IReadOnlyDictionary<string, string> MinimalApiMethods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH"
    };

    private static readonly IReadOnlyDictionary<string, string> HttpAttributeMethods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HttpGet"] = "GET",
        ["HttpPost"] = "POST",
        ["HttpPut"] = "PUT",
        ["HttpDelete"] = "DELETE",
        ["HttpPatch"] = "PATCH",
        ["HttpHead"] = "HEAD",
        ["HttpOptions"] = "OPTIONS"
    };

    private static readonly IReadOnlyDictionary<string, string> FastEndpointMethods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Get"] = "GET",
        ["Post"] = "POST",
        ["Put"] = "PUT",
        ["Delete"] = "DELETE",
        ["Patch"] = "PATCH"
    };

    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;
    private readonly MediatRSymbolClassifier _mediatorClassifier;

    public AspNetCoreEndpointAnalyzer(
        RoslynSourceFilter sourceFilter,
        RoslynGraphFactory graphFactory,
        MediatRSymbolClassifier mediatorClassifier)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(mediatorClassifier);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
        _mediatorClassifier = mediatorClassifier;
    }

    public AspNetCoreEndpointDocumentContext CreateDocumentContext(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var prefixes = new List<MapGroupPrefix>();
        foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>().OrderBy(variable => variable.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (variable.Initializer?.Value is not InvocationExpressionSyntax initializer ||
                !TryResolveMapGroupRoute(initializer, semanticModel, cancellationToken, out var prefix) ||
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol localSymbol ||
                variable.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
            {
                continue;
            }

            prefixes.Add(new MapGroupPrefix(localSymbol, block, variable.SpanStart, prefix));
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>().OrderBy(assignment => assignment.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assignment.Right is not InvocationExpressionSyntax initializer ||
                !TryResolveMapGroupRoute(initializer, semanticModel, cancellationToken, out var prefix) ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not ILocalSymbol localSymbol ||
                assignment.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
            {
                continue;
            }

            prefixes.Add(new MapGroupPrefix(localSymbol, block, assignment.SpanStart, prefix));
        }

        return new AspNetCoreEndpointDocumentContext(prefixes);
    }

    public void AnalyzeType(
        TypeDeclarationSyntax typeDeclaration,
        TypeDeclarationAnalysisResult typeResult,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        AnalyzeMvcController(typeResult.Symbol, semanticModel, graph, cancellationToken);
        AnalyzeFastEndpoints(typeDeclaration, typeResult.Symbol, semanticModel, graph, cancellationToken);
    }

    public void AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        AspNetCoreEndpointDocumentContext context,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var methodName = InvocationSymbolResolver.GetInvokedMethodName(invocation);
        if (methodName is null ||
            !MinimalApiMethods.TryGetValue(methodName, out var httpMethod) ||
            !IsAspNetCoreRouteBuilderInvocation(invocation, semanticModel, cancellationToken))
        {
            return;
        }

        var routeArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (routeArgument is null)
        {
            return;
        }

        var routeTemplate = AspNetCoreRouteResolver.TryResolveString(routeArgument.Expression, semanticModel, cancellationToken);
        if (routeTemplate is null)
        {
            graph.AddDiagnostic(_graphFactory.CreateDiagnostic(
                routeArgument.Expression.GetLocation(),
                "MERIDIAN_ASPNETCORE_ENDPOINT_DYNAMIC_ROUTE",
                "warning",
                $"ASP.NET Core endpoint route for '{methodName}' is dynamic and was not added to the graph."));
            return;
        }

        var prefix = context.ResolveMapGroupPrefix(invocation, semanticModel, cancellationToken) ??
            TryResolveInlineMapGroupPrefix(invocation, semanticModel, cancellationToken);
        var normalizedRoute = AspNetCoreRouteResolver.CombineRoutes(prefix, routeTemplate);
        var handlerExpression = SelectMinimalApiHandlerExpression(invocation);
        var handlerMethod = handlerExpression is null ? null : ResolveMethodGroup(handlerExpression, semanticModel, cancellationToken);
        var endpointSource = IsMinimalApiEndpointInvocation(invocation, semanticModel, cancellationToken) ? "minimalapi_endpoint" : "minimal_api";
        var endpointNode = CreateEndpointNode(
            handlerMethod?.ContainingAssembly.Identity.Name ?? semanticModel.Compilation.Assembly.Identity.Name,
            httpMethod,
            normalizedRoute,
            endpointSource,
            routeArgument.Expression.GetLocation(),
            handlerMethod);

        graph.AddNode(endpointNode);

        var linked = false;
        if (handlerMethod is not null && _sourceFilter.HasAnalyzableSourceLocation(handlerMethod))
        {
            AddEndpointToMethodEdge(endpointNode, handlerMethod, routeArgument.Expression.GetLocation(), graph, "ASP.NET Core endpoint route maps to method group handler");
            linked = true;
        }
        else if (handlerExpression is not null)
        {
            linked = AnalyzeLambdaEndpointFlow(endpointNode, handlerExpression, semanticModel, graph, cancellationToken);
        }

        if (!linked)
        {
            graph.AddDiagnostic(_graphFactory.CreateDiagnostic(
                routeArgument.Expression.GetLocation(),
                "MERIDIAN_ASPNETCORE_ENDPOINT_UNRESOLVED_HANDLER",
                "warning",
                $"ASP.NET Core endpoint '{endpointNode.Label}' has a known route but Meridian could not resolve a source handler."));
        }
    }

    private void AnalyzeMvcController(
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        if (!IsControllerType(typeSymbol))
        {
            return;
        }

        var controllerRoutes = GetControllerRouteParts(typeSymbol, cancellationToken);
        if (controllerRoutes.Count == 0)
        {
            controllerRoutes = [new EndpointRoutePart(null, string.Empty, _sourceFilter.FirstAnalyzableSourceLocation(typeSymbol))];
        }

        foreach (var methodSymbol in typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsStatic &&
                !method.IsImplicitlyDeclared &&
                _sourceFilter.HasAnalyzableSourceLocation(method))
            .OrderBy(method => method.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasMvcAttribute(methodSymbol, "NonAction"))
            {
                continue;
            }

            var actionRoutes = GetActionRouteParts(methodSymbol, cancellationToken);
            if (actionRoutes.Count == 0)
            {
                continue;
            }

            foreach (var controllerRoute in controllerRoutes)
            {
                foreach (var actionRoute in actionRoutes)
                {
                    var route = AspNetCoreRouteResolver.CombineMvcRoutes(controllerRoute.RouteTemplate, actionRoute.RouteTemplate);
                    route = AspNetCoreRouteResolver.ReplaceMvcTokens(route, typeSymbol.Name, methodSymbol.Name);
                    var location = actionRoute.Location ?? controllerRoute.Location ?? _sourceFilter.FirstAnalyzableSourceLocation(methodSymbol);
                    var endpointNode = CreateEndpointNode(
                        methodSymbol.ContainingAssembly.Identity.Name,
                        actionRoute.HttpMethod ?? "ANY",
                        route,
                        "mvc",
                        location,
                        methodSymbol);

                    graph.AddNode(endpointNode);
                    AddEndpointToMethodEdge(endpointNode, methodSymbol, location, graph, "MVC route attribute maps endpoint to action method");
                }
            }
        }
    }

    private void AnalyzeFastEndpoints(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        if (!IsFastEndpointType(typeSymbol))
        {
            return;
        }

        var handlerMethod = ResolveFastEndpointHandler(typeSymbol);
        foreach (var configureDeclaration in typeDeclaration.Members.OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText.Equals("Configure", StringComparison.Ordinal))
            .OrderBy(method => method.SpanStart))
        {
            foreach (var invocation in configureDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>().OrderBy(invocation => invocation.SpanStart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var methodName = InvocationSymbolResolver.GetInvokedMethodName(invocation);
                if (methodName is null ||
                    !FastEndpointMethods.TryGetValue(methodName, out var httpMethod) ||
                    !IsFastEndpointVerbInvocation(invocation, semanticModel, cancellationToken))
                {
                    continue;
                }

                var routeArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
                if (routeArgument is null)
                {
                    continue;
                }

                var routeTemplate = AspNetCoreRouteResolver.TryResolveString(routeArgument.Expression, semanticModel, cancellationToken);
                if (routeTemplate is null)
                {
                    graph.AddDiagnostic(_graphFactory.CreateDiagnostic(
                        routeArgument.Expression.GetLocation(),
                        "MERIDIAN_ASPNETCORE_ENDPOINT_DYNAMIC_ROUTE",
                        "warning",
                        $"FastEndpoints route for '{methodName}' is dynamic and was not added to the graph."));
                    continue;
                }

                var endpointNode = CreateEndpointNode(
                    handlerMethod?.ContainingAssembly.Identity.Name ?? typeSymbol.ContainingAssembly.Identity.Name,
                    httpMethod,
                    routeTemplate,
                    "fastendpoints",
                    routeArgument.Expression.GetLocation(),
                    handlerMethod);
                graph.AddNode(endpointNode);

                if (handlerMethod is not null)
                {
                    AddEndpointToMethodEdge(endpointNode, handlerMethod, routeArgument.Expression.GetLocation(), graph, "FastEndpoints route configuration maps endpoint to handler method");
                }
                else
                {
                    graph.AddDiagnostic(_graphFactory.CreateDiagnostic(
                        routeArgument.Expression.GetLocation(),
                        "MERIDIAN_ASPNETCORE_ENDPOINT_UNRESOLVED_HANDLER",
                        "warning",
                        $"FastEndpoints route '{endpointNode.Label}' has no source ExecuteAsync or HandleAsync handler."));
                }
            }
        }
    }

    private GraphNode CreateEndpointNode(
        string assemblyName,
        string httpMethod,
        string routeTemplate,
        string endpointSource,
        Location location,
        IMethodSymbol? handlerMethod)
    {
        return _graphFactory.CreateEndpointNode(
            assemblyName,
            httpMethod,
            routeTemplate,
            endpointSource,
            location,
            handlerMethod?.ToDisplayString(SymbolDisplay.MethodFormat));
    }

    private void AddEndpointToMethodEdge(
        GraphNode endpointNode,
        IMethodSymbol targetMethod,
        Location location,
        GraphBuilder graph,
        string reason)
    {
        var methodNode = _graphFactory.CreateMethodNode(targetMethod);
        graph.AddNode(methodNode);
        graph.AddEdge(new GraphEdge
        {
            Source = endpointNode.Id,
            Target = methodNode.Id,
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                location,
                endpointNode.Symbol,
                $"{reason} '{methodNode.Symbol}'.")
        });
    }

    private bool AnalyzeLambdaEndpointFlow(
        GraphNode endpointNode,
        ExpressionSyntax handlerExpression,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var linked = false;
        foreach (var invocation in LambdaBodyInvocations(handlerExpression).OrderBy(invocation => invocation.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var methodName = InvocationSymbolResolver.GetInvokedMethodName(invocation);
            var targetMethod = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
            if (targetMethod is not null &&
                methodName is "HandleAsync" or "ExecuteAsync" &&
                _sourceFilter.HasAnalyzableSourceLocation(targetMethod))
            {
                AddEndpointToMethodEdge(endpointNode, targetMethod, invocation.GetLocation(), graph, "ASP.NET Core endpoint lambda delegates to source handler method");
                linked = true;
            }

            if (methodName is not ("Send" or "Publish") ||
                targetMethod is null ||
                !IsMediatorDispatchMethod(targetMethod, methodName))
            {
                continue;
            }

            var messageArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (messageArgument is null || TryResolveDispatchMessage(messageArgument.Expression, semanticModel, cancellationToken) is not { } messageType)
            {
                continue;
            }

            if ((methodName == "Send" && !_mediatorClassifier.IsRequestType(messageType)) ||
                (methodName == "Publish" && !_mediatorClassifier.IsNotificationType(messageType)))
            {
                continue;
            }

            var messageNode = _graphFactory.CreateTypeNodeAllowingMissingSource(messageType);
            graph.AddNode(messageNode);
            graph.AddEdge(new GraphEdge
            {
                Source = endpointNode.Id,
                Target = messageNode.Id,
                Relation = methodName == "Send" ? GraphRelations.Sends : GraphRelations.Publishes,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = _graphFactory.CreateEvidence(
                    invocation.GetLocation(),
                    endpointNode.Symbol,
                    $"ASP.NET Core endpoint lambda directly performs mediator {methodName} for '{messageNode.Symbol}'.")
            });
            linked = true;
        }

        return linked;
    }

    private static IEnumerable<InvocationExpressionSyntax> LambdaBodyInvocations(ExpressionSyntax handlerExpression)
    {
        return handlerExpression switch
        {
            ParenthesizedLambdaExpressionSyntax { Block: { } block } => DirectBodyInvocations(block, includeSelf: false),
            ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } expression } => DirectBodyInvocations(expression, includeSelf: true),
            SimpleLambdaExpressionSyntax { Block: { } block } => DirectBodyInvocations(block, includeSelf: false),
            SimpleLambdaExpressionSyntax { ExpressionBody: { } expression } => DirectBodyInvocations(expression, includeSelf: true),
            AnonymousMethodExpressionSyntax { Block: { } block } => DirectBodyInvocations(block, includeSelf: false),
            _ => []
        };
    }

    private static IEnumerable<InvocationExpressionSyntax> DirectBodyInvocations(SyntaxNode body, bool includeSelf)
    {
        var nodes = includeSelf
            ? body.DescendantNodesAndSelf(ShouldDescendIntoEndpointHandlerNode)
            : body.DescendantNodes(ShouldDescendIntoEndpointHandlerNode);
        return nodes.OfType<InvocationExpressionSyntax>();
    }

    private static bool ShouldDescendIntoEndpointHandlerNode(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax;
    }

    private INamedTypeSymbol? TryResolveDispatchMessage(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        if (TryNormalizeDispatchMessageType(typeInfo.Type ?? typeInfo.ConvertedType) is { } expressionType)
        {
            return expressionType;
        }

        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        return symbol switch
        {
            IParameterSymbol parameterSymbol => TryNormalizeDispatchMessageType(parameterSymbol.Type),
            ILocalSymbol localSymbol => ResolveLocalDispatchMessage(localSymbol, cancellationToken),
            _ => null
        };
    }

    private static INamedTypeSymbol? ResolveLocalDispatchMessage(ILocalSymbol localSymbol, CancellationToken cancellationToken)
    {
        var declaration = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .OrderBy(variable => variable.SpanStart)
            .FirstOrDefault();
        return declaration?.Initializer?.Value is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
            ? TryNormalizeDispatchMessageType(localSymbol.Type)
            : null;
    }

    private static INamedTypeSymbol? TryNormalizeDispatchMessageType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
            namedType.TypeKind is not (TypeKind.Error or TypeKind.Dynamic) &&
            namedType.SpecialType != SpecialType.System_Object
            ? namedType
            : null;
    }

    private static bool IsMediatorDispatchMethod(IMethodSymbol methodSymbol, string methodName)
    {
        if (!methodSymbol.Name.Equals(methodName, StringComparison.Ordinal))
        {
            return false;
        }

        return methodName switch
        {
            "Send" => IsMediatorDispatchType(methodSymbol.ContainingType, "IMediator", "ISender"),
            "Publish" => IsMediatorDispatchType(methodSymbol.ContainingType, "IMediator", "IPublisher"),
            _ => false
        };
    }

    private static bool IsMediatorDispatchType(INamedTypeSymbol? typeSymbol, params string[] acceptedNames)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        return IsMediatorNamedType(typeSymbol, acceptedNames) ||
            typeSymbol.AllInterfaces.Any(interfaceSymbol => IsMediatorNamedType(interfaceSymbol, acceptedNames));
    }

    private static bool IsMediatorNamedType(INamedTypeSymbol typeSymbol, params string[] acceptedNames)
    {
        return MediatRSymbolClassifier.IsSupportedMediatorNamespace(typeSymbol.ContainingNamespace) &&
            acceptedNames.Contains(typeSymbol.Name, StringComparer.Ordinal);
    }

    private static ExpressionSyntax? SelectMinimalApiHandlerExpression(InvocationExpressionSyntax invocation)
    {
        return invocation.ArgumentList.Arguments
            .Skip(1)
            .FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText is null or "handler")
            ?.Expression;
    }

    private IMethodSymbol? ResolveMethodGroup(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            return null;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(expression, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol && _sourceFilter.HasAnalyzableSourceLocation(methodSymbol))
        {
            return methodSymbol;
        }

        return symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .Where(_sourceFilter.HasAnalyzableSourceLocation)
            .OrderBy(method => method.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? TryResolveInlineMapGroupPrefix(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax receiverInvocation } &&
            TryResolveMapGroupRoute(receiverInvocation, semanticModel, cancellationToken, out var prefix)
            ? prefix
            : null;
    }

    private static bool IsAspNetCoreRouteBuilderInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var targetMethod = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (targetMethod is null)
        {
            return false;
        }

        if (targetMethod.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
        {
            return true;
        }

        if (targetMethod.Parameters.FirstOrDefault()?.Type is INamedTypeSymbol firstParameterType &&
            IsEndpointRouteBuilderType(firstParameterType))
        {
            return true;
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type is INamedTypeSymbol receiverType &&
            IsEndpointRouteBuilderType(receiverType);
    }

    private static bool IsEndpointRouteBuilderType(INamedTypeSymbol typeSymbol)
    {
        return IsNamedType(typeSymbol, "Microsoft.AspNetCore.Routing", "IEndpointRouteBuilder") ||
            typeSymbol.AllInterfaces.Any(interfaceSymbol => IsNamedType(interfaceSymbol, "Microsoft.AspNetCore.Routing", "IEndpointRouteBuilder"));
    }

    private static bool IsNamedType(INamedTypeSymbol typeSymbol, string namespaceName, string typeName)
    {
        return typeSymbol.Name.Equals(typeName, StringComparison.Ordinal) &&
            typeSymbol.ContainingNamespace.ToDisplayString().Equals(namespaceName, StringComparison.Ordinal);
    }

    internal static bool TryResolveMapGroupRoute(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string prefix)
    {
        prefix = string.Empty;
        if (!string.Equals(InvocationSymbolResolver.GetInvokedMethodName(invocation), "MapGroup", StringComparison.Ordinal) ||
            !IsAspNetCoreRouteBuilderInvocation(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        var routeArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (routeArgument is null)
        {
            return false;
        }

        var route = AspNetCoreRouteResolver.TryResolveString(routeArgument.Expression, semanticModel, cancellationToken);
        if (route is null)
        {
            return false;
        }

        prefix = route;
        return true;
    }

    private static bool IsMinimalApiEndpointInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return ResolveSourceMethod(invocation, semanticModel, cancellationToken) is { Name: "AddRoute" } sourceMethod &&
            sourceMethod.ContainingType.AllInterfaces.Any(interfaceSymbol =>
                interfaceSymbol.ContainingNamespace.ToDisplayString().Equals("MinimalApi.Endpoint", StringComparison.Ordinal));
    }

    private static IMethodSymbol? ResolveSourceMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sourceSymbol = semanticModel.GetEnclosingSymbol(invocation.SpanStart, cancellationToken) as IMethodSymbol;
        if (sourceSymbol?.MethodKind != MethodKind.AnonymousFunction)
        {
            return sourceSymbol;
        }

        return invocation.Ancestors()
            .OfType<BaseMethodDeclarationSyntax>()
            .Select(declaration => semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as IMethodSymbol)
            .FirstOrDefault(symbol => symbol is not null) ?? sourceSymbol;
    }

    private static IReadOnlyList<EndpointRoutePart> GetControllerRouteParts(INamedTypeSymbol typeSymbol, CancellationToken cancellationToken)
    {
        return typeSymbol.GetAttributes()
            .Select(attribute => TryGetRoutePart(attribute, cancellationToken))
            .Where(routePart => routePart is { HttpMethod: null })
            .Cast<EndpointRoutePart>()
            .ToArray();
    }

    private static IReadOnlyList<EndpointRoutePart> GetActionRouteParts(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        return methodSymbol.GetAttributes()
            .Select(attribute => TryGetRoutePart(attribute, cancellationToken))
            .Where(routePart => routePart is not null)
            .Cast<EndpointRoutePart>()
            .ToArray();
    }

    private static EndpointRoutePart? TryGetRoutePart(AttributeData attribute, CancellationToken cancellationToken)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null || !IsMvcAttribute(attributeClass))
        {
            return null;
        }

        var name = AttributeShortName(attributeClass.Name);
        if (name is null)
        {
            return null;
        }

        if (HttpAttributeMethods.TryGetValue(name, out var httpMethod))
        {
            return new EndpointRoutePart(httpMethod, TryGetAttributeRoute(attribute), TryGetAttributeLocation(attribute, cancellationToken));
        }

        return name.Equals("Route", StringComparison.Ordinal)
            ? new EndpointRoutePart(null, TryGetAttributeRoute(attribute), TryGetAttributeLocation(attribute, cancellationToken))
            : null;
    }

    private static string? AttributeShortName(string? attributeName)
    {
        if (attributeName is null)
        {
            return null;
        }

        return attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName[..^"Attribute".Length]
            : attributeName;
    }

    private static string? TryGetAttributeRoute(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Value is string route)
            {
                return route;
            }
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if ((namedArgument.Key.Equals("Template", StringComparison.Ordinal) ||
                    namedArgument.Key.Equals("Route", StringComparison.Ordinal)) &&
                namedArgument.Value.Value is string route)
            {
                return route;
            }
        }

        return string.Empty;
    }

    private static Location? TryGetAttributeLocation(AttributeData attribute, CancellationToken cancellationToken)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
    }

    private static bool IsControllerType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.Name.EndsWith("Controller", StringComparison.Ordinal) ||
            HasMvcAttribute(typeSymbol, "ApiController") ||
            HasMvcAttribute(typeSymbol, "Route") ||
            InheritsFromMvcController(typeSymbol);
    }

    private static bool InheritsFromMvcController(INamedTypeSymbol typeSymbol)
    {
        for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.ContainingNamespace.ToDisplayString().Equals("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal) &&
                baseType.Name is "Controller" or "ControllerBase")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMvcAttribute(ISymbol symbol, string shortAttributeName)
    {
        return symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { } attributeClass &&
            IsMvcAttribute(attributeClass) &&
            AttributeShortName(attributeClass.Name)?.Equals(shortAttributeName, StringComparison.Ordinal) == true);
    }

    private static bool IsMvcAttribute(INamedTypeSymbol attributeClass)
    {
        return attributeClass.ContainingNamespace.ToDisplayString().Equals("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal);
    }

    private static bool IsFastEndpointType(INamedTypeSymbol typeSymbol)
    {
        for (var candidate = typeSymbol; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate.ContainingNamespace.ToDisplayString().Equals("FastEndpoints", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return typeSymbol.AllInterfaces.Any(interfaceSymbol =>
            interfaceSymbol.ContainingNamespace.ToDisplayString().Equals("FastEndpoints", StringComparison.Ordinal));
    }

    private static bool IsFastEndpointVerbInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken) is { ContainingType: { } containingType } &&
            IsFastEndpointType(containingType);
    }

    private IMethodSymbol? ResolveFastEndpointHandler(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                method.Name is "ExecuteAsync" or "HandleAsync" &&
                _sourceFilter.HasAnalyzableSourceLocation(method))
            .OrderBy(method => method.Name.Equals("ExecuteAsync", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(method => method.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private readonly record struct EndpointRoutePart(string? HttpMethod, string? RouteTemplate, Location? Location);
}

internal sealed class AspNetCoreEndpointDocumentContext
{
    private readonly IReadOnlyList<MapGroupPrefix> _mapGroupPrefixes;

    public AspNetCoreEndpointDocumentContext(IReadOnlyList<MapGroupPrefix> mapGroupPrefixes)
    {
        _mapGroupPrefixes = mapGroupPrefixes;
    }

    public string? ResolveMapGroupPrefix(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not ILocalSymbol localSymbol ||
            invocation.FirstAncestorOrSelf<BlockSyntax>() is not { } invocationBlock)
        {
            return null;
        }

        return _mapGroupPrefixes
            .Where(prefix => SymbolEqualityComparer.Default.Equals(prefix.Symbol, localSymbol) &&
                prefix.SpanStart < invocation.SpanStart &&
                ReferenceEquals(prefix.Block, invocationBlock))
            .OrderBy(prefix => prefix.SpanStart)
            .LastOrDefault()
            .Prefix;
    }
}

internal readonly record struct MapGroupPrefix(ILocalSymbol Symbol, BlockSyntax Block, int SpanStart, string Prefix);
