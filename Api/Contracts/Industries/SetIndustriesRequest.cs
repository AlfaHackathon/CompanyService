namespace Api.Contracts.Industries;

public class SetIndustriesRequest
{
    public required List<string> IndustryCodes { get; set; }
}
