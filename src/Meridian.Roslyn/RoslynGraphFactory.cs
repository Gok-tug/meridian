using System.Globalization;
using Meridian.Abstractions;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class RoslynGraphFactory
{
    private readonly string _rootDirectory;
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly Func<INamedTypeSymbol, string>? _typeNodeKindSelector;

    public RoslynGraphFactory(
        string rootDirectory,
        RoslynSourceFilter sourceFilter,
        Func<INamedTypeSymbol, string>? typeNodeKindSelector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(sourceFilter);
        _rootDirectory = rootDirectory;
        _sourceFilter = sourceFilter;
        _typeNodeKindSelector = typeNodeKindSelector;
    }

    public GraphNode CreateTypeNode(INamedTypeSymbol typeSymbol)
    {
        return CreateTypeNode(typeSymbol, _sourceFilter.FirstAnalyzableSourceLocation(typeSymbol));
    }

    public GraphNode CreateTypeNodeAllowingMissingSource(INamedTypeSymbol typeSymbol)
    {
        return CreateTypeNode(typeSymbol, _sourceFilter.TryFirstAnalyzableSourceLocation(typeSymbol));
    }

    private GraphNode CreateTypeNode(INamedTypeSymbol typeSymbol, Location? location)
    {
        var symbol = typeSymbol.ToDisplayString(SymbolDisplay.TypeFormat);
        return new GraphNode
        {
            Id = $"type:{typeSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = typeSymbol.Name,
            Kind = _typeNodeKindSelector?.Invoke(typeSymbol) ?? GraphNodeKinds.Type,
            Symbol = symbol,
            SourceFile = location is null ? null : SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = location is null ? null : SourcePath.SourceLocation(location),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["type_kind"] = typeSymbol.TypeKind.ToString().ToLowerInvariant()
            }
        };
    }

    public GraphNode CreateEnumNode(INamedTypeSymbol enumSymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(enumSymbol);
        var symbol = enumSymbol.ToDisplayString(SymbolDisplay.TypeFormat);
        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["accessibility"] = enumSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            ["type_kind"] = enumSymbol.TypeKind.ToString().ToLowerInvariant()
        };

        if (enumSymbol.EnumUnderlyingType is { } underlyingType)
        {
            metadata["underlying_type"] = underlyingType.ToDisplayString(SymbolDisplay.TypeFormat);
        }

        return new GraphNode
        {
            Id = $"enum:{enumSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = enumSymbol.Name,
            Kind = GraphNodeKinds.Enum,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = metadata
        };
    }

    public GraphNode CreateEnumMemberNode(IFieldSymbol enumMemberSymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(enumMemberSymbol);
        var containingEnum = enumMemberSymbol.ContainingType;
        var enumSymbol = containingEnum.ToDisplayString(SymbolDisplay.TypeFormat);
        var symbol = enumMemberSymbol.ToDisplayString(SymbolDisplay.MemberFormat);
        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["accessibility"] = enumMemberSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            ["containing_enum"] = enumSymbol,
            ["member_type"] = enumMemberSymbol.Type.ToDisplayString(SymbolDisplay.TypeFormat)
        };

        if (enumMemberSymbol.HasConstantValue)
        {
            metadata["constant_value"] = ConstantValueString(enumMemberSymbol.ConstantValue);
        }

        return new GraphNode
        {
            Id = $"enum_member:{enumMemberSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{containingEnum.Name}.{enumMemberSymbol.Name}",
            Kind = GraphNodeKinds.EnumMember,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = metadata
        };
    }

    public GraphNode CreatePropertyNode(IPropertySymbol propertySymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(propertySymbol);
        var symbol = propertySymbol.ToDisplayString(SymbolDisplay.MemberFormat);
        return new GraphNode
        {
            Id = $"property:{propertySymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{propertySymbol.ContainingType.Name}.{propertySymbol.Name}",
            Kind = GraphNodeKinds.Property,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["accessibility"] = propertySymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                ["containing_type"] = propertySymbol.ContainingType.ToDisplayString(SymbolDisplay.TypeFormat),
                ["has_getter"] = BoolString(propertySymbol.GetMethod is not null),
                ["has_setter"] = BoolString(propertySymbol.SetMethod is not null),
                ["is_static"] = BoolString(propertySymbol.IsStatic),
                ["member_type"] = propertySymbol.Type.ToDisplayString(SymbolDisplay.TypeFormat)
            }
        };
    }

    public GraphNode CreateFieldNode(IFieldSymbol fieldSymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(fieldSymbol);
        var symbol = fieldSymbol.ToDisplayString(SymbolDisplay.MemberFormat);
        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["accessibility"] = fieldSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            ["containing_type"] = fieldSymbol.ContainingType.ToDisplayString(SymbolDisplay.TypeFormat),
            ["is_const"] = BoolString(fieldSymbol.IsConst),
            ["is_readonly"] = BoolString(fieldSymbol.IsReadOnly),
            ["is_static"] = BoolString(fieldSymbol.IsStatic),
            ["member_type"] = fieldSymbol.Type.ToDisplayString(SymbolDisplay.TypeFormat)
        };

        if (fieldSymbol.HasConstantValue)
        {
            metadata["constant_value"] = ConstantValueString(fieldSymbol.ConstantValue);
        }

        return new GraphNode
        {
            Id = $"field:{fieldSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{fieldSymbol.ContainingType.Name}.{fieldSymbol.Name}",
            Kind = GraphNodeKinds.Field,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = metadata
        };
    }

    public GraphNode CreateMethodNode(IMethodSymbol methodSymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(methodSymbol);
        var symbol = methodSymbol.ToDisplayString(SymbolDisplay.MethodFormat);
        return new GraphNode
        {
            Id = $"method:{methodSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
            Kind = GraphNodeKinds.Method,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location)
        };
    }

    public GraphNode CreateGeneratedPropertyNode(IFieldSymbol backingFieldSymbol, string propertyName)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(backingFieldSymbol);
        var containingType = backingFieldSymbol.ContainingType.ToDisplayString(SymbolDisplay.TypeFormat);
        var symbol = $"{containingType}.{propertyName}";
        return new GraphNode
        {
            Id = $"property:{backingFieldSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{backingFieldSymbol.ContainingType.Name}.{propertyName}",
            Kind = GraphNodeKinds.Property,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["accessibility"] = "public",
                ["containing_type"] = containingType,
                ["generated_from_attribute"] = "ObservableProperty",
                ["generated_from_member"] = backingFieldSymbol.ToDisplayString(SymbolDisplay.MemberFormat),
                ["has_getter"] = "true",
                ["has_setter"] = "true",
                ["is_static"] = BoolString(backingFieldSymbol.IsStatic),
                ["member_type"] = backingFieldSymbol.Type.ToDisplayString(SymbolDisplay.TypeFormat)
            }
        };
    }

    public GraphNode CreateMvvmCommandNode(IMethodSymbol methodSymbol, string commandName, bool isAsyncCommand)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(methodSymbol);
        var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplay.TypeFormat);
        var symbol = $"{containingType}.{commandName}";
        return new GraphNode
        {
            Id = $"mvvm_command:{methodSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{methodSymbol.ContainingType.Name}.{commandName}",
            Kind = GraphNodeKinds.MvvmCommand,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["command_type"] = isAsyncCommand ? "IAsyncRelayCommand" : "IRelayCommand",
                ["containing_type"] = containingType,
                ["generated_from_attribute"] = "RelayCommand",
                ["source_method"] = methodSymbol.ToDisplayString(SymbolDisplay.MethodFormat)
            }
        };
    }

    public GraphNode CreateEndpointNode(
        string assemblyName,
        string httpMethod,
        string routeTemplate,
        string endpointSource,
        Location location,
        string? handlerSymbol = null)
    {
        var normalizedHttpMethod = AspNetCoreRouteResolver.NormalizeHttpMethod(httpMethod);
        var normalizedRoute = AspNetCoreRouteResolver.NormalizeRouteTemplate(routeTemplate);
        var label = $"{normalizedHttpMethod} {normalizedRoute}";
        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["endpoint_source"] = endpointSource,
            ["http_method"] = normalizedHttpMethod,
            ["route_template"] = normalizedRoute
        };

        if (!string.IsNullOrWhiteSpace(handlerSymbol))
        {
            metadata["handler_symbol"] = handlerSymbol;
        }

        return new GraphNode
        {
            Id = $"endpoint:{assemblyName}:{normalizedHttpMethod}:{normalizedRoute}",
            Label = label,
            Kind = GraphNodeKinds.Endpoint,
            Symbol = label,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = metadata
        };
    }

    private static string BoolString(bool value)
    {
        return value ? "true" : "false";
    }

    private static string ConstantValueString(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolValue => BoolString(boolValue),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    public GraphEvidence CreateEvidence(Location location, string? symbol, string reason)
    {
        return new GraphEvidence
        {
            File = SourcePath.RelativeFile(location, _rootDirectory),
            Line = SourcePath.Line(location),
            Symbol = symbol,
            Reason = reason
        };
    }

    public GraphEvidence CreateEvidence(string filePath, int? line, string? symbol, string reason)
    {
        return new GraphEvidence
        {
            File = SourcePath.RelativeFile(filePath, _rootDirectory),
            Line = line,
            Symbol = symbol,
            Reason = reason
        };
    }

    public GraphDiagnostic CreateDiagnostic(Location location, string id, string severity, string message)
    {
        return new GraphDiagnostic
        {
            Id = id,
            Severity = severity,
            Message = message,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location)
        };
    }

    public GraphDiagnostic CreateDiagnostic(string filePath, int? line, string id, string severity, string message)
    {
        return new GraphDiagnostic
        {
            Id = id,
            Severity = severity,
            Message = message,
            SourceFile = SourcePath.RelativeFile(filePath, _rootDirectory),
            SourceLocation = line is null ? null : $"L{line}"
        };
    }
}
