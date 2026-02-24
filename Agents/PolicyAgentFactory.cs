using Azure.AI.Projects.OpenAI;

namespace CasoC.Agents;

internal sealed class PolicyAgentFactory
{
    internal const string AgentName = "policy-agent-casec";

    private const string PolicyInstructions =
        "Recibe datos estructurados. " +
        "Redacta respuesta clara y profesional. " +
        "No agregues información nueva. " +
        "No menciones herramientas ni agentes.";

    internal PromptAgentDefinition Build(string deployment)
    {
        return new PromptAgentDefinition(deployment)
        {
            Instructions = PolicyInstructions,
        };
    }
}
