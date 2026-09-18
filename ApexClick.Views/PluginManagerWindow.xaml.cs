using System.Windows;
using ApexClick.Services.Plugins;
using ApexClick.ViewModels;

namespace ApexClick.Views;

public partial class PluginManagerWindow : Window
{
    private readonly PluginRegistry _registry;

    public PluginManagerWindow(MainViewModel main)
    {
        InitializeComponent();
        _registry = main.PluginRegistry;
        Refresh();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        PluginList.ItemsSource = _registry.GetInfo();
        StatusText.Text = $"Загружено: {_registry.Items.Count}";
    }
}
