using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FamicomPlayer;
public partial class LibraryWindow : Window
{
    private readonly CartridgeStore store;
    private readonly Action<CartridgeEntry> insert;
    private readonly Action changed;
    private readonly Func<Task<CookieContainer>>? getCookies;
    private string? selectedId, selectedCollectionId, currentCollectionId, importPath;
    private bool ready, selecting, useThumbnail = true;
    private CancellationTokenSource? metadataCancellation;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim thumbnailSlots = new(4);
    private VideoMetadata? metadata;
    private PlaylistMetadata? playlist;
    private readonly ImageSource shell;
    private sealed class Card : INotifyPropertyChanged
    {
        public CartridgeEntry? Entry { get; }
        public CartridgeCollection? Collection { get; }
        public IReadOnlyList<CartridgeEntry> Members { get; }
        public string Name => Collection?.Name ?? Entry!.Name;
        public string Detail => Collection == null ? "게임팩" : "모음 · " + Members.Count + "개 · 두 번 눌러 열기";
        public bool IsCollection => Collection != null;
        public ImageSource Shell { get; }
        private ImageSource cover, secondCover, thirdCover;
        public ImageSource Cover { get => cover; set { cover = value; Changed(nameof(Cover)); } }
        public ImageSource SecondCover { get => secondCover; set { secondCover = value; Changed(nameof(SecondCover)); } }
        public ImageSource ThirdCover { get => thirdCover; set { thirdCover = value; Changed(nameof(ThirdCover)); } }
        public bool CoverRequested { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public Card(CartridgeEntry? entry, CartridgeCollection? collection, IReadOnlyList<CartridgeEntry> members, ImageSource shell)
        { Entry = entry; Collection = collection; Members = members; Shell = shell; cover = secondCover = thirdCover = DefaultCover(); }
    }
    internal LibraryWindow(CartridgeStore store, Action<CartridgeEntry> insert, Action changed, string currentUrl, string currentTitle, Func<Task<CookieContainer>>? getCookies = null)
    {
        this.store = store; this.insert = insert; this.changed = changed; this.getCookies = getCookies;
        shell = SpriteAssets.Load("library-shell.png", true);
        InitializeComponent(); PreviewShell.Source = PreviewBackShellOne.Source = PreviewBackShellTwo.Source = shell;
        CoverPreview.Source = DefaultCover(); ready = true; Reload();
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        UrlInput.Text = currentUrl; Message.Text = store.LoadWarning ?? "재생목록 링크를 넣으면 게임팩 모음이 만들어집니다.";
        Closed += (_, _) => { ready = false; metadataCancellation?.Cancel(); metadataCancellation?.Dispose(); lifetime.Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { if (ImportProgress.Visibility == Visibility.Visible) CancelImport(this, new RoutedEventArgs()); else Close(); e.Handled = true; } };
    }
    internal static BitmapSource DefaultCover() => CartridgeDisplay.CreateNoiseCover();
    private ImageSource CustomCover(CartridgeEntry entry)
    {
        try { var path = store.ImagePath(entry); if (path != null && File.Exists(path)) { using var stream = File.OpenRead(path); var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); image.Freeze(); return image; } }
        catch (Exception error) when (error is IOException or NotSupportedException) { }
        return DefaultCover();
    }
    private void Reload()
    {
        if (!ready) return;
        var current = store.Collections.Find(c => c.Id == currentCollectionId);
        currentCollectionId = current?.Id;
        BackButton.Visibility = current == null ? Visibility.Collapsed : Visibility.Visible;
        LocationTitle.Text = current?.Name ?? "전체 보관함";
        string search = Search.Text.Trim();
        var cards = new List<Card>();
        if (current == null)
            foreach (var collection in store.Collections)
            {
                var members = store.Entries.Where(e => e.CollectionId == collection.Id).ToList();
                if (collection.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || members.Any(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase))) cards.Add(new Card(null, collection, members, shell));
            }
        cards.AddRange(store.Entries.Where(e => e.CollectionId == currentCollectionId && e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).Select(e => new Card(e, null, new[] { e }, shell)));
        Cards.ItemsSource = cards; EmptyMessage.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyMessage.Text = search.Length > 0 ? "검색 결과 없음" : current != null ? "모음에 게임팩이 없습니다" : "저장된 게임팩 없음";
    }
    private void CardVisible(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: Card card }) _ = LoadCardCover(card); }
    private void CardContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (e.NewValue is Card card) _ = LoadCardCover(card); }
    private async Task LoadCardCover(Card card)
    {
        if (card.CoverRequested || lifetime.IsCancellationRequested) return;
        card.CoverRequested = true;
        for (int index = 0; index < Math.Min(3, card.Members.Count); index++)
        {
            bool acquired = false;
            try
            {
                await thumbnailSlots.WaitAsync(lifetime.Token); acquired = true;
                var entry = card.Members[index]; ImageSource cover = CustomCover(entry);
                if (entry.ImageFile.Length == 0) cover = await YouTubeMetadata.Thumbnail(YouTubeMetadata.VideoId(entry.Url), lifetime.Token) ?? cover;
                if (index == 0) card.Cover = cover; else if (index == 1) card.SecondCover = cover; else card.ThirdCover = cover;
                if (ReferenceEquals(Cards.SelectedItem, card)) SetPreview(card);
            }
            catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException or IOException or NotSupportedException) { }
            finally { if (acquired) thumbnailSlots.Release(); }
        }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => Reload();
    private void NewCartridge(object sender, RoutedEventArgs e)
    {
        metadataCancellation?.Cancel(); selectedId = selectedCollectionId = importPath = null; metadata = null; playlist = null;
        Cards.SelectedItem = null; useThumbnail = true; UrlInput.Clear(); CoverPreview.Source = DefaultCover(); VideoTitle.Text = "";
        Message.Text = "재생목록 링크를 넣으면 모든 영상을 게임팩 모음으로 만듭니다.";
        DeleteButton.Visibility = UnpackButton.Visibility = ImportProgress.Visibility = Visibility.Collapsed;
        SetCollectionPreview(false); SetReady(false); UrlInput.Focus();
    }
    private void SelectCartridge(object sender, SelectionChangedEventArgs e)
    {
        if (Cards.SelectedItem is not Card card) return;
        metadataCancellation?.Cancel(); selecting = true; playlist = null; importPath = null;
        selectedCollectionId = card.Collection?.Id; selectedId = card.Entry?.Id;
        metadata = card.Entry == null ? null : new VideoMetadata(card.Entry.Name, card.Entry.Url, YouTubeMetadata.VideoId(card.Entry.Url));
        useThumbnail = card.Entry?.ImageFile.Length == 0;
        UrlInput.Text = card.Collection?.Url ?? card.Entry!.Url; VideoTitle.Text = card.Name;
        selecting = false; SetPreview(card); SetCollectionPreview(card.IsCollection);
        DeleteButton.Visibility = Visibility.Visible; UnpackButton.Visibility = card.IsCollection ? Visibility.Visible : Visibility.Collapsed;
        ImportProgress.Visibility = Visibility.Collapsed; Message.Text = card.IsCollection ? "게임팩 " + card.Members.Count + "개 · 모음을 열어 팩을 선택하세요." : "";
        SetReady(true);
    }
    private void SetPreview(Card card) { CoverPreview.Source = card.Cover; PreviewBackCoverOne.Source = card.SecondCover; PreviewBackCoverTwo.Source = card.ThirdCover; }
    private void SetCollectionPreview(bool collection)
    {
        PreviewBackOne.Visibility = PreviewBackTwo.Visibility = collection ? Visibility.Visible : Visibility.Collapsed;
        CoverButtons.Visibility = collection ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void UrlChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || selecting) return;
        metadataCancellation?.Cancel(); metadataCancellation?.Dispose(); metadataCancellation = new(); var token = metadataCancellation.Token;
        metadata = null; playlist = null; selectedCollectionId = null; SetReady(false); VideoTitle.Text = ""; Message.Text = "";
        ImportProgress.Visibility = UnpackButton.Visibility = Visibility.Collapsed; SetCollectionPreview(false);
        if (string.IsNullOrWhiteSpace(UrlInput.Text)) return;
        string url = UrlInput.Text;
        try
        {
            await Task.Delay(400, token); YouTubeAddress.Parse(url);
            bool collection = YouTubeAddress.PlaylistId(url).Length > 0; SetCollectionPreview(collection);
            ImportProgress.Visibility = Visibility.Visible;
            if (collection)
            {
                VideoTitle.Text = "재생목록 불러오는 중…";
                var progress = new Progress<int>(count => { if (!token.IsCancellationRequested && playlist == null) { VideoTitle.Text = "게임팩 " + count + "개 불러옴…"; Message.Text = "마지막 페이지까지 확인 중입니다. 취소하면 저장되지 않습니다."; } });
                var cookies = getCookies == null ? null : await getCookies(); token.ThrowIfCancellationRequested();
                var result = await YouTubePlaylist.Resolve(url, cookies, progress, token); token.ThrowIfCancellationRequested();
                if (result.Videos.Count == 0) throw new InvalidDataException("가져올 수 있는 영상이 없습니다.");
                playlist = result; selectedId = null; VideoTitle.Text = result.Title + " · " + result.Videos.Count + "개";
                Message.Text = PlaylistSummary(result); SetReady(true); ImportProgress.Visibility = Visibility.Collapsed;
                var previews = new[] { CoverPreview, PreviewBackCoverOne, PreviewBackCoverTwo };
                for (int index = 0; index < Math.Min(3, result.Videos.Count); index++)
                { var cover = await PreviewThumbnail(result.Videos[index].VideoId, token); token.ThrowIfCancellationRequested(); previews[index].Source = cover; }
            }
            else
            {
                VideoTitle.Text = "제목 불러오는 중…";
                var result = await YouTubeMetadata.Resolve(url, token); token.ThrowIfCancellationRequested();
                metadata = result; VideoTitle.Text = result.Title; SetReady(true); ImportProgress.Visibility = Visibility.Collapsed;
                if (useThumbnail) { var cover = await PreviewThumbnail(result.VideoId, token); token.ThrowIfCancellationRequested(); CoverPreview.Source = cover; }
            }
        }
        catch (OperationCanceledException) { if (!token.IsCancellationRequested) { SetReady(false); VideoTitle.Text = ""; Message.Text = "연결 시간이 초과되었습니다. 링크를 다시 넣어 주세요. 저장되지 않았습니다."; } }
        catch (Exception error) when (error is ArgumentException or System.Net.Http.HttpRequestException or IOException or System.Text.Json.JsonException)
        { if (!token.IsCancellationRequested) { SetReady(false); VideoTitle.Text = ""; Message.Text = error is ArgumentException or InvalidDataException ? error.Message : "정보를 가져오지 못했습니다. 연결과 로그인 상태를 확인하고 다시 시도해 주세요. 저장되지 않았습니다."; } }
        finally { if (!token.IsCancellationRequested) ImportProgress.Visibility = Visibility.Collapsed; }
    }
    private static string PlaylistSummary(PlaylistMetadata result) => (result.Warning.Length > 0 ? result.Warning : "전체 " + result.Videos.Count + "개 영상을 모음으로 저장할 수 있습니다.") + (result.UnavailableCount > 0 ? " 비공개·삭제 등 가져올 수 없는 항목 " + result.UnavailableCount + "개는 제외됩니다." : "");
    private static async Task<BitmapSource> PreviewThumbnail(string videoId, CancellationToken token)
    {
        try { return await YouTubeMetadata.Thumbnail(videoId, token) ?? DefaultCover(); }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or IOException or NotSupportedException) { return DefaultCover(); }
    }
    private void CancelImport(object sender, RoutedEventArgs e)
    {
        metadataCancellation?.Cancel(); metadata = null; playlist = null; SetReady(false); ImportProgress.Visibility = Visibility.Collapsed;
        VideoTitle.Text = ""; Message.Text = "가져오기를 취소했습니다. 게임팩은 저장되지 않았습니다.";
    }
    private void SetReady(bool value)
    {
        SaveButton.IsEnabled = InsertButton.IsEnabled = value;
        SaveButton.Content = selectedCollectionId != null ? "모음 열기" : playlist != null ? "모음 저장" : "저장";
        InsertButton.Content = selectedCollectionId != null || playlist != null ? "첫 팩 재생" : "넣고 재생";
    }
    private async void UseThumbnail(object sender, RoutedEventArgs e)
    {
        useThumbnail = true; importPath = null; CoverPreview.Source = DefaultCover();
        var current = metadata; if (current == null) return;
        try { var cover = await YouTubeMetadata.Thumbnail(current.VideoId, lifetime.Token); if (useThumbnail && metadata == current) CoverPreview.Source = cover ?? DefaultCover(); }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException or IOException) { }
    }
    private void ChooseImage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp", Title = "게임팩 커버" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024) throw new ArgumentException("20MB 이하의 이미지를 선택해 주세요.");
            var image = new BitmapImage(); image.BeginInit(); image.UriSource = new Uri(dialog.FileName); image.DecodePixelWidth = 600; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
            CoverPreview.Source = image; importPath = dialog.FileName; useThumbnail = false; Message.Text = "";
        }
        catch (Exception error) { Message.Text = error.Message; }
    }
    private CartridgeEntry? SaveEntry()
    {
        try
        {
            if (selectedCollectionId != null) return store.Entries.FirstOrDefault(e => e.CollectionId == selectedCollectionId);
            if (playlist != null)
            {
                if (playlist.Url != YouTubeAddress.PlaylistUrl(UrlInput.Text)) throw new ArgumentException("재생목록 정보를 불러오는 중입니다.");
                var result = playlist; var collection = store.ImportCollection(result);
                selectedCollectionId = collection.Id; currentCollectionId = collection.Id; selectedId = null; playlist = null;
                Search.Clear(); Reload(); changed(); DeleteButton.Visibility = UnpackButton.Visibility = Visibility.Visible; SetReady(true);
                Message.Text = "게임팩 " + result.Videos.Count + "개를 모음으로 저장했습니다." + (result.UnavailableCount > 0 ? " 가져올 수 없는 항목 " + result.UnavailableCount + "개 제외." : "");
                return store.Entries.FirstOrDefault(e => e.CollectionId == collection.Id);
            }
            if (metadata == null || metadata.Url != YouTubeAddress.Parse(UrlInput.Text).AbsoluteUri) throw new ArgumentException("영상 제목을 불러오는 중입니다.");
            var entry = store.Save(selectedId, metadata.Title, metadata.Url, importPath, useThumbnail);
            selectedId = entry.Id; importPath = null; currentCollectionId = entry.CollectionId; Reload(); changed(); DeleteButton.Visibility = Visibility.Visible; Message.Text = "저장했습니다."; return entry;
        }
        catch (Exception error) { Message.Text = error.Message; return null; }
    }
    private void SaveCartridge(object sender, RoutedEventArgs e) { if (selectedCollectionId != null) OpenCollection(); else SaveEntry(); }
    private void InsertCartridge(object sender, RoutedEventArgs e) { var entry = SaveEntry(); if (entry == null) return; insert(entry); Close(); }
    private void OpenCollection()
    {
        if (selectedCollectionId == null) return; currentCollectionId = selectedCollectionId; Search.Clear(); Reload();
    }
    private void ActivateCard(object sender, MouseButtonEventArgs e) { if (Cards.SelectedItem is Card { IsCollection: true }) { OpenCollection(); e.Handled = true; } }
    private void CardKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Cards.SelectedItem is Card { IsCollection: true }) { OpenCollection(); e.Handled = true; } }
    private void BackToLibrary(object sender, RoutedEventArgs e) { currentCollectionId = null; Search.Clear(); NewCartridge(sender, e); Reload(); }
    private void UnpackCollection(object sender, RoutedEventArgs e)
    {
        if (selectedCollectionId == null) return;
        try { store.UnpackCollection(selectedCollectionId); currentCollectionId = null; NewCartridge(sender, e); Reload(); changed(); Message.Text = "모음을 풀었습니다. 게임팩은 전체 보관함에 유지됩니다."; }
        catch (Exception error) { Message.Text = error.Message; }
    }
    private void DeleteCartridge(object sender, RoutedEventArgs e)
    {
        if (selectedId == null && selectedCollectionId == null) return;
        bool collection = selectedCollectionId != null;
        string title = collection ? "게임팩 모음 삭제" : "게임팩 삭제";
        string message = collection ? "모음과 안에 있는 모든 게임팩을 보관함에서 삭제할까요?" : "이 게임팩을 보관함에서 삭제할까요?";
        if (!ThemedDialog.Confirm(this, title, message, collection ? "모음 삭제" : "삭제")) return;
        try { if (selectedCollectionId != null) { store.DeleteCollection(selectedCollectionId); currentCollectionId = null; } else store.Delete(selectedId!); changed(); NewCartridge(sender, e); Reload(); }
        catch (Exception error) { Message.Text = error.Message; }
    }
    private void CloseLibrary(object sender, RoutedEventArgs e) => Close();
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not Button) { try { DragMove(); } catch (InvalidOperationException) { } } }
}
