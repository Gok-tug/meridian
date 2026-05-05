namespace Sample.EfCore;

public sealed class CatalogQueries
{
    private readonly CatalogDbContext _context;

    public CatalogQueries(CatalogDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<Customer> ListCustomers()
    {
        return _context.Customers.ToList();
    }

    public IReadOnlyList<Order> ListOrders()
    {
        return _context.Set<Order>().ToList();
    }

    public Task<bool> HasCustomersAsync()
    {
        return _context.Customers.AnyAsync();
    }

    public void AddCustomer(Customer customer)
    {
        _context.Customers.Add(customer);
    }

    public void UpdateCustomer(Customer customer)
    {
        _context.Update(customer);
    }

    public void RemoveCustomer(Customer customer)
    {
        _context.Customers.Remove(customer);
    }

    public void SaveChanges()
    {
        _context.SaveChanges();
    }

    public string CustomersMemberName()
    {
        return nameof(CatalogDbContext.Customers);
    }
}
