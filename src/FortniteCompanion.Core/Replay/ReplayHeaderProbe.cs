// LED-N/Fortnite-ChestCounter (MIT) içindeki ReplayHeaderProbe'dan uyarlanmıştır.
namespace FortniteCompanion.Core.Replay;

/// <summary>
/// Replay dosya başlığının yalnızca ilk birkaç yüz baytını okur. Fortnite kayıt sürerken "canlı" bayrağını açık
/// tutar ve şifre çözme anahtarını sıfır bırakır; gerçek anahtar maç bitince yazılır. Bu nedenle maç bitmeden
/// replay içeriği okunamaz — bu Epic'in bilinçli tasarımıdır ve uygulama bunu aşmaya çalışmaz.
/// </summary>
public static class ReplayHeaderProbe
{
    private const uint FileMagic = 0x1CA2E27F;

    /// <summary>Maç hâlâ kaydediliyorsa true, bitti ise false, başlık okunamazsa null.</summary>
    public static bool? IsLive(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return IsLive(fs);
        }
        catch
        {
            return null;
        }
    }

    public static bool? IsLive(Stream stream)
    {
        try
        {
            using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (br.ReadUInt32() != FileMagic)
            {
                return null;
            }

            var fileVersion = br.ReadUInt32();
            if (fileVersion >= 7)
            {
                var count = br.ReadInt32();
                if (count < 0 || count > 64)
                {
                    return null;
                }
                stream.Seek(count * 20L, SeekOrigin.Current);
            }

            br.ReadUInt32(); // LengthInMS
            br.ReadUInt32(); // NetworkVersion
            br.ReadUInt32(); // Changelist

            var len = br.ReadInt32(); // FriendlyName (FString)
            stream.Seek(len < 0 ? -2L * len : len, SeekOrigin.Current);

            return br.ReadUInt32() != 0;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Replay şifreli ve anahtarı henüz yazılmamışsa (maç sürüyor) true.</summary>
    public static bool KeyMissing(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return KeyMissing(fs);
        }
        catch
        {
            return true;
        }
    }

    public static bool KeyMissing(Stream stream)
    {
        try
        {
            using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (br.ReadUInt32() != FileMagic) return true;
            var fileVersion = br.ReadUInt32();
            if (fileVersion >= 7)
            {
                var count = br.ReadInt32();
                if (count < 0 || count > 64) return true;
                stream.Seek(count * 20L, SeekOrigin.Current);
            }
            stream.Seek(12, SeekOrigin.Current);
            var len = br.ReadInt32();
            stream.Seek(len < 0 ? -2L * len : len, SeekOrigin.Current);
            br.ReadUInt32(); // bIsLive
            br.ReadInt64();  // timestamp
            br.ReadUInt32(); // compressed
            if (br.ReadUInt32() == 0) return false; // şifresiz
            var keyLen = br.ReadInt32();
            if (keyLen <= 0 || keyLen > 256) return true;
            return br.ReadBytes(keyLen).All(b => b == 0);
        }
        catch
        {
            return true;
        }
    }
}
