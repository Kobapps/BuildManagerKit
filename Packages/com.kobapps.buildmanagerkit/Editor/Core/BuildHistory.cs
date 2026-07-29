using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>One past run, as stored in the persistent history.</summary>
    [Serializable]
    public sealed class BuildHistoryEntry
    {
        /// <summary>Machine readable result of the run.</summary>
        public BuildRunResult result = new BuildRunResult();

        /// <summary>Local time the run finished, round-trip ("o") format.</summary>
        public string finishedAt = string.Empty;

        /// <summary>Absolute path of the persisted text log, empty when logs are disabled.</summary>
        public string logFile = string.Empty;

        /// <summary>Local finish time, or <see cref="DateTime.MinValue"/> when unparsable.</summary>
        public DateTime FinishedAt =>
            DateTime.TryParse(finishedAt, null, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.MinValue;

        /// <summary>True when the stored log file still exists on disk.</summary>
        public bool HasLog => !string.IsNullOrEmpty(logFile) && File.Exists(logFile);
    }

    /// <summary>
    /// Persistent record of every build the project has produced.
    ///
    /// History lives in <c>Library/BuildManagerKit/history.json</c> rather than in the settings
    /// asset, so build activity never shows up as a version control change. Logs are written
    /// alongside and survive Editor restarts.
    /// </summary>
    public static class BuildHistory
    {
        [Serializable]
        private sealed class Container
        {
            public List<BuildHistoryEntry> entries = new List<BuildHistoryEntry>();
        }

        private static readonly string k_Path =
            Path.Combine(ProjectPaths.ProjectRoot, "Library/BuildManagerKit/history.json");

        private static Container s_Cache;

        /// <summary>Raised whenever an entry is added or the history is cleared.</summary>
        public static event Action Changed;

        /// <summary>Every stored run, newest first.</summary>
        public static IReadOnlyList<BuildHistoryEntry> Entries
        {
            get
            {
                Load();
                return s_Cache.entries;
            }
        }

        /// <summary>Adds a run to the history and trims it to <paramref name="limit"/> entries.</summary>
        public static void Add(BuildRunResult result, string logFile, int limit)
        {
            if (result == null)
                return;

            Load();

            s_Cache.entries.Insert(0, new BuildHistoryEntry
            {
                result = result,
                logFile = logFile ?? string.Empty,
                finishedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture)
            });

            var maximum = Mathf.Clamp(limit, 1, 1000);
            if (s_Cache.entries.Count > maximum)
            {
                foreach (var dropped in s_Cache.entries.Skip(maximum).ToList())
                    TryDeleteLog(dropped.logFile);

                s_Cache.entries.RemoveRange(maximum, s_Cache.entries.Count - maximum);
            }

            Save();
            Changed?.Invoke();
        }

        /// <summary>Removes every entry and deletes the log files it owned.</summary>
        public static void Clear(bool deleteLogFiles = true)
        {
            Load();

            if (deleteLogFiles)
            {
                foreach (var entry in s_Cache.entries)
                    TryDeleteLog(entry.logFile);
            }

            s_Cache.entries.Clear();
            Save();
            Changed?.Invoke();
        }

        /// <summary>Entries matching a free text query and an optional status filter.</summary>
        /// <param name="query">Case-insensitive substring matched against profile, environment, version and message.</param>
        /// <param name="status">Status to keep, or null for all.</param>
        public static IEnumerable<BuildHistoryEntry> Search(string query, BuildRunStatus? status = null)
        {
            var needle = query?.Trim();

            return Entries.Where(entry =>
            {
                if (status.HasValue && entry.result.status != status.Value)
                    return false;

                if (string.IsNullOrEmpty(needle))
                    return true;

                return Contains(entry.result.profileName, needle)
                       || Contains(entry.result.profileId, needle)
                       || Contains(entry.result.environmentId, needle)
                       || Contains(entry.result.target, needle)
                       || Contains(entry.result.version, needle)
                       || Contains(entry.result.gitBranch, needle)
                       || Contains(entry.result.gitCommit, needle)
                       || Contains(entry.result.message, needle);
            });
        }

        /// <summary>Reads the persisted log of an entry, or an empty string when it is gone.</summary>
        public static string ReadLog(BuildHistoryEntry entry)
        {
            if (entry == null || !entry.HasLog)
                return string.Empty;

            try
            {
                return File.ReadAllText(entry.logFile);
            }
            catch (IOException exception)
            {
                return $"Could not read log file: {exception.Message}";
            }
        }

        /// <summary>Number of consecutive successful runs at the head of the history.</summary>
        public static int CurrentSuccessStreak()
        {
            var streak = 0;
            foreach (var entry in Entries)
            {
                if (!entry.result.Succeeded)
                    break;

                streak++;
            }

            return streak;
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void TryDeleteLog(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // A log we cannot delete is not worth interrupting anything for.
            }
        }

        private static void Load()
        {
            if (s_Cache != null)
                return;

            try
            {
                if (File.Exists(k_Path))
                {
                    s_Cache = JsonUtility.FromJson<Container>(File.ReadAllText(k_Path)) ?? new Container();
                    s_Cache.entries ??= new List<BuildHistoryEntry>();
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BuildManagerKit] Could not read build history: {exception.Message}");
            }

            s_Cache = new Container();
        }

        private static void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(k_Path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(k_Path, JsonUtility.ToJson(s_Cache, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BuildManagerKit] Could not write build history: {exception.Message}");
            }
        }
    }
}
