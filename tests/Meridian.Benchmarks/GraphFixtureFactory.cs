using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Benchmarks;

internal static class GraphFixtureFactory
{
    public static GraphDocument CreateSmallFlowGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "method:Sample:Start.Run()", "Start.Run", "Sample.Start.Run()");
        AddNode(builder, "method:Sample:Middle.Run()", "Middle.Run", "Sample.Middle.Run()");
        AddNode(builder, "method:Sample:End.Run()", "End.Run", "Sample.End.Run()");
        AddNode(builder, "type:Sample:IService", "IService", "Sample.IService", GraphNodeKinds.Type, new() { ["type_kind"] = "interface" });
        AddNode(builder, "type:Sample:Service", "Service", "Sample.Service", GraphNodeKinds.Type);
        AddEdge(builder, "method:Sample:Start.Run()", "method:Sample:Middle.Run()", GraphRelations.Calls, 10, "Start calls Middle.");
        AddEdge(builder, "method:Sample:Middle.Run()", "method:Sample:End.Run()", GraphRelations.Calls, 20, "Middle calls End.");
        AddEdge(builder, "type:Sample:IService", "type:Sample:Service", GraphRelations.RegisteredAs, 30, "DI registration.");
        return builder.Build(".");
    }

    public static GraphDocument CreateBenchmarkPlanningGraph(int componentCount = 12, int methodsPerComponent = 8, bool duplicateEvidence = true)
    {
        var builder = new GraphBuilder();
        for (var component = 0; component < componentCount; component++)
        {
            var interfaceId = $"type:Sample:IComponent{component}";
            var serviceId = $"type:Sample:Component{component}";
            var dependencyId = $"type:Sample:Dependency{component}";
            AddNode(builder, interfaceId, $"IComponent{component}", $"Sample.IComponent{component}", GraphNodeKinds.Type, new() { ["type_kind"] = "interface" });
            AddNode(builder, serviceId, $"Component{component}", $"Sample.Component{component}", GraphNodeKinds.Type);
            AddNode(builder, dependencyId, $"Dependency{component}", $"Sample.Dependency{component}", GraphNodeKinds.Type);
            AddEdge(builder, interfaceId, serviceId, GraphRelations.ImplementedBy, 1, "Implementation edge.");
            AddEdge(builder, serviceId, dependencyId, GraphRelations.Uses, 2, "Dependency use.");

            for (var method = 0; method < methodsPerComponent; method++)
            {
                var methodId = $"method:Sample:Component{component}.Operation{method}()";
                AddNode(builder, methodId, $"Component{component}.Operation{method}", $"Sample.Component{component}.Operation{method}()");
                AddEdge(builder, serviceId, methodId, GraphRelations.Contains, method + 3, "Type contains method.");
                if (method > 0)
                {
                    var previousMethodId = $"method:Sample:Component{component}.Operation{method - 1}()";
                    AddEdge(builder, previousMethodId, methodId, GraphRelations.Calls, method + 20, "Operation calls next operation.");
                    if (duplicateEvidence)
                    {
                        AddEdge(builder, previousMethodId, methodId, GraphRelations.Calls, method + 120, "Duplicate evidence for same structural call.");
                    }
                }
            }
        }

        return builder.Build(".");
    }

    public static GraphDocument CreateBenchmarkContainsNoiseGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "type:Sample:Service", "Service", "Sample.Service", GraphNodeKinds.Type);
        AddNode(builder, "type:Sample:ZDependency", "ZDependency", "Sample.ZDependency", GraphNodeKinds.Type);
        for (var i = 0; i < 20; i++)
        {
            var methodId = $"method:Sample:Service.Method{i}()";
            AddNode(builder, methodId, $"Service.Method{i}", $"Sample.Service.Method{i}()");
            AddEdge(builder, "type:Sample:Service", methodId, GraphRelations.Contains, i + 1, $"Service contains method {i}.");
        }

        AddEdge(builder, "type:Sample:Service", "type:Sample:ZDependency", GraphRelations.Injects, 50, "Service injects dependency.");
        return builder.Build(".");
    }

    public static GraphDocument CreateBenchmarkDiagnosticHeavyGraph()
    {
        var graph = CreateBenchmarkPlanningGraph(componentCount: 4, methodsPerComponent: 4, duplicateEvidence: false);
        var diagnostics = new List<GraphDiagnostic>();
        for (var i = 0; i < 30; i++)
        {
            diagnostics.Add(new GraphDiagnostic
            {
                Id = "MERIDIAN_AXAML_BINDING_UNSUPPORTED",
                Severity = "info",
                Message = $"Unsupported binding pattern {i % 5}.",
                SourceFile = $"Views/View{i % 4}.axaml",
                SourceLocation = $"L{i + 1}"
            });
        }

        for (var i = 0; i < 12; i++)
        {
            diagnostics.Add(new GraphDiagnostic
            {
                Id = "MERIDIAN_AXAML_BINDING_UNRESOLVED",
                Severity = "info",
                Message = $"Unresolved binding {i % 3}.",
                SourceFile = $"Views/View{i % 2}.axaml",
                SourceLocation = $"L{i + 40}"
            });
        }

        diagnostics.Add(new GraphDiagnostic { Id = "MERIDIAN_WORKSPACE", Severity = "warning", Message = "Workspace restore warning." });
        return graph with { Diagnostics = diagnostics };
    }

    public static GraphDocument CreateBenchmarkUiBindingHeavyGraph(int bindingCount = 80)
    {
        var builder = new GraphBuilder();
        AddNode(builder, "type:Sample:SettingsView", "SettingsView", "Sample.SettingsView", GraphNodeKinds.Type);
        AddNode(builder, "type:Sample:SettingsViewModel", "SettingsViewModel", "Sample.SettingsViewModel", GraphNodeKinds.Type);
        AddNode(builder, "type:Sample:SettingsService", "SettingsService", "Sample.SettingsService", GraphNodeKinds.Type);
        AddEdge(builder, "type:Sample:SettingsViewModel", "type:Sample:SettingsService", GraphRelations.Uses, 1, "View model uses service.");
        AddEdge(builder, "type:Sample:SettingsView", "type:Sample:SettingsViewModel", GraphRelations.Uses, 2, "View uses view model.");
        for (var i = 0; i < bindingCount; i++)
        {
            var propertyId = $"property:Sample:SettingsViewModel.Property{i}";
            AddNode(builder, propertyId, $"SettingsViewModel.Property{i}", $"Sample.SettingsViewModel.Property{i}", GraphNodeKinds.Property);
            AddEdge(builder, "type:Sample:SettingsView", propertyId, GraphRelations.BindsTo, i + 10, $"View binds to property {i}.");
        }

        return builder.Build(".");
    }

    private static void AddNode(
        GraphBuilder builder,
        string id,
        string label,
        string symbol,
        string kind = GraphNodeKinds.Method,
        SortedDictionary<string, string>? metadata = null)
    {
        builder.AddNode(new GraphNode
        {
            Id = id,
            Label = label,
            Kind = kind,
            Symbol = symbol,
            SourceFile = "BenchmarkFixture.cs",
            SourceLocation = "L1",
            Metadata = metadata ?? new SortedDictionary<string, string>(StringComparer.Ordinal)
        });
    }

    private static void AddEdge(GraphBuilder builder, string source, string target, string relation, int line, string reason)
    {
        builder.AddEdge(new GraphEdge
        {
            Source = source,
            Target = target,
            Relation = relation,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1,
            Evidence = new GraphEvidence { File = "BenchmarkFixture.cs", Line = line, Reason = reason }
        });
    }
}
