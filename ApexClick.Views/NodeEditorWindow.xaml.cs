using System;
using System.Linq;
using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ApexClick.Models;
using ApexClick.ViewModels;
using ApexClick.FeaturePack.Settings;

namespace ApexClick.Views;

public partial class NodeEditorWindow : Window
{
    private readonly EditorViewModel _viewModel;
    private readonly Func<Task<bool>>? _saveAsync;
    private bool _allowClose;

    public NodeEditorWindow(EditorViewModel viewModel, UserSettingsService? settings = null, Func<Task<bool>>? saveAsync = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _saveAsync = saveAsync;
        Closing += OnClosing;
        DataContext = viewModel;
        AllowDrop = true;
        if (settings is not null)
        {
            GraphCanvas.SnapToGrid = settings.Current.Graph.SnapToGrid;
            GraphCanvas.GridSize = settings.Current.Graph.GridSize;
            GraphCanvas.Zoom = settings.Current.Graph.Zoom;
        }
        ReactiveGrid.IsActive = false;
    }

    private void OnNodePositionChanged(object? sender, ApexClick.FeaturePack.UI.NodePositionChangedEventArgs e)
        => _viewModel.SetNodePosition(e.NodeId, e.X, e.Y);

    private void OnNodeSelectionChanged(object? sender, ApexClick.FeaturePack.UI.NodeSelectionChangedEventArgs e)
    {
        if (e.NodeId is not null) _viewModel.SelectNode(e.NodeId);
    }

    private void OnNodeEditRequested(object? sender, ApexClick.FeaturePack.UI.NodeEditRequestedEventArgs e)
        => new NodePropertiesWindow(_viewModel, e.NodeId) { Owner = this }.ShowDialog();

    private void OnEdgeKindChanged(object? sender, ApexClick.FeaturePack.UI.EdgeKindChangedEventArgs e)
        => _viewModel.SetEdgeKind(e.EdgeId, e.Kind);

    private void OnOpenImageLibrary(object sender, RoutedEventArgs e) => new ImageTemplateLibraryWindow(_viewModel) { Owner = this }.ShowDialog();

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();

    private void OnAddMouseMove(object sender, RoutedEventArgs e) => _viewModel.AddAction(MacroEventType.MouseMove);
    private void OnAddClick(object sender, RoutedEventArgs e) => _viewModel.AddClick(MouseVirtualKeys.Left);
    private void OnAddDoubleClick(object sender, RoutedEventArgs e) => _viewModel.AddDoubleClick(MouseVirtualKeys.Left);
    private void OnAddRightClick(object sender, RoutedEventArgs e) => _viewModel.AddClick(MouseVirtualKeys.Right);
    private void OnAddWheel(object sender, RoutedEventArgs e) => _viewModel.AddWheel(120);
    private void OnAddKey(object sender, RoutedEventArgs e) => _viewModel.AddKeyPress(0x41);
    private void OnAddText(object sender, RoutedEventArgs e) => _viewModel.AddText("Текст");
    private void OnAddWait(object sender, RoutedEventArgs e) => _viewModel.AddWait(1000);
    private void OnAddCondition(object sender, RoutedEventArgs e) => _viewModel.AddLogicNode(MacroEventType.ConditionScreenTemplate);
    private void OnAddPixel(object sender, RoutedEventArgs e) => _viewModel.AddLogicNode(MacroEventType.ConditionScreenPixel);
    private void OnAddOcr(object sender, RoutedEventArgs e) => _viewModel.AddLogicNode(MacroEventType.ConditionOcrText);
    private void OnAddPhotoTrigger(object sender, RoutedEventArgs e) => OnBrowseImages(sender, e);
    private void OnAddLoop(object sender, RoutedEventArgs e) => _viewModel.AddLogicNode(MacroEventType.Loop);
    private void OnAddVariable(object sender, RoutedEventArgs e) => _viewModel.AddLogicNode(MacroEventType.Variable);
    private void OnAddSubScenario(object sender, RoutedEventArgs e) => _viewModel.AddLogicNode(MacroEventType.SubScenarioCall);

    private void OnAddEdge(object sender, RoutedEventArgs e)
    {
        if (SourceNodeBox.SelectedItem is not string source ||
            TargetNodeBox.SelectedItem is not string target ||
            EdgeKindBox.SelectedItem is not MacroGraphEdgeKind kind) return;
        _viewModel.AddEdge(source, target, kind);
    }

    private void OnRemoveEdge(object sender, RoutedEventArgs e) => _viewModel.RemoveEdge(_viewModel.Graph.Edges.LastOrDefault()?.Id ?? string.Empty);

    private void OnEditSelected(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is { } node)
            new NodePropertiesWindow(_viewModel, node.Id) { Owner = this }.ShowDialog();
    }

    private void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is { } node)
            _viewModel.RemoveNode(node.Id);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.IsDirty) return;

        var result = MessageBox.Show(
            this,
            "В редакторе есть несохранённые изменения. Сохранить их перед выходом?",
            "ApexClick — сохранить изменения",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.No)
        {
            _allowClose = true;
            return;
        }

        e.Cancel = true;
        if (_saveAsync is null)
        {
            _allowClose = true;
            Close();
            return;
        }

        bool saved = await _saveAsync();
        if (saved)
        {
            _allowClose = true;
            Close();
        }
    }

    private async void OnBrowseImages(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames)
        {
            try { await _viewModel.AddImageTriggerFromFileAsync(file); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Image trigger"); }
        }
    }

    private void OnImageDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnImageDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var file in files.Where(File.Exists))
        {
            try { await _viewModel.AddImageTriggerFromFileAsync(file); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Image trigger"); }
        }
    }
}
