namespace Sample.BasicCalls;

public sealed class OrderController
{
    public string Get()
    {
        var service = new OrderService();
        return service.Load();
    }
}

public sealed class OrderService
{
    public string Load()
    {
        return "loaded";
    }
}
