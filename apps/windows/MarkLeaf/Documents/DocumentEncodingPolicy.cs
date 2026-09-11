using System.Text;

namespace MarkLeaf.Documents;

/// <summary>Native codec/menu adapter. Document rules are supplied by editor-core.</summary>
public sealed class DocumentEncodingPolicy
{
    private readonly KernelEncoding _value;
    private DocumentEncodingPolicy(KernelEncoding value) => _value = value;
    public string Id => _value.Id;
    public string DisplayName => _value.Label;
    public int CodePage => _value.CodePage;
    public bool HasBom => _value.Bom.Length > 0;
    public static IReadOnlyList<DocumentEncodingPolicy> All { get; } = DocumentCoreRuntime.Call<KernelEncoding[]>("encodings").Select(value => new DocumentEncodingPolicy(value)).ToArray();
    public static DocumentEncodingPolicy Utf8 => FromId("utf-8");
    public static DocumentEncodingPolicy Utf8Bom => FromId("utf-8-bom");
    public static DocumentEncodingPolicy Utf16NoBom => FromId("utf-16");
    public static DocumentEncodingPolicy Utf16Bom => FromId("utf-16-bom");
    public static DocumentEncodingPolicy Gb18030 => FromId("gb18030");
    public static DocumentEncodingPolicy Gbk => FromId("gbk");
    public static DocumentEncodingPolicy Gb2312 => FromId("gb2312");
    public static DocumentEncodingPolicy Big5 => FromId("big5");
    public static DocumentEncodingPolicy ShiftJis => FromId("shift_jis");
    public static DocumentEncodingPolicy UsAscii => FromId("us-ascii");
    public Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    public static DocumentEncodingPolicy FromId(string? id)
    {
        var value = DocumentCoreRuntime.Call<KernelEncoding>("resolveEncoding", new { value = id });
        return All.First(policy => policy.Id == value.Id);
    }
    public static DocumentEncodingPolicy FromEncoding(Encoding encoding, bool hasBom) =>
        All.First(policy => policy.CodePage == encoding.CodePage && policy.HasBom == hasBom);
    public static byte[] Encode(string text, DocumentEncodingPolicy policy) =>
        DocumentCoreRuntime.Call<int[]>("encode", new { text, encoding = policy.Id }).Select(value => checked((byte)value)).ToArray();
    public static string Decode(byte[] bytes, DocumentEncodingPolicy policy) =>
        DocumentCoreRuntime.Call<string>("decode", new { bytes = DocumentCoreRuntime.Bytes(bytes), encoding = policy.Id });
    public static bool ReloadWouldRiskGarbling(byte[] bytes, DocumentEncodingPolicy policy) =>
        DocumentCoreRuntime.Call<bool>("reloadWouldLoseData", new { bytes = DocumentCoreRuntime.Bytes(bytes), encoding = policy.Id });
    public static DetectedEncoding Detect(byte[] bytes)
    {
        var value = DocumentCoreRuntime.Call<KernelEncoding>("detectEncoding", new { bytes = DocumentCoreRuntime.Bytes(bytes) });
        return new DetectedEncoding(FromId(value.Id), value.Bom.Length);
    }
    internal static byte[] GetPreamble(DocumentEncodingPolicy policy) => policy._value.Bom.Select(value => (byte)value).ToArray();
}
public sealed record DetectedEncoding(DocumentEncodingPolicy Policy, int PreambleLength);
