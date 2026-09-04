using System.Linq;
using System.Reflection;

namespace IrodoriTtsYwk.Launcher.Contracts;

/// <summary>
/// 版と上流 pin の読み口（本体 yomiwakechan2 の「版の一元定義」を倣う）。
/// <para>
/// 表示版は <c>AppDisplayVersion</c>（launcher/Directory.Build.props）が唯一の定義箇所で、
/// <c>InformationalVersion</c> としてアセンブリに載る。上流 pin は <b>publish 経路限定</b>の
/// <c>AssemblyMetadata</c>（<c>UpstreamIrodoriTts</c>／<c>UpstreamIrodoriTtsServer</c>）で、
/// 通常のビルドでは属性ごと存在しない＝ここは null を返す。
/// </para>
/// <para>
/// 焼いた値は AboutView と、`/ywk/status.upstream` との突合（腐りの検知点＝配布物の樹と
/// 走っている wrapper が別の pin を名乗ったら告げる）に使う。
/// </para>
/// </summary>
public static class AppVersion
{
    private static readonly Assembly Self = typeof(AppVersion).Assembly;

    /// <summary>表示版（例 <c>v0.1.0</c>）。</summary>
    public static string Display =>
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Self.GetName().Version?.ToString()
        ?? "v0";

    /// <summary>Irodori-TTS の commit（publish でだけ焼かれる。開発ビルドは null）。</summary>
    public static string? UpstreamIrodoriTts => Metadata("UpstreamIrodoriTts");

    /// <summary>Irodori-TTS-Server の commit（同上）。</summary>
    public static string? UpstreamIrodoriTtsServer => Metadata("UpstreamIrodoriTtsServer");

    /// <summary>publish 経路で組まれた成果物か（上流 pin が焼かれているか）。</summary>
    public static bool IsReleaseBuild => UpstreamIrodoriTts is not null;

    private static string? Metadata(string key) => Self
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value;
}
