using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
    private string? selectedId, importPath;
    private bool ready, selecting, useThumbnail = true;
    private CancellationTokenSource? metadataCancellation;
    private VideoMetadata? metadata;
    private readonly ImageSource shell;
    private sealed class Card : INotifyPropertyChanged
    {
        public CartridgeEntry Entry { get; }
        public ImageSource Shell { get; }
        private ImageSource cover;
        public ImageSource Cover { get => cover; set { cover = value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Cover))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public Card(CartridgeEntry entry, ImageSource shell, ImageSource cover) { Entry=entry; Shell=shell; this.cover=cover; }
    }
    internal LibraryWindow(CartridgeStore store, Action<CartridgeEntry> insert, Action changed, string currentUrl, string currentTitle)
    {
        this.store=store; this.insert=insert; this.changed=changed;
        shell=SpriteAssets.Load("library-shell.png",true);
        InitializeComponent(); PreviewShell.Source=shell; CoverPreview.Source=DefaultCover(); ready=true; Reload();
        Loaded += (_,_) => BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(120)));
        UrlInput.Text=currentUrl;
        Message.Text=store.LoadWarning ?? "";
        Closed += (_,_) => { metadataCancellation?.Cancel(); metadataCancellation?.Dispose(); };
        PreviewKeyDown += (_,e) => { if(e.Key==Key.Escape) Close(); };
    }
    internal static BitmapSource DefaultCover() { using var s=BundledAssets.Open("default-cover.png"); var image=BitmapFrame.Create(s,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);image.Freeze();return image; }
    private ImageSource CustomCover(CartridgeEntry entry)
    {
        try { var p=store.ImagePath(entry);if(p!=null && File.Exists(p)){using var s=File.OpenRead(p);var image=BitmapFrame.Create(s,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);image.Freeze();return image;} } catch(Exception e) when(e is IOException or NotSupportedException) { }
        return DefaultCover();
    }
    private void Reload()
    {
        if(!ready)return;
        var cards=store.Entries.Where(e=>e.Name.Contains(Search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).Select(e=>new Card(e,shell,CustomCover(e))).ToList();
        Cards.ItemsSource=cards;EmptyMessage.Visibility=cards.Count==0?Visibility.Visible:Visibility.Collapsed;
        EmptyMessage.Text=store.Entries.Count==0?"저장된 게임팩 없음":"검색 결과 없음";
        foreach(var card in cards.Where(c=>c.Entry.ImageFile.Length==0)) _=LoadCardCover(card);
    }
    private async Task LoadCardCover(Card card)
    {
        try { var data=await YouTubeMetadata.Resolve(card.Entry.Url);var image=await YouTubeMetadata.Thumbnail(data.VideoId);if(image!=null)card.Cover=image; } catch(Exception e) when(e is System.Net.Http.HttpRequestException or OperationCanceledException or IOException) { }
    }
    private void SearchChanged(object sender,TextChangedEventArgs e)=>Reload();
    private void NewCartridge(object sender,RoutedEventArgs e)
    {
        metadataCancellation?.Cancel();selectedId=importPath=null;metadata=null;Cards.SelectedItem=null;useThumbnail=true;
        UrlInput.Clear();CoverPreview.Source=DefaultCover();VideoTitle.Text="";Message.Text="";DeleteButton.Visibility=Visibility.Collapsed;SetReady(false);UrlInput.Focus();
    }
    private void SelectCartridge(object sender,SelectionChangedEventArgs e)
    {
        if(Cards.SelectedItem is not Card card)return;
        metadataCancellation?.Cancel();selecting=true;
        selectedId=card.Entry.Id;importPath=null;useThumbnail=card.Entry.ImageFile.Length==0;
        metadata=new VideoMetadata(card.Entry.Name,card.Entry.Url,YouTubeMetadata.VideoId(card.Entry.Url));
        UrlInput.Text=card.Entry.Url;VideoTitle.Text=card.Entry.Name;CoverPreview.Source=card.Cover;
        selecting=false;DeleteButton.Visibility=Visibility.Visible;Message.Text="";SetReady(true);
    }
    private async void UrlChanged(object sender,TextChangedEventArgs e)
    {
        if(!ready || selecting)return;
        metadataCancellation?.Cancel();metadataCancellation?.Dispose();metadataCancellation=new();var token=metadataCancellation.Token;
        metadata=null;SetReady(false);VideoTitle.Text="";Message.Text="";
        if(string.IsNullOrWhiteSpace(UrlInput.Text))return;
        string url=UrlInput.Text;
        try
        {
            await Task.Delay(400,token);YouTubeAddress.Parse(url);VideoTitle.Text="제목 불러오는 중…";
            var result=await YouTubeMetadata.Resolve(url,token);token.ThrowIfCancellationRequested();
            metadata=result;VideoTitle.Text=result.Title;SetReady(true);
            if(useThumbnail){var cover=await YouTubeMetadata.Thumbnail(result.VideoId,token);token.ThrowIfCancellationRequested();CoverPreview.Source=cover??DefaultCover();}
        }
        catch(OperationCanceledException){if(!token.IsCancellationRequested){VideoTitle.Text="";Message.Text="연결을 확인하고 링크를 다시 넣어 주세요.";}}
        catch(Exception error) when(error is ArgumentException or System.Net.Http.HttpRequestException or IOException)
        {if(!token.IsCancellationRequested){VideoTitle.Text="";Message.Text=error is ArgumentException?error.Message:"제목을 가져오지 못했습니다. 링크를 확인해 주세요.";}}
    }
    private void SetReady(bool value){SaveButton.IsEnabled=InsertButton.IsEnabled=value;}
    private async void UseThumbnail(object sender,RoutedEventArgs e)
    {
        useThumbnail=true;importPath=null;CoverPreview.Source=DefaultCover();
        var current=metadata;if(current==null)return;
        var cover=await YouTubeMetadata.Thumbnail(current.VideoId);
        if(useThumbnail && metadata==current)CoverPreview.Source=cover??DefaultCover();
    }
    private void ChooseImage(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="이미지|*.png;*.jpg;*.jpeg;*.bmp",Title="게임팩 커버"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>20*1024*1024)throw new ArgumentException("20MB 이하의 이미지를 선택해 주세요.");
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(dialog.FileName);image.DecodePixelWidth=600;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();
            CoverPreview.Source=image;importPath=dialog.FileName;useThumbnail=false;Message.Text="";
        }catch(Exception error){Message.Text=error.Message;}
    }
    private CartridgeEntry? SaveEntry()
    {
        try
        {
            if(metadata==null || metadata.Url!=YouTubeAddress.Parse(UrlInput.Text).AbsoluteUri)throw new ArgumentException("영상 제목을 불러오는 중입니다.");
            var entry=store.Save(selectedId,metadata.Title,metadata.Url,importPath,useThumbnail);
            selectedId=entry.Id;importPath=null;Reload();changed();DeleteButton.Visibility=Visibility.Visible;Message.Text="저장했습니다.";return entry;
        }catch(Exception error){Message.Text=error.Message;return null;}
    }
    private void SaveCartridge(object sender,RoutedEventArgs e)=>SaveEntry();
    private void InsertCartridge(object sender,RoutedEventArgs e){var entry=SaveEntry();if(entry==null)return;insert(entry);Close();}
    private void DeleteCartridge(object sender,RoutedEventArgs e)
    {
        if(selectedId==null)return;
        if(MessageBox.Show(this,"게임팩을 삭제할까요?","게임팩",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{store.Delete(selectedId);changed();NewCartridge(sender,e);Reload();}catch(Exception error){Message.Text=error.Message;}
    }
    private void CloseLibrary(object sender,RoutedEventArgs e)=>Close();
    private void DragHeader(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left && e.OriginalSource is not Button){try{DragMove();}catch(InvalidOperationException){}}}
}
