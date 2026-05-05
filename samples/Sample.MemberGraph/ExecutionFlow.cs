namespace Sample.MemberGraph;

public enum ExecutionMode
{
    RainbowTable,
    RuntimeSigning,
    Simulation
}

public sealed class MintTask
{
    private bool _prepared;

    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.RainbowTable;

    public int AttemptCount { get; set; }

    public string? LastExecutorName { get; set; }

    public void MarkPrepared()
    {
        _prepared = true;
    }
}

public interface IExecutionStrategy
{
    ExecutionMode Mode { get; }

    void Execute(MintTask task);
}

public sealed class DefaultExecutionStrategy : IExecutionStrategy
{
    public ExecutionMode Mode => ExecutionMode.RuntimeSigning;

    public void Execute(MintTask task)
    {
        task.AttemptCount++;
        task.LastExecutorName = nameof(DefaultExecutionStrategy);
        if (task.ExecutionMode == ExecutionMode.RuntimeSigning)
        {
            task.MarkPrepared();
        }
    }
}

public sealed class ExecutionStrategyRegistry
{
    private readonly Dictionary<ExecutionMode, IExecutionStrategy> _strategies = [];
    private ExecutionMode _lastResolvedMode;

    public void Register(IExecutionStrategy strategy)
    {
        _strategies[strategy.Mode] = strategy;
    }

    public IExecutionStrategy Resolve(ExecutionMode mode)
    {
        _lastResolvedMode = mode;
        return _strategies[mode];
    }
}

public sealed class MintTaskExecutor
{
    private readonly ExecutionStrategyRegistry _registry = new();
    private string? _lastExecutorName;

    public void Execute(MintTask task)
    {
        var mode = task.ExecutionMode;
        var strategy = _registry.Resolve(mode);
        _lastExecutorName = nameof(ExecutionStrategyRegistry);
        strategy.Execute(task);
    }

    public void ChangeMode(MintTask task, ExecutionMode mode)
    {
        task.ExecutionMode = mode;
    }

    public string ExecutionModePropertyName()
    {
        return nameof(MintTask.ExecutionMode);
    }
}
