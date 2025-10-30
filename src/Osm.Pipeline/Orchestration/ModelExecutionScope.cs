using System;
using Osm.Domain.Configuration;
using Osm.Smo;

namespace Osm.Pipeline.Orchestration;

public sealed record ModelExecutionScope(
    string ModelPath,
    ModuleFilterSnapshot ModuleFilter,
    SupplementalModelOptions SupplementalModels,
    TighteningOptions? TighteningOptions,
    SmoBuildOptions? SmoOptions)
{
    public static ModelExecutionScope Create(
        string modelPath,
        ModuleFilterOptions moduleFilter,
        SupplementalModelOptions? supplementalModels,
        TighteningOptions? tighteningOptions,
        SmoBuildOptions? smoOptions)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path must be provided.", nameof(modelPath));
        }

        if (moduleFilter is null)
        {
            throw new ArgumentNullException(nameof(moduleFilter));
        }

        supplementalModels ??= SupplementalModelOptions.Default;

        return new ModelExecutionScope(
            modelPath,
            ModuleFilterSnapshot.Create(moduleFilter),
            supplementalModels,
            tighteningOptions,
            smoOptions);
    }
}
