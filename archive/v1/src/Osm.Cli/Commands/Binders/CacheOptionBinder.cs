using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class CacheOptionBinder : BinderBase<CacheOptionsOverrides>, ICommandOptionSource
{
    public CacheOptionBinder()
    {
        CacheRootOption = new Option<string?>("--cache-root", "Root directory for evidence caching.");
        RefreshCacheOption = new Option<bool>("--refresh-cache", "Force cache refresh for this execution.");
    }

    public Option<string?> CacheRootOption { get; }

    public Option<bool> RefreshCacheOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return CacheRootOption;
            yield return RefreshCacheOption;
        }
    }

    protected override CacheOptionsOverrides GetBoundValue(BindingContext bindingContext)
        => Bind(bindingContext?.ParseResult ?? throw new ArgumentNullException(nameof(bindingContext)));

    public CacheOptionsOverrides Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        var root = parseResult.GetValueForOption(CacheRootOption);
        var refresh = parseResult.HasOption(RefreshCacheOption) ? true : (bool?)null;
        return new CacheOptionsOverrides(root, refresh);
    }
}
