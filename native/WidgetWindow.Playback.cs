using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamicomPlayer;

public partial class WidgetWindow
{
    private CancellationTokenSource? insertionCancellation;
    private string insertionRequestId = "";
    private bool cartridgeSeatedForPlayback;
    private int completedPlaybackInsertions;
    private bool CartridgeInsertionPending => insertionRequestId.Length > 0;

    private void CancelCartridgeInsertion()
    {
        bool pending = CartridgeInsertionPending;
        insertionRequestId = "";
        insertionCancellation?.Cancel();
        insertionCancellation?.Dispose();
        insertionCancellation = null;
        cartridgeSeatedForPlayback = false;
        if (pending)
        {
            coverCancellation?.Cancel();
            cartridgePlayer?.Stop();
            ResetCartridgeAnimation();
        }
    }

    private void UpdateCartridgeInsertionState(JsonElement state)
    {
        if (playing) cartridgeSeatedForPlayback = false;
        if (!CartridgeInsertionPending) return;
        bool requested = state.TryGetProperty("playbackRequested", out var intent) && intent.ValueKind == JsonValueKind.True;
        bool pending = state.TryGetProperty("insertionPending", out var waiting) && waiting.ValueKind == JsonValueKind.True;
        string request = state.TryGetProperty("insertionRequestId", out var id) ? id.GetString() ?? "" : "";
        bool error = state.TryGetProperty("error", out var problem) && !string.IsNullOrEmpty(problem.GetString());
        if (!requested || !pending || error || (request.Length > 0 && request != insertionRequestId)) CancelCartridgeInsertion();
    }

    private async Task CompleteCartridgeInsertion(JsonElement message)
    {
        string request = message.GetProperty("requestId").GetString() ?? "";
        string videoId = message.GetProperty("videoId").GetString() ?? "";
        string title = message.GetProperty("title").GetString() ?? "";
        if (closing || pageOpen || request.Length == 0 || videoId.Length != 11 || request == insertionRequestId) return;
        CancelCartridgeInsertion();
        var cancellation = new CancellationTokenSource();
        insertionCancellation = cancellation;
        insertionRequestId = request;
        var token = cancellation.Token;
        StopPlaybackConnection();
        try
        {
            // Thumbnail downloads must not hold up the ready video or cancel its seating.
            _ = UpdateCartridgeCover(videoId, title, false);
            token.ThrowIfCancellationRequested();
            await AnimateCartridgeInsertion(token, true);
            token.ThrowIfCancellationRequested();
            if (closing || pageOpen || insertionRequestId != request) return;
            // The page also validates the token so an old transition cannot start a new video.
            string result = await Browser.CoreWebView2.ExecuteScriptAsync("window.famicomplayerNative?.releaseCartridge(" + JsonSerializer.Serialize(request) + ")");
            if (result == "true") completedPlaybackInsertions++;
            else { cartridgeSeatedForPlayback = false; UpdateCartridgeSeating(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (closing) return;
            cartridgeSeatedForPlayback = false;
            ResetCartridgeAnimation();
            await Execute("window.famicomplayerNative?.pause()");
            Notice("재생을 준비하지 못했습니다. 다시 재생해 주세요.");
        }
        finally
        {
            if (insertionRequestId == request)
            {
                insertionRequestId = "";
                insertionCancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
