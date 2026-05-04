using Microsoft.Extensions.DependencyInjection;

namespace Sample.DependencyInjection;

public interface IOrderRepository
{
    string Load();
}

public sealed class SqlOrderRepository : IOrderRepository
{
    public string Load()
    {
        return "order";
    }
}

public sealed class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }

    public string Load()
    {
        return _repository.Load();
    }
}

public static class ServiceRegistration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, SqlOrderRepository>();
        services.AddTransient<OrderService>();
    }
}
