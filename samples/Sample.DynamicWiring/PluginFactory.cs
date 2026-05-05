namespace Sample.DynamicWiring;

public sealed class EmailPlugin
{
}

public sealed class ReportPlugin
{
}

public sealed class RuntimePluginFactory
{
    public Type StaticTypeReference()
    {
        return typeof(EmailPlugin);
    }

    public object? CreateGeneric()
    {
        return Activator.CreateInstance<EmailPlugin>();
    }

    public object? CreateFromTypeOf()
    {
        return Activator.CreateInstance(typeof(ReportPlugin));
    }

    public object? CreateNamedTypeArgument()
    {
        return Activator.CreateInstance(args: null, type: typeof(ReportPlugin));
    }

    public object? CreateRuntime(Type pluginType)
    {
        return Activator.CreateInstance(pluginType);
    }

    public object? CreateGenericRuntime<T>() where T : class
    {
        return Activator.CreateInstance<T>();
    }
}
