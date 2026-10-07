using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamicomPlayer;

internal sealed record PlaylistMetadata(string Title, string Url, IReadOnlyList<VideoMetadata> Videos, int UnavailableCount, string Warning = "");

internal static class YouTubePlaylist
{
    internal sealed record Page(IReadOnlyList<VideoMetadata> Videos, string Continuation, int UnavailableCount);

    internal static async Task<PlaylistMetadata> Resolve(string input, CookieContainer? cookies, IProgress<int>? progress, CancellationToken cancellation)
    {
        using var handler = new HttpClientHandler { CookieContainer = cookies ?? new CookieContainer(), AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en;q=0.8");
        return await Resolve(input, async (uri, body, token) =>
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri);
            if (body != null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
                var jar = handler.CookieContainer.GetCookies(new Uri("https://www.youtube.com"));
                string? sapisid = jar["SAPISID"]?.Value ?? jar["__Secure-3PAPISID"]?.Value;
                if (!string.IsNullOrEmpty(sapisid))
                {
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(timestamp + " " + sapisid + " https://www.youtube.com"))).ToLowerInvariant();
                    request.Headers.TryAddWithoutValidation("Authorization", "SAPISIDHASH " + timestamp + "_" + hash);
                    request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
                }
            }
            using var response = await client.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, progress, cancellation);
    }

    // The transport overload lets tests exercise complete multi-page imports without a live account.
    internal static async Task<PlaylistMetadata> Resolve(string input, Func<Uri, string?, CancellationToken, Task<string>> fetch, IProgress<int>? progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string url = YouTubeAddress.PlaylistUrl(input), id = YouTubeAddress.PlaylistId(input);
        bool mix = id.StartsWith("RD", StringComparison.Ordinal);
        string fetchUrl = mix && YouTubeMetadata.VideoId(YouTubeAddress.Parse(input).AbsoluteUri).Length == 11 ? YouTubeAddress.Parse(input).AbsoluteUri : url;
        if (mix && fetchUrl == url && id.Length == 13) fetchUrl = "https://www.youtube.com/watch?v=" + id[2..] + "&list=" + id;
        string html = await fetch(new Uri(fetchUrl + "&hl=ko"), null, cancellation);
        using var initial = JsonDocument.Parse(ExtractAssignedJson(html, @"(?:var\s+)?ytInitialData\s*=|window\s*\[\s*[""']ytInitialData[""']\s*\]\s*="));
        var data = initial.RootElement;
        var renderer = Find(data, "playlistVideoListRenderer");
        if (renderer.ValueKind == JsonValueKind.Undefined && mix) renderer = Find(data, "playlistPanelRenderer");
        if (renderer.ValueKind == JsonValueKind.Undefined && mix) renderer = Property(Property(Find(data, "twoColumnWatchNextResults"), "playlist"), "playlist");
        // YouTube also serves playlist rows as video lockups in the selected browse tab.
        if (renderer.ValueKind == JsonValueKind.Undefined && !mix) renderer = Find(Property(data, "contents"), "itemSectionRenderer");
        if (renderer.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("재생목록을 열 수 없습니다. 비공개 목록은 먼저 Google 로그인 후 다시 시도해 주세요.");
        var titleRenderer = Find(data, "playlistMetadataRenderer");
        string title = Text(Property(titleRenderer, "title"));
        if (title.Length == 0) title = Text(Property(Find(data, "playlistHeaderRenderer"), "title"));
        if (title.Length == 0) title = Text(Property(renderer, "title"));
        if (title.Length == 0) title = "게임팩 모음";
        if (title.Length > 200) title = title[..200];
        var page = ReadPage(renderer);
        var videos = page.Videos.ToList(); int unavailable = page.UnavailableCount;
        progress?.Report(videos.Count);
        // Mixes are generated endlessly by YouTube; save the currently supplied queue as a snapshot.
        if (mix)
            return new(title, url, videos, unavailable, "자동 믹스는 끝이 없어 현재 제공된 " + videos.Count + "개 영상을 모음으로 저장합니다.");
        string continuation = page.Continuation;
        JsonElement context = default; string key = "";
        if (continuation.Length > 0)
        {
            using var config = JsonDocument.Parse(ExtractAssignedJson(html, @"ytcfg\.set\s*\("));
            context = Property(config.RootElement, "INNERTUBE_CONTEXT").Clone();
            key = Text(Property(config.RootElement, "INNERTUBE_API_KEY"));
            if (context.ValueKind != JsonValueKind.Object) throw new InvalidDataException("재생목록의 다음 페이지 정보를 읽지 못했습니다. 다시 시도해 주세요.");
        }
        var seenContinuations = new HashSet<string>();
        while (continuation.Length > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!seenContinuations.Add(continuation)) throw new InvalidDataException("재생목록의 다음 페이지가 반복되어 가져오기를 중단했습니다. 저장되지 않았으니 다시 시도해 주세요.");
            string body = JsonSerializer.Serialize(new { context, continuation });
            string payload = await fetch(new Uri("https://www.youtube.com/youtubei/v1/browse?prettyPrint=false" + (key.Length == 0 ? "" : "&key=" + Uri.EscapeDataString(key))), body, cancellation);
            using var document = JsonDocument.Parse(payload);
            var continuationItems = Find(document.RootElement, "appendContinuationItemsAction");
            if (continuationItems.ValueKind == JsonValueKind.Undefined) continuationItems = Find(document.RootElement, "reloadContinuationItemsCommand");
            if (continuationItems.ValueKind == JsonValueKind.Undefined) continuationItems = Find(document.RootElement, "playlistVideoListContinuation");
            if (continuationItems.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("재생목록의 다음 페이지를 가져오지 못했습니다. 전체 목록은 아직 저장되지 않았습니다.");
            page = ReadPage(continuationItems);
            videos.AddRange(page.Videos); unavailable += page.UnavailableCount;
            continuation = page.Continuation;
            progress?.Report(videos.Count);
        }
        cancellation.ThrowIfCancellationRequested();
        if (videos.Count == 0) throw new InvalidDataException("재생목록에 가져올 수 있는 영상이 없습니다.");
        return new(title, url, videos, unavailable);
    }

    internal static Page ReadPage(JsonElement root)
    {
        var videos = new List<VideoMetadata>(); string continuation = ""; int unavailable = 0;
        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array) { foreach (var child in element.EnumerateArray()) Visit(child); return; }
            if (element.ValueKind != JsonValueKind.Object) return;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "playlistVideoRenderer" or "playlistPanelVideoRenderer")
                {
                    string videoId = Text(Property(property.Value, "videoId"));
                    string title = Text(Property(property.Value, "title"));
                    var playable = Property(property.Value, "isPlayable");
                    if (!Regex.IsMatch(videoId, "^[a-zA-Z0-9_-]{11}$") || title.Length == 0 || playable.ValueKind == JsonValueKind.False) { unavailable++; continue; }
                    videos.Add(new VideoMetadata(title.Length > 200 ? title[..200] : title, "https://www.youtube.com/watch?v=" + videoId, videoId));
                }
                else if (property.Name == "lockupViewModel")
                {
                    if (Text(Property(property.Value, "contentType")) != "LOCKUP_CONTENT_TYPE_VIDEO") continue;
                    string videoId = Text(Property(property.Value, "contentId"));
                    string title = Text(Property(Find(property.Value, "lockupMetadataViewModel"), "title"));
                    if (!Regex.IsMatch(videoId, "^[a-zA-Z0-9_-]{11}$") || title.Length == 0) { unavailable++; continue; }
                    videos.Add(new VideoMetadata(title.Length > 200 ? title[..200] : title, "https://www.youtube.com/watch?v=" + videoId, videoId));
                }
                else if (property.Name is "continuationCommand" or "nextContinuationData")
                {
                    string token = Text(Property(property.Value, property.Name == "continuationCommand" ? "token" : "continuation"));
                    if (token.Length > 0) continuation = token;
                    else Visit(property.Value); // continuationItemViewModel wraps the actual command.
                }
                else Visit(property.Value);
            }
        }
        Visit(root); return new(videos, continuation, unavailable);
    }

    internal static string ExtractAssignedJson(string html, string pattern)
    {
        foreach (Match match in Regex.Matches(html, pattern, RegexOptions.CultureInvariant))
        {
            int start = match.Index + match.Length;
            while (start < html.Length && char.IsWhiteSpace(html[start])) start++;
            if (start == html.Length || html[start] != '{') continue;
            int depth = 0; bool quoted = false, escaped = false;
            for (int index = start; index < html.Length; index++)
            {
                char character = html[index];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') quoted = false;
                }
                else if (character == '"') quoted = true;
                else if (character == '{') depth++;
                else if (character == '}' && --depth == 0) return html[start..(index + 1)];
            }
        }
        throw new InvalidDataException("재생목록 정보를 읽지 못했습니다. 연결과 로그인 상태를 확인한 뒤 다시 시도해 주세요.");
    }
    private static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static JsonElement Find(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out var found)) return found;
            foreach (var property in element.EnumerateObject()) { var result = Find(property.Value, name); if (result.ValueKind != JsonValueKind.Undefined) return result; }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) { var result = Find(child, name); if (result.ValueKind != JsonValueKind.Undefined) return result; }
        return default;
    }
    private static string Text(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString()?.Trim() ?? "";
        if (element.ValueKind != JsonValueKind.Object) return "";
        if (element.TryGetProperty("simpleText", out var simple)) return Text(simple);
        if (element.TryGetProperty("content", out var content)) return Text(content);
        if (element.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array) return string.Concat(runs.EnumerateArray().Select(run => Text(Property(run, "text"))));
        return "";
    }
}
