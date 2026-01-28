using AutoMapper;
using Aevatar.VibeResearching.Sessions.DTOs;
using Aevatar.VibeResearching.Sessions.ValueObjects;

namespace Aevatar.VibeResearching.Sessions.Application.Mapping;

/// <summary>
/// AutoMapper profile for Sessions module mappings.
/// </summary>
public class SessionsAutoMapperProfile : Profile
{
    public SessionsAutoMapperProfile()
    {
        // Map DTOs to Value Objects
        CreateMap<SessionInputDto, SessionInputValue>();
        CreateMap<VibeLoopDto, VibeLoopValue>();

        // Reverse mappings if needed
        CreateMap<SessionInputValue, SessionInputDto>();
        CreateMap<VibeLoopValue, VibeLoopDto>();
    }
}
