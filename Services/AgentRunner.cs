using Azure.AI.Projects.OpenAI;
using OpenAI.Responses;
using System.ClientModel;

namespace CasoC.Services;

internal sealed class AgentRunner
{
    private readonly TimeSpan _maxBackoff;

    internal AgentRunner(TimeSpan maxBackoff)
    {
        if (maxBackoff <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Max backoff must be a positive time span.");
        }

        _maxBackoff = maxBackoff;
    }

    internal async Task<string> RunPromptAsync(
        ProjectOpenAIClient openAi,
        string plannerAgentName,
        string prompt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Timeout must be a positive time span.");
        }

        ProjectResponsesClient responseClient = openAi.GetProjectResponsesClientForAgent(plannerAgentName);
        cancellationToken.ThrowIfCancellationRequested();
        ClientResult<ResponseResult> created = await responseClient.CreateResponseAsync(prompt);
        ResponseResult response = created.Value;

        TimeSpan delay = TimeSpan.FromSeconds(1);
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (true)
        {
            if (response.Status == ResponseStatus.Completed)
            {
                return response.GetOutputText();
            }

            if (response.Status is ResponseStatus.Failed or ResponseStatus.Incomplete or ResponseStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    $"Planner response ended in terminal status '{response.Status}'. ResponseId: {response.Id}. Error: {response.Error?.Message ?? "n/a"}");
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:F0}s while polling response '{response.Id}'. LastStatus: {response.Status}");
            }

            TimeSpan wait = delay <= remaining ? delay : remaining;
            await Task.Delay(wait, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ClientResult<ResponseResult> latest = await responseClient.GetResponseAsync(response.Id);
            response = latest.Value;

            double nextSeconds = Math.Min(delay.TotalSeconds * 2, _maxBackoff.TotalSeconds);
            delay = TimeSpan.FromSeconds(nextSeconds);
        }
    }
}

