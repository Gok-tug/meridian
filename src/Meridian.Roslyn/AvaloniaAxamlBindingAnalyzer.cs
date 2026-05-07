using System.Xml;
using System.Xml.Linq;
using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class AvaloniaAxamlBindingAnalyzer
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const int MaxDiagnosticsPerFile = 25;

    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public AvaloniaAxamlBindingAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public void AnalyzeProject(Project project, Compilation compilation, GraphBuilder graph, CancellationToken cancellationToken)
    {
        if (project.FilePath is null || Path.GetDirectoryName(project.FilePath) is not { Length: > 0 } projectDirectory)
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.axaml", SearchOption.AllDirectories)
            .Where(_sourceFilter.IsAnalyzableFilePath)
            .OrderBy(SourcePath.Normalize, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyzeFile(filePath, compilation, graph, cancellationToken);
        }
    }

    private void AnalyzeFile(string filePath, Compilation compilation, GraphBuilder graph, CancellationToken cancellationToken)
    {
        var diagnostics = new FileDiagnosticSink(filePath, _graphFactory, graph);
        XDocument document;
        try
        {
            document = XDocument.Load(filePath, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            diagnostics.Add("MERIDIAN_AXAML_PARSE", "warning", exception.LineNumber == 0 ? null : exception.LineNumber, $"AXAML file could not be parsed: {exception.Message}");
            return;
        }
        catch (IOException exception)
        {
            diagnostics.Add("MERIDIAN_AXAML_PARSE", "warning", null, $"AXAML file could not be read: {exception.Message}");
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add("MERIDIAN_AXAML_PARSE", "warning", null, $"AXAML file could not be read: {exception.Message}");
            return;
        }

        if (document.Root is null)
        {
            return;
        }

        var namespaces = AxamlNamespaceMap.From(document.Root);
        var viewType = ResolveTypeFromAttribute(document.Root, "Class", compilation, namespaces, diagnostics);

        Walk(document.Root, new BindingContext(viewType, null, null, null), compilation, diagnostics, graph, cancellationToken);
    }

    private void Walk(
        XElement element,
        BindingContext inheritedContext,
        Compilation compilation,
        FileDiagnosticSink diagnostics,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var namespaces = AxamlNamespaceMap.From(element);
        var context = CreateElementContext(element, inheritedContext, compilation, namespaces, diagnostics, graph);
        foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal))
        {
            AnalyzeBindingAttribute(attribute, element, context, compilation, namespaces, diagnostics, graph);
        }

        foreach (var child in element.Elements().OrderBy(child => Line(child) ?? int.MaxValue).ThenBy(child => child.Name.ToString(), StringComparer.Ordinal))
        {
            Walk(child, context, compilation, diagnostics, graph, cancellationToken);
        }
    }

    private BindingContext CreateElementContext(
        XElement element,
        BindingContext inheritedContext,
        Compilation compilation,
        AxamlNamespaceMap namespaces,
        FileDiagnosticSink diagnostics,
        GraphBuilder graph)
    {
        var viewType = inheritedContext.ViewType;
        if (ResolveTypeFromAttribute(element, "Class", compilation, namespaces, diagnostics) is { } elementViewType)
        {
            viewType = elementViewType;
        }

        var scopeType = inheritedContext.ScopeType;
        var scopeKind = inheritedContext.ScopeKind;
        var scopeAttribute = inheritedContext.ScopeAttribute;

        if (TryResolveDataTypeScope(element, compilation, namespaces, diagnostics, out var dataType, out var dataTypeAttribute, out var dataTypeScopeKind))
        {
            scopeType = dataType;
            scopeKind = dataTypeScopeKind;
            scopeAttribute = dataTypeAttribute;
            if (viewType is { } sourceViewType)
            {
                AddTypeBindingEdge(sourceViewType, dataType, dataTypeAttribute, diagnostics.FilePath, scopeKind, graph);
            }
        }

        if (IsDataTemplate(element) && TryResolveTemplateViewType(element, compilation, namespaces, out var templateViewType) && scopeType is not null && scopeAttribute is not null)
        {
            AddTypeBindingEdge(templateViewType, scopeType, scopeAttribute, diagnostics.FilePath, "data_template", graph);
        }

        return new BindingContext(viewType, scopeType, scopeKind, scopeAttribute);
    }

    private void AnalyzeBindingAttribute(
        XAttribute attribute,
        XElement element,
        BindingContext context,
        Compilation compilation,
        AxamlNamespaceMap namespaces,
        FileDiagnosticSink diagnostics,
        GraphBuilder graph)
    {
        if (!BindingExpression.TryParse(attribute.Value, out var binding))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(binding.Path))
        {
            return;
        }

        var bindingPath = binding.Path.Trim();
        if (TryResolveTypedDataContextCast(bindingPath, compilation, namespaces, out var castScopeType, out var castPath))
        {
            ResolveAndAddBinding(attribute, element, context with
            {
                ScopeType = castScopeType,
                ScopeKind = "typed_datacontext_cast",
                ScopeAttribute = attribute
            }, binding with { Path = castPath }, graph, diagnostics);
            return;
        }

        if (binding.HasUnsupportedRuntimeSource || IsUnsupportedPath(bindingPath))
        {
            diagnostics.Add("MERIDIAN_AXAML_BINDING_UNSUPPORTED", "info", Line(attribute), $"AXAML binding '{bindingPath}' uses unsupported runtime or control-tree binding features.");
            return;
        }

        if (context.ScopeType is null)
        {
            diagnostics.AddUnscopedBinding(Line(attribute));
            return;
        }

        ResolveAndAddBinding(attribute, element, context, binding, graph, diagnostics);
    }

    private void ResolveAndAddBinding(
        XAttribute attribute,
        XElement element,
        BindingContext context,
        BindingExpression binding,
        GraphBuilder graph,
        FileDiagnosticSink diagnostics)
    {
        if (context.ViewType is null || context.ScopeType is null)
        {
            diagnostics.AddUnscopedBinding(Line(attribute));
            return;
        }

        var segment = FirstPathSegment(binding.Path);
        if (string.IsNullOrWhiteSpace(segment))
        {
            return;
        }

        var commandPreferred = attribute.Name.LocalName.Equals("Command", StringComparison.OrdinalIgnoreCase) || segment.EndsWith("Command", StringComparison.Ordinal);
        var targetNode = ResolveTargetNode(context.ScopeType, segment, commandPreferred);
        if (targetNode is null)
        {
            diagnostics.Add("MERIDIAN_AXAML_BINDING_UNRESOLVED", "info", Line(attribute), $"AXAML binding '{binding.Path}' could not be resolved on '{context.ScopeType.ToDisplayString(SymbolDisplay.TypeFormat)}'.");
            return;
        }

        var sourceNode = _graphFactory.CreateTypeNodeAllowingMissingSource(context.ViewType);
        graph.AddNode(sourceNode);
        graph.AddNode(targetNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = targetNode.Id,
            Relation = GraphRelations.BindsTo,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                diagnostics.FilePath,
                Line(attribute),
                context.ViewType.ToDisplayString(SymbolDisplay.TypeFormat),
                $"AXAML {binding.Kind} binding '{binding.Path}' on '{attribute.Name.LocalName}' resolved to '{targetNode.Symbol}'."),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["axaml_element"] = element.Name.LocalName,
                ["binding_attribute"] = attribute.Name.LocalName,
                ["binding_kind"] = binding.Kind,
                ["binding_path"] = binding.Path,
                ["binding_scope"] = context.ScopeKind ?? "unknown",
                ["binding_scope_type"] = context.ScopeType.ToDisplayString(SymbolDisplay.TypeFormat),
                ["has_converter"] = binding.HasConverter ? "true" : "false",
                ["resolved_path_segment"] = segment
            }
        });
    }

    private GraphNode? ResolveTargetNode(INamedTypeSymbol scopeType, string segment, bool commandPreferred)
    {
        if (commandPreferred)
        {
            if (ResolveSourceProperty(scopeType, segment) is { } commandProperty)
            {
                return _graphFactory.CreatePropertyNode(commandProperty);
            }

            if (CommunityToolkitMvvmGeneratedMemberResolver.FindRelayCommandMethod(scopeType, segment, _sourceFilter) is { } commandMethod)
            {
                return _graphFactory.CreateMvvmCommandNode(
                    commandMethod,
                    segment,
                    CommunityToolkitMvvmGeneratedMemberResolver.IsAsyncCommand(commandMethod));
            }
        }

        if (ResolveSourceProperty(scopeType, segment) is { } property)
        {
            return _graphFactory.CreatePropertyNode(property);
        }

        if (CommunityToolkitMvvmGeneratedMemberResolver.FindObservablePropertyBackingField(scopeType, segment, _sourceFilter) is { } backingField)
        {
            return _graphFactory.CreateGeneratedPropertyNode(backingField, segment);
        }

        if (!commandPreferred && CommunityToolkitMvvmGeneratedMemberResolver.FindRelayCommandMethod(scopeType, segment, _sourceFilter) is { } relayCommandMethod)
        {
            return _graphFactory.CreateMvvmCommandNode(
                relayCommandMethod,
                segment,
                CommunityToolkitMvvmGeneratedMemberResolver.IsAsyncCommand(relayCommandMethod));
        }

        return null;
    }

    private IPropertySymbol? ResolveSourceProperty(INamedTypeSymbol scopeType, string propertyName)
    {
        return scopeType.GetMembers(propertyName)
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsImplicitlyDeclared &&
                !property.IsStatic &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod?.DeclaredAccessibility == Accessibility.Public &&
                _sourceFilter.HasAnalyzableSourceLocation(property))
            .OrderBy(property => property.ToDisplayString(SymbolDisplay.MemberFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void AddTypeBindingEdge(INamedTypeSymbol viewType, INamedTypeSymbol targetType, XObject evidenceObject, string filePath, string scopeKind, GraphBuilder graph)
    {
        var sourceNode = _graphFactory.CreateTypeNodeAllowingMissingSource(viewType);
        var targetNode = _graphFactory.CreateTypeNodeAllowingMissingSource(targetType);
        graph.AddNode(sourceNode);
        graph.AddNode(targetNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = targetNode.Id,
            Relation = GraphRelations.BindsTo,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                filePath,
                Line(evidenceObject),
                viewType.ToDisplayString(SymbolDisplay.TypeFormat),
                $"AXAML static binding scope '{scopeKind}' connects view '{viewType.ToDisplayString(SymbolDisplay.TypeFormat)}' to '{targetType.ToDisplayString(SymbolDisplay.TypeFormat)}'."),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["binding_scope"] = scopeKind,
                ["binding_scope_type"] = targetType.ToDisplayString(SymbolDisplay.TypeFormat)
            }
        });
    }

    private bool TryResolveDataTypeScope(
        XElement element,
        Compilation compilation,
        AxamlNamespaceMap namespaces,
        FileDiagnosticSink diagnostics,
        out INamedTypeSymbol dataType,
        out XAttribute dataTypeAttribute,
        out string scopeKind)
    {
        dataType = null!;
        dataTypeAttribute = null!;
        scopeKind = string.Empty;

        var xDataTypeAttribute = element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("DataType", StringComparison.Ordinal) && attribute.Name.NamespaceName.Equals(XamlNamespace, StringComparison.Ordinal));
        if (xDataTypeAttribute is not null)
        {
            if (TryResolveTypeReference(xDataTypeAttribute.Value, compilation, namespaces, out dataType))
            {
                dataTypeAttribute = xDataTypeAttribute;
                scopeKind = IsDataTemplate(element) ? "data_template" : "x_data_type";
                return true;
            }

            diagnostics.Add("MERIDIAN_AXAML_TYPE_UNRESOLVED", "info", Line(xDataTypeAttribute), $"AXAML x:DataType target '{xDataTypeAttribute.Value}' could not be resolved.");
        }

        if (IsDataTemplate(element) && element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals("DataType", StringComparison.Ordinal) && string.IsNullOrEmpty(attribute.Name.NamespaceName)) is { } templateDataTypeAttribute)
        {
            if (TryResolveTypeReference(templateDataTypeAttribute.Value, compilation, namespaces, out dataType))
            {
                dataTypeAttribute = templateDataTypeAttribute;
                scopeKind = "data_template";
                return true;
            }

            diagnostics.Add("MERIDIAN_AXAML_TYPE_UNRESOLVED", "info", Line(templateDataTypeAttribute), $"AXAML DataTemplate DataType target '{templateDataTypeAttribute.Value}' could not be resolved.");
        }

        return false;
    }

    private INamedTypeSymbol? ResolveTypeFromAttribute(XElement element, string localName, Compilation compilation, AxamlNamespaceMap namespaces, FileDiagnosticSink diagnostics)
    {
        if (GetXamlAttribute(element, localName) is not { } attribute)
        {
            return null;
        }

        if (TryResolveTypeReference(attribute.Value, compilation, namespaces, out var typeSymbol))
        {
            return typeSymbol;
        }

        diagnostics.Add("MERIDIAN_AXAML_TYPE_UNRESOLVED", "info", Line(attribute), $"AXAML {localName} target '{attribute.Value}' could not be resolved.");
        return null;
    }

    private static XAttribute? GetXamlAttribute(XElement element, string localName)
    {
        return element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(localName, StringComparison.Ordinal) &&
            attribute.Name.NamespaceName.Equals(XamlNamespace, StringComparison.Ordinal));
    }

    private static bool TryResolveTemplateViewType(XElement element, Compilation compilation, AxamlNamespaceMap namespaces, out INamedTypeSymbol viewType)
    {
        viewType = null!;
        var child = element.Elements().FirstOrDefault(child => child.Name.LocalName.IndexOf('.', StringComparison.Ordinal) < 0);
        if (child is null || !namespaces.TryGetNamespace(child.Name.NamespaceName, out var namespaceName))
        {
            return false;
        }

        return TryResolveMetadataName($"{namespaceName}.{child.Name.LocalName}", compilation, out viewType);
    }

    private static bool TryResolveTypeReference(string value, Compilation compilation, AxamlNamespaceMap namespaces, out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = null!;
        var typeReference = NormalizeTypeReference(value);
        if (string.IsNullOrWhiteSpace(typeReference) || typeReference.Contains('`', StringComparison.Ordinal))
        {
            return false;
        }

        if (typeReference.Contains(':', StringComparison.Ordinal))
        {
            var parts = typeReference.Split(':', 2);
            if (!namespaces.TryGetPrefix(parts[0], out var namespaceName))
            {
                return false;
            }

            return TryResolveMetadataName($"{namespaceName}.{parts[1]}", compilation, out typeSymbol);
        }

        return TryResolveMetadataName(typeReference, compilation, out typeSymbol);
    }

    private static bool TryResolveMetadataName(string metadataName, Compilation compilation, out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = compilation.GetTypeByMetadataName(metadataName) ?? null!;
        return typeSymbol is not null;
    }

    private static string NormalizeTypeReference(string value)
    {
        var typeReference = value.Trim();
        if (typeReference.StartsWith("{x:Type", StringComparison.Ordinal) && typeReference.EndsWith('}'))
        {
            typeReference = typeReference[7..^1].Trim();
        }

        if (typeReference.StartsWith("Type=", StringComparison.Ordinal))
        {
            typeReference = typeReference[5..].Trim();
        }

        return typeReference;
    }

    private static bool TryResolveTypedDataContextCast(string path, Compilation compilation, AxamlNamespaceMap namespaces, out INamedTypeSymbol scopeType, out string castPath)
    {
        scopeType = null!;
        castPath = string.Empty;
        var castStart = path.IndexOf("((", StringComparison.Ordinal);
        const string dataContextMarker = ")DataContext)";
        var dataContextIndex = path.IndexOf(dataContextMarker, StringComparison.Ordinal);
        if (castStart < 0 || dataContextIndex <= castStart)
        {
            return false;
        }

        var typeReference = path[(castStart + 2)..dataContextIndex];
        var remaining = path[(dataContextIndex + dataContextMarker.Length)..].TrimStart('.');
        if (string.IsNullOrWhiteSpace(remaining))
        {
            return false;
        }

        if (!TryResolveTypeReference(typeReference, compilation, namespaces, out scopeType))
        {
            return false;
        }

        castPath = remaining;
        return true;
    }

    private static string FirstPathSegment(string path)
    {
        var trimmed = path.Trim().TrimStart('!');
        var dotIndex = trimmed.IndexOf('.', StringComparison.Ordinal);
        return dotIndex < 0 ? trimmed : trimmed[..dotIndex];
    }

    private static bool IsUnsupportedPath(string path)
    {
        var trimmed = path.TrimStart();
        return trimmed.StartsWith("$parent", StringComparison.Ordinal) ||
            trimmed.StartsWith("#", StringComparison.Ordinal) ||
            trimmed.Contains('[', StringComparison.Ordinal) ||
            trimmed.Contains(']', StringComparison.Ordinal);
    }

    private static bool IsDataTemplate(XElement element)
    {
        return element.Name.LocalName.Equals("DataTemplate", StringComparison.Ordinal);
    }

    private static int? Line(IXmlLineInfo lineInfo)
    {
        return lineInfo.HasLineInfo() ? lineInfo.LineNumber : null;
    }

    private sealed record BindingContext(
        INamedTypeSymbol? ViewType,
        INamedTypeSymbol? ScopeType,
        string? ScopeKind,
        XAttribute? ScopeAttribute);

    private sealed record BindingExpression(
        string Kind,
        string Path,
        bool HasConverter,
        bool HasUnsupportedRuntimeSource)
    {
        public static bool TryParse(string value, out BindingExpression binding)
        {
            binding = null!;
            var trimmed = value.Trim();
            if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            {
                return false;
            }

            var body = trimmed[1..^1].Trim();
            var kind = body.StartsWith("CompiledBinding", StringComparison.Ordinal) ? "compiled_binding" :
                body.StartsWith("Binding", StringComparison.Ordinal) ? "binding" : null;
            if (kind is null)
            {
                return false;
            }

            var expression = body[(kind == "binding" ? "Binding".Length : "CompiledBinding".Length)..].Trim();
            var parts = SplitTopLevel(expression).ToArray();
            var path = string.Empty;
            var hasConverter = false;
            var hasUnsupportedRuntimeSource = false;
            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
                if (equalsIndex < 0)
                {
                    path = string.IsNullOrWhiteSpace(path) ? part : path;
                    continue;
                }

                var key = part[..equalsIndex].Trim();
                var partValue = part[(equalsIndex + 1)..].Trim();
                if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    path = partValue;
                }
                else if (key.Equals("Converter", StringComparison.OrdinalIgnoreCase))
                {
                    hasConverter = true;
                }
                else if (key.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("RelativeSource", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("ElementName", StringComparison.OrdinalIgnoreCase))
                {
                    hasUnsupportedRuntimeSource = true;
                }
            }

            binding = new BindingExpression(kind, path.Trim(), hasConverter, hasUnsupportedRuntimeSource);
            return true;
        }

        private static IEnumerable<string> SplitTopLevel(string value)
        {
            var start = 0;
            var braceDepth = 0;
            var quote = '\0';
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (quote != '\0')
                {
                    if (current == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }

                if (current == '{')
                {
                    braceDepth++;
                    continue;
                }

                if (current == '}')
                {
                    braceDepth = Math.Max(0, braceDepth - 1);
                    continue;
                }

                if (current == ',' && braceDepth == 0)
                {
                    yield return value[start..i];
                    start = i + 1;
                }
            }

            yield return value[start..];
        }
    }

    private sealed class AxamlNamespaceMap
    {
        private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _namespaces = new(StringComparer.Ordinal);

        private AxamlNamespaceMap()
        {
        }

        public static AxamlNamespaceMap From(XElement element)
        {
            var map = new AxamlNamespaceMap();
            foreach (var attribute in element.AncestorsAndSelf().Reverse().SelectMany(current => current.Attributes()).Where(attribute => attribute.IsNamespaceDeclaration))
            {
                var prefix = attribute.Name.NamespaceName.Equals(XNamespace.Xmlns.NamespaceName, StringComparison.Ordinal)
                    ? attribute.Name.LocalName
                    : string.Empty;
                if (TryParseClrNamespace(attribute.Value, out var namespaceName))
                {
                    if (prefix.Length > 0)
                    {
                        map._prefixes[prefix] = namespaceName;
                    }

                    map._namespaces[attribute.Value] = namespaceName;
                }
            }

            return map;
        }

        public bool TryGetPrefix(string prefix, out string namespaceName)
        {
            return _prefixes.TryGetValue(prefix, out namespaceName!);
        }

        public bool TryGetNamespace(string xmlNamespace, out string namespaceName)
        {
            return _namespaces.TryGetValue(xmlNamespace, out namespaceName!);
        }

        private static bool TryParseClrNamespace(string value, out string namespaceName)
        {
            namespaceName = string.Empty;
            const string usingPrefix = "using:";
            const string clrNamespacePrefix = "clr-namespace:";
            if (value.StartsWith(usingPrefix, StringComparison.Ordinal))
            {
                namespaceName = value[usingPrefix.Length..];
                return namespaceName.Length > 0;
            }

            if (value.StartsWith(clrNamespacePrefix, StringComparison.Ordinal))
            {
                var end = value.IndexOf(';', StringComparison.Ordinal);
                namespaceName = end < 0 ? value[clrNamespacePrefix.Length..] : value[clrNamespacePrefix.Length..end];
                return namespaceName.Length > 0;
            }

            return false;
        }
    }

    private sealed class FileDiagnosticSink
    {
        private readonly RoslynGraphFactory _graphFactory;
        private readonly GraphBuilder _graph;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private bool _reportedUnscopedBinding;
        private int _count;

        public FileDiagnosticSink(string filePath, RoslynGraphFactory graphFactory, GraphBuilder graph)
        {
            FilePath = filePath;
            _graphFactory = graphFactory;
            _graph = graph;
        }

        public string FilePath { get; }

        public void AddUnscopedBinding(int? line)
        {
            if (_reportedUnscopedBinding)
            {
                return;
            }

            _reportedUnscopedBinding = true;
            Add("MERIDIAN_AXAML_BINDING_UNSCOPED", "info", line, "AXAML bindings without a static scope were skipped in this file.");
        }

        public void Add(string id, string severity, int? line, string message)
        {
            if (_count >= MaxDiagnosticsPerFile)
            {
                return;
            }

            var key = string.Join('', id, line?.ToString("D10") ?? string.Empty, message);
            if (!_seen.Add(key))
            {
                return;
            }

            _count++;
            _graph.AddDiagnostic(_graphFactory.CreateDiagnostic(FilePath, line, id, severity, message));
        }
    }
}
