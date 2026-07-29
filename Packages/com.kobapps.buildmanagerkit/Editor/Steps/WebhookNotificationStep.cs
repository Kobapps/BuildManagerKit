using System;
using System.Net.Http;
using System.Text;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>Payload shape posted by <see cref="WebhookNotificationStep"/>.</summary>
    public enum WebhookFormat
    {
        /// <summary>Slack incoming webhook: <c>{"text": "..."}</c>.</summary>
        Slack = 0,

        /// <summary>Discord webhook: <c>{"content": "..."}</c>.</summary>
        Discord = 1,

        /// <summary>Microsoft Teams connector card.</summary>
        MicrosoftTeams = 2,

        /// <summary>The full build result as JSON.</summary>
        RawJson = 3
    }

    /// <summary>
    /// Posts a build notification to a chat webhook. The URL is normally read from an environment
    /// variable so it never ends up in version control.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Notifications/Post To Webhook",
        Tooltip = "Sends a build notification to Slack, Discord, Teams or any HTTP endpoint.",
        Scope = BuildStepScope.PostBuild,
        Order = 40)]
    public sealed class WebhookNotificationStep : BuildStep
    {
        [Tooltip("Name of the environment variable holding the webhook URL. Preferred over the inline URL.")]
        [SerializeField] private string m_UrlEnvironmentVariable = "BMK_WEBHOOK_URL";

        [Tooltip("Webhook URL used when the environment variable is not set. Avoid committing secrets.")]
        [SerializeField] private string m_Url = string.Empty;

        [SerializeField] private WebhookFormat m_Format = WebhookFormat.Slack;

        [Tooltip("Message body. Tokens are replaced; {status} and {duration} are added by this action.")]
        [TextArea(2, 6)]
        [SerializeField] private string m_Message =
            "{status}: *{productName}* {version}+{buildNumber} · {envName} · {targetShort} · {branch}@{commit}";

        [Tooltip("Seconds to wait for the endpoint before giving up.")]
        [SerializeField, Min(1)] private int m_TimeoutSeconds = 20;

        /// <inheritdoc />
        public override string Summary => $"{m_Format} → " +
                                          (string.IsNullOrWhiteSpace(m_UrlEnvironmentVariable)
                                              ? m_Url
                                              : "$" + m_UrlEnvironmentVariable);

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(m_UrlEnvironmentVariable) && string.IsNullOrWhiteSpace(m_Url))
                report.AddError("No webhook URL and no environment variable configured.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var url = ResolveUrl(context);
            if (string.IsNullOrWhiteSpace(url))
            {
                context.Log.Warning(
                    $"No webhook URL available (environment variable '{m_UrlEnvironmentVariable}' is not set). Skipping.");
                return;
            }

            var status = context.Status == BuildRunStatus.Succeeded ? "SUCCESS" : context.Status.ToString().ToUpperInvariant();
            context.SetVariable("status", status);
            context.SetVariable("duration",
                BuildTargetUtility.FormatDuration(DateTime.Now - context.StartTime));
            context.RefreshTokens();

            var message = context.Resolve(m_Message);
            var payload = BuildPayload(context, message);

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would POST to the webhook: {message}");
                return;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(m_TimeoutSeconds) };
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = client.PostAsync(url, content).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                    context.Log.Info($"Webhook notified ({(int)response.StatusCode}).");
                else
                    context.Log.Warning($"The webhook returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            catch (Exception exception)
            {
                // A failed notification must never fail a build that otherwise succeeded.
                context.Log.Warning($"Could not reach the webhook: {exception.Message}");
            }
        }

        private string ResolveUrl(BuildContext context)
        {
            if (!string.IsNullOrWhiteSpace(m_UrlEnvironmentVariable))
            {
                var fromEnvironment = context.GetVariable(m_UrlEnvironmentVariable.Trim());
                if (!string.IsNullOrWhiteSpace(fromEnvironment))
                    return fromEnvironment.Trim();
            }

            return context.Resolve(m_Url).Trim();
        }

        private string BuildPayload(BuildContext context, string message)
        {
            switch (m_Format)
            {
                case WebhookFormat.Discord:
                    return "{\"content\":" + Quote(message) + "}";

                case WebhookFormat.MicrosoftTeams:
                    return "{\"@type\":\"MessageCard\",\"@context\":\"https://schema.org/extensions\","
                           + "\"themeColor\":\"" + (context.Status == BuildRunStatus.Succeeded ? "2EB886" : "D00000")
                           + "\",\"title\":" + Quote("Unity build " + context.Status)
                           + ",\"text\":" + Quote(message) + "}";

                case WebhookFormat.RawJson:
                    return context.ToResult(DateTime.Now - context.StartTime).ToJson();

                default:
                    return "{\"text\":" + Quote(message) + "}";
            }
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder("\"");

            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            builder.Append(character);
                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }
}
