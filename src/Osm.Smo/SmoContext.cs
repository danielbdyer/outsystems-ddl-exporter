using System;
using System.Threading;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Osm.Smo;

public sealed class SmoContext : IDisposable
{
    private readonly Lazy<Server> _server;
    private readonly Lazy<Database> _database;
    private readonly Lazy<Scripter> _scripter;
    private readonly Lazy<Sql150ScriptGenerator> _scriptGenerator;
    private bool _disposed;

    public SmoContext()
        : this(databaseName: null)
    {
    }

    public SmoContext(string? databaseName)
    {
        DatabaseName = string.IsNullOrWhiteSpace(databaseName)
            ? "OutSystems"
            : databaseName!;

        _server = new Lazy<Server>(CreateServer, LazyThreadSafetyMode.ExecutionAndPublication);
        _database = new Lazy<Database>(() => CreateDatabase(Server, DatabaseName), LazyThreadSafetyMode.ExecutionAndPublication);
        _scripter = new Lazy<Scripter>(() => CreateScripter(Server), LazyThreadSafetyMode.ExecutionAndPublication);
        _scriptGenerator = new Lazy<Sql150ScriptGenerator>(CreateScriptGenerator, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string DatabaseName { get; }

    public Server Server => _server.Value;

    public Database Database => _database.Value;

    public Scripter Scripter => _scripter.Value;

    public Sql150ScriptGenerator ScriptGenerator => _scriptGenerator.Value;

    private static Server CreateServer()
    {
        return new Server();
    }

    private static Database CreateDatabase(Server server, string databaseName)
    {
        return new Database(server, databaseName);
    }

    private static Scripter CreateScripter(Server server)
    {
        var scripter = new Scripter(server)
        {
            Options = new ScriptingOptions
            {
                IncludeHeaders = false,
                IncludeDatabaseContext = false,
                IncludeIfNotExists = false,
                DriAll = true,
                Triggers = true,
                Indexes = true,
                SchemaQualify = true,
                ScriptDrops = false,
                WithDependencies = false,
                NoFileGroup = true,
                ExtendedProperties = true,
            }
        };

        return scripter;
    }

    private static Sql150ScriptGenerator CreateScriptGenerator()
    {
        return new Sql150ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = true,
            SqlVersion = SqlVersion.Sql150,
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_scripter.IsValueCreated)
        {
            _scripter.Value.Dispose();
        }

        _disposed = true;
    }
}
