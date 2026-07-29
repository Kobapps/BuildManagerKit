using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Runs a <see cref="BuildQueue"/> one profile at a time.
    ///
    /// Multi-platform queues have to switch the active build target between entries, and that can
    /// reload the script domain and kill any running C# method. The queue therefore keeps its
    /// progress in <see cref="SessionState"/> and resumes itself after a reload, so
    /// "build Windows, macOS, Android and iOS" works unattended both in the Editor and in batch
    /// mode.
    /// </summary>
    public static class BuildQueueRunner
    {
        private const string k_StateKey = "BuildManagerKit.QueueState";

        [Serializable]
        private sealed class QueueState
        {
            public string queueId = string.Empty;
            public string environmentOverrideId = string.Empty;
            public int index;
            public bool interactive;
            public string resultFilePath = string.Empty;
            public long startedAtTicks;
            public List<BuildRunResult> results = new List<BuildRunResult>();
        }

        /// <summary>Raised when a queue starts.</summary>
        public static event Action<BuildQueue> QueueStarted;

        /// <summary>Raised after every entry, with that entry's result.</summary>
        public static event Action<BuildRunResult> EntryFinished;

        /// <summary>Raised when the whole queue finishes.</summary>
        public static event Action<BuildQueueResult> QueueFinished;

        /// <summary>True while a queue is in progress, including across domain reloads.</summary>
        public static bool IsRunning => !string.IsNullOrEmpty(SessionState.GetString(k_StateKey, string.Empty));

        /// <summary>Zero based index of the entry currently being built.</summary>
        public static int CurrentIndex => LoadState()?.index ?? -1;

        /// <summary>The queue currently running, or null.</summary>
        public static BuildQueue CurrentQueue
        {
            get
            {
                var state = LoadState();
                return state == null ? null : BuildManagerSettings.Instance.FindQueue(state.queueId);
            }
        }

        /// <summary>
        /// Starts a queue. The call returns as soon as the first entry has been scheduled; use
        /// <see cref="QueueFinished"/> to learn the outcome, or <see cref="RunBlocking"/> in batch
        /// mode.
        /// </summary>
        /// <param name="queue">Queue to run.</param>
        /// <param name="environmentOverride">Environment applied to entries that do not pin one.</param>
        /// <param name="interactive">Show progress dialogs.</param>
        /// <param name="resultFilePath">Optional path the aggregate JSON result is written to.</param>
        public static bool Start(
            BuildQueue queue,
            BuildEnvironment environmentOverride = null,
            bool interactive = false,
            string resultFilePath = null)
        {
            if (queue == null)
            {
                Debug.LogError("[BuildManagerKit] No queue supplied.");
                return false;
            }

            if (IsRunning)
            {
                Debug.LogError("[BuildManagerKit] A queue is already running.");
                return false;
            }

            if (!queue.ActiveEntries.Any())
            {
                Debug.LogError($"[BuildManagerKit] Queue '{queue.Title}' has no enabled entries.");
                return false;
            }

            SaveState(new QueueState
            {
                queueId = queue.id,
                environmentOverrideId = environmentOverride != null ? environmentOverride.Id : string.Empty,
                index = 0,
                interactive = interactive,
                resultFilePath = resultFilePath ?? string.Empty,
                startedAtTicks = DateTime.UtcNow.Ticks
            });

            QueueStarted?.Invoke(queue);
            Debug.Log($"[BuildManagerKit] Queue '{queue.Title}' started ({queue.ActiveEntries.Count()} entries).");

            EditorApplication.delayCall += Step;
            return true;
        }

        /// <summary>
        /// Runs a queue to completion without returning control to the Editor loop. Intended for
        /// batch mode, where <c>delayCall</c> based resumption is not available.
        /// </summary>
        /// <param name="queue">Queue to run.</param>
        /// <param name="environmentOverride">Environment for entries that do not pin one.</param>
        /// <param name="resultFilePath">Optional path for the aggregate JSON result.</param>
        /// <param name="overrides">Per-run build overrides applied to every entry.</param>
        /// <param name="stopOnFirstFailure">Overrides the queue's own stop-on-failure setting.</param>
        public static BuildQueueResult RunBlocking(
            BuildQueue queue,
            BuildEnvironment environmentOverride = null,
            string resultFilePath = null,
            BuildOverrides overrides = null,
            bool? stopOnFirstFailure = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var settings = BuildManagerSettings.Instance;
            var results = new List<BuildRunResult>();

            QueueStarted?.Invoke(queue);

            foreach (var entry in queue.ActiveEntries)
            {
                var environment = entry.environmentOverride
                                  ?? environmentOverride
                                  ?? queue.defaultEnvironment
                                  ?? settings.ActiveEnvironment;

                var result = BuildRunner.Run(new BuildRunRequest
                {
                    Profile = entry.profile,
                    Environment = environment,
                    Overrides = overrides ?? new BuildOverrides(),
                    Interactive = false
                });

                results.Add(result);
                EntryFinished?.Invoke(result);

                if (!result.Succeeded && (stopOnFirstFailure ?? queue.stopOnFirstFailure))
                {
                    Debug.LogError(
                        $"[BuildManagerKit] Queue '{queue.Title}' stopped after '{entry.profile.DisplayName}' failed.");
                    break;
                }
            }

            stopwatch.Stop();

            var queueResult = new BuildQueueResult
            {
                queueName = queue.Title,
                results = results.ToArray(),
                durationSeconds = stopwatch.Elapsed.TotalSeconds
            };

            WriteResultFile(resultFilePath, queueResult);
            QueueFinished?.Invoke(queueResult);
            return queueResult;
        }

        /// <summary>Aborts the queue after the entry currently building finishes.</summary>
        public static void Cancel()
        {
            if (!IsRunning)
                return;

            SessionState.EraseString(k_StateKey);
            Debug.LogWarning("[BuildManagerKit] Queue cancelled.");
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (IsRunning)
                EditorApplication.delayCall += Step;
        }

        private static void Step()
        {
            var state = LoadState();
            if (state == null)
                return;

            if (BuildRunner.IsRunning)
            {
                EditorApplication.delayCall += Step;
                return;
            }

            var settings = BuildManagerSettings.Instance;
            var queue = settings.FindQueue(state.queueId);

            if (queue == null)
            {
                Debug.LogError($"[BuildManagerKit] Queue '{state.queueId}' no longer exists; aborting.");
                SessionState.EraseString(k_StateKey);
                return;
            }

            var entries = queue.ActiveEntries.ToList();
            if (state.index >= entries.Count)
            {
                CompleteQueue(state, queue);
                return;
            }

            var entry = entries[state.index];
            var environment = entry.environmentOverride
                              ?? settings.FindEnvironment(state.environmentOverrideId)
                              ?? queue.defaultEnvironment
                              ?? settings.ActiveEnvironment;

            Debug.Log($"[BuildManagerKit] Queue '{queue.Title}' — entry {state.index + 1}/{entries.Count}: "
                      + $"{entry.profile.DisplayName}");

            var result = BuildRunner.Run(new BuildRunRequest
            {
                Profile = entry.profile,
                Environment = environment,
                Interactive = state.interactive
            });

            // Reload the state: a domain reload during the build would have reset our copy.
            state = LoadState() ?? state;

            // SessionState holds a JSON string; embedding every full log would make it enormous.
            var fullLog = result.log;
            result.log = string.Empty;
            state.results.Add(JsonUtility.FromJson<BuildRunResult>(JsonUtility.ToJson(result)));
            result.log = fullLog;


            state.index++;
            SaveState(state);

            EntryFinished?.Invoke(result);

            if (!result.Succeeded && queue.stopOnFirstFailure)
            {
                Debug.LogError($"[BuildManagerKit] Queue '{queue.Title}' stopped: {result.message}");
                CompleteQueue(state, queue);
                return;
            }

            EditorApplication.delayCall += Step;
        }

        private static void CompleteQueue(QueueState state, BuildQueue queue)
        {
            var duration = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - state.startedAtTicks);

            var queueResult = new BuildQueueResult
            {
                queueName = queue.Title,
                results = state.results.ToArray(),
                durationSeconds = duration.TotalSeconds
            };

            SessionState.EraseString(k_StateKey);
            WriteResultFile(state.resultFilePath, queueResult);

            var succeeded = queueResult.results.Count(result => result.Succeeded);
            var message = $"Queue '{queue.Title}' finished: {succeeded}/{queueResult.results.Length} succeeded "
                          + $"in {BuildTargetUtility.FormatDuration(duration)}.";

            if (queueResult.Succeeded)
                Debug.Log("[BuildManagerKit] " + message);
            else
                Debug.LogError("[BuildManagerKit] " + message);

            QueueFinished?.Invoke(queueResult);
        }

        private static void WriteResultFile(string path, BuildQueueResult result)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var absolute = ProjectPaths.MakeAbsolute(path);
                var directory = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(absolute, result.ToJson());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BuildManagerKit] Could not write the queue result: {exception.Message}");
            }
        }

        private static QueueState LoadState()
        {
            var json = SessionState.GetString(k_StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var state = JsonUtility.FromJson<QueueState>(json);
                if (state != null)
                    state.results ??= new List<BuildRunResult>();

                return state;
            }
            catch (Exception)
            {
                SessionState.EraseString(k_StateKey);
                return null;
            }
        }

        private static void SaveState(QueueState state) =>
            SessionState.SetString(k_StateKey, JsonUtility.ToJson(state));
    }
}
