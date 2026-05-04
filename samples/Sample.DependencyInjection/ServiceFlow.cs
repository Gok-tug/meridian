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

public interface IClock
{
    DateTimeOffset Now();
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now()
    {
        return DateTimeOffset.UtcNow;
    }
}

public sealed class ClockFactory
{
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
        services.AddSingleton<IClock>(_ => new SystemClock());
        services.AddSingleton<ClockFactory>(_ =>
        {
            return new ClockFactory();
        });
        services.AddTransient<OrderService>();
    }
}
