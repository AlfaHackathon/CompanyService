using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Api.Contracts.PublicKnowledge;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record KnowledgeSourceInput([Required, MaxLength(200)] string MaterialId,
    [Required, MaxLength(200)] string VersionId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PutCompanyKnowledgeRequest([Required, MaxLength(16000)] string Description,
    [Required, MaxLength(64)] string[] IndustryCodes, [Required, MaxLength(64)] string[] Exposures,
    [Required, MinLength(1), MaxLength(64)] KnowledgeSourceInput[] SourceRefs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCompanyMaterialRequest([Required, MaxLength(1000)] string Title,
    [Required, MaxLength(200000)] string Text);
