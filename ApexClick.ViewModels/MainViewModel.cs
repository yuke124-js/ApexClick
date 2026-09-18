using System.Linq;
using System.Windows;
using System.Collections.ObjectModel;
using System.IO;
using ApexClick.Models;
using ApexClick.Services.Capture;
using ApexClick.Services.AI;
using ApexClick.Services.Licensing;
using ApexClick.Services.Playback;
using ApexClick.Services.Scheduler;
using ApexClick.Services.Vision;
using ApexClick.Services.Debugger;
using ApexClick.Services.Plugins;
using ApexClick.Services.Community;
using ApexClick.FeaturePack.Licensing;
using ApexClick.FeaturePack.Hotkeys;
using ApexClick.FeaturePack.DataDriven;
using ApexClick.FeaturePack.Export;
using ApexClick.FeaturePack.Screen;
using ApexClick.FeaturePack.GraphEngine;
using ApexClick.FeaturePack.Settings;
using ApexClick.FeaturePack.Audio;
using Microsoft.Win32;

namespace ApexClick.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly InputHookService _inputHookService;
    private readonly PlaybackEngine _playbackEngine;
    private readonly IFeatureFlags _featureFlags;
    private readonly ScreenCaptureService _screenCapture;
    private readonly ImageAnalysisService _imageAnalysis;
    private readonly TimerScheduler _scheduler;
    private double _scheduleIntervalMinutes = 5;
    private int _repeatCount = 1;
    private readonly string _scriptsDirectory;
    private CancellationTokenSource? _stallWatchdogCts;
    private Task? _stallWatchdogTask;

    private readonly LicenseService _licenseService;
    private readonly HotkeySettingsStore _hotkeySettingsStore;
    private readonly DataDrivenRunner _dataDrivenRunner;
    private readonly ICommunityClient _communityClient;
    private readonly IScriptDebugger _scriptDebugger;
    private readonly PluginRegistry _pluginRegistry;
    private readonly StandaloneExporter _standaloneExporter;
    private readonly ExecutionGraphInterpreter _graphInterpreter;
    private readonly VirtualScreenCoordinator _virtualScreenCoordinator;
    private readonly OnlineLicenseClient _onlineLicenseClient;
    private readonly PromptInterpreterService _promptInterpreter;
    private readonly UserSettingsService _settingsService;
    private readonly VoiceTriggerService _voiceTrigger;

    private string _licenseKeyInput = "";
    private string _licenseStatusText = "";
    private string _hotkeyStatusText = "";
    private string _communityAuthor = "";
    private string _aiPrompt = "";
    private string _aiStatusText = "";
    private string _communityCategory = ApexClick.Services.Community.CommunityCategories.Allowed.First();
    private string _debuggerStatusText = "Отладчик не запущен.";
    private MacroScript? _debuggerLoadedScript;

    private bool _isRecording;
    private long _recordingPulseToken;
    private bool _isPlaying;
    private bool _isPlaybackPaused;
    private double _playbackSpeed = 1.0;
    private MacroScript? _selectedScript;
    private string _statusMessage = "Готово.";
    private int _recordedStepCount;
    private long _lastRecordingUiUpdateMs;
    private CancellationTokenSource? _playbackCts;

    public MainViewModel(InputHookService inputHookService, PlaybackEngine playbackEngine, IFeatureFlags featureFlags,
        ScreenCaptureService screenCapture, ImageAnalysisService imageAnalysis, TimerScheduler scheduler,
        LicenseService licenseService, HotkeySettingsStore hotkeySettingsStore, DataDrivenRunner dataDrivenRunner,
        ICommunityClient communityClient, IScriptDebugger scriptDebugger, PluginRegistry pluginRegistry,
        StandaloneExporter standaloneExporter, ExecutionGraphInterpreter graphInterpreter,
        VirtualScreenCoordinator virtualScreenCoordinator, OnlineLicenseClient onlineLicenseClient, PromptInterpreterService promptInterpreter, UserSettingsService settingsService,
        VoiceTriggerService voiceTrigger)
    {
        _inputHookService = inputHookService;
        _playbackEngine = playbackEngine;
        _featureFlags = featureFlags;
        _screenCapture = screenCapture;
        _imageAnalysis = imageAnalysis;
        _scheduler = scheduler;
        _scheduler.JobFailed += (_, e) => StatusMessage = $"Плановый запуск не удался: {e.Exception.Message}";

        _licenseService = licenseService;
        _hotkeySettingsStore = hotkeySettingsStore;
        _dataDrivenRunner = dataDrivenRunner;
        _communityClient = communityClient;
        _scriptDebugger = scriptDebugger;
        _pluginRegistry = pluginRegistry;
        _standaloneExporter = standaloneExporter;
        _graphInterpreter = graphInterpreter;
        _virtualScreenCoordinator = virtualScreenCoordinator;
        _onlineLicenseClient = onlineLicenseClient;
        _promptInterpreter = promptInterpreter;
        _settingsService = settingsService;
        _voiceTrigger = voiceTrigger;
        _voiceTrigger.PhraseRecognized += OnVoicePhraseRecognized;
        _voiceTrigger.Error += (_, message) => StatusMessage = $"Voice trigger: {message}";
        _playbackSpeed = _settingsService.Current.Playback.DefaultSpeed;
        _settingsService.Changed += OnSettingsChanged;
        ConfigureAiFromSettings();
        ConfigureEditorFromSettings();

        _scriptsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexClick", "Scripts");
        Directory.CreateDirectory(_scriptsDirectory);

        StartStopRecordingCommand = new RelayCommand(OnStartStopRecording, () => !IsPlaying);
        PlayLastScriptCommand = new RelayCommand(async () => await OnPlayAsync(), () => !IsRecording && !IsPlaying && (SelectedScript ?? ScriptLibrary.LastOrDefault()) is not null);
        StopPlaybackCommand = new RelayCommand(OnStopPlayback, () => IsPlaying);
        PauseResumePlaybackCommand = new RelayCommand(OnPauseResumePlayback, () => IsPlaying);
        DeleteSelectedScriptCommand = new RelayCommand(OnDeleteSelected, () => SelectedScript is not null && !IsRecording && !IsPlaying);
        OpenLibraryCommand = new RelayCommand(OnOpenLibrary);
        ScheduleSelectedCommand = new RelayCommand(OnScheduleSelected, () => SelectedScript is not null);
        CancelScheduleCommand = new RelayCommand<ScheduledJobInfo>(job => { if (job is not null) OnCancelSchedule(job); });

        ActivateLicenseCommand = new RelayCommand(async () => await OnActivateLicenseAsync(), () => !string.IsNullOrWhiteSpace(LicenseKeyInput));
        ValidateOnlineLicenseCommand = new RelayCommand(async () => await OnValidateOnlineLicenseAsync(), () => _licenseService.Current.KeyId is not null);
        ResetHotkeysCommand = new RelayCommand(OnResetHotkeys);
        RunDataDrivenCommand = new RelayCommand(async () => await OnRunDataDrivenAsync(), () => SelectedScript is not null && !IsRecording && !IsPlaying);
        PublishToCommunityCommand = new RelayCommand(async () => await OnPublishToCommunityAsync(), () => SelectedScript is not null);
        StepDebuggerCommand = new RelayCommand(OnStepDebugger, () => SelectedScript is not null);
        ResetDebuggerCommand = new RelayCommand(OnResetDebugger);
        ExportStandaloneCommand = new RelayCommand(async () => await OnExportStandaloneAsync(), () => SelectedScript is not null && !IsRecording && !IsPlaying);
        GenerateAiScenarioCommand = new RelayCommand(async () => await OnGenerateAiScenarioAsync(), () => _featureFlags.HasAiGenerator && !string.IsNullOrWhiteSpace(AiPrompt) && !IsRecording && !IsPlaying);
        ApproveAiScenarioCommand = new RelayCommand(OnApproveAiScenario, () => SelectedScript?.Metadata.GetValueOrDefault("ai.requiresUserReview") == "true");
        OpenSettingsCommand = new RelayCommand(() => { });

        _inputHookService.OnInputCaptured += OnInputCapturedForUi;

        LicenseStatusText = FormatLicenseStatus();
        HotkeyStatusText = FormatHotkeyStatus();

        LoadLibrary();
        ConfigureVoiceTrigger();
    }

    private void OnInputCapturedForUi(MacroEvent _)
    {
        
        _recordedStepCount++;

        long now = Environment.TickCount64;
        if (now - _lastRecordingUiUpdateMs < 100)
            return;

        _lastRecordingUiUpdateMs = now;
        RecordedStepCount = _recordedStepCount;
        RecordingPulseToken++;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ConfigureAiFromSettings();
        PlaybackSpeed = _settingsService.Current.Playback.DefaultSpeed;
        ConfigureEditorFromSettings();
        ConfigureVoiceTrigger();
        RaiseAllCanExecuteChanged();
    }

    private void ConfigureEditorFromSettings()
    {
        var recording = _settingsService.Current.Recording;
        Editor.ConfigureGraphOptimization(recording.AutoSimplifyGraph, recording.MouseJitterTolerancePixels, recording.MaxGraphPathPoints);
    }

    private void ConfigureVoiceTrigger()
    {
        var audio = _settingsService.Current.Audio;
        _voiceTrigger.Start(new VoiceTriggerOptions
        {
            Enabled = audio.UseMicrophone && audio.VoiceTriggerEnabled,
            Phrase = audio.VoiceTriggerPhrase,
            Culture = string.IsNullOrWhiteSpace(audio.VoiceTriggerCulture) ? System.Globalization.CultureInfo.CurrentUICulture.Name : audio.VoiceTriggerCulture,
            ConfidenceThreshold = 0.72f
        });
    }

    private void OnVoicePhraseRecognized(object? sender, string phrase)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() => OnVoicePhraseRecognized(sender, phrase));
            return;
        }

        if (_settingsService.Current.Audio.VoiceTriggerEnabled &&
            SelectedScript is not null && !IsRecording && !IsPlaying)
        {
            StatusMessage = $"Voice trigger: {phrase}";
            _ = OnPlayAsync();
        }
    }

    private void ConfigureAiFromSettings()
    {
        var ai = _settingsService.Current.AI;
        _promptInterpreter.Configure(ai.Endpoint, ai.ApiKey, ai.Model, ai.Temperature);
    }

    public UserSettingsService UserSettingsService => _settingsService;
    public HotkeySettingsStore HotkeySettingsStore => _hotkeySettingsStore;
    public IScriptDebugger DebuggerService => _scriptDebugger;
    public PluginRegistry PluginRegistry => _pluginRegistry;

    public bool IsRecording
    {
        get => _isRecording;
        private set { SetField(ref _isRecording, value); RaiseAllCanExecuteChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { SetField(ref _isPlaying, value); RaiseAllCanExecuteChanged(); }
    }

    public bool IsPlaybackPaused
    {
        get => _isPlaybackPaused;
        private set { SetField(ref _isPlaybackPaused, value); PauseResumePlaybackCommand.RaiseCanExecuteChanged(); }
    }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set => SetField(ref _playbackSpeed, Math.Clamp(value, 0.5, 2.0));
    }

    public int RepeatCount
    {
        get => _repeatCount;
        set => SetField(ref _repeatCount, Math.Clamp(value, 1, 999));
    }

    public IReadOnlyList<int> RepeatCountPresets { get; } = new[] { 1, 2, 5, 10 };

    public int RecordedStepCount
    {
        get => _recordedStepCount;
        private set => SetField(ref _recordedStepCount, value);
    }

    public long RecordingPulseToken { get => _recordingPulseToken; private set => SetField(ref _recordingPulseToken, value); }
    public bool IsGraphMotionEnabled => _settingsService.Current.Graph.ReactiveGrid;
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ObservableCollection<MacroScript> ScriptLibrary { get; } = new();
    public EditorViewModel Editor { get; } = new();
    public ObservableCollection<ScheduledJobInfo> ScheduledJobs { get; } = new();

    public double ScheduleIntervalMinutes
    {
        get => _scheduleIntervalMinutes;
        set => SetField(ref _scheduleIntervalMinutes, Math.Max(1, value));
    }

    public MacroScript? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (SetField(ref _selectedScript, value))
                Editor.LoadFromScript(value ?? new MacroScript());
            RaiseAllCanExecuteChanged();
        }
    }

    public RelayCommand StartStopRecordingCommand { get; }
    public RelayCommand PlayLastScriptCommand { get; }
    public RelayCommand StopPlaybackCommand { get; }
    public RelayCommand PauseResumePlaybackCommand { get; }
    public RelayCommand DeleteSelectedScriptCommand { get; }
    public RelayCommand OpenLibraryCommand { get; }
    public RelayCommand ScheduleSelectedCommand { get; }
    public RelayCommand<ScheduledJobInfo> CancelScheduleCommand { get; }

    public RelayCommand ActivateLicenseCommand { get; }
    public RelayCommand ValidateOnlineLicenseCommand { get; }
    public RelayCommand ResetHotkeysCommand { get; }
    public RelayCommand RunDataDrivenCommand { get; }
    public RelayCommand PublishToCommunityCommand { get; }
    public RelayCommand StepDebuggerCommand { get; }
    public RelayCommand ResetDebuggerCommand { get; }
    public RelayCommand ExportStandaloneCommand { get; }
    public RelayCommand GenerateAiScenarioCommand { get; }
    public RelayCommand ApproveAiScenarioCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }

    public IReadOnlyList<string> CommunityCategories { get; } = ApexClick.Services.Community.CommunityCategories.Allowed.OrderBy(x=>x).ToList();

    public string LicenseKeyInput
    {
        get => _licenseKeyInput;
        set { SetField(ref _licenseKeyInput, value); ActivateLicenseCommand.RaiseCanExecuteChanged(); ValidateOnlineLicenseCommand.RaiseCanExecuteChanged(); }
    }

    public string LicenseStatusText
    {
        get => _licenseStatusText;
        private set => SetField(ref _licenseStatusText, value);
    }

    public string HotkeyStatusText
    {
        get => _hotkeyStatusText;
        private set => SetField(ref _hotkeyStatusText, value);
    }

    public string CommunityAuthor
    {
        get => _communityAuthor;
        set => SetField(ref _communityAuthor, value);
    }

    public string CommunityCategory
    {
        get => _communityCategory;
        set => SetField(ref _communityCategory, value);
    }

    public string DebuggerStatusText
    {
        get => _debuggerStatusText;
        private set => SetField(ref _debuggerStatusText, value);
    }

        public string AiPrompt { get => _aiPrompt; set { SetField(ref _aiPrompt,value); GenerateAiScenarioCommand.RaiseCanExecuteChanged(); } }
    public string AiStatusText { get => _aiStatusText; private set => SetField(ref _aiStatusText,value); }

    public string PluginStatusText => $"Плагинов зарегистрировано: {_pluginRegistry.Items.Count}";

    private void OnStartStopRecording()
    {
        if (!IsRecording)
        {
            _recordedStepCount = 0;
            RecordedStepCount = 0;
            _lastRecordingUiUpdateMs = Environment.TickCount64;
            _inputHookService.StartRecording();
            IsRecording = true;
            StatusMessage = "Идёт запись… (F6 — остановить)";
            return;
        }

        MacroScript script = _inputHookService.StopRecording();
        IsRecording = false;

        RecordedStepCount = _recordedStepCount;
        RecordingPulseToken++;

        try
        {
            var virtualScreen = _screenCapture.GetVirtualScreenBounds();
            var primary = _screenCapture.GetPrimaryScreenSize();
            script = _virtualScreenCoordinator.NormalizeRecordedScript(
                script, virtualScreen, primary.Width, primary.Height);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Virtual-screen нормализация не применена: {ex.Message}";
        }

        if (script.Events.Count == 0)
        {
            StatusMessage = "Запись остановлена: не зафиксировано ни одного действия, не сохранено.";
            return;
        }

        if (_featureFlags.MaxSavedScenarios is { } max && ScriptLibrary.Count >= max)
        {
            StatusMessage = $"Лимит FREE — {max} сохранённых сценариев. Удалите один из библиотеки, чтобы сохранить новый.";
            return;
        }

        script.Name = $"Сценарий {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        _ = SaveNewScriptAsync(script);
    }

    private async Task SaveNewScriptAsync(MacroScript script)
    {
        try
        {
            string path = Path.Combine(_scriptsDirectory, $"{script.Id}.mscr");
            await MscrScriptSerializer.SaveAsync(script, path);
            ScriptLibrary.Add(script);
            SelectedScript = script;
            StatusMessage = $"Сохранено: «{script.Name}» ({script.Events.Count} шагов).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось сохранить сценарий: {ex.Message}";
        }
    }

    private async Task OnPlayAsync()
    {
        var script = SelectedScript ?? ScriptLibrary.LastOrDefault();
        if (script is null)
        {
            StatusMessage = "Нет сохранённых сценариев для воспроизведения.";
            return;
        }

        script.PlaybackSpeedMultiplier = PlaybackSpeed;
        if (string.Equals(script.Metadata.GetValueOrDefault("ai.requiresUserReview"), "true", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "AI-сценарий требует подтверждения пользователя перед запуском.";
            return;
        }
        _playbackCts = new CancellationTokenSource();
        IsPlaying = true;
        IsPlaybackPaused = false;
        int totalRuns = Math.Max(1, RepeatCount);
        if (_settingsService.Current.Playback.EnableStallWatchdog)
            StartStallWatchdog();

        try
        {
            bool hasGraph = ExecutionGraphPersistence.TryLoad(script, out var executionGraph) && executionGraph is not null;
            for (int run = 1; run <= totalRuns; run++)
            {
                StatusMessage = totalRuns > 1
                    ? $"Воспроизведение «{script.Name}»… прогон {run} из {totalRuns} (F5 ещё раз или Стоп — прервать)"
                    : $"Воспроизведение «{script.Name}»… (F5 ещё раз или Стоп — прервать)";

                if (hasGraph)
                    await _graphInterpreter.ExecuteScriptGraphAsync(script, executionGraph!, cancellationToken: _playbackCts.Token);
                else
                    await _playbackEngine.PlayAsync(script, _playbackCts.Token);
            }
            StatusMessage = totalRuns > 1
                ? $"Воспроизведение «{script.Name}» завершено ({totalRuns} прогонов)."
                : $"Воспроизведение «{script.Name}» завершено.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Воспроизведение остановлено пользователем.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка воспроизведения: {ex.Message}";
        }
        finally
        {
            IsPlaying = false;
            IsPlaybackPaused = false;
            _playbackCts.Dispose();
            _playbackCts = null;
            await StopStallWatchdogAsync();
        }
    }

    private void StartStallWatchdog()
    {
        _stallWatchdogCts?.Cancel();
        _stallWatchdogCts?.Dispose();
        _stallWatchdogCts = new CancellationTokenSource();
        var token = _stallWatchdogCts.Token;

        _stallWatchdogTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settingsService.Current.Playback.StallWatchdogIntervalMs));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    byte[] frame = await _screenCapture.CaptureFrameAsync(token);
                    if (!_imageAnalysis.DetectWindowHang(frame, TimeSpan.FromSeconds(1)))
                        continue;

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = "ⓘ Экран не меняется — возможно, целевое приложение ожидает действие. Воспроизведение не приостанавливается автоматически.";
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                
            }
        }, token);
    }

    private async Task StopStallWatchdogAsync()
    {
        var cts = Interlocked.Exchange(ref _stallWatchdogCts, null);
        var task = Interlocked.Exchange(ref _stallWatchdogTask, null);
        if (cts is null) return;

        cts.Cancel();
        try
        {
            if (task is not null) await task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void OnPauseResumePlayback()
    {
        if (!IsPlaying) return;
        if (IsPlaybackPaused)
        {
            _playbackEngine.ResumeManual();
            IsPlaybackPaused = false;
            StatusMessage = "Воспроизведение продолжено.";
        }
        else
        {
            _playbackEngine.PauseManual();
            IsPlaybackPaused = true;
            StatusMessage = "Воспроизведение на паузе.";
        }
    }

    private void OnStopPlayback() => _playbackCts?.Cancel();

    private void OnDeleteSelected()
    {
        if (SelectedScript is not { } script) return;

        try
        {
            string path = Path.Combine(_scriptsDirectory, $"{script.Id}.mscr");
            if (File.Exists(path)) File.Delete(path);
            ScriptLibrary.Remove(script);
            SelectedScript = null;
            StatusMessage = "Сценарий удалён.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось удалить файл: {ex.Message}";
        }
    }

    private void OnOpenLibrary() => LoadLibrary();

    public void ScheduleFromFunction(MacroScript script, TimeSpan interval)
    {
        var path = Path.Combine(_scriptsDirectory, $"{script.Id}.mscr");
        Guid id = _scheduler.Schedule(path, new ScheduleTrigger.Interval(interval));
        ScheduledJobs.Add(new ScheduledJobInfo(id, script.Name, interval));
        StatusMessage = $"«{script.Name}» запланирован каждые {interval.TotalMinutes:0.#} мин.";
    }

    public void CancelScheduleFromFunction(ScheduledJobInfo job)
    {
        _scheduler.Cancel(job.Id);
        ScheduledJobs.Remove(job);
        StatusMessage = "Расписание отменено.";
    }

    private void OnScheduleSelected()
    {
        if (SelectedScript is not { } script) return;
        string path = Path.Combine(_scriptsDirectory, $"{script.Id}.mscr");
        var interval = TimeSpan.FromMinutes(ScheduleIntervalMinutes);
        Guid id = _scheduler.Schedule(path, new ScheduleTrigger.Interval(interval));
        ScheduledJobs.Add(new ScheduledJobInfo(id, script.Name, interval));
        StatusMessage = $"«{script.Name}» запланирован каждые {ScheduleIntervalMinutes:0.#} мин.";
    }

    private void OnCancelSchedule(ScheduledJobInfo job)
    {
        _scheduler.Cancel(job.Id);
        ScheduledJobs.Remove(job);
        StatusMessage = "Расписание отменено.";
    }

    public void PrepareEditor()
    {
        Editor.IsEditingEnabled = _featureFlags.HasConditionalLogic || (SelectedScript?.Events.Count ?? 0) > 0;
        Editor.LoadFromScript(SelectedScript ?? new MacroScript());
    }

    public async Task<bool> SaveEditorChangesAsync()
    {
        if (SelectedScript is not { } original) return false;

        var updated = Editor.SaveToScript();

        var script = new MacroScript
        {
            Id = original.Id,
            Name = original.Name,
            CreatedAtUtc = original.CreatedAtUtc,
            LastModifiedUtc = original.LastModifiedUtc,
            PlaybackSpeedMultiplier = original.PlaybackSpeedMultiplier,
            CommunityCategory = original.CommunityCategory
        };

        script.Events.AddRange(updated.Events);
        script.Triggers.AddRange(original.Triggers);
        foreach (var item in original.Metadata)
            script.Metadata[item.Key] = item.Value;
        foreach (var node in Editor.Graph.Nodes)
        {
            script.Metadata[$"graph.node.{node.Id}.x"] = node.EditorX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            script.Metadata[$"graph.node.{node.Id}.y"] = node.EditorY.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var executionGraph = ExecutionGraphPersistence.FromMacroGraph(Editor.Graph);
        ExecutionGraphPersistence.Save(script, executionGraph);
        var embeddedAssets = Editor.WorkingScript?.EmbeddedAssets ?? original.EmbeddedAssets;
        foreach (var asset in embeddedAssets)
            script.EmbeddedAssets[asset.Key] = asset.Value;

        try
        {
            string path = Path.Combine(_scriptsDirectory, $"{script.Id}.mscr");
            await MscrScriptSerializer.SaveAsync(script, path);
            int index = ScriptLibrary.IndexOf(original);
            if (index >= 0) ScriptLibrary[index] = script;
            SelectedScript = script;
            Editor.MarkSaved();
            StatusMessage = "Изменения в редакторе сохранены.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось сохранить изменения: {ex.Message}";
            return false;
        }
    }

    private async Task OnGenerateAiScenarioAsync()
    {
        try
        {
            var script = await _promptInterpreter.GenerateFromPromptAsync(AiPrompt, CancellationToken.None);
            ScriptLibrary.Add(script); SelectedScript = script;
            AiStatusText = "Готово к проверке: запуск заблокирован до подтверждения.";
            StatusMessage = $"AI-сценарий «{script.Name}» создан. Проверьте граф и нажмите «Подтвердить AI».";
            ApproveAiScenarioCommand.RaiseCanExecuteChanged();
        }
        catch(Exception ex) { AiStatusText = ex.Message; StatusMessage = $"AI-генерация не удалась: {ex.Message}"; }
    }

    private void OnApproveAiScenario()
    {
        if (SelectedScript is null) return;
        SelectedScript.Metadata.Remove("ai.requiresUserReview");
        SelectedScript.Metadata["ai.userApprovedUtc"] = DateTime.UtcNow.ToString("O");
        AiStatusText = "AI-сценарий подтверждён.";
        StatusMessage = "AI-сценарий подтверждён и теперь может быть запущен.";
        ApproveAiScenarioCommand.RaiseCanExecuteChanged();
    }

    private async Task OnActivateLicenseAsync()
    {
        try
        {
            var state = await _licenseService.ActivateOfflineAsync(LicenseKeyInput);
            LicenseStatusText = FormatLicenseStatus();
            StatusMessage = $"Лицензия активирована: {state.Tier} (офлайн, действует до {state.ExpiresUtc:yyyy-MM-dd}). " +
                "Функции free/pro применяются к новым фичам сразу; для уже загруженных сервисов может понадобиться перезапуск.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось активировать лицензию: {ex.Message}";
        }
    }

    private async Task OnValidateOnlineLicenseAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("APEXCLICK_LICENSE_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            StatusMessage = "Онлайн-лицензирование: задайте APEXCLICK_LICENSE_ENDPOINT.";
            return;
        }

        try
        {
            var state = await _licenseService.ValidateOnlineResultAsync(_onlineLicenseClient, endpoint, CancellationToken.None);
            LicenseStatusText = FormatLicenseStatus();
            StatusMessage = $"Онлайн-валидация успешна: {state.Tier}, до {state.ExpiresUtc:yyyy-MM-dd}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Онлайн-валидация не удалась: {ex.Message}";
        }
    }

    private string FormatLicenseStatus()
    {
        var s = _licenseService.Current;
        return s.ActivatedUtc is null
            ? "Лицензия: Free (ключ не активирован)."
            : $"Лицензия: {s.Tier}, активирована {s.ActivatedUtc:yyyy-MM-dd}, истекает {s.ExpiresUtc:yyyy-MM-dd}.";
    }

    private void OnResetHotkeys()
    {
        _hotkeySettingsStore.Save(new[]
        {
            new HotkeyBinding("Record", 0, 0x75, "F6"),
            new HotkeyBinding("Play", 0, 0x74, "F5"),
        });
        HotkeyStatusText = FormatHotkeyStatus();
        StatusMessage = "Хоткеи сброшены на F6/F5 по умолчанию. Применится после перезапуска приложения.";
    }

    private string FormatHotkeyStatus() =>
        "Хоткеи: " + string.Join(", ", _hotkeySettingsStore.Load().Select(b => $"{b.Action}={b.Display}"));

    private async Task OnRunDataDrivenAsync()
    {
        if (SelectedScript is not { } script) return;

        var dialog = new OpenFileDialog { Filter = "CSV файлы (*.csv)|*.csv", Title = "Выбрать CSV для data-driven прогона" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            int count = 0;
            await foreach (var variant in _dataDrivenRunner.RunCsvAsync(script, dialog.FileName, _playbackCts?.Token ?? CancellationToken.None))
            {
                await _playbackEngine.PlayAsync(variant, _playbackCts?.Token ?? CancellationToken.None);
                count++;
                StatusMessage = $"Data-driven: выполнено {count} строк…";
            }
            StatusMessage = $"Data-driven прогон завершён: {count} строк.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка data-driven прогона: {ex.Message}";
        }
    }

    private async Task OnPublishToCommunityAsync()
    {
        if (SelectedScript is not { } script) return;
        try
        {
            await _communityClient.PublishAsync(script, CommunityCategory, CommunityAuthor, CancellationToken.None);
            StatusMessage = $"«{script.Name}» опубликован в локальной галерее (категория: {CommunityCategory}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Публикация отклонена: {ex.Message}";
        }
    }

    private void OnStepDebugger()
    {
        if (SelectedScript is not { } script) return;
        if (!ReferenceEquals(_debuggerLoadedScript, script))
        {
            _scriptDebugger.Load(script);
            _debuggerLoadedScript = script;
        }

        var step = _scriptDebugger.Step();
        DebuggerStatusText = step is null
            ? "Отладчик: конец сценария."
            : $"Отладчик: шаг {step.Type} @ {step.TimestampMs} мс.";
    }

    private void OnResetDebugger()
    {
        _scriptDebugger.Reset();
        _debuggerLoadedScript = null;
        DebuggerStatusText = "Отладчик сброшен.";
    }

    private async Task OnExportStandaloneAsync()
    {
        if (SelectedScript is not { } script) return;

        var dialog = new OpenFolderDialog { Title = "Папка для standalone EXE" };
        if (dialog.ShowDialog() != true) return;

        StatusMessage = "Экспорт standalone EXE (dotnet publish)…";
        try
        {
            await _standaloneExporter.PublishAsync(script, dialog.FolderName);
            StatusMessage = $"Экспортировано в {dialog.FolderName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Экспорт не удался: {ex.Message}";
        }
    }

    private void LoadLibrary()
    {
        ScriptLibrary.Clear();
        if (!Directory.Exists(_scriptsDirectory)) return;

        foreach (string file in Directory.EnumerateFiles(_scriptsDirectory, "*.mscr"))
        {
            try
            {
                ScriptLibrary.Add(MscrScriptSerializer.LoadAsync(file).GetAwaiter().GetResult());
            }
            catch
            {
                
            }
        }
    }

    private void RaiseAllCanExecuteChanged()
    {
        StartStopRecordingCommand.RaiseCanExecuteChanged();
        PlayLastScriptCommand.RaiseCanExecuteChanged();
        StopPlaybackCommand.RaiseCanExecuteChanged();
        PauseResumePlaybackCommand.RaiseCanExecuteChanged();
        DeleteSelectedScriptCommand.RaiseCanExecuteChanged();
        ScheduleSelectedCommand.RaiseCanExecuteChanged();
        RunDataDrivenCommand.RaiseCanExecuteChanged();
        PublishToCommunityCommand.RaiseCanExecuteChanged();
        StepDebuggerCommand.RaiseCanExecuteChanged();
        ExportStandaloneCommand.RaiseCanExecuteChanged();
    }
}

public sealed record ScheduledJobInfo(Guid Id, string ScriptName, TimeSpan Interval);
