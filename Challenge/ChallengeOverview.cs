namespace MMONavigator.Services;

public class ChallengeOverview {
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Image { get; set; }
    public string? Video { get; set; }
    public string? Audio { get; set; }
    public string? MapFilePath { get; set; }
    public string? AudioDescription { get; set; }
    public List<ChallengeSpecs>? Events { get; set; }
}