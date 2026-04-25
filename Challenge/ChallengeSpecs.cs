namespace MMONavigator.Services;

public class ChallengeSpecs {
    public string? ParentId { get; set; }
    public string LocationId { get; set; }
    public int? OrderNumber { get; set; }
    public bool IsOrdered { get; set; }
    public List<string> PreRequisites { get; set; } = [];
    public string? Description { get; set; }
    public string? Image { get; set; }
    public string? Video { get; set; }
    public string? Audio { get; set; }
    public string? AudioDescription { get; set; }
    public CoordinateData? Coordinates { get; set; }
    public string? CoordinatesLabel { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public decimal DistanceTrigger { get; set; }
    public bool IncludeElevation { get; set; }
    public string? SoundEnter { get; set; }
    public string? SoundExit { get; set; }
    public bool StartTimer { get; set; }
    public bool EndTimer { get; set; }
    public bool LogTime { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsHidden { get; set; }
    public bool LockLocationEntry { get; set; } = true;
    public string? NextHint { get; set; }
    public string? Hint { get; set; }
    public string? HintImage { get; set; }
    public string? HintAudio { get; set; }
    public string? HintAudioDescription { get; set; }
    public string? HintVideo { get; set; }
    public string? HintVideoDescription { get; set; }
    public int? DwellTimeInSeconds { get; set; }
    public int? DwellTimeInFeet { get; set; }
    public bool DrawDwellCircle { get; set; }
    public bool MustStayInCircle { get; set; }
    public bool MustKeepHeartBeat { get; set; }
    public bool TriggerFogOfWarOn { get; set; }
    public bool TriggerFogOfWarOff { get; set; }
    public decimal? HeartBeatGracePeriodSeconds { get; set; }
    public decimal? TimeLimitToNextLocationInSeconds { get; set; }
    public bool IsStartLocation { get; set; }
    public bool IsEndLocation { get; set; }
    public decimal? MaxVelocity { get; set; }
    public List<string> InvalidationZones { get; set; } = []; //if these zones are triggered before the next lcoation, the challenge is invalidated
    public string? ChallengeAction { get; set; } //What happens when the whole chain finishes?
    public string? ChallengeCompleteImage { get; set; }
    public string? ChallengeCompleteVideo { get; set; }
    public string? ChallengeCompleteAudio { get; set; }
    public string? ChallengeCompleteMessage { get; set; }
    public string? ChallengeFailedImage { get; set; }
    public string? ChallengeFailedVideo { get; set; }
    public string? ChallengeFailedAudio { get; set; }
    public string? ChallengeFailedMessage { get; set; }
}