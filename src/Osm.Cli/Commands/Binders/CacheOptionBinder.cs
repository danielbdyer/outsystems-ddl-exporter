using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class CacheOptionBinder : BinderBase<CacheOptionsOverrides>
{
    public CacheOptionBinder()
    {
        CacheRootOption = new Option<string?>("--cache-root", "Root directory for evidence caching.");
        RefreshCacheOption = new Option<bool>("--refresh-cache", "Force cache refresh for this execution.");
        CacheMaxAgeSecondsOption = new Option<double?>(
            "--cache-max-age-seconds",
            description: "Maximum age (in seconds) to retain evidence cache entries.");
        CacheMaxEntriesOption = new Option<int?>(
            "--cache-max-entries",
            description: "Maximum number of evidence cache entries to retain.");
    }

    public Option<string?> CacheRootOption { get; }

    public Option<bool> RefreshCacheOption { get; }

    public Option<double?> CacheMaxAgeSecondsOption { get; }

    public Option<int?> CacheMaxEntriesOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return CacheRootOption;
            yield return RefreshCacheOption;
            yield return CacheMaxAgeSecondsOption;
            yield return CacheMaxEntriesOption;
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
        var maxAgeSeconds = parseResult.GetValueForOption(CacheMaxAgeSecondsOption);
        TimeSpan? maxAge = null;
        if (maxAgeSeconds.HasValue && maxAgeSeconds.Value > 0)
        {
            maxAge = TimeSpan.FromSeconds(maxAgeSeconds.Value);
        }

        var maxEntries = parseResult.GetValueForOption(CacheMaxEntriesOption);
        if (maxEntries.HasValue && maxEntries.Value < 0)
        {
            maxEntries = 0;
        }

        return new CacheOptionsOverrides(root, refresh, maxAge, maxEntries);
    }
}
