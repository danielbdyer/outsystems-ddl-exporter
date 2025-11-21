using System;
using Osm.Domain.Abstractions;
using Spectre.Console;

namespace Osm.Cli;

public sealed class SpectreConsoleProgressService : ITaskProgress
{
    private readonly ProgressContext _context;

    public SpectreConsoleProgressService(ProgressContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IProgressTask Start(string description, double total = 100)
    {
        var task = _context.AddTask(description, new ProgressTaskSettings
        {
            MaxValue = total,
            AutoStart = true
        });
        return new SpectreProgressTask(task);
    }
}

public sealed class SpectreProgressTask : IProgressTask
{
    private readonly ProgressTask _task;

    public SpectreProgressTask(ProgressTask task)
    {
        _task = task ?? throw new ArgumentNullException(nameof(task));
    }

    public void Description(string description)
    {
        _task.Description = description;
    }

    public void Increment(double amount)
    {
        _task.Increment(amount);
    }

    public void Value(double value)
    {
        _task.Value = value;
    }

    public void MaxValue(double max)
    {
        _task.MaxValue = max;
    }

    public void Dispose()
    {
        // If the task is finished, we might want to stop it explicitly if not already?
        // Spectre.Console tasks complete when they reach max value, or when StopTask is called.
        // We'll assume reaching max value or the progress context ending handles it.
        // However, forcing it to max value on dispose might be good practice if it was a "using" block.
        if (!_task.IsFinished)
        {
            _task.Value = _task.MaxValue;
        }
    }
}
