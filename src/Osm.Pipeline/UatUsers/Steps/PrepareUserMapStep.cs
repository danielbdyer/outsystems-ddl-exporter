using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class PrepareUserMapStep : IPipelineStep<UatUsersContext>
{
    public string Name => "prepare-user-map";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var templateLines = new List<string>
        {
            "SourceUserId,TargetUserId,Note"
        };

        context.Artifacts.WriteLines("00_user_map.template.csv", templateLines);

        var mapPath = context.UserMapPath;
        if (!File.Exists(mapPath))
        {
            var defaultPath = context.Artifacts.GetDefaultUserMapPath();
            if (!string.Equals(defaultPath, mapPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
                File.WriteAllLines(mapPath, templateLines);
            }
            else if (!File.Exists(defaultPath))
            {
                File.WriteAllLines(defaultPath, templateLines);
            }

            context.SetUserMap(Array.Empty<UserMappingEntry>());
            return Task.CompletedTask;
        }

        var mappings = UserMapLoader.Load(mapPath);
        context.SetUserMap(mappings);
        return Task.CompletedTask;
    }
}
