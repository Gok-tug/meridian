namespace Microsoft.EntityFrameworkCore;

public abstract class DbContext
{
    public DbSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new DbSet<TEntity>();
    }

    public void Update<TEntity>(TEntity entity) where TEntity : class
    {
    }

    public int SaveChanges()
    {
        return 0;
    }
}

public sealed class DbSet<TEntity> where TEntity : class
{
    public List<TEntity> ToList()
    {
        return [];
    }

    public Task<bool> AnyAsync()
    {
        return Task.FromResult(false);
    }

    public void Add(TEntity entity)
    {
    }

    public void Remove(TEntity entity)
    {
    }
}
