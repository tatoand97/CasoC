using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using System.ClientModel;
using System.Text.Json;

namespace CasoC.Services;

internal sealed class AgentReconciler
{
    private readonly AIProjectClient _projectClient;

    internal AgentReconciler(AIProjectClient projectClient)
    {
        _projectClient = projectClient;
    }

    internal async Task<ReconcileResult> ReconcileAsync(
        string agentName,
        PromptAgentDefinition desiredDefinition,
        CancellationToken cancellationToken)
    {
        AgentVersion? latest = await TryGetLatestVersionAsync(agentName, cancellationToken);
        string desiredSignature = BuildDefinitionSignature(desiredDefinition);

        if (latest is null)
        {
            ClientResult<AgentVersion> created = await _projectClient.Agents.CreateAgentVersionAsync(
                agentName,
                new AgentVersionCreationOptions(desiredDefinition),
                cancellationToken);

            return new ReconcileResult(created.Value, "created", desiredSignature);
        }

        string currentSignature = BuildDefinitionSignature(latest.Definition);
        if (string.Equals(currentSignature, desiredSignature, StringComparison.Ordinal))
        {
            return new ReconcileResult(latest, "unchanged", desiredSignature);
        }

        ClientResult<AgentVersion> updated = await _projectClient.Agents.CreateAgentVersionAsync(
            agentName,
            new AgentVersionCreationOptions(desiredDefinition),
            cancellationToken);

        return new ReconcileResult(updated.Value, "updated", desiredSignature);
    }

    internal async Task<AgentVersion?> TryGetLatestVersionAsync(string agentName, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _projectClient.Agents.GetAgentAsync(agentName, cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            return null;
        }

        await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: 1,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            return version;
        }

        return null;
    }

    internal static string BuildDefinitionSignature(AgentDefinition definition)
    {
        using JsonDocument document = JsonDocument.Parse(BinaryData.FromObjectAsJson(definition).ToString());

        string model = ReadString(document.RootElement, "model", "Model");
        string instructions = ReadString(document.RootElement, "instructions", "Instructions");
        string tools = ReadToolsSignature(document.RootElement);

        return JsonSerializer.Serialize(new
        {
            model,
            instructions,
            tools,
        });
    }

    private static string ReadToolsSignature(JsonElement root)
    {
        JsonElement toolsElement = root.TryGetProperty("tools", out JsonElement t1)
            ? t1
            : root.TryGetProperty("Tools", out JsonElement t2)
                ? t2
                : default;

        return toolsElement.ValueKind == JsonValueKind.Array
            ? toolsElement.GetRawText()
            : "[]";
    }

    private static string ReadString(JsonElement element, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (element.TryGetProperty(candidate, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}

internal sealed record ReconcileResult(AgentVersion Version, string ReconciliationStatus, string Signature);
