using AutoMapper;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// AutoMapper profile for Knowledge module mappings.
/// </summary>
public class KnowledgeAutoMapperProfile : Profile
{
    public KnowledgeAutoMapperProfile()
    {
        // Add mappings here if needed
        // For now, the module primarily uses protobuf contracts directly
        // Example:
        // CreateMap<FactProposal, FactProposalDto>();
    }
}
