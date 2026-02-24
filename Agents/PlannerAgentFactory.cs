using Azure.AI.Projects.OpenAI;

namespace CasoC.Agents;

internal sealed class PlannerAgentFactory
{
    internal const string AgentName = "planner-agent-casec";

    private const string PlannerInstructions =
        "Si la consulta requiere datos de órdenes, delega a OrderAgent. " +
        "Una vez recibas los datos, pásalos a PolicyAgent. " +
        "Devuelve únicamente la respuesta final consolidada. " +
        "No inventes datos. " +
        "No expongas detalles técnicos internos.";

    private readonly AgentToolFactory _toolFactory;

    internal PlannerAgentFactory(AgentToolFactory toolFactory)
    {
        _toolFactory = toolFactory;
    }

    internal PromptAgentDefinition Build(string deployment, string orderAgentId, string policyAgentId)
    {
        PromptAgentDefinition definition = new(deployment)
        {
            Instructions = PlannerInstructions,
            Tools =
            {
                _toolFactory.CreateAgentTool(orderAgentId, "order_agent"),
                _toolFactory.CreateAgentTool(policyAgentId, "policy_agent"),
            }
        };

        return definition;
    }
}
