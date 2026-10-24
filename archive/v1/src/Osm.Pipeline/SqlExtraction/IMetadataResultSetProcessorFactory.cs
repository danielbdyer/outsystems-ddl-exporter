using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Osm.Pipeline.SqlExtraction;

internal interface IMetadataResultSetProcessorFactory
{
    IReadOnlyList<IResultSetProcessor> Create(MetadataContractOverrides overrides, ILoggerFactory? loggerFactory);
}
