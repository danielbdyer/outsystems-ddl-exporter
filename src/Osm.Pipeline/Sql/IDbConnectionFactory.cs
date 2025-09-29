using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Sql;

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
