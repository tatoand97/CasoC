# CasoC

`CasoC` es un repositorio de bootstrap / IaC logico puro para un Caso C A2A real en Microsoft Foundry sobre .NET 10.

El repo valida el proyecto Foundry, valida el deployment del modelo, valida un `OrderAgent` externo existente, crea o reconcilia `PolicyAgent`, valida dos conexiones A2A y crea o reconcilia `PlannerAgent` con dos A2A tools. No consume prompts de negocio, no ejecuta flujo funcional y no expone API HTTP.

## Target Architecture

Usuario
  |
  v
PlannerAgent
  |- A2A -> OrderAgent
  '- A2A -> PolicyAgent

Y dentro de `OrderAgent`:

OrderAgent
  |
  v
MCP
  |
  v
APIM
  |
  v
API REST

## Repository Responsibilities

- Validar `CasoC:ProjectEndpoint`
- Validar acceso al proyecto Foundry
- Validar `CasoC:ModelDeploymentName`
- Validar `CasoC:OrderAgentId` como agente externo existente
- Crear o reconciliar `PolicyAgent`
- Validar `CasoC:OrderA2AConnectionName`
- Validar `CasoC:PolicyA2AConnectionName`
- Crear o reconciliar `PlannerAgent` con exactamente dos `A2APreviewTool`
- Imprimir resumen final de ids, nombres, versiones y conexiones
- Terminar

## Prerequisites

- Un proyecto de Microsoft Foundry accesible por endpoint de proyecto
- Un model deployment existente en el mismo proyecto
- Un `OrderAgent` existente en el mismo proyecto, creado por otro repo o proceso
- Una A2A connection existente para el endpoint de `OrderAgent`
- Una A2A connection existente para el endpoint de `PolicyAgent`
- Credenciales validas para `DefaultAzureCredential`

## Configuration

Configura `appsettings.json` con esta seccion:

```json
{
  "CasoC": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "<existing-order-agent-id-or-version-id>",
    "PolicyAgentName": "policy-agent-casec-a2a",
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
- `OrderAgentId` es requerido
- `PolicyAgentName` es requerido
- `PlannerAgentName` es requerido
- `OrderA2AConnectionName` es requerido
- `PolicyA2AConnectionName` es requerido
- `OrderA2ABaseUri` y `PolicyA2ABaseUri` son opcionales y solo se usan cuando la connection no es `RemoteA2A`

## Official A2A Pattern

Este repo sigue el patron oficial A2A de Foundry:

- `PlannerAgent` usa A2A tools y no workflow
- `PlannerAgent` no usa MCP directo
- `PlannerAgent` no usa tools tipo `agent`
- `OrderAgent` sigue siendo externo y solo se valida
- `PolicyAgent` se crea o reconcilia aqui
- `PlannerAgent` se crea o reconcilia aqui
- El consumer del caso debe invocar solo a `PlannerAgent`
- Las conexiones A2A se crean previamente en Foundry portal y el codigo solo las resuelve por nombre

## Operational Note

Si `PolicyAgent` se crea o actualiza en este repo pero la connection A2A de Policy todavia no existe, el programa aborta claramente antes de reconciliar `PlannerAgent`.

Esto permite una secuencia operativa de dos pasos cuando haga falta:

1. Ejecutar bootstrap para crear o reconciliar `PolicyAgent`
2. Crear manualmente la connection A2A de Policy en Foundry portal
3. Reejecutar bootstrap para reconciliar `PlannerAgent`

## Execution

```powershell
dotnet run
```

La ejecucion es solo de bootstrap. Este repo no acepta prompts de negocio y no invoca runtime funcional de `OrderAgent` ni de `PolicyAgent`.

## Expected Console Shape

```text
[CONFIG] Endpoint validated => https://<resource>.services.ai.azure.com/api/projects/<project>
[VALIDATION] Project access validated
[CONFIG] Deployment validated => <deployment-name>
[VALIDATION] OrderAgent validated => id: <order-agent-id>, name: <order-agent-name>, version: <order-agent-version>
[RECONCILE] policy-agent-casec-a2a => created|updated|unchanged
[VALIDATION] Order A2A connection validated => name: <order-connection>, id: <order-connection-id>, type: <order-connection-type>
[VALIDATION] Policy A2A connection validated => name: <policy-connection>, id: <policy-connection-id>, type: <policy-connection-type>
[RECONCILE] planner-agent-casec-a2a => created|updated|unchanged
[SUMMARY] PlannerAgent bootstrap for A2A completed
```

## Out of Scope

- Workflow orchestration
- Backend fan-out
- Direct MCP from `PlannerAgent`
- Direct runtime invocation of `OrderAgent` desde este repo
- Direct runtime invocation of `PolicyAgent` desde este repo
- Prompt consumption
- Business-flow execution
- HTTP API exposure
