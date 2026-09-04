using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Vr2SaveTool
{
    /// <summary>
    /// Win32 のネイティブダイアログ（#32770）を扱う口。
    /// VOICEROID2 の［音声保存］が出す保存ダイアログは WPF ではないので Friendly では掴めない。
    /// ここだけ生の User32 で抜ける（列挙・WM_SETTEXT・BM_CLICK）。
    /// WM_SETTEXT / WM_GETTEXT はシステムがプロセス境界を跨いでマーシャリングするので
    /// 別プロセスのコントロールにも効く。
    /// </summary>
    internal static class NativeDialogs
    {
        private const uint WM_SETTEXT = 0x000C;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const uint BM_CLICK = 0x00F5;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

        internal sealed class WinInfo
        {
            public IntPtr Handle;
            public string ClassName = "";
            public string Text = "";
            public int CtrlId;
            public int Depth;
            public bool Visible;

            public override string ToString()
            {
                return new string(' ', Depth * 2)
                       + "hwnd=0x" + Handle.ToInt64().ToString("X")
                       + " class='" + ClassName + "'"
                       + " id=" + CtrlId
                       + " visible=" + (Visible ? "1" : "0")
                       + " text='" + Text + "'";
            }
        }

        internal static string ClassOf(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        internal static string TextOf(IntPtr hWnd)
        {
            int len = (int)SendMessageW(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            if (len <= 0)
            {
                var sb0 = new StringBuilder(512);
                GetWindowTextW(hWnd, sb0, sb0.Capacity);
                return sb0.ToString();
            }
            var sb = new StringBuilder(len + 2);
            SendMessageW(hWnd, WM_GETTEXT, new IntPtr(sb.Capacity), sb);
            return sb.ToString();
        }

        /// <summary>指定プロセスの、可視トップレベルウィンドウをすべて返す。</summary>
        internal static List<WinInfo> TopLevelWindows(int pid)
        {
            var result = new List<WinInfo>();
            EnumWindows((h, l) =>
            {
                uint wpid;
                GetWindowThreadProcessId(h, out wpid);
                if (wpid != (uint)pid) return true;
                if (!IsWindowVisible(h)) return true;
                result.Add(new WinInfo
                {
                    Handle = h,
                    ClassName = ClassOf(h),
                    Text = TextOf(h),
                    CtrlId = GetDlgCtrlID(h),
                    Depth = 0,
                    Visible = true,
                });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>子ウィンドウを深さ優先で列挙する（ダイアログの構造を読むための診断用）。</summary>
        internal static List<WinInfo> Descendants(IntPtr parent, int depth = 1, int maxDepth = 6)
        {
            var result = new List<WinInfo>();
            if (depth > maxDepth) return result;
            var children = new List<IntPtr>();
            EnumChildWindows(parent, (h, l) => { children.Add(h); return true; }, IntPtr.Zero);
            foreach (IntPtr h in children)
            {
                // EnumChildWindows は孫まで返すので、直接の子だけ拾い直すのは面倒。
                // ここでは平坦な一覧で十分（class と id で識別する）。
                result.Add(new WinInfo
                {
                    Handle = h,
                    ClassName = ClassOf(h),
                    Text = TextOf(h),
                    CtrlId = GetDlgCtrlID(h),
                    Depth = depth,
                    Visible = IsWindowVisible(h),
                });
            }
            return result;
        }

        /// <summary>
        /// 保存ダイアログのファイル名入力欄（Edit）を探す。
        /// Vista 以降のコモンダイアログは ComboBoxEx32 &gt; ComboBox &gt; Edit（ctrl id 1148 系）だが、
        /// 版差があるので「Edit クラスで可視のもの」を上から拾う方式にする。
        /// </summary>
        internal static IntPtr FindFileNameEdit(IntPtr dialog)
        {
            // ナビゲーションペインの検索欄も class="Edit" なので、素朴に「最初の Edit」を採ると取り違える。
            // 実機（Windows 11・VOICEROID2 の「名前を付けて保存」2026-09-04）ではファイル名欄は
            // ComboBox 配下の Edit で ctrl id = 1001 だった。まず既知の id で狙う。
            IntPtr byId = IntPtr.Zero;
            EnumChildWindows(dialog, (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                if (!string.Equals(ClassOf(h), "Edit", StringComparison.OrdinalIgnoreCase)) return true;
                int id = GetDlgCtrlID(h);
                if (id != 1001 && id != 1148 && id != 1152) return true;
                byId = h;
                return false;
            }, IntPtr.Zero);
            if (byId != IntPtr.Zero) return byId;

            // 次に ComboBoxEx32 / ComboBox（ファイル名コンボ）配下の Edit。
            IntPtr combo = IntPtr.Zero;
            EnumChildWindows(dialog, (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                string cls = ClassOf(h);
                if (!cls.Equals("ComboBoxEx32", StringComparison.OrdinalIgnoreCase)
                    && !cls.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) return true;
                combo = h;
                return false;
            }, IntPtr.Zero);

            IntPtr found = IntPtr.Zero;
            if (combo != IntPtr.Zero)
            {
                EnumChildWindows(combo, (h, l) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    if (!string.Equals(ClassOf(h), "Edit", StringComparison.OrdinalIgnoreCase)) return true;
                    found = h;
                    return false;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero) return found;
            }

            // 退路＝可視 Edit の先頭（旧様式のダイアログ）
            EnumChildWindows(dialog, (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                if (!string.Equals(ClassOf(h), "Edit", StringComparison.OrdinalIgnoreCase)) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>ダイアログ内のボタンを、表示文字列の部分一致で探す（&amp; のアクセスキーは除いて比較）。</summary>
        internal static IntPtr FindButton(IntPtr dialog, params string[] contains)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(dialog, (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                string cls = ClassOf(h);
                if (!cls.Equals("Button", StringComparison.OrdinalIgnoreCase)) return true;
                string txt = TextOf(h).Replace("&", "");
                foreach (string c in contains)
                {
                    if (txt.IndexOf(c, StringComparison.Ordinal) >= 0)
                    {
                        found = h;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>ダイアログ内の、指定コントロール ID のボタンを探す（IDOK=1・IDCANCEL=2）。</summary>
        internal static IntPtr FindButtonById(IntPtr dialog, int ctrlId)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(dialog, (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                if (!ClassOf(h).Equals("Button", StringComparison.OrdinalIgnoreCase)) return true;
                if (GetDlgCtrlID(h) != ctrlId) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        internal static void SetText(IntPtr hWnd, string text)
        {
            SendMessageW(hWnd, WM_SETTEXT, IntPtr.Zero, text);
        }

        private const uint WM_CHAR = 0x0102;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint EM_SETSEL = 0x00B1;
        private const int VK_BACK = 0x08;
        private const int VK_DELETE = 0x2E;
        private const int VK_RETURN = 0x0D;

        /// <summary>
        /// エディットに「人が打つのと同じ」文字入力を送る。
        ///
        /// 実機で分かった事実（2026-09-04）：Windows のコモン保存ダイアログは、
        /// 2 回目以降に開かれた時、WM_SETTEXT で欄の表示だけ書き換えても**内部の檔名を更新しない**。
        /// その状態で［保存］を押すと前回の檔名で保存され、上書き確認だけが新しい欄と食い違う。
        /// WM_CHAR は引数にポインタを持たないのでプロセスを跨いでも届き、ダイアログの内部状態も追随する。
        /// </summary>
        internal static void TypeText(IntPtr edit, string text, int perCharDelayMs = 4)
        {
            // 全選択して消す
            SendMessageW(edit, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
            PostMessageW(edit, WM_CHAR, new IntPtr(VK_BACK), IntPtr.Zero);
            Thread.Sleep(80);
            foreach (char ch in text)
            {
                PostMessageW(edit, WM_CHAR, new IntPtr(ch), IntPtr.Zero);
                if (perCharDelayMs > 0) Thread.Sleep(perCharDelayMs);
            }
            Thread.Sleep(200);
        }

        /// <summary>オートコンプリートが末尾に足した選択部分を消す（Delete キー相当）。</summary>
        internal static void PressDelete(IntPtr hWnd)
        {
            PostMessageW(hWnd, WM_KEYDOWN, new IntPtr(VK_DELETE), IntPtr.Zero);
            PostMessageW(hWnd, WM_KEYUP, new IntPtr(VK_DELETE), IntPtr.Zero);
        }

        /// <summary>Enter キーを送る（保存ダイアログの確定）。</summary>
        internal static void PressEnter(IntPtr hWnd)
        {
            PostMessageW(hWnd, WM_KEYDOWN, new IntPtr(VK_RETURN), IntPtr.Zero);
            PostMessageW(hWnd, WM_KEYUP, new IntPtr(VK_RETURN), IntPtr.Zero);
        }

        /// <summary>
        /// ボタンを押す。モーダルダイアログのボタンを SendMessage で叩くと、
        /// 相手が処理を終えるまでこちらが固まる（保存処理は数秒かかる）ので PostMessage を使う。
        /// </summary>
        internal static void Click(IntPtr hWnd)
        {
            PostMessageW(hWnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        internal static void Foreground(IntPtr hWnd)
        {
            try { SetForegroundWindow(hWnd); } catch (Exception) { }
        }

        internal static bool Alive(IntPtr hWnd)
        {
            return IsWindow(hWnd) && IsWindowVisible(hWnd);
        }

        /// <summary>
        /// 条件に合うトップレベルウィンドウが現れるまで待つ。
        /// </summary>
        internal static WinInfo WaitForWindow(int pid, Func<WinInfo, bool> match, int timeoutMs, int pollMs = 50)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                foreach (WinInfo w in TopLevelWindows(pid))
                {
                    if (match(w)) return w;
                }
                Thread.Sleep(pollMs);
            }
            return null;
        }

        /// <summary>条件に合うトップレベルウィンドウが消えるまで待つ。</summary>
        internal static bool WaitForWindowGone(IntPtr hWnd, int timeoutMs, int pollMs = 50)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!Alive(hWnd)) return true;
                Thread.Sleep(pollMs);
            }
            return false;
        }
    }
}
