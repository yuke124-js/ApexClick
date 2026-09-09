using System.Collections.Concurrent;
using ApexClick.Models;
using ApexClick.Services.Playback;

namespace ApexClick.Services.Scheduler;

public sealed class TimerScheduler : IScheduler, IDisposable
{
    private sealed class ScheduledJob : IDisposable
    {
        public ScheduledJob(Guid id, string scenarioFilePath, ScheduleTrigger trigger, Timer timer)
        {
            Id = id; ScenarioFilePath = scenarioFilePath; Trigger = trigger; Timer = timer;
        }
        public Guid Id { get; }
        public string ScenarioFilePath { get; }
        public ScheduleTrigger Trigger { get; }
        public Timer Timer { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public int Running;
        public void Dispose() { Cancellation.Cancel(); Timer.Dispose(); Cancellation.Dispose(); }
    }

    private readonly ConcurrentDictionary<Guid, ScheduledJob> _jobs = new();
    private readonly PlaybackEngine _playbackEngine;

    public TimerScheduler(PlaybackEngine playbackEngine)
    {
        _playbackEngine = playbackEngine;
    }

    public event EventHandler<ScheduledJobFailedEventArgs>? JobFailed;

    public Guid Schedule(string scenarioFilePath, ScheduleTrigger trigger)
    {
        if (!File.Exists(scenarioFilePath))
            throw new FileNotFoundException("Файл сценария (.mscr) не найден.", scenarioFilePath);

        var id = Guid.NewGuid();
        var dueTime = ComputeInitialDue(trigger);
        var period = trigger is ScheduleTrigger.Interval interval ? interval.Every : Timeout.InfiniteTimeSpan;

        var timer = new Timer(_ => _ = OnTickAsync(id), null, dueTime, period);
        _jobs[id] = new ScheduledJob(id, scenarioFilePath, trigger, timer);
        return id;
    }

    public void Cancel(Guid scheduleId)
    {
        if (_jobs.TryRemove(scheduleId, out var job))
            job.Dispose();
    }

    public IReadOnlyList<Guid> ListScheduled() => _jobs.Keys.ToArray();

    private static TimeSpan ComputeInitialDue(ScheduleTrigger trigger) => trigger switch
    {
        ScheduleTrigger.Interval interval => interval.Every,
        ScheduleTrigger.DailyAt dailyAt => TimeUntilNextDailyOccurrence(dailyAt.TimeOfDay),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };

    private static TimeSpan TimeUntilNextDailyOccurrence(TimeSpan timeOfDay)
    {
        var now = DateTime.Now;
        var todayAt = now.Date + timeOfDay;
        var next = todayAt > now ? todayAt : todayAt.AddDays(1);
        return next - now;
    }

    private async Task OnTickAsync(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job)) return;
        if (Interlocked.Exchange(ref job.Running, 1) != 0) return;

        try
        {
            if (job.Trigger is ScheduleTrigger.DailyAt dailyAt)
                job.Timer.Change(TimeUntilNextDailyOccurrence(dailyAt.TimeOfDay), Timeout.InfiniteTimeSpan);

            var script = await MscrScriptSerializer.LoadAsync(job.ScenarioFilePath, job.Cancellation.Token);
            if (string.Equals(script.Metadata.GetValueOrDefault("ai.requiresUserReview"), "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AI-сценарий требует явного подтверждения пользователя и не может быть запущен планировщиком.");
            await _playbackEngine.PlayAsync(script, job.Cancellation.Token);
        }
        catch (Exception ex)
        {
            JobFailed?.Invoke(this, new ScheduledJobFailedEventArgs(id, job.ScenarioFilePath, ex));
        }
        finally
        {
            Volatile.Write(ref job.Running, 0);
        }
    }

    public void Dispose()
    {
        foreach (var job in _jobs.Values)
            job.Dispose();
        _jobs.Clear();
    }
}

public sealed class ScheduledJobFailedEventArgs(Guid scheduleId, string scenarioFilePath, Exception exception) : EventArgs
{
    public Guid ScheduleId { get; } = scheduleId;
    public string ScenarioFilePath { get; } = scenarioFilePath;
    public Exception Exception { get; } = exception;
}
