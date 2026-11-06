using System.Net.Http.Headers;

namespace ArcanoCore.Application.Common;

public static class ContentTypes
{
    public const string OctetStream = "application/octet-stream";

    // File() in the API throws on a content type it cannot parse, which makes every download fail with 500.
    // This parser is no looser than the one File() uses, so a value it accepts can be downloaded.
    public static string OrOctetStream(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out _) ? contentType! : OctetStream;
}
