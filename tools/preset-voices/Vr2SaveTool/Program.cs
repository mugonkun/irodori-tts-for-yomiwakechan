using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vr2SaveTool
{
    /// <summary>
    /// 便 P・VOICEROID2 一次 wav 取り出し道具。
    ///
    ///   Vr2SaveTool.exe dump  [--log &lt;path&gt;]
    ///       実機の UI 木（TextEditView の LogicalTree）とプリセット名一覧、
    ///       トップレベルウィンドウを書き出す。索引を決め打ちにしないための確認。
    ///
    ///   Vr2SaveTool.exe batch &lt;jobs.tsv&gt; [--log &lt;path&gt;] [--timeout &lt;ms&gt;]
    ///       jobs.tsv は 1 行 1 本、TAB 区切りで  プリセット名 \t 本文檔(UTF-8) \t 出力 wav パス。
    ///       '#' で始まる行と空行は無視。1 回のアタッチで全件を回す。
    ///
    /// 出力は UTF-8。ログは標準出力と（指定時は）檔の両方へ。
    /// </summary>
    internal static class Program
    {
        private static StreamWriter _logFile;

        private static void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
            Console.WriteLine(line);
            if (_logFile != null) { _logFile.WriteLine(line); _logFile.Flush(); }
        }

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (Exception) { }

            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: Vr2SaveTool.exe dump|batch <jobs.tsv> [--log <path>] [--timeout <ms>]");
                return 2;
            }

            string logPath = null;
            int timeoutMs = 120000;
            string command = args[0];
            string jobsPath = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--log" && i + 1 < args.Length) { logPath = args[++i]; }
                else if (args[i] == "--timeout" && i + 1 < args.Length) { timeoutMs = int.Parse(args[++i]); }
                else if (jobsPath == null) { jobsPath = args[i]; }
            }

            if (logPath != null)
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _logFile = new StreamWriter(logPath, false, new UTF8Encoding(false));
            }

            try
            {
                var ui = new Vr2Ui(Log);
                if (command == "dumpdlg")
                {
                    ui.AttachProcessOnly();
                    ui.DumpDialogTrees();
                    Log("dumpdlg 完了");
                    return 0;
                }
                ui.Attach();

                if (command == "dump")
                {
                    ui.DumpEditTree();
                    Log("---- プリセット一覧 ----");
                    List<string> names = ui.ScanPresets();
                    for (int i = 0; i < names.Count; i++) Log("  [" + i + "] " + names[i]);
                    ui.DumpTopLevel("---- トップレベルウィンドウ ----");
                    Log("dump 完了");
                    return 0;
                }

                if (command == "batch")
                {
                    if (jobsPath == null) { Console.Error.WriteLine("batch には jobs.tsv が要ります。"); return 2; }
                    string[] lines = File.ReadAllLines(jobsPath, Encoding.UTF8);
                    List<string> presets = ui.ScanPresets();
                    Log("検出したプリセット " + presets.Count + " 件");

                    int ok = 0, ng = 0;
                    foreach (string raw in lines)
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        string[] cols = line.Split('\t');
                        if (cols.Length < 3) { Log("行の欄が足りません（3 欄必要）: " + line); ng++; continue; }
                        string preset = cols[0].Trim();
                        string textFile = cols[1].Trim();
                        string outPath = cols[2].Trim();
                        string text = File.ReadAllText(textFile, Encoding.UTF8).Trim();
                        Log("==== " + preset + " → " + outPath + " ====");
                        try
                        {
                            ui.SelectPreset(preset);
                            ui.SetText(text);
                            ui.SaveWav(outPath, timeoutMs, text);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            Log("失敗: " + ex.Message);
                            ng++;
                            // 中途半端な檔を残さない（次の便が「出来ている」と誤認しないため）
                            foreach (string junk in new[] { outPath, Path.ChangeExtension(outPath, ".txt") })
                            {
                                try { if (File.Exists(junk)) { File.Delete(junk); Log("失敗分を削除: " + junk); } }
                                catch (Exception) { }
                            }
                            try { ui.CloseStrayDialogs(); } catch (Exception) { }
                        }
                        // 次の 1 本に移る前に、余計な窓が消えるのを待つ（前の保存の残りが次に混ざらないように）
                        try { ui.WaitUntilQuiet(30000); } catch (Exception ex) { Log("後始末で例外（続行）: " + ex.Message); }
                    }
                    Log("batch 完了: 成功 " + ok + " / 失敗 " + ng);
                    return ng == 0 ? 0 : 1;
                }

                Console.Error.WriteLine("未知のコマンド: " + command);
                return 2;
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                Log(ex.StackTrace ?? "");
                return 1;
            }
            finally
            {
                if (_logFile != null) _logFile.Dispose();
            }
        }
    }
}
