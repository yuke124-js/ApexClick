using System.Windows;
using ApexClick.Services.Debugger;
using ApexClick.ViewModels;

namespace ApexClick.Views;

public partial class DebuggerWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IScriptDebugger _debugger;

    public DebuggerWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        _debugger = main.DebuggerService;
        Refresh();
    }

    private void OnStepGraph(object sender, RoutedEventArgs e)
    {
        if (_main.SelectedScript is null) return;
        _debugger.LoadGraph(_main.SelectedScript);
        _debugger.StepGraph();
        Refresh();
    }

    private void OnStepEvent(object sender, RoutedEventArgs e)
    {
        if (_main.SelectedScript is null) return;
        if (_debugger.Current is null) _debugger.Load(_main.SelectedScript);
        _debugger.Step();
        Refresh();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _debugger.Reset();
        Refresh();
    }

    private void Refresh()
    {
        EventList.Items.Clear();
        if (_main.SelectedScript is { } script)
        {
            foreach (var evt in script.Events.Take(250))
                EventList.Items.Add($"{evt.TimestampMs,7} ms   {evt.Type,-24}  {evt.Id:N}");
        }

        CurrentNodeText.Text = _debugger.CurrentGraphNode?.DisplayType ?? "—";
        VariableList.Items.Clear();
        foreach (var pair in _debugger.Variables)
            VariableList.Items.Add($"{pair.Key} = {pair.Value}");

        DebugText.Text = _debugger.Current is { } current
            ? $"Событие: {current.Type}, t={current.TimestampMs} ms"
            : "Готов к шагу.";
    }
}
