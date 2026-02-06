using AutoMapper;
using Aevatar.VibeResearching.Comments.DTOs;
using Aevatar.VibeResearching.Comments.Entities;

namespace Aevatar.VibeResearching.Comments.Mapping;

/// <summary>
/// AutoMapper profile for Comments module mappings.
/// </summary>
public class CommentsAutoMapperProfile : Profile
{
    public CommentsAutoMapperProfile()
    {
        CreateMap<NodeComment, CommentDto>();

        CreateMap<NodeComment, CommentPreviewDto>()
            .ForMember(dest => dest.ContentPreview, opt => opt.MapFrom(src =>
                src.Content.Length > CommentsConsts.MaxPreviewContentLength
                    ? src.Content.Substring(0, CommentsConsts.MaxPreviewContentLength) + "..."
                    : src.Content));
    }
}
