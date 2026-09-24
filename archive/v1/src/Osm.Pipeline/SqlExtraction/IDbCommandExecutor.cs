using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

/// <summary>
/// Provides an interception point for executing <see cref="DbCommand"/> instances when reading multi-result-set payloads.
/// </summary>
public interface IDbCommandExecutor
{
    /// <summary>
    /// Executes the command and returns a data reader for consuming result sets.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="behavior">The requested command behavior flags.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A data reader positioned before the first result set.</returns>
    Task<DbDataReader> ExecuteReaderAsync(DbCommand command, CommandBehavior behavior, CancellationToken cancellationToken);
}
