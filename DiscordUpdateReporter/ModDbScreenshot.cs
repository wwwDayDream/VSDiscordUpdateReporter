using System;

namespace DiscordUpdateReporter;

public class ModDbScreenshot
{
    public int FileId { get; set; }
    public string MainFile { get; set; }
    public string Filename { get; set; }
    public string ThumbnailFilename { get; set; }
    public DateTimeOffset Created { get; set; }
}