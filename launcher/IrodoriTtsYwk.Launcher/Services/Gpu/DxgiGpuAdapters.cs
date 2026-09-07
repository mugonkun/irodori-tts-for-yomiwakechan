using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IrodoriTtsYwk.Launcher.Services.Gpu;

/// <summary>
/// アダプタの名前と<b>専用</b>メモリの総量を DXGI から数える実機用の口（裁定 110 D2・2026-09-08）。
/// <para>
/// <c>CreateDXGIFactory1</c> →<c>IDXGIFactory1::EnumAdapters1</c> →
/// <c>IDXGIAdapter1::GetDesc1</c> の 3 手だけ。読むのは <c>Description</c>・
/// <c>DedicatedVideoMemory</c>・<c>AdapterLuid</c> の 3 欄で、
/// <b><c>SharedSystemMemory</c> は読まない</b>（司令官の指示 2＝共有メモリは集計から外す）。
/// </para>
/// <para>
/// <b>vtable の席順が命</b>＝<c>[ComImport]</c> の宣言順がそのまま呼び出し口の番号になる。
/// <c>IDXGIObject</c>（4）→<c>IDXGIFactory</c>（5）→<c>IDXGIFactory1</c>（2）と、
/// <c>IDXGIObject</c>（4）→<c>IDXGIAdapter</c>（3）→<c>IDXGIAdapter1</c>（1）。
/// 呼ばない席も<b>宣言だけは要る</b>（席を詰めると別の関数を呼ぶ）。
/// </para>
/// <para>
/// <b>ソフトウェアのアダプタは数えない</b>＝<c>DXGI_ADAPTER_FLAG_SOFTWARE</c> と
/// <c>Microsoft Basic Render Driver</c> は GPU ではない。
/// </para>
/// </summary>
public sealed class DxgiGpuAdapters : IGpuAdapterInfoSource
{
    private const uint AdapterFlagSoftware = 2;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const string BasicRenderDriver = "Microsoft Basic Render Driver";

    /// <summary>
    /// いま居るアダプタ（<b>例外を投げない</b>＝<c>dxgi.dll</c> が無い機体・COM が落ちた機体は空）。
    /// </summary>
    public IReadOnlyList<GpuAdapterInfo> Adapters()
    {
        try
        {
            return Enumerate();
        }
        catch (DllNotFoundException)
        {
            return [];
        }
        catch (EntryPointNotFoundException)
        {
            return [];
        }
        catch (COMException)
        {
            return [];
        }
        catch (InvalidCastException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            // COM interop そのものが無い土台（trim・AOT）
            return [];
        }
    }

    private static List<GpuAdapterInfo> Enumerate()
    {
        var found = new List<GpuAdapterInfo>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factoryObject) != 0 || factoryObject is null)
        {
            return found;
        }

        var factory = (IDXGIFactory1)factoryObject;
        try
        {
            for (uint index = 0; index < 64; index++)
            {
                var hr = factory.EnumAdapters1(index, out var adapter);
                if (hr == DxgiErrorNotFound || adapter is null)
                {
                    break;
                }

                try
                {
                    // **失敗でも柄が返ってきたら放す**（是正・2026-09-08）＝`DXGI_ERROR_NOT_FOUND`
                    // 以外の失敗で out 引数が埋まっていた回だけ、参照が 1 つ残っていた
                    // （この列挙は 60 s に 1 度まで走り直せるので、積み上がる形だった）。
                    if (hr != 0)
                    {
                        break;
                    }

                    if (adapter.GetDesc1(out var desc) != 0)
                    {
                        continue;
                    }

                    if ((desc.Flags & AdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    var name = (desc.Description ?? string.Empty).Trim();
                    if (name.Contains(BasicRenderDriver, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dedicated = (ulong)desc.DedicatedVideoMemory;
                    found.Add(new GpuAdapterInfo(
                        OsGpuMemory.LuidToken(desc.AdapterLuid.HighPart, desc.AdapterLuid.LowPart),
                        name,
                        dedicated > long.MaxValue ? long.MaxValue : (long)dedicated));
                }
                finally
                {
                    Marshal.FinalReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(factoryObject);
        }

        return found;
    }

    /// <summary><c>LUID</c>（<c>LowPart</c>＝符号なし・<c>HighPart</c>＝符号つき）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary><c>DXGI_ADAPTER_DESC1</c>（<c>SIZE_T</c> は <c>nuint</c>）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // ---- IDXGIObject（4 席・呼ばない） ----
        void SetPrivateData();

        void SetPrivateDataInterface();

        void GetPrivateData();

        void GetParent();

        // ---- IDXGIFactory（5 席・呼ばない） ----
        void EnumAdapters();

        void MakeWindowAssociation();

        void GetWindowAssociation();

        void CreateSwapChain();

        void CreateSoftwareAdapter();

        // ---- IDXGIFactory1（2 席） ----
        [PreserveSig]
        int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);

        [PreserveSig]
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // ---- IDXGIObject（4 席・呼ばない） ----
        void SetPrivateData();

        void SetPrivateDataInterface();

        void GetPrivateData();

        void GetParent();

        // ---- IDXGIAdapter（3 席・呼ばない） ----
        void EnumOutputs();

        void GetDesc();

        void CheckInterfaceSupport();

        // ---- IDXGIAdapter1（1 席） ----
        [PreserveSig]
        int GetDesc1(out AdapterDesc1 desc);
    }

    // **必ず System32 から読む**（是正・2026-09-08）＝`dxgi.dll` は KnownDLLs に無く、既定の
    // 探索順はアプリの置き場が先。配布先の置き場は利用者が書けるので、置き換えの筋を閉じる。
    [DllImport("dxgi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateDXGIFactory1(
        ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? factory);
}
