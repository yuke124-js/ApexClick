using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using ApexClick.FeaturePack.Audio;
using ApexClick.FeaturePack.Settings;
using ApexClick.FeaturePack.Hotkeys;

namespace ApexClick.Views.Settings;

public partial class SettingsWindow : Window
{
    private readonly UserSettingsService _service;
    private readonly HotkeySettingsStore _hotkeys;
    private readonly UserSettings _working;
    private readonly List<HotkeyBinding> _workingHotkeys;
    public ObservableCollection<AudioInputDevice> AudioDevices { get; } = new();

    public SettingsWindow(UserSettingsService service, AudioDeviceService audio, HotkeySettingsStore hotkeys)
    {
        InitializeComponent();
        _service = service;
        _hotkeys = hotkeys;
        _working = Clone(service.Current);
        _workingHotkeys = hotkeys.Load().ToList();
        _working.AI.ApiKey = service.Current.AI.ApiKey;
        DataContext = _working;

        foreach (var device in audio.GetInputDevices())
            AudioDevices.Add(device);

        ApiKeyBox.Password = _working.AI.ApiKey;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _working.AI.ApiKey = ApiKeyBox.Password;
        _service.Save(_working);
        _hotkeys.Save(_workingHotkeys);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnResetHotkeys(object sender, RoutedEventArgs e)
    {
        _workingHotkeys.Clear();
        _workingHotkeys.Add(new HotkeyBinding("Record", 0, 0x75, "F6"));
        _workingHotkeys.Add(new HotkeyBinding("Play", 0, 0x74, "F5"));
    }

    private static UserSettings Clone(UserSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json)
            ?? new UserSettings();
    }
}
