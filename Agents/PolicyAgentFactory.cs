using Azure.AI.Projects.Agents;

namespace CasoC.Agents;

internal static class PolicyAgentFactory
{
    private const string PolicyInstructions =
        """
        You receive structured order data.
        Rules:
        1. Write a clear and professional final response for the user.
        2. Do not add new information.
        3. Do not mention tools, protocols, connections, internal systems, or agents.
        4. Do not expose technical details.
        5. Return only the final user-facing response.
        """;

    internal static PromptAgentDefinition Build(string modelDeploymentName)
    {
        return new PromptAgentDefinition(modelDeploymentName)
        {
            Instructions = PolicyInstructions,
        };
    }
}
