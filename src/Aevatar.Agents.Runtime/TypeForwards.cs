using System.Runtime.CompilerServices;
using Aevatar.Agents.Core.Factory;

// ============================================================
//  Type forwarding shim
//
//  中文 + ASCII:
//  - `Aevatar.Agents.Runtime` is kept as a compatibility wrapper package.
//  - Runtime shared base types are implemented in `Aevatar.Agents.Runtime.Abstractions`.
//  - Forward types so existing references keep working.
// ============================================================

[assembly: TypeForwardedTo(typeof(GAgentActorFactoryBase))]

