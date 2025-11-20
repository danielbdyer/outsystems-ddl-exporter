using System;

namespace Osm.Cli;

public sealed class ConsoleProgressBar : IProgress<(int Completed, int Total)>, IDisposable
{
    private readonly object _lock = new();
    private int _lastLength = 0;
    private bool _disposed;

    public void Report((int Completed, int Total) value)
    {
        if (_disposed || Console.IsOutputRedirected) return;

        lock (_lock)
        {
            var (completed, total) = value;
            if (total == 0) return;

            var percent = (double)completed / total;
            var barLength = 50;
            var filledLength = (int)(barLength * percent);
            if (filledLength < 0) filledLength = 0;
            if (filledLength > barLength) filledLength = barLength;

            var bar = new string('#', filledLength).PadRight(barLength, '-');
            var text = $"\r[{bar}] {completed}/{total} ({percent:P0})";

            // Clear remaining line if shorter
            if (text.Length < _lastLength)
            {
                Console.Write(text.PadRight(_lastLength));
            }
            else
            {
                Console.Write(text);
            }

            _lastLength = text.Length;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                if (!Console.IsOutputRedirected)
                {
                    Console.WriteLine();
                }
                _disposed = true;
            }
        }
    }
}
