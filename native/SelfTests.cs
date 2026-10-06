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
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: embedded private Forge artwork, transparent sprite composition, cartridge CRUD and restart persistence, copied custom cover survives source removal, invalid entry rejection, corrupt store recovery, bundled playback bridge, Mix/short/live/embed normalization, timestamps and playlists.");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), error.ToString()); return 1; }
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
