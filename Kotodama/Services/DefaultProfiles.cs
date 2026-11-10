using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EchoXIV.Profiles;

namespace EchoXIV.Services
{
    /// <summary>Reads the default profiles embedded from Kotodama/Profiles/Defaults.</summary>
    internal static class DefaultProfiles
    {
        private const string Prefix = "Kotodama.Profiles/";

        public static IReadOnlyList<DefaultProfileFile> Load()
        {
            var assembly = typeof(DefaultProfiles).Assembly;
            var files = new List<DefaultProfileFile>();
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;

                // "Kotodama.Profiles/Raid/glossary.tsv" (RecursiveDir may use '\' on Windows builds).
                var parts = resource[Prefix.Length..].Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    continue;

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null)
                    continue;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                files.Add(new DefaultProfileFile(parts[0], parts[1], reader.ReadToEnd()));
            }

            return files;
        }
    }
}
