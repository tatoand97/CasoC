namespace CasoC;

internal sealed class CasoCSettings
{
    public const string SectionName = "CasoC";

    public string? ProjectEndpoint { get; init; }

    public string? ModelDeploymentName { get; init; }

    public string? OrderAgentId { get; init; }

    public string? PolicyAgentName { get; init; }

    public string? PlannerAgentName { get; init; }

    public string? OrderA2AConnectionName { get; init; }

    public string? PolicyA2AConnectionName { get; init; }

    public string? OrderA2ABaseUri { get; init; }

    public string? PolicyA2ABaseUri { get; init; }
}
