using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

// ============================================================
//  EnvVarNonParallel
//
//  WHY:
//  - Some tests temporarily set process-wide environment variables
//    (workspace root, command allowlist).
//  - These MUST NOT run in parallel, otherwise tests will flake.
// ============================================================

[CollectionDefinition("EnvVarNonParallel", DisableParallelization = true)]
public sealed class EnvVarNonParallelCollection
{
}