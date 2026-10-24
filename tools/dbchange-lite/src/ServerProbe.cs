using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace DbChangeLite;

public sealed class ServerProbeRefused() : InvalidOperationException("dbchange-lite refuses DacFx's exec xp_msver before SqlClient sends it.");

public sealed class ServerProbe : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private static readonly object Gate = new();
    private static IDisposable? subscription;

    public static void Install()
    {
        lock (Gate)
        {
            subscription ??= DiagnosticListener.AllListeners.Subscribe(new ServerProbe());
        }
    }

    public static bool Refuses(SqlCommand command)
    {
        var text = command.CommandText.Trim();
        return text.Contains("xp_msver", StringComparison.OrdinalIgnoreCase)
            && (command.CommandType == System.Data.CommandType.StoredProcedure || text.StartsWith("exec", StringComparison.OrdinalIgnoreCase));
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "SqlClientDiagnosticListener")
        {
            listener.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Key != "Microsoft.Data.SqlClient.WriteCommandBefore" || value.Value is null)
        {
            return;
        }

        if (value.Value.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand command && Refuses(command))
        {
            throw new ServerProbeRefused();
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }
}
