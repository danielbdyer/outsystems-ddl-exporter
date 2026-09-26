using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DbChange.Io;

#pragma warning disable RS0030 // Process and ProcessStartInfo are banned in io (io/BannedSymbols.txt): this file is the one place that starts a program.

/// <summary>How a command's outcome is delivered: <see cref="Command.Run(Command, CancellationToken)"/>, or a test's stand-in for a program or for an outcome.</summary>
public delegate Ran Runner(Command command, CancellationToken cancel);

/// <summary>
/// The one way io runs a program (R6): <see cref="Program"/> with <see cref="Arguments"/>, from <see cref="Directory"/> (dbchange's own
/// working directory when null), with <see cref="Environment"/> applied to dbchange's environment (a null value removes the variable),
/// for at most <see cref="Timeout"/>. A rooted program runs as given, and a name holding a directory separator is taken from
/// <see cref="Directory"/>; a bare name is looked for in the folder of the program running dbchange (the dotnet root under dotnet
/// dbchange.dll, the tool folder under the apphost), then in each absolute PATH entry in order, and never in the working directory,
/// where a checkout could plant one. On Windows a bare name without an extension gains .exe, and a bare .cmd or .bat name is not run,
/// since cmd.exe re-parses their arguments; on Linux and macOS a file needs an execute bit, as execvp requires. Standard input is closed
/// at once, so a program that reads it gets end of file; output and errors are read apart, concurrently and as UTF-8, which git, dotnet,
/// docker and git-lfs write to a pipe whatever the console's code page; the program shares dbchange's console, so a Ctrl-C reaches it too.
/// Past Timeout the whole process tree is killed and <see cref="Ran.TimedOut"/> carries what was read. When the caller's token or the
/// run's (<see cref="Interruption"/>) is cancelled, the tree is killed and OperationCanceledException is thrown, also when the program
/// ended on the same signal; a cleanup command that must run after an interruption sets <see cref="Interruptible"/> false and is bounded
/// by its Timeout alone. After the program exits, at most five seconds are waited for both streams to end, since a descendant that
/// inherited the pipes (a build server) can hold them open; what they carried by then is returned.
/// </summary>
public sealed record Command(string Program, IReadOnlyList<string> Arguments, TimeSpan Timeout)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly IReadOnlyDictionary<string, string?> Unchanged = new Dictionary<string, string?>(StringComparer.Ordinal);

    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(5);

    public string? Directory { get; init; }

    public IReadOnlyDictionary<string, string?> Environment { get; init; } = Unchanged;

    /// <summary>Whether the current run's interruption ends this command too; false for a cleanup that must run after one.</summary>
    public bool Interruptible { get; init; } = true;

    /// <summary>The runner outside a test: the command run as it is.</summary>
    public static Ran Run(Command command, CancellationToken cancel) => command.Run(cancel);

    public Ran Run(CancellationToken cancel = default)
    {
        var (file, why) = Resolved(Program, Directory);
        if (file is null)
        {
            return new Ran.NotFound(Program, why);
        }

        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Directory ?? "" };
        foreach (var argument in Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in Environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception("no process was started");
        }
        catch (Win32Exception e)
        {
            return new Ran.NotFound(Program, "'" + file + "' cannot be started: " + e.Message);
        }

        using (process)
        {
            process.StandardInput.Close();
            var (output, errors) = (new Captured(process.StandardOutput.BaseStream), new Captured(process.StandardError.BaseStream));
            using var interrupted = Interruptible ? CancellationTokenSource.CreateLinkedTokenSource(cancel, Interruption.RunToken) : CancellationTokenSource.CreateLinkedTokenSource(cancel);
            bool exited;
            try
            {
                exited = process.WaitForExitAsync(interrupted.Token).Wait(Timeout);
            }
            catch (AggregateException e) when (e.InnerException is OperationCanceledException)
            {
                Ended(process);
                throw new OperationCanceledException("The run was interrupted while " + this + " ran.", e.InnerException, Interrupter(cancel));
            }

            if (interrupted.Token.IsCancellationRequested)
            {
                Ended(process);
                throw new OperationCanceledException("The run was interrupted as " + this + " ended.", Interrupter(cancel));
            }

            if (!exited)
            {
                Ended(process);
                output.Drained();
                errors.Drained();
                return new Ran.TimedOut(Timeout, output.Text, errors.Text);
            }

            output.Drained();
            errors.Drained();
            return new Ran.Exited(process.ExitCode, output.Text, errors.Text);
        }
    }

    public override string ToString() => Program + (Arguments.Count == 0 ? "" : " " + string.Join(' ', Arguments));

    /// <summary>The token that interrupted a run: the caller's when it is cancelled, else the run's.</summary>
    private static CancellationToken Interrupter(CancellationToken cancel) => cancel.IsCancellationRequested ? cancel : Interruption.RunToken;

    /// <summary>A duration as a message writes it: 10 minutes, 20 seconds, 200 milliseconds.</summary>
    internal static string Written(TimeSpan duration) => duration switch
    {
        { TotalMinutes: >= 1 } when duration.Seconds == 0 => Counted(duration.TotalMinutes, "minute"),
        { TotalSeconds: >= 1 } => Counted(duration.TotalSeconds, "second"),
        _ => Counted(duration.TotalMilliseconds, "millisecond"),
    };

    private static string Counted(double count, string unit) => count.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? "" : "s");

    /// <summary>The file that runs for a program's name, or why none does.</summary>
    internal static (string? File, string Why) Resolved(string program, string? directory)
    {
        var windows = OperatingSystem.IsWindows();
        if (Path.IsPathFullyQualified(program))
        {
            return (program, "");
        }

        if (program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || program.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return (Path.GetFullPath(program, directory is null ? System.IO.Directory.GetCurrentDirectory() : Path.GetFullPath(directory)), "");
        }

        var extension = Path.GetExtension(program);
        if (windows && (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, "'" + program + "' is a .cmd or .bat program, which dbchange never runs by name, since cmd.exe re-parses its arguments.");
        }

        var name = windows && extension.Length == 0 ? program + ".exe" : program;
        var folders = Searched();
        foreach (var candidate in folders.Select(folder => Path.Combine(folder, name)))
        {
            if (File.Exists(candidate) && Executable(candidate))
            {
                return (candidate, "");
            }
        }

        return (null, "'" + program + "' is on no folder of the PATH (" + string.Join(Path.PathSeparator, folders) + ").");
    }

    /// <summary>The folders a bare name is looked for in: the running program's, then each absolute PATH entry; never the working directory.</summary>
    private static List<string> Searched()
    {
        var folders = new List<string>();
        if (Path.GetDirectoryName(System.Environment.ProcessPath) is { Length: > 0 } own)
        {
            folders.Add(own);
        }

        folders.AddRange((System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(entry => entry.Trim().Trim('"')).Where(Path.IsPathFullyQualified));
        return folders;
    }

    /// <summary>Whether a file found by name may run: any file on Windows; on Linux and macOS one with an execute bit, as execvp requires.</summary>
    private static bool Executable(string file) =>
        OperatingSystem.IsWindows() || (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;

    /// <summary>The program and every process it started, killed, and waited for briefly so its id is free.</summary>
    private static void Ended(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // it ended by itself first
        }

        process.WaitForExit(Drain);
    }

    /// <summary>
    /// A stream read as UTF-8 into text from the moment the program starts, so a full pipe never stalls it; what has arrived is readable
    /// at any time. The pipe .NET hands over is synchronous, so its reader is a thread of its own rather than a thread-pool task, which
    /// would hold a pool thread for the program's lifetime and starve the timers that cancellation and timeouts run on.
    /// </summary>
    private sealed class Captured
    {
        private readonly StringBuilder text = new();
        private readonly Thread reading;

        public Captured(Stream stream)
        {
            reading = new Thread(() => Read(stream)) { IsBackground = true, Name = "dbchange pipe reader" };
            reading.Start();
        }

        public string Text
        {
            get
            {
                lock (text)
                {
                    return text.ToString();
                }
            }
        }

        /// <summary>Waits up to five seconds for the stream to end; a descendant that inherited the pipe can hold it longer, and what was read stands.</summary>
        public void Drained() => reading.Join(Drain);

        private void Read(Stream stream)
        {
            var decoder = Utf8.GetDecoder();
            var (bytes, chars) = (new byte[1 << 14], new char[Utf8.GetMaxCharCount(1 << 14)]);
            try
            {
                int read;
                while ((read = stream.Read(bytes)) > 0)
                {
                    Append(chars, decoder.GetChars(bytes, 0, read, chars, 0, flush: false));
                }

                Append(chars, decoder.GetChars(bytes, 0, 0, chars, 0, flush: true));
            }
            catch (IOException)
            {
                // the pipe broke as the program was killed; what was read stands
            }
        }

        private void Append(char[] chars, int count)
        {
            lock (text)
            {
                text.Append(chars, 0, count);
            }
        }
    }
}

/// <summary>What running a program came to: it exited with a code, its program was not found or could not start, or it ran past its timeout and was killed.</summary>
public abstract record Ran
{
    private Ran()
    {
    }

    public T Match<T>(Func<Exited, T> exited, Func<NotFound, T> notFound, Func<TimedOut, T> timedOut) => this switch
    {
        Exited e => exited(e),
        NotFound n => notFound(n),
        TimedOut t => timedOut(t),
        _ => throw new UnreachableException(),
    };

    /// <summary>The program ran to its end: its exit code, and what it wrote on each stream.</summary>
    public sealed record Exited(int Code, string Output, string Errors) : Ran;

    /// <summary>No program ran: <see cref="Why"/> says which folders were searched, or why the file found cannot be started.</summary>
    public sealed record NotFound(string Program, string Why) : Ran;

    /// <summary>The program ran past <see cref="Timeout"/> and was killed with every process it started; what each stream carried by then.</summary>
    public sealed record TimedOut(TimeSpan Timeout, string Output, string Errors) : Ran;
}
