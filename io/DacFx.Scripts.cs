using System;
using System.IO;
using Microsoft.SqlServer.Dac;

namespace DbChange.Io;

public static partial class DacFx
{
    /// <summary>
    /// A DacFx call that generates a deployment script, given files of its own for it: PublishOptions' DatabaseScriptPath and
    /// MasterDbScriptPath in a folder made for the call, which is deleted when the call returns, as far as the file system lets it. DacFx
    /// makes no file it is given and writes none for a SQL Server target's master script, so MasterDbScript is null. Given no path,
    /// DacFx (170.5.96 and 162.5.57 alike) names each file after the target database alone,
    /// &lt;temp&gt;/&lt;database&gt;_Update&lt;n&gt;.publish.sql for the first n no file holds, found by File.Exists and then made by
    /// File.Create, and deletes it once read. Two calls that script one database name at once, in one process or two, can so take the
    /// same file: one returns the other's script, or a null script when the other deleted the file first, as spike S2's three plans of
    /// the sample changes against one copy met on LocalDB.
    /// </summary>
    internal static PublishResult Scripted(PublishOptions options, Func<PublishOptions, PublishResult> call)
    {
        var folder = Directory.CreateTempSubdirectory("dbchange-script-").FullName;
        try
        {
            options.DatabaseScriptPath = Path.Combine(folder, "database.sql");
            options.MasterDbScriptPath = Path.Combine(folder, "master.sql");
            return call(options);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (Write.FileSystemFailure(e))
            {
                // a scanner holds a file; the folder stays in the temporary folder, as a script file DacFx cannot delete stays there
            }
        }
    }
}
