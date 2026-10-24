using System;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Osm.Smo;

internal static class ScripterExtensions
{
    public static void Dispose(this Scripter? scripter)
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
