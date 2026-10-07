using System;
using System.IO;
using System.Security.Cryptography;

namespace FamicomPlayer;

internal static class SelfTests
{
    internal static int Run()
    {
        try
        {
            foreach (var asset in new[] { "console.png", "tv.png", "pad.png", "volume-knob.png", "cartridge.png", "library-shell.png", "default-cover.png", "CartridgeCrt.ps", "App.ico" })
                using (var stream = BundledAssets.Open(asset)) if (stream.Length < 100) throw new Exception("Bundled asset is empty: " + asset);
            var hardware = SpriteAssets.Load("console.png");
            var pixel = new byte[4]; hardware.CopyPixels(new System.Windows.Int32Rect(0,0,1,1), pixel, 4, 0);
            if (pixel[3] != 0) throw new Exception("Sprite background is not transparent.");
            CheckCartridgeStore();
            CheckCartridgeBreath();
            CheckSignInNavigation();
            CollectionTests.Run();
            if (!BundledAssets.ReadText("YouTubeBridge.js").Contains("window.famicomplayerNative")) throw new Exception("Bundled playback bridge missing.");
            const string requested = "https://www.youtube.com/watch?v=2qfoSxRRCJc&list=RD2qfoSxRRCJc&index=1";
            if (YouTubeAddress.Parse(requested).AbsoluteUri != requested) throw new Exception("Mix must preserve both the selected video and RD playlist.");
            foreach (var link in new[] { "youtu.be/2qfoSxRRCJc", "youtube.com/shorts/2qfoSxRRCJc", "youtube.com/live/2qfoSxRRCJc", "youtube.com/embed/2qfoSxRRCJc" })
                if (YouTubeAddress.Parse(link).AbsoluteUri != "https://www.youtube.com/watch?v=2qfoSxRRCJc") throw new Exception("Watch address conversion failed: " + link);
            if (!YouTubeAddress.Parse("youtu.be/2qfoSxRRCJc?t=1m23s").Query.EndsWith("t=1m23s")) throw new Exception("Timestamp lost.");
            if (YouTubeAddress.Parse("youtube.com/playlist?list=PL1234567890").AbsolutePath != "/playlist") throw new Exception("Playlist lost.");
            foreach (var link in new[] { "https://youtube.com.evil.org/watch?v=2qfoSxRRCJc", "file:///etc/passwd", "https://user@youtube.com/watch?v=2qfoSxRRCJc", "youtube.com/watch?v=bad", "youtube.com/playlist?list=bad", "youtube.com/" })
            {
                bool rejected = false;
                try { YouTubeAddress.Parse(link); } catch (ArgumentException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid address accepted: " + link);
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: bundled artwork, transparent sprites, cartridge CRUD and restart persistence, collection pagination/cancellation/migration/import/update/unpack/delete, copied custom covers, invalid entry rejection, corrupt store recovery, two-puff WAV, sign-in navigation policy, playback bridge, Mix/short/live/embed normalization, timestamps and playlists.");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), error.ToString()); return 1; }
    }
    private static void CheckSignInNavigation()
    {
        foreach (string url in new[] { "https://accounts.google.com/signin/v2/challenge", "https://accounts.youtube.com/accounts/SetSID", "https://www.google.com/accounts/SetSID", "https://www.youtube.com/" })
            if (!BrowserNavigationPolicy.IsAllowed(url)) throw new Exception("Sign-in handoff was blocked: " + url);
        foreach (string url in new[] { "http://accounts.youtube.com/", "https://accounts.youtube.com.evil.test/", "https://user@accounts.google.com/", "https://accounts.google.com:444/", "https://www.google.com/search", "file:///tmp/a", "about:blank" })
            if (BrowserNavigationPolicy.IsAllowed(url)) throw new Exception("Unexpected sign-in navigation allowed: " + url);
        if (!BrowserNavigationPolicy.IsAllowed("about:blank", true)) throw new Exception("Popup initialization blocked.");
    }
    private static void CheckCartridgeBreath()
    {
        using var stream = WidgetWindow.MakeCartridgeBreath();
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF" || reader.ReadInt32() != stream.Length - 8)
            throw new Exception("Cartridge breath WAV header is invalid.");
        stream.Position = 24;
        int rate = reader.ReadInt32();
        if (rate != 22050 || stream.Length != 44 + 16758 * 2) throw new Exception("Cartridge breath WAV duration is invalid.");
        double Energy(double start, double end)
        {
            stream.Position = 44 + (long)(start * rate) * 2;
            double sum = 0; int count = (int)((end - start) * rate);
            for (int i = 0; i < count; i++) { double sample = reader.ReadInt16(); sum += sample * sample; }
            return Math.Sqrt(sum / count);
        }
        if (Energy(.06, .24) < 80 || Energy(.43, .61) < 80 || Energy(.30, .38) != 0 || Energy(.68, .75) != 0)
            throw new Exception("Cartridge breath must contain two audible puffs separated by silence.");
        static double SignalEnergy(Stream wave)
        {
            wave.Position = 44;
            using var samples = new BinaryReader(wave, System.Text.Encoding.ASCII, true);
            double total = 0;
            while (wave.Position < wave.Length) { double sample = samples.ReadInt16(); total += sample * sample; }
            return total;
        }
        using var click = WidgetWindow.MakeClick();
        double clickEnergy = SignalEnergy(click);
        double relativeLevel = Math.Sqrt(SignalEnergy(stream) / clickEnergy);
        if (Math.Abs(relativeLevel - .5) > .001) throw new Exception("Cartridge breath level must be 50% of the click over the same playback window.");
        if (Math.Abs(Math.Sqrt(clickEnergy / 2866) - 688.5) > 1) throw new Exception("Cartridge click did not receive its 30% level increase.");
    }
    private static void CheckCartridgeStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FamicomPlayer-Test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CartridgeStore(directory);
            string original = Path.Combine(directory, "import.png");
            using (var source = BundledAssets.Open("default-cover.png")) using (var target = File.Create(original)) source.CopyTo(target);
            var a = store.Save(null, "밤 산책", "https://youtu.be/2qfoSxRRCJc?t=30", original, false);
            File.Delete(original);
            store = new CartridgeStore(directory);
            if (store.Entries.Count != 1 || !File.Exists(store.ImagePath(store.Entries[0])) || !store.Entries[0].Url.EndsWith("t=30")) throw new Exception("Custom cartridge did not survive restart/source removal.");
            var b = store.Save(null, "자동 커버", "https://youtube.com/playlist?list=PL1234567890", null, true);
            var updated = store.Save(a.Id, "이름 수정", a.Url, null, true);
            if (store.Entries.Count != 2 || updated.ImageFile.Length != 0 || File.Exists(store.ImagePath(a))) throw new Exception("Cartridge update/orphan cleanup failed.");
            foreach (var invalid in new[] { "https://evil.example/watch?v=2qfoSxRRCJc", "not a url" })
            {
                bool rejected = false; try { store.Save(null,"invalid",invalid,null,true); } catch (ArgumentException) { rejected = true; }
                if (!rejected || store.Entries.Count != 2) throw new Exception("Invalid cartridge was persisted.");
            }
            store.Delete(a.Id); store = new CartridgeStore(directory);
            if (store.Entries.Count != 1 || store.Entries[0].Id != b.Id) throw new Exception("Delete persistence failed.");
            File.WriteAllText(Path.Combine(directory,"cartridges.json"), "{corrupt");
            store = new CartridgeStore(directory);
            if (store.LoadWarning == null || Directory.GetFiles(directory,"*.damaged-*").Length != 1) throw new Exception("Corrupt library was not preserved.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
