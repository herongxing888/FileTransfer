using CommunityToolkit.Mvvm.ComponentModel;
using FileTransfer.App.Services;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;

    [ObservableProperty]
    private AppState _state;

    public MainViewModel(IDispatcher dispatcher, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;
    }
}
