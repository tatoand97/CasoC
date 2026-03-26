using Azure.AI.Projects.OpenAI;

namespace CasoC.Agents;

internal sealed class PlannerAgentFactory
{
    internal const string AgentName = "planner-agent-casec-composer";

    private const string PlannerInstructions =
        """
        Redactas la respuesta final para el usuario.
        Usa solo la solicitud original, los datos validados de la orden y el resultado validado de politica incluidos en el prompt.
        No inventes datos.
        No menciones herramientas, MCP, agentes, servicios ni backend.
        Responde de forma clara, breve y en lenguaje natural.
        """;

    internal static PromptAgentDefinition Build(string deployment)
    {
        return new PromptAgentDefinition(deployment)
        {
            Instructions = PlannerInstructions,
        };
    }
}
