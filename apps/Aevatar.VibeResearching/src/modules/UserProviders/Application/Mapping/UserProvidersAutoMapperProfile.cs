using AutoMapper;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.ValueObjects;

namespace Aevatar.VibeResearching.UserProviders.Application.Mapping;

/// <summary>
/// AutoMapper profile for UserProviders module.
/// </summary>
public sealed class UserProvidersAutoMapperProfile : Profile
{
    public UserProvidersAutoMapperProfile()
    {
        CreateMap<AvailableProvider, AvailableProviderDto>();
    }
}
