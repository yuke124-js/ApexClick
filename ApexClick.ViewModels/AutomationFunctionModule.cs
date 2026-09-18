using ApexClick.Contracts;

namespace ApexClick.ViewModels;

public sealed class AutomationFunctionModule : IFunctionModule
{
    public AutomationFunctionModule(MainViewModel mainViewModel)
    {
        ContentViewModel = mainViewModel;
    }

    public string Id => "automation";
    public string DisplayName => "Автоматизация";

    public string IconGlyph => "▶";

    public int SortOrder => 0;
    public object ContentViewModel { get; }
}
