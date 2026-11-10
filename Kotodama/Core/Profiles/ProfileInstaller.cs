using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EchoXIV.Profiles
{
    /// <summary>One shipped default file, e.g. ("Raid", "glossary.tsv", contents).</summary>
    public sealed record DefaultProfileFile(string Profile, string FileName, string Content);

    public sealed record ProfileInstallResult(IReadOnlyList<string> Created, IReadOnlyList<string> Errors);

    /// <summary>
    /// Writes the shipped default profiles into the plugin config folder (docs/PLAN.md P4.2).
    /// A user file is only ever created when it is missing, so edits survive upgrades. Every run
    /// also refreshes <c>profiles/.defaults/</c>, a copy of the current defaults to compare against.
    /// </summary>
    public static class ProfileInstaller
    {
        public const string DefaultsDirectoryName = ".defaults";

        private const string DefaultsReadme =
            "These are the profiles that ship with this Kotodama version. They are rewritten on every\n" +
            "start, so don't edit them here: edit the folders one level up instead. Kotodama never\n" +
            "overwrites those. To pick up a new default, copy the file (or the lines you want) from here.\n";

        public static ProfileInstallResult Install(string profilesRoot, IEnumerable<DefaultProfileFile> defaults)
        {
            var created = new List<string>();
            var errors = new List<string>();
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            foreach (var file in defaults)
            {
                if (!IsSafeName(file.Profile) || !IsSafeName(file.FileName))
                {
                    errors.Add($"Ignored default with unsafe name: {file.Profile}/{file.FileName}");
                    continue;
                }

                try
                {
                    var userPath = Path.Combine(profilesRoot, file.Profile, file.FileName);
                    if (!File.Exists(userPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
                        File.WriteAllText(userPath, file.Content, utf8);
                        created.Add($"{file.Profile}/{file.FileName}");
                    }

                    var defaultsPath = Path.Combine(profilesRoot, DefaultsDirectoryName, file.Profile, file.FileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(defaultsPath)!);
                    if (!File.Exists(defaultsPath) || File.ReadAllText(defaultsPath, utf8) != file.Content)
                        File.WriteAllText(defaultsPath, file.Content, utf8);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{file.Profile}/{file.FileName}: {ex.Message}");
                }
            }

            try
            {
                var readme = Path.Combine(profilesRoot, DefaultsDirectoryName, "README.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(readme)!);
                File.WriteAllText(readme, DefaultsReadme, utf8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"README.txt: {ex.Message}");
            }

            return new ProfileInstallResult(created, errors);
        }

        private static bool IsSafeName(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            name != "." && name != ".." &&
            !name.StartsWith('.');
    }
}
