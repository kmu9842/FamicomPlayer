using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;

internal sealed record VideoMetadata(string Title, string Url, string VideoId);

internal static class YouTubeMetadata
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<string, VideoMetadata> titles = new();
    private static readonly ConcurrentDictionary<string, BitmapSource> covers = new();

    internal static string VideoId(string url)
    {
        var match = Regex.Match(url, @"[?&]v=([a-zA-Z0-9_-]{11})(?:&|$)");
        return match.Success ? match.Groups[1].Value : "";
    }

    internal static async Task<VideoMetadata> Resolve(string input, CancellationToken cancellation = default)
    {
        string url = YouTubeAddress.Parse(input).AbsoluteUri;
        if (titles.TryGetValue(url, out var cached)) return cached;
        using var response = await client.GetAsync("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(url), cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        string title = json.RootElement.GetProperty("title").GetString()?.Trim() ?? "";
        if (title.Length == 0) throw new InvalidDataException("영상 제목을 가져오지 못했습니다.");
        string id = VideoId(url);
        if (id.Length == 0 && json.RootElement.TryGetProperty("thumbnail_url", out var thumbnail))
        {
            var match = Regex.Match(thumbnail.GetString() ?? "", @"/vi/([a-zA-Z0-9_-]{11})/");
            if (match.Success) id = match.Groups[1].Value;
        }
        var metadata = new VideoMetadata(title.Length > 200 ? title[..200] : title, url, id);
        titles[url] = metadata; return metadata;
    }

    internal static async Task<BitmapSource?> Thumbnail(string videoId, CancellationToken cancellation = default)
    {
        if (!Regex.IsMatch(videoId, @"^[a-zA-Z0-9_-]{11}$")) return null;
        if (covers.TryGetValue(videoId, out var cached)) return cached;
        // These 16:9 variants avoid the black bars baked into hqdefault.jpg.
        foreach (string resolution in new[] { "maxresdefault", "mqdefault" })
        {
            try
            {
                using var response = await client.GetAsync($"https://i.ytimg.com/vi/{videoId}/{resolution}.jpg", cancellation);
                if (!response.IsSuccessStatusCode) continue;
                using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync(cancellation));
                BitmapSource frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (frame.PixelWidth < 200) continue; // YouTube's missing-image placeholder.
                frame.Freeze(); covers[videoId] = frame; return frame;
            }
            catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
            catch (HttpRequestException) { }
            catch (NotSupportedException) { }
        }
        return null;
    }
}
