using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sample.AvaloniaBindings.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string? _searchText;

    public string Title { get; } = "Expansion library";

    public bool IsBusy { get; private set; }

    public IReadOnlyList<TextExpansionItem> Expansions { get; } = Array.Empty<TextExpansionItem>();

    [RelayCommand]
    private Task SaveAsync()
    {
        _searchText ??= Title;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OnRemoveExpansion()
    {
        IsBusy = false;
    }
}

public sealed class TextExpansionItem
{
    public string Name { get; init; } = string.Empty;

    public string Replacement { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }
}
