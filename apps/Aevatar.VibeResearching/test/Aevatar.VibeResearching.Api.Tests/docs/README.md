# Aevatar.VibeResearching.Api.Tests

## Structure
```
Aevatar.VibeResearching.Api.Tests/
├─ PaperCollabWorkflowTests.cs
├─ TestDoubles.cs
├─ TestEnvironments.cs
├─ WorkflowTestHelpers.cs
└─ docs/README.md
```

## Decisions
- Split fixtures, helpers, and doubles into dedicated files to keep test cases focused.
- Centralize workflow-variable helpers to avoid reflection logic duplication.

## Guidelines
- Keep tests in `*Tests.cs` files and avoid mixing fixtures into test classes.
- Put stubs/fakes in `TestDoubles.cs`.
- Put environment setup in `TestEnvironments.cs`.
- Use `WorkflowTestHelpers` for coordinator variables and DAG setup.

## Change Log
- 2026-02-01: Split `PaperCollabWorkflowTests.cs` helpers into dedicated files.
