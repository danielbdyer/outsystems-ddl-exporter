using System;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Osm.Smo;

internal static class ScripterExtensions
{
    // Named explicitly (and invoked by name from SmoContext) rather than as a
    // `Dispose` extension that bound invisibly via namespace import — that made it
    // look like dead code to readers and tooling while it was actually load-bearing
    // (it disconnects and disposes the underlying SqlConnection).
    public static void DisconnectAndDispose(Scripter? scripter)
    {
        if (scripter is null)
        {
            return;
        }

        var connection = scripter.Server?.ConnectionContext;
        if (connection is null)
        {
            return;
        }

        try
        {
            if (connection.IsOpen)
            {
                connection.Disconnect();
            }
        }
        catch (ConnectionFailureException)
        {
            // Ignore disconnect failures; disposing the underlying SqlConnection handles cleanup.
        }
        catch (InvalidOperationException)
        {
            // Ignore invalid operations triggered by already closed connections.
        }

        connection.SqlConnectionObject?.Dispose();
    }
}
