# CasoC

`CasoC` es un repositorio de bootstrap / logical IaC para Azure AI Foundry en .NET 8.

Su unica responsabilidad es preparar y reconciliar agentes dentro de un proyecto Foundry. Este repo no consume agentes, no ejecuta prompts y no orquesta flujo de negocio. El consumo y la orquestacion viven fuera de este repositorio, en otro repo o servicio.

## Scope

- Validar `CasoC:AzureOpenAiEndpoint`
- Validar acceso al proyecto Foundry
- Validar `CasoC:AzureOpenAiDeployment`
- Validar el agente externo configurado en `CasoC:OrderAgentId`
- Reconciliar `policy-agent-casec`
- Reconciliar `planner-agent-casec-orchestrated`
- Imprimir resumen final de bindings, ids y versiones
- Terminar con codigo `0` si todo sale bien

## Agentes

- `OrderAgent` es externo a este repo. `CasoC` solo valida que exista y pueda ser referenciado.
- `PolicyAgent` se crea o actualiza por reconciliacion.
- `PlannerAgent` se crea o actualiza por reconciliacion.

## Rol del repositorio

- Prepara el proyecto Foundry para que otros consumidores puedan enlazar los agentes esperados.
- Valida que `OrderAgentId` apunte a un agente externo existente.
- Reconciliacion de `policy-agent-casec` y `planner-agent-casec-orchestrated`.
- Termina despues del bootstrap; no existe fase de ejecucion de negocio en este repo.

## Lo que este repo no hace

- No ejecuta el flujo `OrderAgent -> PolicyAgent -> PlannerAgent`
- No consume ni invoca agentes como runtime
- No ejecuta prompts de prueba
- No hace polling de respuestas
- No imprime respuestas de negocio
- No expone API HTTP
- No actua como consola de consumo
- No contiene el servicio o repo que consume/orquesta estos agentes

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

## Ejecucion

```powershell
dotnet run
```

La ejecucion hace bootstrap del proyecto Foundry y termina. No existe una fase posterior de consumo.

## Salida esperada

```text
[CONFIG] Endpoint validado => https://<resource>.services.ai.azure.com/api/projects/<project>
[CONFIG] Deployment validado => gpt-5.1-chat
[VALIDATION] OrderAgentId validado => OrderAgent -> OrderAgent (id: agent-id, version: 3)
[RECONCILE] policy-agent-casec => unchanged (id: policy-agent-casec:3, version: 3)
[RECONCILE] planner-agent-casec-orchestrated => created (id: planner-agent-casec-orchestrated:1, version: 1)
[SUMMARY] Bindings => OrderAgent=OrderAgent (id: agent-id, version: 3); PolicyAgent=policy-agent-casec (id: policy-agent-casec:3, version: 3); PlannerAgent=planner-agent-casec-orchestrated (id: planner-agent-casec-orchestrated:1, version: 1)
[SUMMARY] Foundry bootstrap completed
```

## Out of scope

- runtime consumption
- order/policy/planner orchestration
- prompt execution
- API exposure

## Requisitos

- .NET SDK 8 o superior
- Endpoint de Azure AI Foundry Project
- Permisos para listar agentes, leer deployments y crear versiones de agentes
- Credenciales validas para `AzureCliCredential`
