# Tasks Document

- [x] 1. Add event module interfaces
  - File: `src/Aevatar.Agents.AI.Core/EventModules/IEventModule.cs`
  - Define `IEventModule` and `IEventModuleHost`
  - _Requirements: 1_

- [x] 2. RoleAIGAgent hosts modules and dispatches events
  - File: `src/Aevatar.Agents.AI.Core/RoleAIGAgent.cs`
  - Add module registry + deterministic dispatch + best-effort error handling
  - _Requirements: 2_

- [x] 3. StepExecution handler adapts into module
  - File: `src/Aevatar.Agents.AI.Core/RoleAIGAgent.cs`
  - `SetStepExecutionHandler` wraps handler as module
  - _Requirements: 3_

- [x] 4. YAML module assembly
  - File: `src/Aevatar.Agents.AI.Core/RoleAgentFactory.cs`
  - Parse `extensions.event_modules` + `extensions.event_routes`
  - Instantiate module set and bind routes
  - _Requirements: 4_

- [ ] 5. Cognitive module adapters
  - Files:
    - `src/Aevatar.Agents.Cognitive/Execution/*Module.cs` (new)
  - Wrap `WorkflowOrchestrator` / `CognitiveStepExecutor` / trace into modules
  - _Requirements: 1, 4_

- [x] 6. Route matching and filtering
  - File: `src/Aevatar.Agents.AI.Core/EventModules/*` (new)
  - Implement simple route filters (event.type / step_type)
  - _Requirements: 4_

- [ ] 7. Documentation + examples
  - File: `src/Aevatar.Agents.Cognitive/docs/COORDINATOR_OVERVIEW.md`
  - Add YAML module assembly example
  - _Requirements: 4_
