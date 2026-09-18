using System;
using System.Windows;
using System.IO;
using ApexClick.Contracts;
using ApexClick.Launcher.Interop;
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
using ApexClick.FeaturePack.Settings;
using ApexClick.FeaturePack.Audio;
using ApexClick.FeaturePack.Screen;
using ApexClick.FeaturePack.GraphEngine;
using ApexClick.FeaturePack.Hotkeys;
using ApexClick.FeaturePack.DataDriven;
using ApexClick.FeaturePack.Export;
using ApexClick.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApexClick.Launcher;

public partial class App : Application
{
    public IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("DI not initialized.");
    private ServiceProvider? _serviceProvider;
    private HotkeyManager? _hotkeyManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddSingleton<InputHookService>();
        services.AddSingleton<PlaybackTimingService>();
        services.AddSingleton<PlaybackEngine>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<ImageAnalysisService>();
        services.AddSingleton<TimerScheduler>();

        var licenseService = new LicenseService();
        services.AddSingleton(licenseService);
        services.AddSingleton<IFeatureFlags>(licenseService.ResolveFlags());

        services.AddSingleton<HotkeySettingsStore>();
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<AudioDeviceService>();
        services.AddSingleton<VoiceTriggerService>();
        services.AddSingleton<DataDrivenRunner>();
        services.AddSingleton<LocalCommunityClient>();
        services.AddHttpClient<HttpCommunityClient>((sp, client) =>
        { var endpoint = Environment.GetEnvironmentVariable("APEXCLICK_COMMUNITY_ENDPOINT"); if(!string.IsNullOrWhiteSpace(endpoint)) client.BaseAddress = new Uri(endpoint.EndsWith("/") ? endpoint : endpoint + "/"); });
        services.AddSingleton<ICommunityClient>(sp =>
        { var endpoint = Environment.GetEnvironmentVariable("APEXCLICK_COMMUNITY_ENDPOINT"); return string.IsNullOrWhiteSpace(endpoint) ? sp.GetRequiredService<LocalCommunityClient>() : sp.GetRequiredService<HttpCommunityClient>(); });
        services.AddSingleton<IScriptDebugger, ScriptDebugger>();
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<StandaloneExporter>();
        services.AddHttpClient<PromptInterpreterService>(client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddSingleton<ISubScenarioLoader, MscrSubScenarioLoader>();
        services.AddSingleton<IGraphConditionEvaluator, VisionGraphConditionEvaluator>();
        services.AddSingleton<ExecutionGraphInterpreter>();
        services.AddSingleton<VirtualScreenCoordinator>();
        services.AddHttpClient<OnlineLicenseClient>(client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IFunctionModule, AutomationFunctionModule>();
        services.AddSingleton<IFunctionModule, SchedulerFunctionModule>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();

        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<LauncherShellWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var plugins = _serviceProvider.GetRequiredService<PluginRegistry>();
        try { plugins.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins")); } catch {  }

        var shell = _serviceProvider.GetRequiredService<LauncherShellWindow>();
        shell.Show();

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var hotkeySettingsStore = _serviceProvider.GetRequiredService<HotkeySettingsStore>();

        Views.PlaybackHudWindow? hud = null;
        mainViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.IsPlaying)) return;
            if (mainViewModel.IsPlaying)
            {
                hud ??= new Views.PlaybackHudWindow(mainViewModel);
                hud.Show();
            }
            else
            {
                hud?.Hide();
            }
        };

        _hotkeyManager = new HotkeyManager(shell);
        void RegisterConfiguredHotkeys()
        {
            _hotkeyManager!.UnregisterAll();
            foreach (var binding in hotkeySettingsStore.Load())
            {
                Action? action = binding.Action switch
                {
                    "Record" => () => mainViewModel.StartStopRecordingCommand.Execute(null),
                    "Play" => () => mainViewModel.PlayLastScriptCommand.Execute(null),
                    "Pause" => () => mainViewModel.PauseResumePlaybackCommand.Execute(null),
                    "Stop" => () => mainViewModel.StopPlaybackCommand.Execute(null),
                    _ => null,
                };
                if (action is not null)
                    _hotkeyManager.Register(binding.Modifiers, binding.VirtualKey, action);
            }
        }

        RegisterConfiguredHotkeys();
        hotkeySettingsStore.Changed += (_, _) => RegisterConfiguredHotkeys();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
