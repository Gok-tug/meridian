using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sample.MvvmFlow;

public sealed partial class RecordingViewModel
{
    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string? selectedMacroName;

    [RelayCommand]
    private void ToggleRecording()
    {
        _isRecording = !_isRecording;
        selectedMacroName ??= nameof(RecordingViewModel);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await Task.CompletedTask;
    }

    [RelayCommand]
    private Task OnExportAsync()
    {
        selectedMacroName ??= "export";
        return Task.CompletedTask;
    }
}
