using System.Text;

namespace MarkLeaf.Documents;

public sealed class DocumentEncodingPolicy
{
    private static readonly EncodingProvider CodePages = CodePagesEncodingProvider.Instance;

    static DocumentEncodingPolicy()
    {
        Encoding.RegisterProvider(CodePages);
    }

    private DocumentEncodingPolicy(string id, string displayName, int codePage, bool hasBom = false)
    {
        Id = id;
        DisplayName = displayName;
        CodePage = codePage;
        HasBom = hasBom;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int CodePage { get; }

    public bool HasBom { get; }

    public static DocumentEncodingPolicy Utf8 { get; } = new("utf-8", "UTF-8", 65001);

    public static DocumentEncodingPolicy Utf8Bom { get; } = new("utf-8-bom", "UTF-8 with BOM", 65001, hasBom: true);

    public static DocumentEncodingPolicy Utf16NoBom { get; } = new("utf-16", "UTF-16", 1200);

    public static DocumentEncodingPolicy Utf16Bom { get; } = new("utf-16-bom", "UTF-16 with BOM", 1200, hasBom: true);

    public static DocumentEncodingPolicy Gb18030 { get; } = new("gb18030", "GB18030", 54936);

    public static DocumentEncodingPolicy Gbk { get; } = new("gbk", "GBK", 936);

    public static DocumentEncodingPolicy Gb2312 { get; } = new("gb2312", "GB2312", 20936);

    public static DocumentEncodingPolicy Big5 { get; } = new("big5", "Big5", 950);

    public static DocumentEncodingPolicy ShiftJis { get; } = new("shift_jis", "Shift_JIS", 932);

    public static DocumentEncodingPolicy UsAscii { get; } = new("us-ascii", "US-ASCII", 20127);

    public static IReadOnlyList<DocumentEncodingPolicy> All { get; } =
    [
        Utf8,
        Utf8Bom,
        Utf16NoBom,
        Utf16Bom,
        Gb18030,
        Gbk,
        Gb2312,
        Big5,
        ShiftJis,
        UsAscii,
    ];

    public Encoding CreateEncoding()
    {
        return CodePage switch
        {
            65001 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
            1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
            _ => Encoding.GetEncoding(
                CodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback),
        };
    }

    public static DocumentEncodingPolicy FromId(string? id)
    {
        if (string.Equals(id, "UTF-8 without BOM", StringComparison.OrdinalIgnoreCase))
        {
            return Utf8;
        }

        if (string.Equals(id, "UTF-16 without BOM", StringComparison.OrdinalIgnoreCase))
        {
            return Utf16NoBom;
        }

        return All.FirstOrDefault(policy =>
                string.Equals(policy.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(policy.DisplayName, id, StringComparison.OrdinalIgnoreCase))
            ?? Utf8;
    }

    public static DocumentEncodingPolicy FromEncoding(Encoding encoding, bool hasBom)
    {
        return encoding.CodePage switch
        {
            65001 => hasBom ? Utf8Bom : Utf8,
            1200 or 1201 => hasBom ? Utf16Bom : Utf16NoBom,
            54936 => Gb18030,
            20936 => Gb2312,
            936 => Gbk,
            950 => Big5,
            932 => ShiftJis,
            20127 => UsAscii,
            _ => All.FirstOrDefault(policy => policy.CodePage == encoding.CodePage) ?? Utf8,
        };
    }

    public static byte[] Encode(string text, DocumentEncodingPolicy policy)
    {
        var body = policy.CreateEncoding().GetBytes(text);
        return policy.HasBom
            ? GetPreamble(policy).Concat(body).ToArray()
            : body;
    }

    public static string Decode(byte[] bytes, DocumentEncodingPolicy policy)
    {
        var body = StripKnownPreamble(bytes);
        return policy.CreateEncoding().GetString(body);
    }

    public static bool ReloadWouldRiskGarbling(byte[] bytes, DocumentEncodingPolicy policy)
    {
        try
        {
            var decoded = Decode(bytes, policy);
            if (decoded.Contains('\uFFFD', StringComparison.Ordinal))
            {
                return true;
            }

            var roundTrip = Encode(decoded, policy);
            return !NormalizedBytes(bytes).SequenceEqual(NormalizedBytes(roundTrip));
        }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException or ArgumentException)
        {
            return true;
        }
    }

    public static DetectedEncoding Detect(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            return new DetectedEncoding(Utf8Bom, 3);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            return new DetectedEncoding(Utf16Bom, 2);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            return new DetectedEncoding(new DocumentEncodingPolicy("utf-16be-bom", "UTF-16 with BOM", 1201, hasBom: true), 2);
        }

        if (LooksLikeUtf16WithoutBom(bytes) && !ReloadWouldRiskGarbling(bytes, Utf16NoBom))
        {
            return new DetectedEncoding(Utf16NoBom, 0);
        }

        if (!ReloadWouldRiskGarbling(bytes, Utf8))
        {
            return new DetectedEncoding(Utf8, 0);
        }

        DocumentEncodingPolicy? bestPolicy = null;
        var bestScore = int.MinValue;
        foreach (var policy in new[] { Gb2312, Gbk, Gb18030, Big5, ShiftJis })
        {
            var text = TryDecodeRoundTrip(bytes, policy);
            if (text is null)
            {
                continue;
            }

            var score = PlausibilityScore(text, policy);
            if (score > bestScore
                || score == bestScore && EncodingTieBreakPriority(policy) > EncodingTieBreakPriority(bestPolicy))
            {
                bestScore = score;
                bestPolicy = policy;
            }
        }

        return new DetectedEncoding(bestPolicy ?? Utf8, 0);
    }

    internal static byte[] GetPreamble(DocumentEncodingPolicy policy)
    {
        return policy.CodePage switch
        {
            65001 => [0xef, 0xbb, 0xbf],
            1200 => [0xff, 0xfe],
            _ => [],
        };
    }

    private static string? TryDecodeRoundTrip(byte[] bytes, DocumentEncodingPolicy policy)
    {
        try
        {
            var text = Decode(bytes, policy);
            if (text.Contains('\uFFFD', StringComparison.Ordinal))
            {
                return null;
            }

            var roundTrip = Encode(text, policy);
            return NormalizedBytes(bytes).SequenceEqual(NormalizedBytes(roundTrip)) ? text : null;
        }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException or ArgumentException)
        {
            return null;
        }
    }

    private static int PlausibilityScore(string text, DocumentEncodingPolicy policy)
    {
        var score = 0;
        var cjkCount = 0;
        var kanaCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value == 0 || value is >= 1 and <= 8 || value is >= 11 and <= 12 || value is >= 14 and <= 31)
            {
                score -= 24;
                continue;
            }

            if (value is >= 0x3400 and <= 0x4DBF || value is >= 0x4E00 and <= 0x9FFF)
            {
                cjkCount++;
                score += 3;
            }
            else if (value is >= 0x3040 and <= 0x30FF || value is >= 0xFF65 and <= 0xFF9F)
            {
                kanaCount++;
                score += 2;
            }
            else
            {
                score += 1;
            }
        }

        if (policy == ShiftJis && kanaCount > 0)
        {
            score += 18;
        }

        if ((policy == Gb2312 || policy == Gbk || policy == Gb18030 || policy == Big5) && cjkCount > 0)
        {
            score += cjkCount * 8 + 6;
        }

        if (policy == ShiftJis && cjkCount > kanaCount)
        {
            score -= cjkCount * 8;
        }

        return score;
    }

    private static int EncodingTieBreakPriority(DocumentEncodingPolicy? policy)
    {
        if (policy == Gbk) return 5;
        if (policy == Gb2312) return 4;
        if (policy == Gb18030) return 3;
        if (policy == Big5) return 2;
        if (policy == ShiftJis) return 1;
        return 0;
    }

    private static bool LooksLikeUtf16WithoutBom(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
        {
            return false;
        }

        var evenNulls = 0;
        var oddNulls = 0;
        for (var index = 0; index < bytes.Length; index += 2)
        {
            if (bytes[index] == 0) evenNulls++;
            if (bytes[index + 1] == 0) oddNulls++;
        }

        var threshold = Math.Max(1, bytes.Length / 8);
        return evenNulls >= threshold || oddNulls >= threshold;
    }

    private static byte[] StripKnownPreamble(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            return bytes[3..];
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) || bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            return bytes[2..];
        }

        return bytes;
    }

    private static byte[] NormalizedBytes(byte[] bytes) => StripKnownPreamble(bytes);
}

public sealed record DetectedEncoding(DocumentEncodingPolicy Policy, int PreambleLength);
