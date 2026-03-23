using Azure.AI.Projects.OpenAI;

namespace CasoC.Agents;

internal sealed class PlannerAgentFactory
{
    internal const string AgentName = "planner-agent-casec-orchestrated";

    private const string PlannerInstructions =
        "Recibes contexto ya recopilado por la aplicación. " +
        "Sintetiza el resultado final de forma clara y breve. " +
        "Usa únicamente la información provista. " +
        "No inventes datos. " +
        "No expongas detalles técnicos internos.";

    internal static PromptAgentDefinition Build(string deployment)
    {
        return new PromptAgentDefinition(deployment)
        {
            Instructions = PlannerInstructions,
        };
    }
}
