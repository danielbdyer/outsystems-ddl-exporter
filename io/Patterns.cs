using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// The operations' recognize: blocks, read from the files that exist: each markdown file of a folder, tests/Golden/patterns/ until M7
/// moves each block into its operation's file under knowledge/ops/, named for the operation it recognizes. A file without a block adds no
/// pattern, and a folder that does not exist holds none, so every change it would have recognized is unclassified and proved.
/// </summary>
public static class Patterns
{
    /// <summary>
    /// The pattern of each *.md file directly in <paramref name="folder"/> that holds a recognize: block, in the order of the files' names;
    /// a block that breaks the grammar is pattern.malformed, and a file this identity cannot read is pattern.unreadable.
    /// </summary>
    public static Result<SortedArray<Pattern>> Read(string folder)
    {
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal).ToList() : [];
        var read = new List<Pattern>();
        foreach (var file in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new Error("pattern.unreadable",
                    Path.GetFileName(file) + " in " + folder + " cannot be read: " + e.GetType().Name + ".",
                    "Give this identity read access to the file, or move it out of the folder.");
            }

            if (Pattern.Parse(Path.GetFileNameWithoutExtension(file), text).Failed(out var pattern, out var error))
            {
                return error;
            }

            read.AddRange(pattern is null ? [] : [pattern]);
        }

        return SortedArray.Of(read);
    }
}
