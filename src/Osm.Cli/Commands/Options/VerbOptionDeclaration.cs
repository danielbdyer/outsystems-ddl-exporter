using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Parsing;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Options;

internal sealed class VerbOptionDeclaration<TOverrides>
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly IReadOnlyList<Option> _options;
    private readonly IReadOnlyList<ICommandOptionSource> _optionSources;
    private readonly ModuleFilterOptionBinder? _moduleFilterBinder;
    private readonly CacheOptionBinder? _cacheBinder;
    private readonly SqlOptionBinder? _sqlBinder;
    private readonly TighteningOptionBinder? _tighteningBinder;
    private readonly SchemaApplyOptionBinder? _schemaApplyBinder;
    private readonly UatUsersOptionBinder? _uatUsersBinder;
    private readonly Func<VerbOverrideBindingContext, TOverrides> _overrideFactory;
    private readonly ImmutableArray<IVerbOptionExtension> _extensions;

    public VerbOptionDeclaration(
        string verbName,
        CliGlobalOptions globalOptions,
        IReadOnlyList<Option> options,
        IReadOnlyList<ICommandOptionSource> optionSources,
        ModuleFilterOptionBinder? moduleFilterBinder,
        CacheOptionBinder? cacheBinder,
        SqlOptionBinder? sqlBinder,
        TighteningOptionBinder? tighteningBinder,
        SchemaApplyOptionBinder? schemaApplyBinder,
        UatUsersOptionBinder? uatUsersBinder,
        Func<VerbOverrideBindingContext, TOverrides> overrideFactory,
        IReadOnlyList<IVerbOptionExtension> extensions)
    {
        if (string.IsNullOrWhiteSpace(verbName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(verbName));
        }

        VerbName = verbName;
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _optionSources = optionSources ?? throw new ArgumentNullException(nameof(optionSources));
        _moduleFilterBinder = moduleFilterBinder;
        _cacheBinder = cacheBinder;
        _sqlBinder = sqlBinder;
        _tighteningBinder = tighteningBinder;
        _schemaApplyBinder = schemaApplyBinder;
        _uatUsersBinder = uatUsersBinder;
        _overrideFactory = overrideFactory ?? throw new ArgumentNullException(nameof(overrideFactory));
        _extensions = extensions is null ? ImmutableArray<IVerbOptionExtension>.Empty : extensions.ToImmutableArray();
    }

    public string VerbName { get; }

    public UatUsersOptionBinder? UatUsersBinder => _uatUsersBinder;

    public void Configure(Command command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        command.AddGlobalOption(_globalOptions.ConfigPath);

        foreach (var option in _options)
        {
            command.AddOption(option);
        }

        foreach (var source in _optionSources)
        {
            foreach (var option in source.Options)
            {
                command.AddOption(option);
            }
        }
    }

    public VerbBoundOptions<TOverrides> Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        var moduleFilter = _moduleFilterBinder?.Bind(parseResult);
        var cache = _cacheBinder?.Bind(parseResult);
        var sql = _sqlBinder?.Bind(parseResult);
        var tightening = _tighteningBinder?.Bind(parseResult);
        var schemaApply = _schemaApplyBinder?.Bind(parseResult);
        var uatUsers = _uatUsersBinder?.Bind(parseResult);

        var context = new VerbOverrideBindingContext(
            parseResult,
            moduleFilter,
            cache,
            sql,
            tightening,
            schemaApply,
            uatUsers);

        var overrides = _overrideFactory(context);

        var extensionValues = ImmutableDictionary.CreateBuilder<Type, object?>();
        foreach (var extension in _extensions)
        {
            var value = extension.Bind(parseResult);
            extensionValues[extension.ResultType] = value;
        }

        return new VerbBoundOptions<TOverrides>(
            parseResult.GetValueForOption(_globalOptions.ConfigPath),
            moduleFilter,
            cache,
            sql,
            tightening,
            schemaApply,
            uatUsers,
            overrides,
            extensionValues.ToImmutable());
    }
}
