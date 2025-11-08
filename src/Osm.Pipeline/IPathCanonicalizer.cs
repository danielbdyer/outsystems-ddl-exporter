using System;

namespace Osm.Pipeline;

public interface IPathCanonicalizer
{
    string Canonicalize(string path);

    string? CanonicalizeOrNull(string? path);
}
