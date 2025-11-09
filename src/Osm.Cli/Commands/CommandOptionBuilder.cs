using System;
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

    public static Command AddTighteningOptions(Command command, TighteningOptionBinder binder)
        => AddOptions(command, binder);

    public static Command AddSchemaApplyOptions(Command command, SchemaApplyOptionBinder binder)
        => AddOptions(command, binder);

    private static Command AddOptions(Command command, ICommandOptionSource optionSource)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (optionSource is null)
        {
            throw new ArgumentNullException(nameof(optionSource));
        }

        var options = optionSource.Options ?? throw new ArgumentNullException(nameof(optionSource.Options));

        foreach (var option in options)
        {
            command.AddOption(option);
        }

        return command;
    }
}
