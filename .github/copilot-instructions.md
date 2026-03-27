# Copilot Instructions

## Project Directives

- Treat `CasoC` as a pure Foundry bootstrap repository for a real Caso C A2A topology.
- Use only the `CasoCA2ASettings` contract documented in `README.md`.
- `OrderAgent` is external and must only be validated through `CasoC:OrderAgentId`.
- `PolicyAgent` and `PlannerAgent` are created or reconciled in this repository.
- `PlannerAgent` must keep exactly two `A2APreviewTool` bindings and must not use direct MCP.
