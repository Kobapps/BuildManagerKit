using System.Linq;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Editor and launcher for build queues — the "ship every platform" button.
    /// </summary>
    internal sealed class QueuesView : BuildManagerView
    {
        /// <inheritdoc />
        internal override string Title => "Queues";

        /// <inheritdoc />
        internal override string Badge => Settings.Queues.Count.ToString();

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            var page = EckLayout.Page();

            var serializedObject = new SerializedObject(Settings);

            var intro = new EckCard(
                "Build queues",
                "A queue builds several profiles back to back. Switching platforms between entries reloads the "
                + "script domain, so the queue stores its progress and resumes itself — it keeps going unattended, "
                + "in the Editor and on CI.");

            var toolbar = new EckToolbar();

            toolbar.Add(EckButton.Secondary(EckIcons.Plus + " New Queue", () =>
            {
                Settings.QueuesMutable.Add(new BuildQueue
                {
                    id = "queue-" + (Settings.Queues.Count + 1),
                    displayName = "New Queue"
                });
                Settings.Save();
                Window.RefreshCurrentView();
            }));

            if (BuildQueueRunner.IsRunning)
            {
                toolbar.Add(EckButton.Danger("Cancel Running Queue", () =>
                {
                    BuildQueueRunner.Cancel();
                    Window.RefreshCurrentView();
                }));
            }

            intro.Add(toolbar);
            page.Add(intro);

            if (Settings.Queues.Count == 0)
            {
                page.Add(new EckEmptyState(
                    "No queues configured yet",
                    "A queue is an ordered list of profiles built one after another — a release run, in one press."));

                return page;
            }

            var queuesProperty = serializedObject.FindProperty("m_Queues");

            for (var i = 0; i < queuesProperty.arraySize; i++)
            {
                var queue = Settings.Queues[i];
                if (queue == null)
                    continue;

                page.Add(BuildQueueCard(serializedObject, queuesProperty.GetArrayElementAtIndex(i), queue, i));
            }

            page.Bind(serializedObject);
            return page;
        }

        private VisualElement BuildQueueCard(
            SerializedObject serializedObject,
            SerializedProperty property,
            BuildQueue queue,
            int index)
        {
            var card = new EckCard(queue.Title, $"{queue.ActiveEntries.Count()} enabled entries");

            var run = EckButton.Primary("Run Queue", () =>
            {
                if (!EditorUtility.DisplayDialog(
                        "Run queue",
                        $"Build {queue.ActiveEntries.Count()} profile(s) from '{queue.Title}'?\n\n"
                        + "Switching platforms between entries can take a while.",
                        "Run",
                        "Cancel"))
                    return;

                BuildQueueRunner.Start(queue, Settings.ActiveEnvironment, true);
                Window.RefreshCurrentView();
            });

            run.SetEnabled(!BuildRunner.IsRunning && !BuildQueueRunner.IsRunning && queue.ActiveEntries.Any());

            card.WithHeaderAction(EckButton.Danger("Remove", () =>
            {
                if (!EditorUtility.DisplayDialog("Remove queue", $"Remove '{queue.Title}'?", "Remove", "Cancel"))
                    return;

                Settings.QueuesMutable.RemoveAt(index);
                Settings.Save();
                Window.RefreshCurrentView();
            }));

            card.WithHeaderAction(run);

            if (BuildQueueRunner.IsRunning && BuildQueueRunner.CurrentQueue == queue)
            {
                card.Add(new EckBanner(EckTone.Warning,
                    $"Running — entry {BuildQueueRunner.CurrentIndex + 1} of {queue.ActiveEntries.Count()}"));
            }

            EckProperty.DrawChildren(card, property, serializedObject);
            return card;
        }
    }
}
