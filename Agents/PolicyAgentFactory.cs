using Azure.AI.Projects.OpenAI;

namespace CasoC.Agents;

internal sealed class PolicyAgentFactory
{
    internal const string AgentName = "policy-agent-casec";

    private const string PolicyInstructions =
        """
        Recibes datos validados de una orden en formato JSON.
        Evalua solo si requiere accion adicional usando exclusivamente los campos provistos.
        No inventes datos.
        Devuelve un unico objeto JSON valido sin markdown ni texto adicional con este formato exacto:
        {"requiresAction": true|false, "message": "explicacion breve"}
        No menciones herramientas, MCP, agentes, servicios ni backend.
        """;

    internal static PromptAgentDefinition Build(string deployment)
    {
        return new PromptAgentDefinition(deployment)
        {
            Instructions = PolicyInstructions,
        };
    }
}
