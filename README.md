# CasoC - Bootstrap Foundry-only (.NET 8)

`CasoC` es un repositorio de bootstrap/logical IaC para Azure AI Foundry. Su responsabilidad es validar configuracion y acceso al proyecto, validar el agente externo `OrderAgent`, reconciliar los agentes propios del caso y terminar.

## Que hace este repo

- Valida `CasoC:AzureOpenAiEndpoint`
- Valida `CasoC:AzureOpenAiDeployment`
- Valida el agente externo configurado en `CasoC:OrderAgentId`
- Reconcilia `policy-agent-casec`
- Reconcilia `planner-agent-casec-orchestrated`
- Imprime un resumen final de bindings, ids y versiones

## Modelo operativo

- `OrderAgent` es externo a este repo. `CasoC` solo valida que exista y pueda ser referenciado.
- `PolicyAgent` se crea o actualiza por reconciliacion.
- `PlannerAgent` se crea o actualiza por reconciliacion.
- El proceso termina despues del bootstrap. No hay ejecucion funcional posterior.

## Configuracion

Configura `appsettings.json` en la raiz del proyecto:

```json
{
  "CasoC": {
    "AzureOpenAiEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAiDeployment": "<deployment-name>",
    "OrderAgentId": "<existing-order-agent-id>"
  }
}
```

## Ejecutar

```powershell
dotnet run
```

## Salida esperada

```text
[CONFIG] Endpoint validado => https://<resource>.services.ai.azure.com/api/projects/<project>
[CONFIG] Deployment validado => gpt-5.1-chat
[VALIDATION] OrderAgentId validado => OrderAgent (latest version: 3)
[RECONCILE] policy-agent-casec => unchanged (id: policy-agent-casec:3, version: 3)
[RECONCILE] planner-agent-casec-orchestrated => created (id: planner-agent-casec-orchestrated:1, version: 1)
[SUMMARY] Bindings => OrderAgent=OrderAgent (id: agent-id, version: 3); PolicyAgent=policy-agent-casec (id: policy-agent-casec:3, version: 3); PlannerAgent=planner-agent-casec-orchestrated (id: planner-agent-casec-orchestrated:1, version: 1)
[SUMMARY] Foundry bootstrap completed
```

## Que ya no hace este repo

- No consume agentes
- No ejecuta prompts
- No orquesta `OrderAgent -> PolicyAgent -> PlannerAgent`
- No usa Responses API como runtime
- No expone API HTTP
- No imprime respuestas de negocio para ordenes

## Out of scope

- runtime consumption
- order/policy/planner orchestration
- prompt execution
- API exposure

## Requisitos

- .NET SDK 8+
- Endpoint de Azure AI Foundry Project
- Permisos para listar agentes, leer deployments y crear versiones de agentes
- Credenciales validas para `AzureCliCredential`
