using Azure.AI.Projects.OpenAI;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CasoC.Agents;

internal sealed partial class AgentToolFactory
{
    private static readonly Regex ToolNamePattern = MyRegex();

    internal static AgentTool CreateAgentTool(string agentId, string toolName)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new InvalidOperationException("Agent tool requires a non-empty agentId.");
        }

        if (string.IsNullOrWhiteSpace(toolName) || !ToolNamePattern.IsMatch(toolName))
        {
            throw new InvalidOperationException(
                $"Agent tool name '{toolName}' is invalid. Expected pattern: ^[a-z0-9_-]+$.");
        }

        AgentToolPayload payloadModel = new("agent", agentId, toolName);
        BinaryData payload = BinaryData.FromString(JsonSerializer.Serialize(payloadModel));

        AgentTool? tool = ModelReaderWriter.Read<AgentTool>(payload) ?? throw new InvalidOperationException(
                $"Failed to deserialize agent tool payload for toolName '{toolName}'.");

        return tool;
    }

    private sealed record AgentToolPayload(string Type, string Agent_id, string Name);

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

}

