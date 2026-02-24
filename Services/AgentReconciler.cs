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

    internal string BuildDefinitionSignature(AgentDefinition definition)
    {
        using JsonDocument document = JsonDocument.Parse(BinaryData.FromObjectAsJson(definition).ToString());

        string model = ReadString(document.RootElement, "model", "Model");
        string instructions = ReadString(document.RootElement, "instructions", "Instructions");

        List<ToolSignature> tools = [];

        JsonElement toolsElement = document.RootElement.TryGetProperty("tools", out JsonElement t1)
            ? t1
            : document.RootElement.TryGetProperty("Tools", out JsonElement t2)
                ? t2
                : default;

        if (toolsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tool in toolsElement.EnumerateArray())
            {
                string toolType = ReadString(tool, "type", "Type");
                string agentId = string.Equals(toolType, "agent", StringComparison.OrdinalIgnoreCase)
                    ? ReadString(tool, "agent_id", "agentId", "AgentId")
                    : string.Empty;
                string agentName = string.Equals(toolType, "agent", StringComparison.OrdinalIgnoreCase)
                    ? ReadString(tool, "agent_name", "agentName", "AgentName")
                    : string.Empty;
                string toolName = ReadString(tool, "name", "Name");

                tools.Add(new ToolSignature(
                    toolType,
                    agentId,
                    agentName,
                    toolName));
            }
        }

        List<ToolSignature> orderedTools =
        [
            .. tools.OrderBy(x => x.Type, StringComparer.Ordinal)
                .ThenBy(x => x.AgentId, StringComparer.Ordinal)
                .ThenBy(x => x.AgentName, StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
        ];

        return JsonSerializer.Serialize(new
        {
            model,
            instructions,
            tools = orderedTools,
        });
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

    private readonly record struct ToolSignature(string Type, string AgentId, string AgentName, string Name);
}

internal sealed record ReconcileResult(AgentVersion Version, string ReconciliationStatus, string Signature);

