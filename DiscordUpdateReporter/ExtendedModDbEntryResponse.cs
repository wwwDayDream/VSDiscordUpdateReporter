using System.Net;

namespace DiscordUpdateReporter;

public class ExtendedModDbEntryResponse
{
    public ExtendedModDbEntry? Mod { get; set; }
    public HttpStatusCode StatusCode { get; set; }
}