using System;
using System.IO;

namespace FamicomPlayer;

internal static class BundledAssets
{
    internal static Stream Open(string name) => typeof(BundledAssets).Assembly.GetManifestResourceStream("FamicomPlayer.Assets." + name)
        ?? throw new InvalidOperationException("Bundled asset is missing: " + name);

    internal static string ReadText(string name)
    {
        using var stream = Open(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
