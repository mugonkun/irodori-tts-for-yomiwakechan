using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Codeer.Friendly;
using Codeer.Friendly.Windows;
using Codeer.Friendly.Windows.Grasp;
using RM.Friendly.WPFStandardControls;

namespace Vr2SaveTool
{
    internal sealed class Vr2Exception : Exception
    {
        public Vr2Exception(string message) : base(message) { }
    }

    /// <summary>
    /// VOICEROID2（32bit WPF・API 無し）を Codeer.Friendly で駆動し、［音声保存］で wav を取り出す。
    ///
    /// UI 木の当たりは yomiwakechan2 の Vr2Controller.cs / _v1_reference/VR2_Talker.cs を「読んで」倣った：
    ///   - タブ＝AI.Framework.Wpf.Controls.TitledTabControl の [0]=ボイスプリセット / [1]=調声
    ///   - 本文欄・再生・音声保存＝AI.Talk.Editor.TextEditView の LogicalTree の索引
    ///   - プリセット一覧＝System.Windows.Controls.ListView の [0]=標準 / [1]=ユーザー
    ///   - プリセット名＝調声タブの AI.Framework.Wpf.Controls.TextBoxEx[0]
    /// ただし索引は決め打ちにせず、dump で実機の木を出して確定する（Resolve* が自動判定も持つ）。
    /// </summary>
    internal sealed class Vr2Ui
    {
        private WindowsAppFriend _app;
        private Process _process;
        private WindowControl _top;
        private WPFTabControl _voicePresetTab;
        private WPFTabControl _tuneTab;
        private WPFTextBox _talkTextBox;
        private WPFButtonBase _saveButton;
        private WPFListView _stdList;
        private WPFListView _usrList;
        private readonly Action<string> _log;

        public int ProcessId { get { return _process.Id; } }

        public Vr2Ui(Action<string> log)
        {
            _log = log;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        /// <summary>プロセス検出とアタッチだけ（UI 木は触らない）。モーダルダイアログが出ている最中でも安全。</summary>
        public void AttachProcessOnly()
        {
            _process = FindEditorProcess();
            if (_process == null)
            {
                throw new Vr2Exception("VOICEROID2 が起動していません（タイトル 'VOICEROID2' のウィンドウが見つからない）。");
            }
            _log("プロセス: PID=" + _process.Id + " Title='" + _process.MainWindowTitle + "'");

            if (Environment.Is64BitOperatingSystem)
            {
                bool isWow64;
                if (IsWow64Process(_process.Handle, out isWow64) && !isWow64)
                {
                    throw new Vr2Exception("VOICEROID2 が 64bit と判定されました。本道具は 32bit 版前提です。");
                }
            }

            _app = new WindowsAppFriend(_process);
            _top = ResolveMainWindow();
        }

        /// <summary>UI 木（タブ・本文欄・音声保存ボタン・プリセット一覧）を解決する。</summary>
        public void Attach()
        {
            AttachProcessOnly();

            // モーダルダイアログが残っているとタブ操作が刺さるので、先に畳む。
            CloseStrayDialogs();

            AppVar[] tabs = _top.GetFromTypeFullName("AI.Framework.Wpf.Controls.TitledTabControl");
            if (tabs.Length < 2) throw new Vr2Exception("TitledTabControl が " + tabs.Length + " 個しか見つかりません。対応外の版の可能性。");
            _voicePresetTab = new WPFTabControl(tabs[0]);
            _tuneTab = new WPFTabControl(tabs[1]);

            AppVar[] editViews = _top.GetFromTypeFullName("AI.Talk.Editor.TextEditView");
            if (editViews.Length == 0) throw new Vr2Exception("AI.Talk.Editor.TextEditView が見つかりません。");
            IWPFDependencyObjectCollection<DependencyObject> editUis = editViews[0].LogicalTree(TreeRunDirection.Descendants);
            _log("TextEditView LogicalTree の要素数: " + editUis.Count);

            _talkTextBox = new WPFTextBox(editUis[4]);   // v1/本体の実績索引
            _saveButton = ResolveSaveButton(editUis);

            _tuneTab.EmulateChangeSelectedIndex(1);
            _voicePresetTab.EmulateChangeSelectedIndex(0);
            _stdList = new WPFListView(_top.GetFromTypeFullName("System.Windows.Controls.ListView")[0]);
            _tuneTab.EmulateChangeSelectedIndex(1);
            _voicePresetTab.EmulateChangeSelectedIndex(1);
            AppVar[] listViews = _top.GetFromTypeFullName("System.Windows.Controls.ListView");
            _usrList = listViews.Length > 1 ? new WPFListView(listViews[1]) : null;
            _log("プリセット一覧: 標準 " + _stdList.ItemCount + " 件 / ユーザー " + (_usrList == null ? 0 : _usrList.ItemCount) + " 件");
        }

        private WindowControl ResolveMainWindow()
        {
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
                var main = new WindowControl(_app, _process.MainWindowHandle);
                if (main.GetFromTypeFullName("AI.Talk.Editor.TextEditView").Length > 0) return main;
            }
            foreach (WindowControl w in WindowControl.GetTopLevelWindows(_app))
            {
                if (w.GetFromTypeFullName("AI.Talk.Editor.TextEditView").Length > 0) return w;
            }
            return WindowControl.FromZTop(_app);
        }

        private static Process FindEditorProcess()
        {
            for (int i = 0; i < 3; i++)
            {
                foreach (Process p in Process.GetProcesses())
                {
                    if (p.MainWindowHandle != IntPtr.Zero
                        && (p.MainWindowTitle == "VOICEROID2" || p.MainWindowTitle == "VOICEROID2*"))
                    {
                        return p;
                    }
                }
                if (i < 2) Thread.Sleep(500);
            }
            return null;
        }

        // ---- UI 木の診断 -------------------------------------------------

        internal static string SafeTypeName(AppVar v)
        {
            try { return (string)v["GetType"]()["FullName"]().Core; }
            catch (Exception) { return "(型不明)"; }
        }

        private static string SafeStringProp(AppVar v, string prop)
        {
            try
            {
                AppVar a = v[prop]();
                object core = a.Core;
                return core == null ? "" : core.ToString();
            }
            catch (Exception) { return ""; }
        }

        /// <summary>TextEditView の LogicalTree を索引つきで書き出す（索引を決め打ちにしないための実機確認）。</summary>
        public void DumpEditTree()
        {
            AppVar[] editViews = _top.GetFromTypeFullName("AI.Talk.Editor.TextEditView");
            IWPFDependencyObjectCollection<DependencyObject> editUis = editViews[0].LogicalTree(TreeRunDirection.Descendants);
            _log("---- TextEditView LogicalTree（" + editUis.Count + " 要素）----");
            for (int i = 0; i < editUis.Count; i++)
            {
                AppVar item = editUis[i];
                string type = SafeTypeName(item);
                string name = SafeStringProp(item, "Name");
                string tip = SafeStringProp(item, "ToolTip");
                string content = "";
                if (type.IndexOf("TextBlock", StringComparison.Ordinal) >= 0)
                {
                    content = SafeStringProp(item, "Text");
                }
                _log(string.Format("[{0,3}] {1}{2}{3}{4}",
                    i, type,
                    name == "" ? "" : "  Name='" + name + "'",
                    tip == "" ? "" : "  ToolTip='" + tip + "'",
                    content == "" ? "" : "  Text='" + content + "'"));
            }
        }

        /// <summary>
        /// ［音声保存］ボタンを解決する。ToolTip / Name に「音声保存」を含む ButtonBase を優先し、
        /// 見つからなければ v1 実績の索引 24 に落とす。
        /// </summary>
        private WPFButtonBase ResolveSaveButton(IWPFDependencyObjectCollection<DependencyObject> editUis)
        {
            // 実機の木は Button → StackPanel → Image → TextBlock(ラベル) の並びで平坦に出る。
            // ラベル「音声保存」の直前にある Button が目当て、という当たり方をする。
            int lastButton = -1;
            for (int i = 0; i < editUis.Count; i++)
            {
                AppVar item = editUis[i];
                string type = SafeTypeName(item);
                if (type == "System.Windows.Controls.Button" || type.EndsWith(".ButtonBase", StringComparison.Ordinal))
                {
                    lastButton = i;
                    continue;
                }
                if (type.IndexOf("TextBlock", StringComparison.Ordinal) < 0) continue;
                string label = SafeStringProp(item, "Text");
                if (label.IndexOf("音声保存", StringComparison.Ordinal) >= 0 && lastButton >= 0)
                {
                    _log("音声保存ボタン: 索引 " + lastButton + " をラベル「" + label + "」（索引 " + i + "）から自動判定");
                    return new WPFButtonBase(editUis[lastButton]);
                }
            }
            if (editUis.Count > 24)
            {
                _log("音声保存ボタン: 自動判定できず、v1 実績の索引 24 を採る（type=" + SafeTypeName(editUis[24]) + "）");
                return new WPFButtonBase(editUis[24]);
            }
            throw new Vr2Exception("音声保存ボタンを特定できません（LogicalTree の要素数 " + editUis.Count + "）。");
        }

        // ---- プリセット ---------------------------------------------------

        private sealed class PresetRef
        {
            public int TabIndex;   // 0=標準 / 1=ユーザー
            public int Index;
            public string Name;
        }

        private List<PresetRef> _presets;

        /// <summary>標準／ユーザーの両タブを巡回してプリセット名を採る（本体 Vr2Controller の手順に倣う）。</summary>
        public List<string> ScanPresets()
        {
            _presets = new List<PresetRef>();
            var names = new List<string>();
            ScanTab(_stdList, 0, names);
            if (_usrList != null) ScanTab(_usrList, 1, names);
            return names;
        }

        private void ScanTab(WPFListView list, int tabIndex, List<string> names)
        {
            if (list == null) return;
            _voicePresetTab.EmulateChangeSelectedIndex(tabIndex);
            for (int i = 0; i < list.ItemCount; i++)
            {
                list.EmulateChangeSelectedIndex(i);
                _tuneTab.EmulateChangeSelectedIndex(1);
                string name;
                try
                {
                    name = new WPFTextBox(
                        _tuneTab.VisualTree(TreeRunDirection.Descendants).ByType("AI.Framework.Wpf.Controls.TextBoxEx")[0]).Text;
                }
                catch (Exception ex)
                {
                    _log("  (タブ " + tabIndex + " 索引 " + i + ") 名前の取得に失敗: " + ex.Message);
                    continue;
                }
                if (names.Contains(name))
                {
                    _log("  プリセット名「" + name + "」が重複。後から見つかった方を無視。");
                    continue;
                }
                names.Add(name);
                _presets.Add(new PresetRef { TabIndex = tabIndex, Index = i, Name = name });
            }
        }

        public void SelectPreset(string presetName)
        {
            if (_presets == null) ScanPresets();
            PresetRef target = null;
            foreach (PresetRef p in _presets)
            {
                if (p.Name == presetName) { target = p; break; }
            }
            if (target == null)
            {
                throw new Vr2Exception("プリセット「" + presetName + "」が見つかりません。存在するのは: " + string.Join(" / ", _presets.ConvertAll(x => x.Name).ToArray()));
            }
            _tuneTab.EmulateChangeSelectedIndex(1);
            _voicePresetTab.EmulateChangeSelectedIndex(target.TabIndex);
            (target.TabIndex == 0 ? _stdList : _usrList).EmulateChangeSelectedIndex(target.Index);
            _log("プリセット選択: 「" + target.Name + "」（タブ " + target.TabIndex + " 索引 " + target.Index + "）");
            Thread.Sleep(200);
        }

        public void SetText(string text)
        {
            _talkTextBox.EmulateChangeText(text);
            Thread.Sleep(150);
            _log("本文を設定（" + text.Length + " 字）");
        }

        // ---- 音声保存 -----------------------------------------------------

        /// <summary>
        /// ［音声保存］を押し、ネイティブ保存ダイアログに檔名を流し込んで保存する。
        /// 保存ボタンの押下は Async（モーダルダイアログでこちらが固まらないように）。
        /// </summary>
        public void SaveWav(string outPath, int timeoutMs) { SaveWav(outPath, timeoutMs, null); }

        public void SaveWav(string outPath, int timeoutMs, string expectedText)
        {
            string dir = Path.GetDirectoryName(outPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(outPath)) File.Delete(outPath);
            string txtSide = Path.ChangeExtension(outPath, ".txt");
            if (File.Exists(txtSide)) File.Delete(txtSide);

            CloseStrayDialogs();

            _log("［音声保存］を押す（Async）");
            _saveButton.EmulateClick(new Async());

            var sw = Stopwatch.StartNew();

            // 0) VOICEROID2 2.x は先に WPF の「音声保存」設定窓（AI.Talk.Editor.SaveWaveWindow）を出す。
            //    「１つのファイルに書き出す」を選んで OK を押す（設定が「毎回表示しない」なら出ないので任意扱い）。
            HandleSaveWaveSettingsWindow(15000);

            // 1) 保存ダイアログ（可視 Edit を持つ #32770）を待つ
            NativeDialogs.WinInfo dlg = NativeDialogs.WaitForWindow(
                _process.Id,
                w => w.ClassName == "#32770" && NativeDialogs.FindFileNameEdit(w.Handle) != IntPtr.Zero,
                timeoutMs);
            if (dlg == null)
            {
                DumpTopLevel("保存ダイアログが出ませんでした。現在のトップレベル:");
                throw new Vr2Exception("保存ダイアログ（#32770・ファイル名欄あり）が " + timeoutMs + " ms 以内に現れませんでした。");
            }
            _log("保存ダイアログ: hwnd=0x" + dlg.Handle.ToInt64().ToString("X") + " text='" + dlg.Text + "'");
            foreach (NativeDialogs.WinInfo c in NativeDialogs.Descendants(dlg.Handle))
            {
                _log("  " + c);
            }

            // 2) ファイル名欄にフルパスを流し込む。
            //    ダイアログは表示直後に「前回の檔名」を自分で入れ直すことがあり、こちらの流し込みと競る
            //    （実機で 1 本目の檔名のまま 2 本目が保存された事故を観測。2026-09-04）。
            //    そこで ①落ち着くまで待ち ②入れ ③二度続けて同じ値が読めるまで入れ直す。
            IntPtr edit = NativeDialogs.FindFileNameEdit(dlg.Handle);
            NativeDialogs.Foreground(dlg.Handle);
            Thread.Sleep(400);
            string echoed = "";
            bool stable = false;
            for (int attempt = 0; attempt < 4 && !stable; attempt++)
            {
                NativeDialogs.TypeText(edit, outPath);
                // オートコンプリートが末尾に補完を足して選択している場合は Delete で落とす
                for (int fix = 0; fix < 3; fix++)
                {
                    string cur = NativeDialogs.TextOf(edit);
                    if (cur == outPath) break;
                    if (cur.StartsWith(outPath, StringComparison.Ordinal))
                    {
                        _log("オートコンプリートの補完を消す: '" + cur + "'");
                        NativeDialogs.PressDelete(edit);
                        Thread.Sleep(150);
                    }
                    else break;
                }
                Thread.Sleep(200);
                string a = NativeDialogs.TextOf(edit);
                Thread.Sleep(250);
                string b = NativeDialogs.TextOf(edit);
                echoed = b;
                stable = a == outPath && b == outPath;
                if (!stable) _log("ファイル名欄が安定しません（試行 " + (attempt + 1) + "）: '" + a + "' / '" + b + "'");
            }
            if (!stable)
            {
                throw new Vr2Exception("ファイル名欄に目的のパスを安定して入れられませんでした（欄の値: '" + echoed + "'）。");
            }
            _log("ファイル名欄に設定（2 回続けて一致）: '" + echoed + "'");

            // 3) ［保存］
            IntPtr saveBtn = NativeDialogs.FindButtonById(dlg.Handle, 1);
            if (saveBtn == IntPtr.Zero) saveBtn = NativeDialogs.FindButton(dlg.Handle, "保存", "Save", "OK");
            if (saveBtn == IntPtr.Zero) throw new Vr2Exception("保存ダイアログの［保存］ボタンが見つかりません。");
            _log("［保存］を押す（hwnd=0x" + saveBtn.ToInt64().ToString("X") + "）");
            NativeDialogs.Click(saveBtn);

            // 3b) 閉じるのを待つ。途中で「既に存在します／置き換えますか？」が出たら、
            //     **そこに書かれた檔名が目的のものである時だけ**［はい］。違う檔名なら［いいえ］で止める
            //     （他の便の成果を巻き添えで上書きしないため）。
            long closeLimit = Math.Max(5000, timeoutMs - sw.ElapsedMilliseconds);
            var closeWatch = Stopwatch.StartNew();
            bool closed = false;
            while (closeWatch.ElapsedMilliseconds < closeLimit)
            {
                if (!NativeDialogs.Alive(dlg.Handle)) { closed = true; break; }
                NativeDialogs.WinInfo confirm = FindOverwriteConfirm(dlg.Handle);
                if (confirm != null)
                {
                    string msg = OverwriteMessage(confirm.Handle);
                    if (msg.IndexOf(outPath, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        IntPtr yes = NativeDialogs.FindButtonById(confirm.Handle, 6);
                        if (yes == IntPtr.Zero) yes = NativeDialogs.FindButton(confirm.Handle, "はい", "Yes");
                        _log("上書き確認（目的の檔）: ［はい］");
                        NativeDialogs.Click(yes);
                    }
                    else
                    {
                        IntPtr no = NativeDialogs.FindButtonById(confirm.Handle, 7);
                        if (no == IntPtr.Zero) no = NativeDialogs.FindButton(confirm.Handle, "いいえ", "No");
                        _log("上書き確認が別の檔を指している。［いいえ］で止める: " + msg);
                        NativeDialogs.Click(no);
                        Thread.Sleep(300);
                        IntPtr cancel = NativeDialogs.FindButtonById(dlg.Handle, 2);
                        if (cancel != IntPtr.Zero) NativeDialogs.Click(cancel);
                        throw new Vr2Exception("保存ダイアログが別の檔名（" + msg.Replace("\r", " ").Replace("\n", " ") + "）を保存しようとしたので中止しました。");
                    }
                }
                Thread.Sleep(100);
            }
            if (!closed)
            {
                DumpTopLevel("保存ダイアログが閉じません。現在のトップレベル:");
                throw new Vr2Exception("保存ダイアログが閉じませんでした。");
            }
            _log("保存ダイアログが閉じた（" + sw.ElapsedMilliseconds + " ms）");

            // 4) 完了ダイアログ（「情報」等）を閉じつつ、檔ができるのを待つ
            long limit = timeoutMs;
            while (sw.ElapsedMilliseconds < limit)
            {
                CloseStrayDialogs();
                if (File.Exists(outPath))
                {
                    // 書き込み完了まで少し待つ（サイズが安定するまで）
                    long last = -1;
                    for (int i = 0; i < 40; i++)
                    {
                        long len = new FileInfo(outPath).Length;
                        if (len > 0 && len == last) break;
                        last = len;
                        Thread.Sleep(100);
                    }
                    // 完了ダイアログが遅れて出ることがあるので、もう一度掃く
                    Thread.Sleep(300);
                    CloseStrayDialogs();
                    _log("保存完了: " + outPath + " (" + new FileInfo(outPath).Length + " bytes, " + sw.ElapsedMilliseconds + " ms)");
                    if (File.Exists(txtSide))
                    {
                        // VOICEROID2 は wav と一緒に本文の .txt を書く。これを使って
                        // 「この wav が、この本文の音である」ことを機械的に確かめてから捨てる。
                        if (expectedText != null) VerifyCompanionText(txtSide, expectedText);
                        File.Delete(txtSide);
                        _log("同時出力された .txt を削除: " + txtSide);
                    }
                    else if (expectedText != null)
                    {
                        _log("注意: 同時出力の .txt が無いので本文の突き合わせは省略した。");
                    }
                    return;
                }
                Thread.Sleep(100);
            }
            DumpTopLevel("檔ができませんでした。現在のトップレベル:");
            throw new Vr2Exception("保存後 " + timeoutMs + " ms 以内に wav が現れませんでした: " + outPath);
        }

        /// <summary>
        /// wav と一緒に出る .txt の中身が、こちらが流し込んだ本文と一致するか確かめる。
        /// 一致しなければ「別の本文の音を、この檔名で保存してしまった」ことなので失敗にする。
        /// 文字コードは実機で UTF-16LE / Shift-JIS のどちらもあり得るので両方で読んで判定する。
        /// </summary>
        private void VerifyCompanionText(string txtPath, string expectedText)
        {
            byte[] bytes = File.ReadAllBytes(txtPath);
            string head = expectedText.Length >= 12 ? expectedText.Substring(0, 12) : expectedText;
            var tried = new List<string>();
            foreach (System.Text.Encoding enc in new System.Text.Encoding[]
                     { System.Text.Encoding.Unicode, System.Text.Encoding.UTF8, System.Text.Encoding.GetEncoding(932) })
            {
                string s;
                try { s = enc.GetString(bytes); } catch (Exception) { continue; }
                s = s.TrimStart('﻿');
                if (s.IndexOf(head, StringComparison.Ordinal) >= 0)
                {
                    _log("本文の突き合わせ OK（" + enc.WebName + " で先頭 " + head.Length + " 字が一致）");
                    return;
                }
                tried.Add(enc.WebName + ":'" + (s.Length > 20 ? s.Substring(0, 20) : s) + "'");
            }
            throw new Vr2Exception(
                "保存された wav が別の本文のものです（同時出力の .txt が一致しない）。期待の先頭='" + head
                + "' 実際=" + string.Join(" ", tried.ToArray()));
        }

        /// <summary>保存ダイアログの上に出る「既に存在します／置き換えますか？」の窓を探す。</summary>
        private NativeDialogs.WinInfo FindOverwriteConfirm(IntPtr saveDialog)
        {
            foreach (NativeDialogs.WinInfo w in NativeDialogs.TopLevelWindows(_process.Id))
            {
                if (w.Handle == saveDialog) continue;
                if (w.ClassName != "#32770") continue;
                if (NativeDialogs.FindFileNameEdit(w.Handle) != IntPtr.Zero) continue;
                string msg = OverwriteMessage(w.Handle);
                if (msg.IndexOf("既に存在します", StringComparison.Ordinal) >= 0
                    || msg.IndexOf("置き換え", StringComparison.Ordinal) >= 0
                    || msg.IndexOf("上書き", StringComparison.Ordinal) >= 0)
                {
                    return w;
                }
            }
            return null;
        }

        /// <summary>ダイアログ内の Static の文言を連結して返す。</summary>
        private static string OverwriteMessage(IntPtr dialog)
        {
            var sb = new System.Text.StringBuilder();
            foreach (NativeDialogs.WinInfo c in NativeDialogs.Descendants(dialog))
            {
                if (!c.ClassName.Equals("Static", StringComparison.OrdinalIgnoreCase)) continue;
                if (c.Text.Length == 0) continue;
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(c.Text);
            }
            return sb.ToString();
        }

        /// <summary>次の 1 本に移る前に、余計な窓（#32770・保存設定窓）が消えるまで待つ。</summary>
        public void WaitUntilQuiet(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool busy = false;
                foreach (NativeDialogs.WinInfo w in NativeDialogs.TopLevelWindows(_process.Id))
                {
                    if (w.ClassName == "#32770") { busy = true; break; }
                }
                if (!busy)
                {
                    foreach (WindowControl w in WindowControl.GetTopLevelWindows(_app))
                    {
                        string t;
                        try { t = w.TypeFullName; } catch (Exception) { continue; }
                        if (t == "AI.Talk.Editor.SaveWaveWindow") { busy = true; break; }
                    }
                }
                if (!busy) return;
                CloseStrayDialogs();
                Thread.Sleep(200);
            }
            _log("警告: " + timeoutMs + " ms 待っても余計な窓が消えませんでした。");
            DumpTopLevel("  現在のトップレベル:");
        }

        /// <summary>
        /// VOICEROID2 2.x の WPF 保存設定窓（AI.Talk.Editor.SaveWaveWindow・タイトル「音声保存」）を捌く。
        /// ファイル分割は必ず「１つのファイルに書き出す」にしてから OK。
        /// ポーズ（開始 0 / 終了 800 ms 等）は司令官の設定なので**触らない**（実測値はログに残す）。
        /// 窓が出ない構成（「音声保存時に毎回設定を表示する」が外れている）なら何もしない。
        /// </summary>
        private void HandleSaveWaveSettingsWindow(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            WindowControl dlg = null;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                foreach (WindowControl w in WindowControl.GetTopLevelWindows(_app))
                {
                    string t;
                    try { t = w.TypeFullName; } catch (Exception) { continue; }
                    if (t == "AI.Talk.Editor.SaveWaveWindow") { dlg = w; break; }
                }
                if (dlg != null) break;
                // #32770（ファイル選択）が先に出たなら設定窓は無い構成
                foreach (NativeDialogs.WinInfo w in NativeDialogs.TopLevelWindows(_process.Id))
                {
                    if (w.ClassName == "#32770" && NativeDialogs.FindFileNameEdit(w.Handle) != IntPtr.Zero)
                    {
                        _log("保存設定窓は出ませんでした（ファイル選択ダイアログが先）。");
                        return;
                    }
                }
                Thread.Sleep(50);
            }
            if (dlg == null)
            {
                _log("保存設定窓（SaveWaveWindow）は現れませんでした。そのまま進む。");
                return;
            }

            IWPFDependencyObjectCollection<DependencyObject> tree = dlg.AppVar.VisualTree(TreeRunDirection.Descendants);
            AppVar single = null, ok = null;
            var pauses = new List<string>();
            for (int i = 0; i < tree.Count; i++)
            {
                AppVar item = tree[i];
                string type = SafeTypeName(item);
                if (type == "System.Windows.Controls.RadioButton")
                {
                    string content = SafeStringProp(item, "Content");
                    if (single == null && content.IndexOf("１つのファイル", StringComparison.Ordinal) >= 0) single = item;
                }
                else if (type == "System.Windows.Controls.Button")
                {
                    if (ok == null && SafeStringProp(item, "Content") == "OK") ok = item;
                }
                else if (type == "AI.Framework.Wpf.Controls.FormattedTextBox")
                {
                    pauses.Add(SafeStringProp(item, "Text"));
                }
            }
            _log("保存設定窓: ポーズ欄の現在値 = [" + string.Join(", ", pauses.ToArray()) + "]（触らない）");

            if (single == null) throw new Vr2Exception("保存設定窓に「１つのファイルに書き出す」が見つかりません。");
            bool wasChecked = false;
            try { object c = single["IsChecked"]().Core; wasChecked = c is bool && (bool)c; } catch (Exception) { }
            if (!wasChecked)
            {
                new WPFButtonBase(single).EmulateClick();
                _log("ファイル分割を「１つのファイルに書き出す」へ変更した（元は別の選択）。");
            }
            else
            {
                _log("ファイル分割は既に「１つのファイルに書き出す」。");
            }

            if (ok == null) throw new Vr2Exception("保存設定窓に OK ボタンが見つかりません。");
            _log("保存設定窓の OK を押す（Async）");
            new WPFButtonBase(ok).EmulateClick(new Async());
            Thread.Sleep(300);
        }

        /// <summary>「情報」「確認」等の付随ダイアログを見つけたら既定ボタンで閉じる。</summary>
        public void CloseStrayDialogs()
        {
            // 前回の失敗などで WPF の保存設定窓が残っていたらキャンセルで畳む
            foreach (WindowControl w in WindowControl.GetTopLevelWindows(_app))
            {
                string t;
                try { t = w.TypeFullName; } catch (Exception) { continue; }
                if (t != "AI.Talk.Editor.SaveWaveWindow") continue;
                try
                {
                    IWPFDependencyObjectCollection<DependencyObject> tree = w.AppVar.VisualTree(TreeRunDirection.Descendants);
                    for (int i = 0; i < tree.Count; i++)
                    {
                        if (SafeTypeName(tree[i]) != "System.Windows.Controls.Button") continue;
                        if (SafeStringProp(tree[i], "Content") != "キャンセル") continue;
                        _log("残っていた保存設定窓をキャンセルで閉じる");
                        new WPFButtonBase(tree[i]).EmulateClick(new Async());
                        Thread.Sleep(400);
                        break;
                    }
                }
                catch (Exception ex) { _log("保存設定窓の後始末に失敗（続行）: " + ex.Message); }
            }

            foreach (NativeDialogs.WinInfo w in NativeDialogs.TopLevelWindows(_process.Id))
            {
                if (w.ClassName != "#32770") continue;
                // ファイル名欄を持つ＝保存ダイアログなので触らない
                if (NativeDialogs.FindFileNameEdit(w.Handle) != IntPtr.Zero) continue;
                IntPtr ok = NativeDialogs.FindButtonById(w.Handle, 1);
                if (ok == IntPtr.Zero) ok = NativeDialogs.FindButton(w.Handle, "OK", "はい", "Yes");
                if (ok == IntPtr.Zero) continue;
                // 安全弁：保存ダイアログの［保存］を誤って押さない（初期化中で Edit が見えない瞬間がある）。
                string btnText = NativeDialogs.TextOf(ok).Replace("&", "");
                if (btnText.IndexOf("保存", StringComparison.Ordinal) >= 0)
                {
                    _log("付随ダイアログの既定ボタンが「" + btnText + "」なので触らない: text='" + w.Text + "'");
                    continue;
                }
                _log("付随ダイアログを閉じる: text='" + w.Text + "' button='" + btnText + "'");
                NativeDialogs.Click(ok);
                Thread.Sleep(200);
            }
        }

        /// <summary>
        /// メイン以外のトップレベルウィンドウ（＝ダイアログ）の WPF 木を索引つきで書き出す。
        /// VOICEROID2 2.x の［音声保存］は Win32 の #32770 ではなく WPF の独自ダイアログなので、
        /// ここを読んで操作先を決める。
        /// </summary>
        public void DumpDialogTrees()
        {
            foreach (WindowControl w in WindowControl.GetTopLevelWindows(_app))
            {
                string cls = w.WindowClassName;
                string text = w.GetWindowText();
                string type;
                try { type = w.TypeFullName; } catch (Exception) { type = "(取得不可)"; }
                _log("== Window class='" + cls + "' text='" + text + "' type=" + type);
                if (text == "VOICEROID2" || text == "VOICEROID2*") { _log("   （メインウィンドウなので木は省略）"); continue; }
                try
                {
                    IWPFDependencyObjectCollection<DependencyObject> tree = w.AppVar.VisualTree(TreeRunDirection.Descendants);
                    _log("   VisualTree " + tree.Count + " 要素");
                    for (int i = 0; i < tree.Count; i++)
                    {
                        AppVar item = tree[i];
                        string t = SafeTypeName(item);
                        string name = SafeStringProp(item, "Name");
                        string s = "";
                        if (t.IndexOf("TextBlock", StringComparison.Ordinal) >= 0 || t.IndexOf("TextBox", StringComparison.Ordinal) >= 0)
                            s = SafeStringProp(item, "Text");
                        else if (t.IndexOf("Button", StringComparison.Ordinal) >= 0 || t.IndexOf("ContentControl", StringComparison.Ordinal) >= 0)
                            s = SafeStringProp(item, "Content");
                        _log(string.Format("   [{0,3}] {1}{2}{3}", i, t,
                            name == "" ? "" : "  Name='" + name + "'",
                            s == "" ? "" : "  Value='" + s + "'"));
                    }
                }
                catch (Exception ex)
                {
                    _log("   VisualTree の取得に失敗: " + ex.Message);
                }
            }
        }

        public void DumpTopLevel(string header)
        {
            _log(header);
            foreach (NativeDialogs.WinInfo w in NativeDialogs.TopLevelWindows(_process.Id))
            {
                _log("  " + w);
                if (w.ClassName == "#32770")
                {
                    foreach (NativeDialogs.WinInfo c in NativeDialogs.Descendants(w.Handle))
                    {
                        _log("    " + c);
                    }
                }
            }
        }
    }
}
