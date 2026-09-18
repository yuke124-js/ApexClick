using System.Linq;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ApexClick.ViewModels;

namespace ApexClick.Views;

public partial class ImageTemplateLibraryWindow : Window
{
    private readonly EditorViewModel _editor;

    public ImageTemplateLibraryWindow(EditorViewModel editor)
    {
        InitializeComponent();
        _editor = editor;
        Refresh();
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
        };
        if (dialog.ShowDialog(this) != true) return;
        await AddFilesAsync(dialog.FileNames);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await AddFilesAsync(files);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async Task AddFilesAsync(IEnumerable<string> files)
    {
        foreach (var file in files.Where(File.Exists))
        {
            try { await _editor.AddImageTriggerFromFileAsync(file); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Image trigger"); }
        }
        Refresh();
    }

    private void Refresh() => AssetList.ItemsSource = _editor.ImageAssets.ToArray();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
