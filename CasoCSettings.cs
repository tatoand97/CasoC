namespace CasoC;

internal sealed class CasoCSettings
{
    public const string SectionName = "CasoC";

    public string? AzureOpenAiEndpoint { get; init; }

    public string? AzureOpenAiDeployment { get; init; }

    public string? OrderAgentId { get; init; }
}
