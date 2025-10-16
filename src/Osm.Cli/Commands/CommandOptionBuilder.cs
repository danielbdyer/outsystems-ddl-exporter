using System;
using System.Collections.Generic;
using System.CommandLine;
using Osm.Cli.Commands.Binders;

namespace Osm.Cli.Commands;

internal static class CommandOptionBuilder
{
    public static Command AddSqlOptions(Command command, SqlOptionBinder binder)
        => AddOptions(command, binder);

    public static Command AddModuleFilterOptions(Command command, ModuleFilterOptionBinder binder)
        => AddOptions(command, binder);

    public static Command AddCacheOptions(Command command, CacheOptionBinder binder)
        => AddOptions(command, binder);

    private static Command AddOptions(Command command, SqlOptionBinder binder)
        => AddOptions(command, binder?.Options ?? throw new ArgumentNullException(nameof(binder)));

    private static Command AddOptions(Command command, ModuleFilterOptionBinder binder)
        => AddOptions(command, binder?.Options ?? throw new ArgumentNullException(nameof(binder)));

    private static Command AddOptions(Command command, CacheOptionBinder binder)
        => AddOptions(command, binder?.Options ?? throw new ArgumentNullException(nameof(binder)));

    private static Command AddOptions(Command command, IEnumerable<Option> options)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        foreach (var option in options ?? throw new ArgumentNullException(nameof(options)))
        {
            command.AddOption(option);
        }

        return command;
    }
}
