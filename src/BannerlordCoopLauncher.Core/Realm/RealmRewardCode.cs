using System.Security.Cryptography;
using System.Text;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Realm;

// The Realm starter reward code. One code per Steam account: the code holds the
// player's SteamID64 plus a short signature, so staff can check in Coodsz Host
// that the code came from the tools and which Steam account it belongs to.
// The same account always gets the same code, so a second claim shows up as
// the same code in the ticket history. The signing key ships inside the tools,
// so this stops copied or made-up codes, not someone who takes the tool apart.
public static class RealmRewardCode
{
    public const string Prefix = "RLM";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TagLength = 6;
    private static readonly byte[] Key = { 0xAA, 0xC0, 0x07, 0x38, 0xC2, 0x27, 0xDB, 0xEE, 0xC4, 0x6C, 0xE9, 0xE0, 0xDD, 0x5C, 0xEC, 0xFC, 0x8E, 0x05, 0xA3, 0x69, 0x07, 0x43, 0xBE, 0x19, 0xD9, 0x05, 0x8B, 0xEF, 0x4A, 0x54, 0xFF, 0xE6 };

    public const string NoSteam = "Start Steam and sign in, then click the reward button again. The code is made from your Steam account.";

    public static string Create(ulong steamId64)
    {
        var data = new byte[8 + TagLength];
        WriteId(data, steamId64);
        Array.Copy(Tag(steamId64), 0, data, 8, TagLength);
        var text = Encode(data);
        var groups = Enumerable.Range(0, (text.Length + 3) / 4).Select(i => text.Substring(i * 4, Math.Min(4, text.Length - i * 4)));
        return Prefix + "-" + string.Join("-", groups);
    }

    public static string? ForCurrentSteamUser()
    {
        var id = SteamRegistry.TryGetSteamId64();
        return id == null ? null : Create(id.Value);
    }

    public static bool TryCheck(string? code, out ulong steamId64)
    {
        steamId64 = 0;
        if (string.IsNullOrWhiteSpace(code))
            return false;
        var text = new string(code.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (text.StartsWith(Prefix, StringComparison.Ordinal))
            text = text[Prefix.Length..];
        var data = Decode(text);
        if (data == null || data.Length != 8 + TagLength)
            return false;
        var id = ReadId(data);
        var expected = Tag(id);
        if (!CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, TagLength), data.AsSpan(8, TagLength)))
            return false;
        if (id < SteamIdBase || id > SteamIdBase + uint.MaxValue)
            return false;
        steamId64 = id;
        return true;
    }

    public static string ProfileUrl(ulong steamId64) => "https://steamcommunity.com/profiles/" + steamId64;

    public const ulong SteamIdBase = 76561197960265728UL;

    private static byte[] Tag(ulong steamId64)
    {
        var id = new byte[8];
        WriteId(id, steamId64);
        var message = Encoding.ASCII.GetBytes("realm-reward-v1:").Concat(id).ToArray();
        return HMACSHA256.HashData(Key, message);
    }

    private static void WriteId(byte[] buffer, ulong value)
    {
        for (var i = 7; i >= 0; i--)
        {
            buffer[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }

    private static ulong ReadId(byte[] buffer)
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | buffer[i];
        return value;
    }

    // Crockford base32: no I, L, O or U, so the code is easy to read and type.
    private static string Encode(byte[] data)
    {
        var builder = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    private static byte[]? Decode(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            var c = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
