# CasoC

`CasoC` es un repositorio de bootstrap / IaC logico puro para un Caso C A2A real en Microsoft Foundry sobre .NET 8.

Su responsabilidad es validar el proyecto Foundry, validar el deployment del modelo, validar las conexiones A2A requeridas y crear o reconciliar un unico `PlannerAgent`. Este repo no consume prompts de negocio, no hace fan-out desde backend y no ejecuta el caso en runtime.

## Architecture

- `PlannerAgent` es el unico punto de entrada del caso.
- `PlannerAgent` delega internamente por A2A tool.
- `PlannerAgent` no llama MCP directamente.
- `OrderAgent` resuelve la parte tecnica de ordenes.
- `PolicyAgent` transforma o formatea la respuesta final.
- La app consumidora solo debe invocar a `PlannerAgent`.

## What This Repo Does

- Valida `CasoC:ProjectEndpoint`
- Valida acceso al proyecto Foundry
- Valida `CasoC:ModelDeploymentName`
- Resuelve y valida `CasoC:OrderA2AConnectionName`
- Resuelve y valida `CasoC:PolicyA2AConnectionName`
- Exige `OrderA2ABaseUri` o `PolicyA2ABaseUri` cuando la conexion no es `RemoteA2A`
- Reconcilia unicamente `PlannerAgent`
- Imprime resumen final de bindings y prerequisitos
- Termina

## Prerequisites

- Un proyecto de Microsoft Foundry accesible por endpoint de proyecto
- Un model deployment existente en el mismo proyecto
- Una conexion A2A para el endpoint de Order ya creada en Foundry
- Una conexion A2A para el endpoint de Policy ya creada en Foundry
- Credenciales validas para `DefaultAzureCredential`

## Configuration

Configura `appsettings.json` con esta seccion:

```json
{
  "CasoC": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "PlannerAgentName": "planner-agent-casec-a2a",
    "OrderA2AConnectionName": "<order-a2a-connection-name>",
    "PolicyA2AConnectionName": "<policy-a2a-connection-name>",
    "OrderA2ABaseUri": "",
    "PolicyA2ABaseUri": ""
  }
}
```

Reglas:

- `ProjectEndpoint` es requerido
- `ModelDeploymentName` es requerido
- `PlannerAgentName` es requerido
- `OrderA2AConnectionName` es requerido
- `PolicyA2AConnectionName` es requerido
- `OrderA2ABaseUri` y `PolicyA2ABaseUri` son opcionales y solo se usan cuando la conexion no es `RemoteA2A`

## Official A2A Pattern

Este repo sigue el patron oficial A2A de Foundry:

- La conexion A2A ya debe existir en el proyecto Foundry
- El codigo resuelve la conexion por nombre con `projectClient.Connections.GetConnection(...)`
- `PlannerAgent` usa `A2APreviewTool`
- No se usan workflows
- No se usan tools tipo `agent`
- No se agrega MCP directo al `PlannerAgent`

## Execution

```powershell
dotnet run
```

La ejecucion hace bootstrap del proyecto Foundry, imprime el resumen y termina. No existe fase de consumo funcional dentro de este repo.

## Expected Output

```text
[CONFIG] Endpoint validated => https://<resource>.services.ai.azure.com/api/projects/<project>
[VALIDATION] Project access validated
[VALIDATION] Model deployment validated => <deployment-name>
[VALIDATION] Order A2A connection validated => name: <order-connection>, id: <order-id>, type: <order-type>
[VALIDATION] Policy A2A connection validated => name: <policy-connection>, id: <policy-id>, type: <policy-type>
[RECONCILE] planner-agent-casec-a2a => created|updated|unchanged (id: <planner-id>, version: <version>)
[SUMMARY] PlannerAgent => name: planner-agent-casec-a2a, id: <planner-id>, version: <version>
[SUMMARY] Order A2A connection => name: <order-connection>, id: <order-id>, type: <order-type>
[SUMMARY] Policy A2A connection => name: <policy-connection>, id: <policy-id>, type: <policy-type>
[SUMMARY] PlannerAgent bootstrap for A2A completed
```

## Out of Scope

- Workflow orchestration
- Backend fan-out
- Direct MCP from `PlannerAgent`
- Direct runtime invocation of `OrderAgent` or `PolicyAgent` desde este repo
- Prompt execution
- Functional smoke tests
- API exposure
