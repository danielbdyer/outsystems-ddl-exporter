using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

public sealed record PolicyContext(OsmModel Model, ProfileSnapshot Profile, TighteningOptions Options)
{
    public static Result<PolicyContext> Create(OsmModel? model, ProfileSnapshot? profile, TighteningOptions? options)
    {
        if (model is null)
        {
            return Result<PolicyContext>.Failure(
                ValidationError.Create("policy.context.model.missing", "Policy evaluation requires a model."));
        }

        if (profile is null)
        {
            return Result<PolicyContext>.Failure(
                ValidationError.Create("policy.context.profile.missing", "Policy evaluation requires a profiling snapshot."));
        }

        if (options is null)
        {
            return Result<PolicyContext>.Failure(
                ValidationError.Create("policy.context.options.missing", "Policy evaluation requires tightening options."));
        }

        return Result<PolicyContext>.Success(new PolicyContext(model, profile, options));
    }
}
