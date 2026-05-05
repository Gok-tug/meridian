namespace Microsoft.EntityFrameworkCore;

public abstract class DbContext
{
    public DbSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new DbSet<TEntity>();
    }
}

public sealed class DbSet<TEntity> where TEntity : class
{
    public List<TEntity> ToList()
    {
        return [];
    }

    public void Add(TEntity entity)
    {
    }
}
