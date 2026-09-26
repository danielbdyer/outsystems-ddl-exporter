using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class DbCommandExecutor : IDbCommandExecutor
{
    public static DbCommandExecutor Instance { get; } = new();

    private DbCommandExecutor()
    {
    }

    public Task<DbDataReader> ExecuteReaderAsync(DbCommand command, CommandBehavior behavior, CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return command.ExecuteReaderAsync(behavior, cancellationToken);
    }
}
