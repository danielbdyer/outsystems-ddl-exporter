using System;
using DbChange.Io;

namespace DbChange.Tests;

/// <summary>
/// A copy a test makes on the run's SQL Server through io/LocalServer.Create, registered under a repository root and, when a package is
/// given, published to under the profile given; disposing it drops the database and its registry row through LocalServer.Drop, and a
/// drop that fails fails the test, naming the error's code.
/// </summary>
internal sealed class DisposableCopy : IDisposable
{
    private DisposableCopy(SqlServer.Copy copy) => Copy = copy;

    public SqlServer.Copy Copy { get; }

    /// <summary>A copy already made, dropped when this is disposed.</summary>
    public static DisposableCopy Of(SqlServer.Copy copy) => new(copy);

    public static DisposableCopy Create(string repositoryRoot, string server, string? dacpac = null, PublishProfile.Strict? profile = null)
    {
        var made = new DisposableCopy(Expect.Value(LocalServer.Create(repositoryRoot, server, SqlServer.QueryLog.Start(repositoryRoot))));
        if (dacpac is null)
        {
            return made;
        }

        try
        {
            Expect.Value(made.Copy.Publish(dacpac, profile ?? throw new ArgumentNullException(nameof(profile)), SqlServer.QueryLog.Start(repositoryRoot)));
            return made;
        }
        catch
        {
            made.Dispose();
            throw;
        }
    }

    public void Dispose() => Expect.Value(LocalServer.Drop(Copy, SqlServer.QueryLog.Start(Copy.Root)));

    public override string ToString() => Copy.ToString();
}
