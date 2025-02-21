using System;
using System.Text.Json.Serialization;

namespace DiscordUpdateReporter;

public class ExtendedModDbEntry
{
    public int? ModId { get; set; }
    public int? AssetId { get; set; }
    public string? Name { get; set; }
    public string? Text { get; set; }
    public string? Author { get; set; }
    public string? UrlAlias { get; set; }
    public string? LogoFilename { get; set; }
    public string? LogoFile { get; set; }
    public string? HomepageUrl { get; set; }
    public string? SourcecodeUrl { get; set; }
    public string? TrailerVideoUrl { get; set; }
    public string? IssueTrackerUrl { get; set; }
    public string? WikiUrl { get; set; }
    public int? Downloads { get; set; }
    public int? Follows { get; set; }
    public int? TrendingPoints { get; set; }
    public int? Comments { get; set; }
    public string? Side { get; set; }
    public string? Type { get; set; }
    public string? Created { get; set; }
    public string? LastModified { get; set; }
    public string[]? Tags { get; set; }
    public ModDbScreenshot[]? Screenshots { get; set; }
    public ExtendedModDbEntryRelease[]? Releases { get; set; }

    [JsonIgnore]
    public DateTimeOffset CorrectedLastModifiedDate =>
        DateTimeOffset.Parse(Created?.TrimEnd() + "Z");

    [JsonIgnore]
    public DateTimeOffset CorrectedCreatedDate =>
        DateTimeOffset.Parse(LastModified?.TrimEnd() + "Z");
}