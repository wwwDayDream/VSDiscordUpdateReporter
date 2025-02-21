using System;
using System.Globalization;
using Newtonsoft.Json;

namespace DiscordUpdateReporter;

public class ExtendedModDbEntryRelease
{
    public int? ReleaseId { get; set; }
    public string? MainFile { get; set; }
    public string? Filename { get; set; }
    public int? FileId { get; set; }
    public int? Downloads { get; set; }
    public string[]? Tags { get; set; }
    public string? ModIdStr { get; set; }
    public string? ModVersion { get; set; }
    public string Created { get; set; }

    [JsonIgnore]
    public DateTimeOffset CorrectedCreatedDate =>
        DateTimeOffset.Parse(Created.TrimEnd() + "Z");
}