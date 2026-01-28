namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for file-based agent mailbox service.
/// Implementation: Infrastructure.MongoDB.Workspace.FileMailboxService
///
/// This is currently a marker interface for DI injection.
/// Methods are not exposed here because they depend on SraMailboxMessage (protobuf)
/// from Agents.Contracts, which Infrastructure.Domain does not reference.
/// Domain-layer consumers resolve this via DI and cast or use it through
/// orchestration wiring in Agents.Domain.
/// </summary>
public interface IFileMailboxService
{
}
