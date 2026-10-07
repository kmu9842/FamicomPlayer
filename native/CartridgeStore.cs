using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;

public sealed record CartridgeEntry(string Id, string Name, string Url, string ImageFile, DateTime CreatedUtc)
{
    public string? CollectionId { get; init; }
    public string Description => string.IsNullOrEmpty(ImageFile) ? "YouTube 썸네일 · 자동" : "나만의 커버";
}

public sealed record CartridgeCollection(string Id, string Name, string Url, DateTime CreatedUtc);

internal sealed class CartridgeStore
{
    private readonly string directory;
    private string StorePath => Path.Combine(directory, "cartridges.json");
    internal List<CartridgeEntry> Entries { get; private set; } = new();
    internal List<CartridgeCollection> Collections { get; private set; } = new();
    private sealed record LibraryData(int Version, List<CartridgeEntry> Entries, List<CartridgeCollection> Collections);
    internal string? LoadWarning { get; private set; }
    internal CartridgeStore(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(Path.Combine(directory, "covers"));
        if (!File.Exists(StorePath)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(StorePath));
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                Entries = document.RootElement.Deserialize<List<CartridgeEntry>>() ?? new();
            else
            {
                var data = document.RootElement.Deserialize<LibraryData>() ?? throw new InvalidDataException();
                if (data.Version != 1 || data.Entries == null || data.Collections == null) throw new InvalidDataException();
                Entries = data.Entries; Collections = data.Collections;
            }
            if (Entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Url) || e.ImageFile == null) || Entries.Select(e => e.Id).Distinct().Count() != Entries.Count)
                throw new InvalidDataException("게임팩 데이터가 올바르지 않습니다.");
            if (Collections.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Url)) || Collections.Select(c => c.Id).Distinct().Count() != Collections.Count)
                throw new InvalidDataException("게임팩 모음 데이터가 올바르지 않습니다.");
            foreach (var collection in Collections) YouTubeAddress.Parse(collection.Url);
            if (Entries.Any(e => e.CollectionId != null && !Collections.Any(c => c.Id == e.CollectionId))) throw new InvalidDataException("게임팩 모음을 찾을 수 없습니다.");
            foreach (var e in Entries) { YouTubeAddress.Parse(e.Url); ImagePath(e); }
        }
        catch (Exception e) when (e is JsonException or IOException or ArgumentException or InvalidDataException)
        {
            // Preserve the original before allowing any new saves.
            File.Copy(StorePath, StorePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
            LoadWarning = "보관함 파일을 읽지 못해 원본을 백업했습니다. 새 보관함으로 시작합니다.";
            Entries = new(); Collections = new();
        }
    }
    internal string? ImagePath(CartridgeEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ImageFile)) return null;
        if (Path.GetFileName(entry.ImageFile) != entry.ImageFile || !entry.ImageFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("잘못된 커버 경로입니다.");
        return Path.Combine(directory, "covers", entry.ImageFile);
    }
    internal CartridgeEntry Save(string? id, string name, string url, string? importPath, bool automaticCover)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 200) throw new ArgumentException("영상 제목을 확인해 주세요.");
        string normalized = YouTubeAddress.Parse(url).AbsoluteUri;
        var existing = Entries.Find(e => e.Id == id);
        string imageFile = automaticCover ? "" : existing?.ImageFile ?? "";
        string? newImage = null;
        if (!automaticCover && !string.IsNullOrEmpty(importPath))
        {
            var info = new FileInfo(importPath);
            if (!info.Exists || info.Length > 20 * 1024 * 1024) throw new ArgumentException("20MB 이하의 이미지 파일을 선택해 주세요.");
            using var stream = File.OpenRead(importPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];
            if (source.PixelWidth > 16000 || source.PixelHeight > 16000 || (long)source.PixelWidth * source.PixelHeight > 64000000)
                throw new ArgumentException("이미지가 너무 큽니다. 6400만 화소 이하로 줄여 주세요.");
            imageFile = Guid.NewGuid().ToString("N") + ".png";
            newImage = Path.Combine(directory, "covers", imageFile);
            var resized = new System.Windows.Media.Imaging.TransformedBitmap(source, new System.Windows.Media.ScaleTransform(Math.Min(1, 1280d / Math.Max(source.PixelWidth, source.PixelHeight)), Math.Min(1, 1280d / Math.Max(source.PixelWidth, source.PixelHeight))));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(resized));
            using var output = File.Create(newImage); encoder.Save(output);
        }
        var entry = new CartridgeEntry(existing?.Id ?? Guid.NewGuid().ToString("N"), name, normalized, imageFile, existing?.CreatedUtc ?? DateTime.UtcNow) { CollectionId = existing?.CollectionId };
        var next = Entries.ToList();
        int position = next.FindIndex(e => e.Id == entry.Id);
        if (position < 0) next.Add(entry); else next[position] = entry;
        try { Commit(next); }
        catch { if (newImage != null) File.Delete(newImage); throw; }
        if (existing != null && existing.ImageFile != imageFile) RemoveUnusedImage(existing);
        return entry;
    }
    internal void Delete(string id)
    {
        var entry = Entries.Find(e => e.Id == id);
        if (entry == null) return;
        Commit(Entries.Where(e => e.Id != id).ToList());
        RemoveUnusedImage(entry);
    }
    internal CartridgeCollection ImportCollection(PlaylistMetadata playlist)
    {
        if (playlist.Videos.Count == 0) throw new ArgumentException("모음에 넣을 영상이 없습니다.");
        string url = YouTubeAddress.PlaylistUrl(playlist.Url);
        string name = playlist.Title.Trim();
        if (name.Length == 0 || name.Length > 200) throw new ArgumentException("모음 제목을 확인해 주세요.");
        var existing = Collections.Find(c => c.Url == url);
        var collection = new CartridgeCollection(existing?.Id ?? Guid.NewGuid().ToString("N"), name, url, existing?.CreatedUtc ?? DateTime.UtcNow);
        var previous = Entries.Where(e => e.CollectionId == collection.Id).ToList();
        var available = previous.GroupBy(e => YouTubeMetadata.VideoId(e.Url)).ToDictionary(g => g.Key, g => new Queue<CartridgeEntry>(g));
        var imported = new List<CartridgeEntry>();
        foreach (var video in playlist.Videos)
        {
            string normalized = YouTubeAddress.Parse(video.Url).AbsoluteUri;
            if (video.Title.Length == 0 || video.Title.Length > 200 || YouTubeMetadata.VideoId(normalized).Length != 11) throw new ArgumentException("재생목록의 영상 정보를 확인해 주세요.");
            var old = available.TryGetValue(video.VideoId, out var matches) && matches.Count > 0 ? matches.Dequeue() : null;
            imported.Add(new CartridgeEntry(old?.Id ?? Guid.NewGuid().ToString("N"), video.Title, normalized, old?.ImageFile ?? "", old?.CreatedUtc ?? DateTime.UtcNow) { CollectionId = collection.Id });
        }
        var next = Entries.Where(e => e.CollectionId != collection.Id).Concat(imported).ToList();
        var collections = Collections.Where(c => c.Id != collection.Id).Append(collection).OrderBy(c => c.CreatedUtc).ToList();
        Commit(next, collections);
        foreach (var old in previous) RemoveUnusedImage(old);
        return collection;
    }
    internal void UnpackCollection(string id)
    {
        Commit(Entries.Select(e => e.CollectionId == id ? e with { CollectionId = null } : e).ToList(), Collections.Where(c => c.Id != id).ToList());
    }
    internal void DeleteCollection(string id)
    {
        var removed = Entries.Where(e => e.CollectionId == id).ToList();
        Commit(Entries.Where(e => e.CollectionId != id).ToList(), Collections.Where(c => c.Id != id).ToList());
        foreach (var entry in removed) RemoveUnusedImage(entry);
    }
    private void Commit(List<CartridgeEntry> next, List<CartridgeCollection>? collections = null)
    {
        collections ??= Collections;
        File.WriteAllText(StorePath + ".tmp", JsonSerializer.Serialize(new LibraryData(1, next, collections), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(StorePath + ".tmp", StorePath, true);
        Entries = next; Collections = collections;
    }
    private void RemoveUnusedImage(CartridgeEntry entry)
    {
        if (Entries.Any(e => e.ImageFile == entry.ImageFile)) return;
        var path = ImagePath(entry);
        try { if (path != null) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
