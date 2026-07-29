using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Tolerant command line parser for the arguments Unity passes through after
    /// <c>-executeMethod</c>.
    ///
    /// Names are matched loosely: <c>-bmkProfile</c>, <c>--bmk-profile</c>, <c>-bmk.profile</c> and
    /// <c>-BMK_PROFILE</c> all resolve to the same key, and both <c>-name value</c> and
    /// <c>-name=value</c> forms are accepted. That keeps CI YAML readable no matter which
    /// convention a team already uses.
    /// </summary>
    public sealed class CommandLineArgs
    {
        private readonly Dictionary<string, string> m_Values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string[] m_Raw;

        /// <summary>Parses an explicit argument array.</summary>
        public CommandLineArgs(string[] arguments)
        {
            m_Raw = arguments ?? Array.Empty<string>();
            Parse(m_Raw);
        }

        /// <summary>Parses the arguments Unity was launched with.</summary>
        public static CommandLineArgs FromProcess() => new CommandLineArgs(System.Environment.GetCommandLineArgs());

        /// <summary>The unmodified argument array.</summary>
        public IReadOnlyList<string> Raw => m_Raw;

        /// <summary>True when the named argument was supplied.</summary>
        public bool Has(string name) => m_Values.ContainsKey(Normalize(name));

        /// <summary>Reads a string argument.</summary>
        /// <param name="name">Argument name, in any supported spelling.</param>
        /// <param name="fallback">Returned when the argument is absent or empty.</param>
        public string GetString(string name, string fallback = null) =>
            m_Values.TryGetValue(Normalize(name), out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

        /// <summary>Reads an integer argument.</summary>
        public int? GetInt(string name)
        {
            var value = GetString(name);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;
        }

        /// <summary>
        /// Reads a boolean flag. A bare <c>-flag</c> counts as true; <c>-flag false</c> and
        /// <c>-flag=0</c> count as false.
        /// </summary>
        public bool GetBool(string name, bool fallback = false)
        {
            if (!m_Values.TryGetValue(Normalize(name), out var value))
                return fallback;

            if (string.IsNullOrEmpty(value))
                return true;

            if (bool.TryParse(value, out var parsed))
                return parsed;

            return value != "0" && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a boolean that distinguishes "not supplied" from "supplied as false", so an
        /// override can leave the profile's own value alone.
        /// </summary>
        public bool? GetBoolOrNull(string name) => Has(name) ? GetBool(name, true) : (bool?)null;

        /// <summary>
        /// Reads an enum argument by name, case-insensitively. Returns null when absent, and
        /// reports the accepted values when the name does not match.
        /// </summary>
        /// <typeparam name="T">Enum type to parse.</typeparam>
        /// <param name="name">Argument name.</param>
        /// <param name="onInvalid">Called with a human readable message when parsing fails.</param>
        public T? GetEnum<T>(string name, Action<string> onInvalid = null) where T : struct, Enum
        {
            var value = GetString(name);

            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Enum.TryParse<T>(value.Trim(), true, out var parsed) && Enum.IsDefined(typeof(T), parsed))
                return parsed;

            // Flags enums accept combined values that IsDefined rejects, e.g. "ARM64,X86_64".
            if (typeof(T).IsDefined(typeof(FlagsAttribute), false) &&
                Enum.TryParse<T>(value.Trim(), true, out var flags))
                return flags;

            onInvalid?.Invoke(
                $"'{value}' is not a valid {typeof(T).Name}. Accepted values: "
                + string.Join(", ", Enum.GetNames(typeof(T))));

            return null;
        }

        /// <summary>Reads a list argument split on <c>;</c> or <c>,</c>.</summary>
        public string[] GetList(string name)
        {
            var value = GetString(name);
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(entry => entry.Trim())
                    .Where(entry => entry.Length > 0)
                    .ToArray();
        }

        /// <summary>Renders the parsed arguments for logging.</summary>
        public override string ToString()
        {
            var builder = new StringBuilder();
            foreach (var pair in m_Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');

            return builder.ToString().TrimEnd();
        }

        private void Parse(string[] arguments)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                if (string.IsNullOrEmpty(argument) || argument[0] != '-')
                    continue;

                var trimmed = argument.TrimStart('-');
                if (trimmed.Length == 0)
                    continue;

                var equals = trimmed.IndexOf('=');
                if (equals > 0)
                {
                    m_Values[Normalize(trimmed.Substring(0, equals))] = trimmed.Substring(equals + 1);
                    continue;
                }

                var next = i + 1 < arguments.Length ? arguments[i + 1] : null;
                var isFlag = string.IsNullOrEmpty(next) || next.StartsWith("-", StringComparison.Ordinal);

                m_Values[Normalize(trimmed)] = isFlag ? string.Empty : next;

                if (!isFlag)
                    i++;
            }
        }

        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                if (character == '-' || character == '_' || character == '.')
                    continue;

                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }
    }
}
