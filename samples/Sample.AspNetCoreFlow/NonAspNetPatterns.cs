namespace Sample.AspNetCoreFlow;

public sealed class CustomRouteAttribute : Attribute
{
    public CustomRouteAttribute(string template)
    {
    }
}

public sealed class CustomHttpGetAttribute : Attribute
{
    public CustomHttpGetAttribute(string template)
    {
    }
}

[CustomRoute("fake/[controller]")]
public sealed class FakeController
{
    [CustomHttpGet("{id}")]
    public void Read(int id)
    {
    }
}

public static class FakeMapper
{
    public static void MapGet(string route, Delegate handler)
    {
    }
}

public sealed class FakeEndpoint
{
    public void Configure()
    {
        Get("/fake-fastendpoint");
    }

    public void Get(string route)
    {
    }
}

public static class NonAspNetPatterns
{
    public static void MapRoutes()
    {
        FakeMapper.MapGet("/fake-minimal", () => { });
    }
}
