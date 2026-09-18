using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ApexClick.FeaturePack.Audio;
using ApexClick.FeaturePack.Settings;
using ApexClick.FeaturePack.Hotkeys;

namespace ApexClick.Views.Settings;

public sealed class HotkeyRow : INotifyPropertyChanged
{
    private string _keyDisplay;
    private bool _isCapturing;

    public HotkeyRow(string action, string label, HotkeyBinding binding)
    {
        Action = action;
        Label = label;
        Modifiers = binding.Modifiers;
        VirtualKey = binding.VirtualKey;
        _keyDisplay = binding.Display;
    }

    public string Action { get; }
    public string Label { get; }
    public uint Modifiers { get; private set; }
    public uint VirtualKey { get; private set; }

    public string KeyDisplay
    {
        get => _keyDisplay;
        private set { _keyDisplay = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeyDisplay))); }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set { _isCapturing = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCapturing))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CaptureButtonLabel))); }
    }

    public string CaptureButtonLabel => IsCapturing ? "Нажмите клавишу…" : "Записать";

    public void Apply(uint modifiers, uint virtualKey, string display)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        KeyDisplay = display;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class SettingsWindow : Window
{
    private readonly UserSettingsService _service;
    private readonly HotkeySettingsStore _hotkeys;
    private readonly UserSettings _working;
    private readonly List<HotkeyBinding> _workingHotkeys;
    public ObservableCollection<AudioInputDevice> AudioDevices { get; } = new();
    public ObservableCollection<HotkeyRow> HotkeyRows { get; } = new();

    private static readonly (string Action, string Label)[] HotkeyActions =
    {
        ("Record", "Начать/остановить запись"),
        ("Play", "Воспроизвести"),
        ("Pause", "Пауза / Продолжить"),
        ("Stop", "Стоп"),
    };

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

        foreach (var (action, label) in HotkeyActions)
        {
            var binding = _workingHotkeys.FirstOrDefault(b => b.Action == action)
                ?? new HotkeyBinding(action, 0, 0, "—");
            HotkeyRows.Add(new HotkeyRow(action, label, binding));
        }

        PreviewKeyDown += OnHotkeyCaptureKeyDown;
    }

    private void OnStartCapture(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HotkeyRow row }) return;
        foreach (var other in HotkeyRows) other.IsCapturing = false;
        row.IsCapturing = true;
        Keyboard.Focus(this);
    }

    private void OnHotkeyCaptureKeyDown(object sender, KeyEventArgs e)
    {
        var row = HotkeyRows.FirstOrDefault(r => r.IsCapturing);
        if (row is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            row.IsCapturing = false;
            e.Handled = true;
            return;
        }

        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        string display = FormatDisplay(modifiers, key);

        row.Apply(modifiers, vk, display);
        row.IsCapturing = false;

        var existing = _workingHotkeys.FirstOrDefault(b => b.Action == row.Action);
        if (existing is not null) _workingHotkeys.Remove(existing);
        _workingHotkeys.Add(new HotkeyBinding(row.Action, modifiers, vk, display));

        e.Handled = true;
    }

    private static string FormatDisplay(uint modifiers, Key key)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
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
        _workingHotkeys.Add(new HotkeyBinding("Pause", 0, 0x76, "F7"));
        _workingHotkeys.Add(new HotkeyBinding("Stop", 0, 0x77, "F8"));

        HotkeyRows.Clear();
        foreach (var (action, label) in HotkeyActions)
        {
            var binding = _workingHotkeys.First(b => b.Action == action);
            HotkeyRows.Add(new HotkeyRow(action, label, binding));
        }
    }

    private static UserSettings Clone(UserSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json)
            ?? new UserSettings();
    }
}
