using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace FamicomPlayer;

public partial class WidgetWindow
{
    private readonly DispatcherTimer connectionSoundTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Stopwatch connectionSoundClock = new();
    private MemoryStream? breathStream;
    private SoundPlayer? breathPlayer;
    private bool playbackConnectionPending, connectionCueExpired, connectionCueAudible;
    private double nextBreathAt, breathEndsAt;

    private void InitializePlaybackSounds()
    {
        breathStream = MakeCartridgeBreath();
        breathPlayer = new SoundPlayer(breathStream);
        breathPlayer.Load();
        connectionSoundTimer.Tick += (_, _) => TickPlaybackConnection();
    }

    private void BeginPlaybackConnection()
    {
        if (closing || pageOpen) return;
        CancelCartridgeInsertion();
        StopPlaybackConnection();
        playbackConnectionPending = true;
        connectionCueExpired = false;
        nextBreathAt = .7;
        connectionSoundClock.Restart();
        connectionSoundTimer.Start();
    }

    private void StopPlaybackConnection()
    {
        connectionSoundTimer.Stop();
        connectionSoundClock.Stop();
        playbackConnectionPending = false;
        if (connectionCueAudible) breathPlayer?.Stop();
        connectionCueAudible = false;
    }

    private void UpdatePlaybackConnection(JsonElement state)
    {
        bool requested = state.TryGetProperty("playbackRequested", out var request) && request.ValueKind == JsonValueKind.True;
        bool buffering = state.TryGetProperty("buffering", out var waiting) && waiting.ValueKind == JsonValueKind.True;
        bool error = state.TryGetProperty("error", out var problem) && !string.IsNullOrEmpty(problem.GetString());
        if (playing || !requested || !buffering || error || pageOpen || closing)
        {
            StopPlaybackConnection();
            connectionCueExpired = false;
        }
        else if (buffering && !playbackConnectionPending && !connectionCueExpired)
            BeginPlaybackConnection();
    }

    private void TickPlaybackConnection()
    {
        double elapsed = connectionSoundClock.Elapsed.TotalSeconds;
        if (elapsed >= breathEndsAt) connectionCueAudible = false;
        if (!playbackConnectionPending || pageOpen || closing || !IsVisible || elapsed >= 18)
        {
            connectionCueExpired = elapsed >= 18;
            StopPlaybackConnection();
            return;
        }
        if (!preferences.ClickSound || Volume.Value <= 0)
        {
            if (connectionCueAudible) breathPlayer?.Stop();
            connectionCueAudible = false;
            return;
        }
        if (elapsed < nextBreathAt) return;
        nextBreathAt = elapsed + 3.2;
        // Two short puffs, with room between pairs. The insertion click remains louder.
        if (!Program.Smoke)
        {
            breathPlayer?.Play();
            connectionCueAudible = true;
            breathEndsAt = elapsed + .76;
        }
    }

    private void DisposePlaybackSounds()
    {
        CancelCartridgeInsertion();
        StopPlaybackConnection();
        breathPlayer?.Dispose();
        breathStream?.Dispose();
    }

    internal static MemoryStream MakeCartridgeBreath()
    {
        const int rate = 22050, count = 16758; // 760 ms, including a silent gap.
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            var random = new Random(178);
            double smooth = 0, previous = 0;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double local = t < .34 ? t - .025 : t - .395;
                double envelope = local > 0 && local < .265 ? Math.Pow(Math.Sin(Math.PI * local / .265), 1.3) : 0;
                // Filtered air noise gives the soft "hoo, hoo" of blowing a cartridge.
                smooth = .69 * smooth + .31 * (random.NextDouble() * 2 - 1);
                double air = smooth - .5 * previous;
                previous = smooth;
                writer.Write((short)(2300 * envelope * air));
            }
        }
        // Compare both effects over the same playback window, including the gap
        // between puffs. Raw generator gains do not represent their audible levels.
        using var click = MakeClick();
        byte[] reference = click.ToArray(), samples = stream.GetBuffer();
        double referenceEnergy = 0, breathEnergy = 0;
        for (int i = 44; i < reference.Length; i += 2)
        { double value = BinaryPrimitives.ReadInt16LittleEndian(reference.AsSpan(i, 2)); referenceEnergy += value * value; }
        for (int i = 44; i < stream.Length; i += 2)
        { double value = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(i, 2)); breathEnergy += value * value; }
        double gain = .5 * Math.Sqrt(referenceEnergy / breathEnergy);
        for (int i = 44; i < stream.Length; i += 2)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(i, 2));
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i, 2), (short)Math.Clamp(Math.Round(value * gain), short.MinValue, short.MaxValue));
        }
        stream.Position = 0;
        return stream;
    }
}
