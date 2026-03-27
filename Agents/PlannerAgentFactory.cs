using Azure.AI.Projects.Agents;

namespace CasoC.Agents;

internal static class PlannerAgentFactory
{
    private const string PlannerInstructions =
        """
        You are a planner/orchestrator agent.
        Your job is to answer the user by delegating through the configured A2A tools.
        Rules:
        1. If the request requires order data, call the Order agent first.
        2. After receiving the order data, call the Policy agent to produce the final user-facing response.
        3. Do not invent data.
        4. Do not expose implementation details.
        5. Do not mention tools, protocols, connections, or agents.
        6. Return only the final consolidated answer for the user.
        """;

    internal static PromptAgentDefinition Build(
        string modelDeploymentName,
        A2AToolBinding orderBinding,
        A2AToolBinding policyBinding)
    {
        EnsureDistinctBindings(orderBinding, policyBinding);

        PromptAgentDefinition definition = new(modelDeploymentName)
        {
            Instructions = PlannerInstructions,
        };

        definition.Tools.Add(CreateOrderA2ATool(orderBinding));
        definition.Tools.Add(CreatePolicyA2ATool(policyBinding));

        return definition;
    }

    private static void EnsureDistinctBindings(A2AToolBinding orderBinding, A2AToolBinding policyBinding)
    {
        if (string.Equals(orderBinding.Id, policyBinding.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "PlannerAgent requires two distinct A2A connections: one for OrderAgent and one for PolicyAgent.");
        }
    }

    private static A2APreviewTool CreateOrderA2ATool(A2AToolBinding binding)
    {
        if (!string.Equals(binding.TargetAgent, "OrderAgent", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The first PlannerAgent A2A tool must target OrderAgent.");
        }

        return CreateA2ATool(binding);
    }

    private static A2APreviewTool CreatePolicyA2ATool(A2AToolBinding binding)
    {
        if (!string.Equals(binding.TargetAgent, "PolicyAgent", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The second PlannerAgent A2A tool must target PolicyAgent.");
        }

        return CreateA2ATool(binding);
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
