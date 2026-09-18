using ApexClick.Contracts;
using ApexClick.ViewModels;

namespace ApexClick.Launcher;

public sealed class LauncherViewModel : ViewModelBase
{
    private IFunctionModule? _selectedModule;

    public LauncherViewModel(IModuleRegistry registry)
    {
        Modules = registry.Modules;
        SelectedModule = Modules.FirstOrDefault();
    }

    public IReadOnlyList<IFunctionModule> Modules { get; }

    public IFunctionModule? SelectedModule
    {
        get => _selectedModule;
        set => SetField(ref _selectedModule, value);
    }
}
