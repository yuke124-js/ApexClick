namespace ApexClick.Services.Scheduler;

public abstract record ScheduleTrigger
{
    public sealed record Interval(TimeSpan Every) : ScheduleTrigger;
    public sealed record DailyAt(TimeSpan TimeOfDay) : ScheduleTrigger;
}

public interface IScheduler
{
    Guid Schedule(string scenarioFilePath, ScheduleTrigger trigger);
    void Cancel(Guid scheduleId);
    IReadOnlyList<Guid> ListScheduled();
}
