using System;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Samples
{
    /// <summary>
    /// Uploads the finished build to an S3 compatible bucket.
    ///
    /// Demonstrates the four things a production action usually needs: configuration fields, a
    /// useful collapsed summary, validation that runs before anything expensive happens, and dry
    /// run support.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Custom/Upload To CDN",
        Tooltip = "Uploads the build (or the zip archive, when one was produced) to a bucket.",
        Scope = BuildStepScope.PostBuild)]
    public sealed class UploadToCdnStep : BuildStep
    {
        [Tooltip("Bucket name, tokens allowed: releases/{env}")]
        [SerializeField] private string m_Bucket = "releases/{env}";

        [Tooltip("Environment variable holding the credentials. Never store secrets in the asset.")]
        [SerializeField] private string m_TokenEnvironmentVariable = "CDN_TOKEN";

        [Tooltip("Upload the .zip produced by the Zip Output action rather than the raw folder.")]
        [SerializeField] private bool m_PreferArchive = true;

        /// <inheritdoc />
        public override string Summary => m_Bucket;

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(m_Bucket))
                report.AddError("Bucket is empty.");

            // Fail the whole build in ten seconds instead of after a two hour IL2CPP compile.
            if (string.IsNullOrEmpty(context.GetVariable(m_TokenEnvironmentVariable)))
                report.AddError($"Environment variable '{m_TokenEnvironmentVariable}' is not set.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var bucket = context.Resolve(m_Bucket);

            // Zip Output publishes 'archivePath'; fall back to the raw output when it did not run.
            var source = m_PreferArchive
                ? context.GetVariable("archivePath", context.OutputPath)
                : context.OutputPath;

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would upload '{source}' to '{bucket}'.");
                return;
            }

            context.Log.Info($"Uploading '{source}' to '{bucket}'…");

            var result = ProcessRunner.RunShell(
                $"aws s3 cp {ProcessRunner.Quote(source)} s3://{bucket}/ --recursive",
                timeoutMs: 30 * 60 * 1000,
                onLine: (line, isError) =>
                    context.Log.Write(isError ? BuildLogLevel.Warning : BuildLogLevel.Debug, line));

            if (!result.Succeeded)
                throw new BuildStepException($"Upload failed with exit code {result.ExitCode}.");

            var url = $"https://cdn.example.com/{bucket}/{System.IO.Path.GetFileName(source)}";

            // Later actions — a webhook notification, for instance — can use {downloadUrl}.
            context.SetVariable("downloadUrl", url);
            context.Log.Success($"Uploaded: {url}");
        }
    }
}
