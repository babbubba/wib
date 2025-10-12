namespace WIB.Application.Contracts.Ml;

public class MlCandidateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Conf { get; set; }
}

public class MlSuggestionsResponse
{
    public List<MlCandidateDto> TypeCandidates { get; set; } = new();
    public List<MlCandidateDto> CategoryCandidates { get; set; } = new();
}

public class MlPredictionResult
{
    public Guid? TypeId { get; set; }
    public Guid? CategoryId { get; set; }
    public float Confidence { get; set; }
}

public class MlFeedbackRequest
{
    public string LabelRaw { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public Guid FinalTypeId { get; set; }
    public Guid? FinalCategoryId { get; set; }
}

