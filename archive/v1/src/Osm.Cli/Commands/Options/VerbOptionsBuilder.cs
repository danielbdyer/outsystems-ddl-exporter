using System;
using System.Collections.Generic;
using System.CommandLine;
using Osm.Cli.Commands.Binders;

namespace Osm.Cli.Commands.Options;

internal sealed class VerbOptionsBuilder<TOverrides> : IVerbOptionsBuilder
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly List<Option> _options = new();
    private readonly List<ICommandOptionSource> _optionSources = new();
    private readonly List<IVerbOptionExtension> _extensions = new();
    private Func<VerbOverrideBindingContext, TOverrides>? _overrideFactory;

    public VerbOptionsBuilder(CliGlobalOptions globalOptions)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
    }

    public VerbOptionsBuilder<TOverrides> UseModuleFilter(ModuleFilterOptionBinder binder)
    {
        _ = binder ?? throw new ArgumentNullException(nameof(binder));
        _optionSources.Add(binder);
        ModuleFilterBinder = binder;
        return this;
    }

    public VerbOptionsBuilder<TOverrides> UseCache(CacheOptionBinder binder)
    {
        _ = binder ?? throw new ArgumentNullException(nameof(binder));
        _optionSources.Add(binder);
        CacheBinder = binder;
        return this;
    }

    public VerbOptionsBuilder<TOverrides> UseSql(SqlOptionBinder binder)
    {
        _ = binder ?? throw new ArgumentNullException(nameof(binder));
        _optionSources.Add(binder);
        SqlBinder = binder;
        return this;
    }

    public VerbOptionsBuilder<TOverrides> UseTightening(TighteningOptionBinder binder)
    {
        _ = binder ?? throw new ArgumentNullException(nameof(binder));
        _optionSources.Add(binder);
        TighteningBinder = binder;
        return this;
    }

    public VerbOptionsBuilder<TOverrides> UseSchemaApply(SchemaApplyOptionBinder binder)
    {
        _ = binder ?? throw new ArgumentNullException(nameof(binder));
        _optionSources.Add(binder);
        SchemaApplyBinder = binder;
        return this;
    }

    public VerbOptionsBuilder<TOverrides> UseUatUsers(UatUsersOptionBinder binder)
    {
        _ = binder ?? throw new ArgumentNullException(nameof(binder));
        _optionSources.Add(binder);
        UatUsersBinder = binder;
        return this;
    }

    public VerbOptionsBuilder<TOverrides> AddOption(Option option)
    {
        if (option is null)
        {
            throw new ArgumentNullException(nameof(option));
        }

        _options.Add(option);
        return this;
    }

    public VerbOptionsBuilder<TOverrides> AddExtension(IVerbOptionExtension extension)
    {
        if (extension is null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        extension.Configure(this);
        _extensions.Add(extension);
        return this;
    }

    public VerbOptionsBuilder<TOverrides> BindOverrides(Func<VerbOverrideBindingContext, TOverrides> factory)
    {
        _overrideFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public VerbOptionDeclaration<TOverrides> Build(string verbName)
    {
        if (string.IsNullOrWhiteSpace(verbName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(verbName));
        }

        if (_overrideFactory is null)
        {
            throw new InvalidOperationException("Override factory must be provided.");
        }

        return new VerbOptionDeclaration<TOverrides>(
            verbName,
            _globalOptions,
            _options,
            _optionSources,
            ModuleFilterBinder,
            CacheBinder,
            SqlBinder,
            TighteningBinder,
            SchemaApplyBinder,
            UatUsersBinder,
            _overrideFactory,
            _extensions);
    }

    void IVerbOptionsBuilder.AddOption(Option option)
        => AddOption(option);

    public ModuleFilterOptionBinder? ModuleFilterBinder { get; private set; }

    public CacheOptionBinder? CacheBinder { get; private set; }

    public SqlOptionBinder? SqlBinder { get; private set; }

    public TighteningOptionBinder? TighteningBinder { get; private set; }

    public SchemaApplyOptionBinder? SchemaApplyBinder { get; private set; }

    public UatUsersOptionBinder? UatUsersBinder { get; private set; }
}
