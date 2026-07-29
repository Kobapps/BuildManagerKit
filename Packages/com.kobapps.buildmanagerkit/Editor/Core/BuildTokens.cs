using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Token substitution used by output paths, file names, shell commands and notification
    /// messages. Tokens are written as <c>{name}</c> and may carry a format argument for the
    /// date/time tokens, e.g. <c>{date:yyyy-MM-dd}</c>.
    /// </summary>
    public static class BuildTokens
    {
        private static readonly Regex k_TokenPattern =
            new Regex(@"\{(?<name>[A-Za-z0-9_]+)(?::(?<format>[^}]*))?\}", RegexOptions.Compiled);

        private static readonly char[] k_InvalidNameChars = BuildInvalidNameChars();

        /// <summary>Documentation for every built-in token, shown in the Editor window.</summary>
        public static readonly (string Token, string Description)[] Documentation =
        {
            ("{projectRoot}", "Absolute path of the folder that contains Assets/"),
            ("{projectName}", "Name of the project folder"),
            ("{productName}", "PlayerSettings.productName (after environment overrides)"),
            ("{companyName}", "PlayerSettings.companyName (after environment overrides)"),
            ("{bundleId}", "Application identifier of the target being built"),
            ("{profile}", "Build profile id"),
            ("{profileName}", "Build profile display name"),
            ("{env}", "Environment id, e.g. prod"),
            ("{envName}", "Environment display name"),
            ("{ENV}", "Environment id in upper case"),
            ("{target}", "Build target, e.g. StandaloneWindows64"),
            ("{targetShort}", "Short target name, e.g. Win64"),
            ("{platform}", "Build target group, e.g. Standalone"),
            ("{version}", "Version string applied to the build"),
            ("{versionDots}", "Version with dots removed, e.g. 142"),
            ("{buildNumber}", "Numeric build counter"),
            ("{executable}", "Resolved player file name including extension"),
            ("{extension}", "Player file extension without the dot"),
            ("{branch}", "Git branch name"),
            ("{commit}", "Short git commit hash"),
            ("{commitLong}", "Full git commit hash"),
            ("{dirty}", "'dirty' when the working copy has changes, otherwise empty"),
            ("{user}", "Name of the user running the build"),
            ("{machine}", "Name of the machine running the build"),
            ("{date}", "Build date, default yyyy-MM-dd, accepts a format: {date:yyMMdd}"),
            ("{time}", "Build time, default HHmmss, accepts a format: {time:HH-mm}"),
            ("{datetime}", "Build timestamp, default yyyy-MM-dd_HHmmss"),
            ("{timestamp}", "Unix timestamp in seconds"),
            ("{buildType}", "'Development' or 'Release'"),
            ("{outputDir}", "Resolved output directory (post build steps only)"),
            ("{outputPath}", "Resolved player path (post build steps only)")
        };

        /// <summary>
        /// Replaces every <c>{token}</c> in <paramref name="template"/> using
        /// <paramref name="values"/>. Unknown tokens are left untouched so typos stay visible.
        /// </summary>
        /// <param name="template">Text containing tokens.</param>
        /// <param name="values">Token name to value map, case-insensitive.</param>
        /// <param name="timestamp">Timestamp used by the date/time tokens.</param>
        public static string Resolve(string template, IReadOnlyDictionary<string, string> values, DateTime timestamp)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            return k_TokenPattern.Replace(template, match =>
            {
                var name = match.Groups["name"].Value;
                var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;

                switch (name.ToLowerInvariant())
                {
                    case "date":
                        return timestamp.ToString(string.IsNullOrEmpty(format) ? "yyyy-MM-dd" : format,
                            CultureInfo.InvariantCulture);
                    case "time":
                        return timestamp.ToString(string.IsNullOrEmpty(format) ? "HHmmss" : format,
                            CultureInfo.InvariantCulture);
                    case "datetime":
                        return timestamp.ToString(string.IsNullOrEmpty(format) ? "yyyy-MM-dd_HHmmss" : format,
                            CultureInfo.InvariantCulture);
                    case "timestamp":
                        return new DateTimeOffset(timestamp.ToUniversalTime())
                            .ToUnixTimeSeconds()
                            .ToString(CultureInfo.InvariantCulture);
                }

                if (values == null)
                    return match.Value;

                // Exact first: {env} and {ENV} are deliberately distinct tokens.
                if (values.TryGetValue(name, out var value))
                    return value ?? string.Empty;

                // Then a forgiving pass, so a variable named api_url also answers to {API_URL}.
                foreach (var pair in values)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                        return pair.Value ?? string.Empty;
                }

                return match.Value;
            });
        }

        /// <summary>True when the template contains at least one token.</summary>
        public static bool HasTokens(string template) =>
            !string.IsNullOrEmpty(template) && k_TokenPattern.IsMatch(template);

        /// <summary>Every token name referenced by the template.</summary>
        public static IEnumerable<string> GetReferencedTokens(string template)
        {
            if (string.IsNullOrEmpty(template))
                yield break;

            foreach (Match match in k_TokenPattern.Matches(template))
                yield return match.Groups["name"].Value;
        }

        /// <summary>
        /// Turns arbitrary text into something safe to use as a file or folder name: invalid
        /// characters and whitespace collapse into single underscores.
        /// </summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            var lastWasSeparator = false;

            foreach (var character in value)
            {
                var invalid = char.IsWhiteSpace(character) || Array.IndexOf(k_InvalidNameChars, character) >= 0;
                if (invalid)
                {
                    if (!lastWasSeparator && builder.Length > 0)
                    {
                        builder.Append('_');
                        lastWasSeparator = true;
                    }

                    continue;
                }

                builder.Append(character);
                lastWasSeparator = false;
            }

            return builder.ToString().Trim('_');
        }

        /// <summary>
        /// Turns arbitrary text into a valid C# preprocessor symbol: anything outside
        /// <c>[A-Za-z0-9_]</c> collapses to a single underscore, and a leading digit is prefixed.
        ///
        /// This is stricter than <see cref="Sanitize"/>, which only has to produce a legal file
        /// name. A hyphen is fine in a folder name but makes <c>#if ENV_MY-ENV</c> uncompilable,
        /// so environment ids have to go through this before becoming defines.
        /// </summary>
        public static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            var lastWasUnderscore = false;

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                {
                    builder.Append(character);
                    lastWasUnderscore = character == '_';
                    continue;
                }

                if (!lastWasUnderscore && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasUnderscore = true;
                }
            }

            var result = builder.ToString().Trim('_');

            if (result.Length == 0)
                return string.Empty;

            return char.IsDigit(result[0]) ? "_" + result : result;
        }

        /// <summary>True when <paramref name="value"/> is usable as a scripting define symbol.</summary>
        public static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();

            if (char.IsDigit(trimmed[0]))
                return false;

            return trimmed.All(character => char.IsLetterOrDigit(character) || character == '_');
        }

        private static char[] BuildInvalidNameChars()
        {
            var chars = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
            foreach (var character in System.IO.Path.GetInvalidPathChars())
                chars.Add(character);

            // Characters that are legal on some platforms but cause pain in shells and URLs.
            foreach (var character in new[] { ':', '*', '?', '"', '<', '>', '|', '\\', '/' })
                chars.Add(character);

            var result = new char[chars.Count];
            chars.CopyTo(result);
            return result;
        }
    }
}
