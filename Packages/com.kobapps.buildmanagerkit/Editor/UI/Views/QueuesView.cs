using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
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
            var root = new ScrollView();
            root.AddToClassList("bmk-scroll");

            var serializedObject = new SerializedObject(Settings);

            var intro = BuildManagerUI.Card(
                "Build queues",
                "A queue builds several profiles back to back. Switching platforms between entries reloads the "
                + "script domain, so the queue stores its progress and resumes itself — it keeps going unattended, "
                + "in the Editor and on CI.");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("bmk-row");

            toolbar.Add(new Button(() =>
            {
                Settings.QueuesMutable.Add(new BuildQueue
                {
                    id = "queue-" + (Settings.Queues.Count + 1),
                    displayName = "New Queue"
                });
                Settings.Save();
                Window.RefreshCurrentView();
            }) { text = "+ New Queue" });

            if (BuildQueueRunner.IsRunning)
            {
                var cancel = new Button(() =>
                {
                    BuildQueueRunner.Cancel();
                    Window.RefreshCurrentView();
                }) { text = "Cancel Running Queue" };
                cancel.AddToClassList("bmk-button-danger");
                toolbar.Add(cancel);
            }

            intro.Add(toolbar);
            root.Add(intro);

            if (Settings.Queues.Count == 0)
            {
                root.Add(BuildManagerUI.EmptyState("No queues configured yet."));
                return root;
            }

            var queuesProperty = serializedObject.FindProperty("m_Queues");

            for (var i = 0; i < queuesProperty.arraySize; i++)
            {
                var queue = Settings.Queues[i];
                if (queue == null)
                    continue;

                root.Add(BuildQueueCard(serializedObject, queuesProperty.GetArrayElementAtIndex(i), queue, i));
            }

            root.Bind(serializedObject);
            return root;
        }

        private VisualElement BuildQueueCard(
            SerializedObject serializedObject,
            SerializedProperty property,
            BuildQueue queue,
            int index)
        {
            var card = BuildManagerUI.Card();

            var header = new VisualElement();
            header.AddToClassList("bmk-row");

            var title = new Label(queue.Title);
            title.AddToClassList("bmk-card__title");
            title.style.marginBottom = 0;

            var count = BuildManagerUI.Muted($"  {queue.ActiveEntries.Count()} enabled entries");

            var run = BuildManagerUI.PrimaryButton("Run Queue", () =>
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

            var remove = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Remove queue", $"Remove '{queue.Title}'?", "Remove", "Cancel"))
                    return;

                Settings.QueuesMutable.RemoveAt(index);
                Settings.Save();
                Window.RefreshCurrentView();
            }) { text = "Remove" };
            remove.AddToClassList("bmk-button-danger");

            header.Add(title);
            header.Add(count);
            header.Add(BuildManagerUI.Spacer());
            header.Add(remove);
            header.Add(run);
            card.Add(header);

            if (BuildQueueRunner.IsRunning && BuildQueueRunner.CurrentQueue == queue)
            {
                var progress = BuildManagerUI.Muted(
                    $"Running — entry {BuildQueueRunner.CurrentIndex + 1} of {queue.ActiveEntries.Count()}");
                progress.style.color = new Color(0.82f, 0.60f, 0.13f);
                card.Add(progress);
            }

            var body = new VisualElement();
            BuildManagerUI.DrawChildren(body, property, serializedObject);
            card.Add(body);

            return card;
        }
    }
}
