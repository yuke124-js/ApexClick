using ApexClick.Models;
using System.Collections.ObjectModel;
using ApexClick.Contracts;
using ApexClick.Services.Scheduler;

namespace ApexClick.ViewModels;

public sealed class SchedulerFunctionModule : IFunctionModule
{
    public SchedulerFunctionModule(MainViewModel main, TimerScheduler scheduler)
    {
        ContentViewModel = new SchedulerFunctionViewModel(main, scheduler);
    }

    public string Id => "scheduler";
    public string DisplayName => "Планировщик";
    public string IconGlyph => "◷";
    public int SortOrder => 10;
    public object ContentViewModel { get; }
}

public sealed class SchedulerFunctionViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly TimerScheduler _scheduler;
    private double _intervalMinutes = 5;
    private MacroScript? _selectedScript;

    public SchedulerFunctionViewModel(MainViewModel main, TimerScheduler scheduler)
    {
        _main = main;
        _scheduler = scheduler;
        ScheduleCommand = new RelayCommand(OnSchedule, () => SelectedScript is not null);
        CancelCommand = new RelayCommand<ScheduledJobInfo>(job => { if (job is not null) _main.CancelScheduleFromFunction(job); });
    }

    public ObservableCollection<ScheduledJobInfo> Jobs => _main.ScheduledJobs;
    public ObservableCollection<MacroScript> Scripts => _main.ScriptLibrary;
    public MacroScript? SelectedScript { get => _selectedScript; set { if (SetField(ref _selectedScript, value)) ScheduleCommand.RaiseCanExecuteChanged(); } }
    public double IntervalMinutes { get => _intervalMinutes; set => SetField(ref _intervalMinutes, Math.Max(1, value)); }
    public RelayCommand ScheduleCommand { get; }
    public RelayCommand<ScheduledJobInfo> CancelCommand { get; }
    public string SchedulerInfo => "Запуски выполняются через существующий TimerScheduler; AI-сценарии без подтверждения не запускаются.";

    private void OnSchedule()
    {
        if (SelectedScript is null) return;
        _main.ScheduleFromFunction(SelectedScript, TimeSpan.FromMinutes(IntervalMinutes));
    }
}
