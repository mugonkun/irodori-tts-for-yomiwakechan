using System;
using System.Net;
using System.Net.Sockets;

namespace IrodoriTtsYwk.Launcher.Services.Server;

/// <summary>ポートが空いているかを見る口（テストは偽物を差す）。</summary>
public interface IPortProbe
{
    /// <summary><paramref name="host"/>:<paramref name="port"/> に bind できるか。</summary>
    bool IsFree(string host, int port);
}

/// <summary>
/// 起こす前の TCP 検査（裁定 52＝<b>塞がっていたら止まって告知する。次のポートを探さない</b>）。
/// <para>
/// 実際に 1 回 bind して即座に閉じる（<c>ExclusiveAddressUse</c> を立てて
/// <c>SO_REUSEADDR</c> の相乗りを防ぐ＝uvicorn が後で失敗する状態を先に見つける）。
/// </para>
/// <para>
/// これだけでは検査と起動の間の狭い窓が残るので、<see cref="ServerBindFailure"/> が
/// uvicorn の bind 失敗行も見る（二重の網）。
/// </para>
/// </summary>
public sealed class TcpPortProbe : IPortProbe
{
    public bool IsFree(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            return false;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            // 名前指定は使わない（契約 ⑴＝127.0.0.1 の 1 本だけ）。読めない指定は検査を省く。
            return true;
        }

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(address, port);
            listener.ExclusiveAddressUse = true;
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch (SocketException)
            {
                // 閉じ損ねは無害
            }

            listener?.Dispose();
        }
    }
}
