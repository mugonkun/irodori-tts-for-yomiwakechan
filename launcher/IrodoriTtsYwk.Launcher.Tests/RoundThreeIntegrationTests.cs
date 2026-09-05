using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IrodoriTtsYwk.Launcher.Services.Gpu;
using Xunit;

namespace IrodoriTtsYwk.Launcher.Tests;

/// <summary>
/// 便 D（3）＝統合席の釘。無人検分（<c>probe/d-launch-probe.ps1</c>）の段 k が
/// <b>実窓で</b>捕まえた 1 件＝「サポートされていない 16 ビット アプリケーション」の
/// ハードエラー窓（設計書 §23-2）。
/// <para>
/// <b>実測（この機体・2026-09-05）</b>＝プロセスのエラーモードが <c>0</c> の個体から
/// テキスト檔に付けた <c>python.exe</c> へ <c>Process.Start</c> を撃つと、Windows が窓を出し
/// <c>Start</c> は<b>その窓が押されるまで返らない</b>（40 秒で打ち切っても返らなかった）。
/// 同じ材料をエラーモード <c>0x8003</c> の個体から撃つと <b>12〜19 ms</b> で
/// <c>Win32Exception</c> が返る。テストの宿主（<c>dotnet test</c>）は後者なので、
/// <b>この檔の釘は「窓が出るか」ではなく「掛け方が正しいか」を押さえる</b>＝
/// ⑴ 掛けるのは<b>スレッド</b>のエラーモードで、プロセスの物は動かさない
/// （動かすと起こした python が引き継ぐ）⑵ 撃ち終えたら必ず元へ戻す
/// （<c>Task.Run</c> のスレッドはプールの使い回し）⑶ 例外は握り潰さない。
/// </para>
/// </summary>
public sealed class RoundThreeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ywk-d3-int-" + Guid.NewGuid().ToString("N")[..8]);

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    /// <summary><c>python.exe</c> の名を付けたテキスト檔（台本の段 k と同じ材料）。</summary>
    private string BadPython()
    {
        var dir = Path.Combine(_root, "runtime-cpu");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "python.exe");
        File.WriteAllText(path, "this is not an executable\r\n");
        return path;
    }

    private static Process Spawn(string fileName) =>
        new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

    [Fact]
    public void 起こせない実行檔は窓を待たずに例外で返る()
    {
        using var process = Spawn(BadPython());

        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<Win32Exception>(() => ProcessRunner.StartWithoutHardErrorBox(process));
        watch.Stop();

        // 握り潰さない＝呼び手（ServerProcess・ModelFetcher）が理由 1 行を作れる。
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"起こす段が {watch.Elapsed} 掛かった");
    }

    [Fact]
    public void 掛けるのはスレッドのエラーモードでプロセスの物は動かさない()
    {
        // プロセスのエラーモードを動かすと**起こした子が引き継ぐ**＝wrapper の python にまで
        // 「窓を出さない」約束が広がる。掛けるのはスレッドの物だけである。
        var processModeBefore = GetErrorMode();
        var threadModeBefore = GetThreadErrorMode();

        using (var process = Spawn(BadPython()))
        {
            Assert.Throws<Win32Exception>(() => ProcessRunner.StartWithoutHardErrorBox(process));
        }

        Assert.Equal(processModeBefore, GetErrorMode());
        Assert.Equal(threadModeBefore, GetThreadErrorMode());
    }

    [Fact]
    public void 起こせた個体でもスレッドのエラーモードは元へ戻る()
    {
        var threadModeBefore = GetThreadErrorMode();

        using var process = Spawn(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("exit 7");

        Assert.True(ProcessRunner.StartWithoutHardErrorBox(process));
        process.WaitForExit(20_000);

        Assert.Equal(7, process.ExitCode);
        Assert.Equal(threadModeBefore, GetThreadErrorMode());
    }

    [Fact]
    public async Task 別スレッドで撃つ口も同じ始末をする()
    {
        var threadModeBefore = GetThreadErrorMode();

        using var process = Spawn(BadPython());
        var start = ProcessRunner.StartAsync(process);
        await Assert.ThrowsAsync<Win32Exception>(async () => await start);

        Assert.Equal(threadModeBefore, GetThreadErrorMode());
        Assert.Equal(TaskStatus.Faulted, start.Status);
    }

    [Fact]
    public async Task 起こせない実行檔は列挙の口でも理由1行で返る()
    {
        // ProcessRunner.RunAsync（窓の GPU 列挙・門の検分が通る路）＝**例外を投げない**契約。
        var runner = new ProcessRunner { StartTimeout = TimeSpan.FromSeconds(3) };

        var watch = Stopwatch.StartNew();
        var result = await runner.RunAsync(
            BadPython(), [], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        watch.Stop();

        Assert.False(result.Started);
        Assert.False(result.TimedOut);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"起こす段が {watch.Elapsed} 掛かった");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
