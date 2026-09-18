using System.Windows;
using System.Windows.Controls;
using ApexClick.ViewModels;
using ApexClick.FeaturePack.Settings;
using ApexClick.FeaturePack.Audio;

namespace ApexClick.Views;

public partial class AutomationModuleView : UserControl
{
    public AutomationModuleView()
    {
        InitializeComponent();
    }

    private void OnGraphNodeEditRequested(object? sender,ApexClick.FeaturePack.UI.NodeEditRequestedEventArgs e)
    {
        if(DataContext is MainViewModel vm){ var w=new NodePropertiesWindow(vm.Editor,e.NodeId){Owner=Window.GetWindow(this)}; w.ShowDialog(); }
    }

    private void OnGraphEdgeKindChanged(object? sender, ApexClick.FeaturePack.UI.EdgeKindChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Editor.SetEdgeKind(e.EdgeId,e.Kind);
    }

    private void OnGraphNodePositionChanged(object? sender, ApexClick.FeaturePack.UI.NodePositionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Editor.SetNodePosition(e.NodeId, e.X, e.Y);
    }

    private void OnOpenDebuggerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedScript is null) return;
        new DebuggerWindow(vm) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void OnOpenPluginsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            new PluginManagerWindow(vm) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        new Settings.SettingsWindow(vm.UserSettingsService, new AudioDeviceService(), vm.HotkeySettingsStore)
        { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void OnOpenEditorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedScript is null)
        {
            MessageBox.Show("Сначала выберите сценарий в библиотеке.", "ApexClick");
            return;
        }

        vm.PrepareEditor();
        var editor = new NodeEditorWindow(vm.Editor, vm.UserSettingsService, vm.SaveEditorChangesAsync)
        { Owner = Window.GetWindow(this) };
        editor.ShowDialog();
    }
}
