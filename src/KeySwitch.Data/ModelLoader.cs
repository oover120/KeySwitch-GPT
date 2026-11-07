using System.IO.Compression;
using KeySwitch.Core;
namespace KeySwitch.Data;

public static class ModelLoader
{
    public static IReadOnlyList<LanguageProfile> Load() => new[] { Layouts.English, Layouts.Russian }.Select(Load).ToArray();
    private static LanguageProfile Load(KeyboardLayout layout)
    {
        var assembly = typeof(ModelLoader).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(layout.Id + ".ksm.gz", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (reader.ReadUInt32() != 0x314D534B) throw new InvalidDataException("Unsupported model format");
        return new(layout, PackedLexicon.Read(reader), PackedLexicon.Read(reader));
    }
}
