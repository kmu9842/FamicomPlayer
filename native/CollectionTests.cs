using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamicomPlayer;

internal static class CollectionTests
{
    internal static void Run()
    {
        const string url = "https://www.youtube.com/playlist?list=PL123456789012";
        const string first = """
            <script>ytcfg.set({"INNERTUBE_CONTEXT":{"client":{"clientName":"WEB","clientVersion":"test"}},"INNERTUBE_API_KEY":"test"});
            var ytInitialData = {"metadata":{"playlistMetadataRenderer":{"title":"모음 {\"A\"}"}},"contents":{"playlistVideoListRenderer":{"contents":[
            {"playlistVideoRenderer":{"videoId":"abcdefghijk","title":{"runs":[{"text":"첫 번째"}]},"isPlayable":true}},
            {"playlistVideoRenderer":{"videoId":"lmnopqrstuv","title":{"simpleText":"Private video"},"isPlayable":false}},
            {"continuationItemViewModel":{"continuationCommand":{"innertubeCommand":{"continuationCommand":{"token":"next-page"}}}}}
            ]}}};</script>
            """;
        const string second = """
            {"onResponseReceivedActions":[{"appendContinuationItemsAction":{"continuationItems":[
            {"lockupViewModel":{"contentId":"12345678901","contentType":"LOCKUP_CONTENT_TYPE_VIDEO","metadata":{"lockupMetadataViewModel":{"title":{"content":"두 번째"}}}}},
            {"playlistVideoRenderer":{"videoId":"abcdefghijk","title":{"simpleText":"반복 영상"}}}
            ]}}]}
            """;
        int calls = 0;
        var playlist = YouTubePlaylist.Resolve(url, (uri, body, token) =>
        {
            calls++;
            if (calls == 1 && (body != null || uri.AbsolutePath != "/playlist")) throw new Exception("Initial playlist request failed.");
            if (calls == 2 && (body == null || !body.Contains("next-page") || uri.AbsolutePath != "/youtubei/v1/browse")) throw new Exception("Playlist continuation failed.");
            return Task.FromResult(calls == 1 ? first : second);
        }, null, CancellationToken.None).GetAwaiter().GetResult();
        if (calls != 2 || playlist.Videos.Count != 3 || playlist.UnavailableCount != 1 || playlist.Title != "모음 {\"A\"}" || playlist.Videos[2].Title != "반복 영상") throw new Exception("Multi-page playlist contents/order were lost.");
        bool failed = false;
        try { YouTubePlaylist.Resolve(url, (uri, body, token) => Task.FromResult(body == null ? first : "{}"), null, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { failed = true; }
        if (!failed) throw new Exception("Incomplete playlist response was accepted.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        failed = false;
        try { YouTubePlaylist.Resolve(url, (uri, body, token) => Task.FromResult(first), null, cancelled.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { failed = true; }
        if (!failed) throw new Exception("Playlist cancellation was ignored.");
        var directory = Path.Combine(Path.GetTempPath(), "FamicomPlayer-Collections-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var loose = new CartridgeEntry("legacy", "이전 게임팩", "https://www.youtube.com/watch?v=ABCDEFGHIJK", "", DateTime.UtcNow);
            File.WriteAllText(Path.Combine(directory, "cartridges.json"), JsonSerializer.Serialize(new[] { loose }));
            var store = new CartridgeStore(directory);
            if (store.LoadWarning != null || store.Entries.Count != 1 || store.Collections.Count != 0) throw new Exception("Legacy cartridge library migration failed.");
            var collection = store.ImportCollection(playlist);
            var originalIds = store.Entries.Where(e => e.CollectionId == collection.Id).Select(e => e.Id).ToArray();
            store = new CartridgeStore(directory);
            if (store.Entries.Count != 4 || store.Collections.Count != 1 || store.Entries.Single(e => e.Id == "legacy").CollectionId != null) throw new Exception("Collection restart persistence/loose cartridge failed.");
            var firstEntry = store.Entries.First(e => e.CollectionId == collection.Id);
            store.Save(firstEntry.Id, "수정", firstEntry.Url, null, true);
            if (store.Entries.First(e => e.CollectionId == collection.Id).Id != firstEntry.Id) throw new Exception("Editing a pack changed playlist order.");
            store.ImportCollection(playlist);
            if (store.Collections.Count != 1 || !store.Entries.Where(e => e.CollectionId == collection.Id).Select(e => e.Id).SequenceEqual(originalIds)) throw new Exception("Import duplicated the collection or failed to reuse repeated-video packs.");
            string before = File.ReadAllText(Path.Combine(directory, "cartridges.json"));
            failed = false;
            try { store.ImportCollection(playlist with { Videos = playlist.Videos.Append(new VideoMetadata("invalid", "https://evil.example/", "bad")).ToList() }); }
            catch (ArgumentException) { failed = true; }
            if (!failed || File.ReadAllText(Path.Combine(directory, "cartridges.json")) != before) throw new Exception("Failed import was partially persisted.");
            store.UnpackCollection(collection.Id); store = new CartridgeStore(directory);
            if (store.Collections.Count != 0 || store.Entries.Count != 4 || store.Entries.Any(e => e.CollectionId != null)) throw new Exception("Unpacking lost cartridges.");
            collection = store.ImportCollection(playlist); store.DeleteCollection(collection.Id); store = new CartridgeStore(directory);
            if (store.Collections.Count != 0 || store.Entries.Count != 4) throw new Exception("Collection deletion affected loose cartridges.");
        }
        finally
        {
            string resolved = Path.GetFullPath(directory), tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("FamicomPlayer-Collections-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    internal static async Task<string> CheckLive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var results = new System.Collections.Generic.List<object>();
        foreach (string url in new[] { "https://www.youtube.com/playlist?list=PL3EED4C1D684D3ADF", "https://www.youtube.com/playlist?list=PL19E79A0638C8D449", "https://www.youtube.com/watch?v=2qfoSxRRCJc&list=RD2qfoSxRRCJc" })
        {
            var result = await YouTubePlaylist.Resolve(url, new CookieContainer(), null, timeout.Token);
            if (result.Videos.Count == 0) throw new Exception("Live playlist was empty.");
            if (url.Contains("PL19E79A0638C8D449") && result.Videos.Count <= 100) throw new Exception("Live playlist pagination stopped on the first page.");
            results.Add(new { result.Title, Count = result.Videos.Count, result.UnavailableCount, result.Warning });
        }
        return JsonSerializer.Serialize(results);
    }
}
