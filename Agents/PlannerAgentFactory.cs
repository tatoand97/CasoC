using Azure.AI.Projects.Agents;

namespace CasoC.Agents;

internal static class PlannerAgentFactory
{
    private const string PlannerInstructions =
        """
        You are a planner/orchestrator agent.
        Your job is to handle the user request by delegating work through the configured A2A tools.
        Rules:
        1. If the request requires order data, call the Order agent first.
        2. Once order data is available, call the Policy agent to transform it into a clear final response.
        3. Do not invent data.
        4. Do not expose internal implementation details.
        5. Do not mention tools, connections, protocols, or agents.
        6. Return only the final consolidated answer for the user.
        """;

    internal static PromptAgentDefinition Build(
        string modelDeploymentName,
        A2AToolBinding orderBinding,
        A2AToolBinding policyBinding)
    {
        PromptAgentDefinition definition = new(modelDeploymentName)
        {
            Instructions = PlannerInstructions,
        };

        definition.Tools.Add(CreateA2ATool(orderBinding));
        definition.Tools.Add(CreateA2ATool(policyBinding));

        return definition;
    }

    private static A2APreviewTool CreateA2ATool(A2AToolBinding binding)
    {
        A2APreviewTool tool = new()
        {
            ProjectConnectionId = binding.Id,
        };

        if (binding.BaseUri is not null)
        {
            tool.BaseUri = binding.BaseUri;
        }

        return tool;
    }
}
