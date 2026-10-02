using System.Diagnostics;
using Microsoft.Extensions.AI;
using Polly.Timeout;

namespace EnvReporter.Web;

public sealed class AgentClient(IChatClient chatClient, ILogger<AgentClient> logger)
{
    private static readonly ActivitySource ActivitySource = new("EnvReporter.Web.AgentClient");

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // Prompt 本文は属性に残さず、画面から Agent までの処理時間を追跡する。
        using var activity = ActivitySource.StartActivity("agent.responses");
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.prompt.length", prompt.Length);

        try
        {
            var response = await chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                cancellationToken: cancellationToken);

            var answer = response.Text;
            activity?.SetTag("gen_ai.response.length", answer.Length);
            logger.LogInformation("Agent response received for prompt length {PromptLength}", prompt.Length);
            return answer;
        }
        catch (TimeoutRejectedException ex)
        {
            throw new TimeoutException("Agent の応答が制限時間を超えました。", ex);
        }
    }
}
