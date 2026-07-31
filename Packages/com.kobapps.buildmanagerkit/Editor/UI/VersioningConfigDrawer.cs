using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Draws a <see cref="VersioningConfig"/> as two independent switches with their own details:
    /// version management and build number management.
    ///
    /// Everything is conditional on purpose. A field that cannot affect the build — a version file
    /// path while the file is switched off, an explicit version while the version comes from a git
    /// tag, a counter while the policy is a timestamp — is hidden rather than greyed out, because a
    /// visible value reads as a value in use. The same drawer serves the Build Manager window and
    /// the Inspector, so the two never disagree about which fields matter.
    /// </summary>
    [CustomPropertyDrawer(typeof(VersioningConfig))]
    internal sealed class VersioningConfigDrawer : PropertyDrawer
    {
        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList("bmk-versioning");

            var manageVersion = property.FindPropertyRelative("manageVersion");
            var source = property.FindPropertyRelative("source");
            var version = property.FindPropertyRelative("version");
            var useVersionFile = property.FindPropertyRelative("useVersionFile");
            var versionFilePath = property.FindPropertyRelative("versionFilePath");
            var manageBuildNumber = property.FindPropertyRelative("manageBuildNumber");
            var policy = property.FindPropertyRelative("buildNumberPolicy");
            var buildNumber = property.FindPropertyRelative("buildNumber");

            root.Add(Field(manageVersion, "Manage version"));

            var versionDetails = Indented();
            root.Add(versionDetails);

            versionDetails.Add(Field(useVersionFile, "Version from a text file"));

            var filePath = Field(versionFilePath, "Version file");
            filePath.tooltip = "Relative to the project root. The first non-empty line is the version, and a "
                               + "bump made by the Increment Version action is written back here.";
            versionDetails.Add(filePath);

            var sourceField = SourcePopup(source);
            versionDetails.Add(sourceField);

            var explicitVersion = Field(version, "Version");
            versionDetails.Add(explicitVersion);

            root.Add(Field(manageBuildNumber, "Manage build number"));

            var numberDetails = Indented();
            root.Add(numberDetails);

            var policyField = Field(policy, "Build number policy");
            numberDetails.Add(policyField);

            var counter = Field(buildNumber, "Build number");
            counter.tooltip = "The stored counter. Auto Increment bumps it after every successful build.";
            numberDetails.Add(counter);

            var summary = KUIText.Muted(string.Empty);
            root.Add(summary);

            void Refresh()
            {
                property.serializedObject.Update();

                var versionManaged = manageVersion.boolValue;
                var fromFile = useVersionFile.boolValue ||
                               (VersionSource)source.intValue == VersionSource.VersionFile;

                Show(versionDetails, versionManaged);
                Show(filePath, versionManaged && useVersionFile.boolValue);

                // The file is the version once it is switched on, so the source picker would be a
                // control with no effect.
                Show(sourceField, versionManaged && !fromFile);
                Show(explicitVersion,
                    versionManaged && !fromFile && (VersionSource)source.intValue == VersionSource.Profile);

                var numberManaged = manageBuildNumber.boolValue;

                // Every policy except the timestamp can end up reading the stored counter — the commit
                // count falls back to it outside a git checkout, which is exactly the case where a
                // hidden field would be baffling.
                var storedCounter = (BuildNumberPolicy)policy.intValue != BuildNumberPolicy.Timestamp;

                Show(numberDetails, numberManaged);
                Show(policyField, numberManaged);
                Show(counter, numberManaged && storedCounter);

                summary.text = Summarize(manageVersion.boolValue, fromFile, versionFilePath.stringValue,
                    (VersionSource)source.intValue, version.stringValue, manageBuildNumber.boolValue,
                    (BuildNumberPolicy)policy.intValue);
            }

            // Track every property the visibility depends on, so undo, a CLI edit picked up by a
            // reimport and an Inspector open next to the window all keep the card honest.
            root.TrackPropertyValue(manageVersion, _ => Refresh());
            root.TrackPropertyValue(useVersionFile, _ => Refresh());
            root.TrackPropertyValue(source, _ => Refresh());
            root.TrackPropertyValue(version, _ => Refresh());
            root.TrackPropertyValue(versionFilePath, _ => Refresh());
            root.TrackPropertyValue(manageBuildNumber, _ => Refresh());
            root.TrackPropertyValue(policy, _ => Refresh());
            root.schedule.Execute(Refresh);

            return root;
        }

        /// <summary>
        /// The version source picker, without the legacy <see cref="VersionSource.VersionFile"/>
        /// entry — that is the "Version from a text file" toggle now. An asset still holding the old
        /// value keeps it listed so the picker never silently rewrites it.
        /// </summary>
        private static VisualElement SourcePopup(SerializedProperty source)
        {
            var choices = new List<VersionSource>
            {
                VersionSource.PlayerSettings,
                VersionSource.Profile,
                VersionSource.GitTag
            };

            var current = (VersionSource)source.intValue;
            if (!choices.Contains(current))
                choices.Insert(0, current);

            var popup = new PopupField<VersionSource>("Version source", choices, current, Describe, Describe)
            {
                tooltip = "Where the version string comes from when it is not read from a text file."
            };

            popup.RegisterValueChangedCallback(evt =>
            {
                source.intValue = (int)evt.newValue;
                source.serializedObject.ApplyModifiedProperties();
            });

            // Follows external edits (undo, the CLI, another inspector) without fighting the user's
            // own selection, which has already been written above.
            popup.TrackPropertyValue(source, changed =>
            {
                var value = (VersionSource)changed.intValue;
                if (!Equals(popup.value, value))
                    popup.SetValueWithoutNotify(value);
            });

            return popup;
        }

        private static string Describe(VersionSource source)
        {
            switch (source)
            {
                case VersionSource.Profile: return "Explicit value";
                case VersionSource.GitTag: return "Git tag";
                case VersionSource.VersionFile: return "Version file (legacy)";
                default: return "PlayerSettings (leave as it is)";
            }
        }

        private static string Summarize(
            bool manageVersion,
            bool fromFile,
            string filePath,
            VersionSource source,
            string version,
            bool manageBuildNumber,
            BuildNumberPolicy policy)
        {
            if (!manageVersion && !manageBuildNumber)
                return "Build Manager Kit leaves the version and the build number exactly as the project has them.";

            var versionPart = !manageVersion
                ? "The version is left as the project has it"
                : fromFile
                    ? $"The version is read from {filePath}"
                    : source == VersionSource.Profile
                        ? $"The version is {version}"
                        : source == VersionSource.GitTag
                            ? "The version comes from the git tag"
                            : "The version comes from PlayerSettings";

            var numberPart = !manageBuildNumber
                ? "the build number is left alone"
                : policy == BuildNumberPolicy.AutoIncrementOnSuccess
                    ? "the build number increments after every successful build"
                    : policy == BuildNumberPolicy.GitCommitCount
                        ? "the build number is the git commit count"
                        : policy == BuildNumberPolicy.Timestamp
                            ? "the build number is a timestamp"
                            : "the build number is whatever is stored here";

            return versionPart + " and " + numberPart + ".";
        }

        private static PropertyField Field(SerializedProperty property, string label)
        {
            var field = new PropertyField(property, label);
            field.Bind(property.serializedObject);
            return field;
        }

        private static VisualElement Indented()
        {
            var container = new VisualElement();
            container.AddToClassList("bmk-versioning__details");
            return container;
        }

        private static void Show(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
