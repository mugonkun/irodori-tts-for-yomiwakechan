namespace IrodoriTtsYwk.Launcher.ViewModels;

/// <summary>
/// <b>利用者の目に入る文字列の唯一の出所</b>（v2.0 段 C・<c>v2-plan.md</c> C-1・<c>v2-copy.md</c> §1）。
/// <para>
/// <see cref="UiText"/> とは<b>別の檔</b>である＝<b><see cref="UiStrings"/> は文・<see cref="UiText"/> は数</b>。
/// 数の書式（<c>GiB</c>・<c>ms</c>・桁区切り）はログと画面で食い違わせない決め事なので
/// <see cref="UiText"/> のまま 1 字も動かさない。ここに集めるのは<b>人間が読む文</b>だけである。
/// </para>
/// <para>
/// <b>ここに無い文字列を画面へ直に書かない。</b><c>Views/*.xaml</c> の
/// <c>Text=</c>／<c>Content=</c>／<c>Header=</c>／<c>ToolTip=</c> は
/// <c>{x:Static vm:UiStrings.…}</c> でここを引く。<c>WordLintTests</c> が
/// ⑴ この class の public な文字列 ⑵ XAML の可視属性 の 2 つだけを舐め、
/// 憲章 §6-1 の隠す語が 1 つでも現れたら落ちる。
/// </para>
/// <para>
/// <b>寄せない物</b>＝ログへ落とす文・例外の文・JSON の鍵・契約の欄名（`v2-plan.md` §3 の表）。
/// それらは工学側の語彙であり、<b>綴りが資産</b>なので触らない。
/// </para>
/// </summary>
public static class UiStrings
{
    // ===================== 主窓（MainWindow.xaml）=====================

    /// <summary>タブ「発話テスト」（<c>decisions.md</c> 116 が名指しで決めた札＝<b>据え置き</b>）。</summary>
    public const string TabTry = "発話テスト";

    /// <summary>タブ「声」（旧「話者」）。</summary>
    public const string TabVoices = "声";

    /// <summary>タブ「設定」。</summary>
    public const string TabSettings = "設定";

    /// <summary>歯車の中のタブ「詳しい状態」（旧「状態」）。</summary>
    public const string TabStatus = "詳しい状態";

    /// <summary>歯車の中のタブ「このアプリについて」。</summary>
    public const string TabAbout = "このアプリについて";

    /// <summary>歯車の釦を読み上げるときの名（UIA から読める札）。</summary>
    public const string GearName = "詳しい状態とこのアプリについて";

    /// <summary>歯車の層を閉じる。</summary>
    public const string CloseButton = "閉じる";

    /// <summary>記録を在り処ごと開く（帯・設定 › 詳細・詳しい状態の 3 面で同じ札）。</summary>
    public const string OpenLogButton = "ログを開く";

    /// <summary>報告の受け皿（<c>decisions.md</c> 131）＝画面に出るときは必ずこの 1 行。</summary>
    public const string ReportHint =
        "うまくいかないときは、このファイルを添えて X の @yomiwakechan までお知らせください。";

    // ===================== 詳しい状態（StatusView.xaml）=====================

    /// <summary>大きな 1 行の札（旧「状態」）。</summary>
    public const string StatusStateLabel = "いまの様子";

    /// <summary>準備へ進む 1 手（旧「取得へ進む」）。</summary>
    public const string StatusAcquireButton = "はじめの準備をする";

    /// <summary>動かすための一式を入れ直す 1 手（旧「実行系を組み直す」）。</summary>
    public const string StatusRebuildButton = "動かすための一式を入れ直す";

    /// <summary>走っている入れ直しをやめる。</summary>
    public const string StatusRebuildCancelButton = "やめる";

    /// <summary>
    /// <b>入れ直しの代金の 1 行</b>（<c>StatusRebuildRuntimeText</c> の括弧の中）＝
    /// 落としたものが揃っていて取り直しが要らない回（是正・段 G・medium 19）。
    /// <para>
    /// <b>綴りの直しどころ</b>＝「取得キャッシュ」は 設定 › 詳細 では
    /// <see cref="SettingsCacheLabel"/>＝「一時ファイル」と呼んでおり、同じ物に 2 つの名が出ていた。
    /// 「原檔」は `v2-copy.md` §9-1 が「ファイル」に替えた語、<c>GiB</c> は憲章 §6-1 が
    /// 詳細の中だけに限った綴りで、この行は<b>畳みの外</b>に在る。
    /// </para>
    /// </summary>
    public const string RefetchNothingToDo =
        "一時ファイルが揃っているので、ダウンロードのやり直しはありません";

    /// <summary>同・足りない回の頭（後ろに件数を継ぐ）。</summary>
    public const string RefetchMissingHead = "一時ファイルが ";

    /// <summary>同・件数の後ろ（後ろに丸めた量を継ぐ）。</summary>
    public const string RefetchMissingMiddle = " 件足りないので、押すと ";

    /// <summary>同・締め。</summary>
    public const string RefetchMissingTail = " をダウンロードし直します";

    /// <summary>
    /// <b>配信中に × を押したときの 1 行</b>（憲章 §4-21 の後半の<b>逐語</b>）。
    /// 出るのは本体の読み上げが走っている間だけで、走っていない回は今までどおり黙って閉じる。
    /// </summary>
    public const string ExitWhileHostBusy =
        "いま読み分けちゃん2 の読み上げに使われています。終了しますか。";

    /// <summary>畳みの見出し（設定・発話テスト・声・このアプリについて とも同じ 1 語）。</summary>
    public const string AdvancedHeader = "詳細（上級者向け）";

    /// <summary>畳みの冒頭 1 行（設定と詳しい状態で同文）。</summary>
    public const string AdvancedIntro =
        "ここは、うまく動かないときに中を見るための欄です。ふだんは見なくてかまいません。";

    /// <summary>詳細 1 行目＝使っているグラフィックス（旧「GPU」）。</summary>
    public const string StatusGpuLabel = "使うグラフィックス";

    /// <summary>詳細 2 行目＝いま動いている場所（旧「デバイス」）。</summary>
    public const string StatusDeviceLabel = "いま動いている場所";

    /// <summary>詳細 3 行目＝動かし方（旧「変種」）。</summary>
    public const string StatusVariantLabel = "動かし方";

    /// <summary>詳細 4 行目＝準備運転（旧「暖機」）。</summary>
    public const string StatusWarmupLabel = "準備運転";

    /// <summary>詳細 5 行目＝声の下ごしらえ（旧「参照潜在」）。</summary>
    public const string StatusPrecomputeLabel = "声の下ごしらえ";

    /// <summary>詳細 6 行目＝GPU メモリ（札は据え置き・既定 OFF のまま）。</summary>
    public const string StatusMemoryLabel = "GPU メモリ";

    /// <summary>詳細 6 行目の 2 段目＝下ごしらえ済みの声（旧「潜在キャッシュ」）。</summary>
    public const string StatusLatentCacheLabel = "下ごしらえ済みの声";

    /// <summary>詳細 6 行目の 3 段目＝元の音声から読む声（旧「参照ボイス」）。</summary>
    public const string StatusVoiceMemoryLabel = "元の音声から読む声";

    /// <summary>
    /// GPU メモリの註（旧い長い註＝<c>torch の allocator …</c> は<b>消した</b>・
    /// 差し替えは 1 行だけ＝<c>v2-copy.md</c> §1-2 の 166 行目）。
    /// </summary>
    public const string StatusMemoryNote = "数字は、いま GPU にどれだけ載っているかの目安です。";

    /// <summary>詳細の最後＝記録（旧「ログ（末尾 20 行）」）。</summary>
    public const string StatusLogLabel = "記録（末尾）";

    // ===================== 発話テスト（TryView.xaml）=====================

    /// <summary>左の欄の札（旧「本文」）。</summary>
    public const string TryInputLabel = "しゃべらせる文";

    /// <summary>声の選択（旧「話者」）。</summary>
    public const string TryVoiceLabel = "声";

    /// <summary>品質の 2 択（旧「サンプリング歩数」）。</summary>
    public const string TryQualityLabel = "品質";

    /// <summary>品質＝はやい（旧 釦「10」）。</summary>
    public const string TryQualityFast = "はやい";

    /// <summary>品質＝きれい（旧 釦「40」）。</summary>
    public const string TryQualityFine = "きれい";

    /// <summary>話し方の指示（旧「演技指示（caption・空＝既定）」）。</summary>
    public const string TryCaptionLabel = "話し方の指示（任意・例：明るく／落ち着いて）";

    /// <summary>速さ（旧「読み速さ（0.25〜4.0・空＝既定）」）。</summary>
    public const string TrySpeedLabel = "速さ（1.0 がふつう）";

    /// <summary>詳細の中＝歩数の数値欄（札は据え置き）。</summary>
    public const string TryStepsLabel = "サンプリング歩数";

    /// <summary>詳細の中＝文への効き。</summary>
    public const string TryCfgTextLabel = "文への効き（cfg_scale_text）";

    /// <summary>詳細の中＝演技の効き。</summary>
    public const string TryCfgCaptionLabel = "演技の効き（cfg_scale_caption）";

    /// <summary>詳細の中＝声への効き。</summary>
    public const string TryCfgSpeakerLabel = "声への効き（cfg_scale_speaker）";

    /// <summary>詳細の中＝乱数の種（札は据え置き）。</summary>
    public const string TrySeedLabel = "乱数の種（空＝毎回変わる）";

    /// <summary>撃つ釦（<c>decisions.md</c> 130 Q4）。</summary>
    public const string TrySynthesizeButton = "しゃべらせる";

    /// <summary>直前の音をもう一度（旧「もう一度再生」）。</summary>
    public const string TryReplayButton = "もう一度";

    /// <summary>止める。</summary>
    public const string TryStopButton = "停止";

    /// <summary>檔に書く（旧「wav に保存…」）。</summary>
    public const string TrySaveButton = "音声ファイルに保存…";

    // ===================== 声（VoicesView.xaml）=====================

    /// <summary>一覧の列＝名前（旧「表示名（＝話者 id）」）。</summary>
    public const string VoicesNameColumn = "名前";

    /// <summary>その列の吹き出し。</summary>
    public const string VoicesNameColumnTip =
        "読み分けちゃん2 と「発話テスト」で選ぶ名前です。日本語で構いません。";

    /// <summary>一覧の列＝元の音声ファイル（旧「参照 wav の檔名」）。</summary>
    public const string VoicesFileColumn = "元の音声ファイル";

    /// <summary>その列の吹き出し。</summary>
    public const string VoicesFileColumnTip =
        "アプリが写しておいた音声ファイルの名前です（声の名前ではありません）。";

    /// <summary>一覧の列＝種別（札は据え置き・値は <see cref="VoiceRow.KindText"/>）。</summary>
    public const string VoicesKindColumn = "種別";

    /// <summary>一覧の列＝下ごしらえ（旧「参照潜在」）。</summary>
    public const string VoicesLatentColumn = "下ごしらえ";

    /// <summary>右の欄の見出し（旧「話者を追加する」）。</summary>
    public const string VoicesAddHeader = "声を追加する";

    /// <summary>① の札（旧「① 参照する音声」）。</summary>
    public const string VoicesSourceLabel = "① 元にする音声ファイル（wav）";

    /// <summary>檔を選ぶ窓を出す。</summary>
    public const string VoicesBrowseButton = "選ぶ…";

    /// <summary>① の吹き出し。</summary>
    public const string VoicesSourceTip = "「選ぶ…」で選ぶか、ファイルの場所を貼り付けてください。";

    /// <summary>② の札（旧「② 話者の名前（日本語可）」）。</summary>
    public const string VoicesNameLabel = "② 名前（日本語で構いません）";

    /// <summary>③ の札（旧「演技指示の既定（任意）」）。</summary>
    public const string VoicesCaptionLabel = "話し方の指示（任意・例：明るく元気に）";

    /// <summary>追加の釦（旧「この内容で追加する」）。</summary>
    public const string VoicesAddButton = "この声を追加する";

    /// <summary>選んでいる 1 名の見出し（旧「選んでいる話者」）。</summary>
    public const string VoicesSelectedHeader = "選んでいる声";

    /// <summary>試聴。</summary>
    public const string VoicesPreviewButton = "試聴";

    /// <summary>試聴を止める。</summary>
    public const string VoicesStopPreviewButton = "停止";

    /// <summary>消す。</summary>
    public const string VoicesRemoveButton = "削除";

    /// <summary>一覧を読み直す。</summary>
    public const string VoicesRefreshButton = "一覧を更新";

    /// <summary>詳細の中＝下ごしらえを撃つ（旧「参照潜在を焼く」）。</summary>
    public const string VoicesPrecomputeButton = "この声を下ごしらえしておく（次から少し速くなります）";

    /// <summary>詳細の中＝同梱を戻す（旧「同梱のプリセットを入れ直す」）。</summary>
    public const string VoicesRestorePresetsButton = "最初から入っている声を入れ直す";

    // ===================== 設定（SettingsView.xaml）=====================

    /// <summary>ふだんの設定 ⑴（旧「ランチャの起動と同時にサーバを起こす」）。</summary>
    public const string SettingsAutoStart = "アプリを開いたら、自動で読み上げできるようにする";

    /// <summary>ふだんの設定 ⑵（旧「サーバが待機になったら暖機を撃つ」）。</summary>
    public const string SettingsWarmup = "開いたときに、よく使う声を先に準備しておく";

    /// <summary>⑵ の註（旧「暖機の最中でも合成はできます（…）」）。</summary>
    public const string SettingsWarmupNote = "準備中でも、しゃべらせられます。";

    /// <summary>ふだんの設定 ⑶＝声のファイルの場所。</summary>
    public const string SettingsVoicesDirLabel = "声のファイルの場所";

    /// <summary>その場所を開く。</summary>
    public const string SettingsOpenFolderButton = "フォルダを開く";

    /// <summary>詳細 1 行目＝使うグラフィックス（旧「GPU」）。</summary>
    public const string SettingsGpuLabel = "使うグラフィックス";

    /// <summary>詳細 1 行目の釦（旧「数え直す」）。</summary>
    public const string SettingsRefreshGpuButton = "見つけ直す";

    /// <summary>詳細 2 行目＝動かし方（旧「実行系の種類（変種）」）。</summary>
    public const string SettingsVariantLabel = "動かし方";

    /// <summary>詳細 2 行目の註（押す前に代金を告げる）。</summary>
    public const string SettingsVariantNote =
        "変えると、動かすための一式を 5 GB 前後ダウンロードし直します（10 分ほど）。";

    /// <summary>詳細 3 行目＝音質と速さ（旧「精度（上級者）」）。</summary>
    public const string SettingsPrecisionLabel = "音質と速さ";

    /// <summary>詳細 4 行目＝声の下ごしらえ（旧「参照潜在キャッシュ」）。</summary>
    public const string SettingsPrecomputeLabel = "声の下ごしらえ";

    /// <summary>詳細 4 行目のチェック（旧「話者の登録時に参照潜在（.pt）を焼く」）。</summary>
    public const string SettingsPrecomputeCheck =
        "声を追加したときに、下ごしらえしておく（次から少し速くなります）";

    /// <summary>詳細 4 行目の註（旧「中間（■）＝変種の既定に任せる…」）。</summary>
    public const string SettingsPrecomputeNote = "中間（■）＝おまかせ・チェック＝必ずする・空＝しない。";

    /// <summary>詳細 5 行目＝読み上げの動作（旧「起動」）。</summary>
    public const string SettingsRunLabel = "読み上げの動作";

    /// <summary>詳細 5 行目の 1 つ目（旧「サーバ停止」）。</summary>
    public const string SettingsStopButton = "いったん止める";

    /// <summary>詳細 5 行目の 2 つ目（旧「サーバ起動」）。</summary>
    public const string SettingsStartButton = "もう一度動かす";

    /// <summary>詳細 5 行目の 3 つ目（旧「初回取得をやり直す」）。</summary>
    public const string SettingsFirstRunButton = "はじめの準備をやり直す";

    /// <summary>
    /// 詳細 6 行目＝更新のときの差分取り直し（`v2-copy.md` §4 の 6 行目・既定 ON）。
    /// 段 C は 7 行で置いた＝この 1 行が段 E の持ち物だったため。段 F で入って <b>8 行</b>になる。
    /// </summary>
    public const string SettingsDifferentialUpdate = "更新のとき、新しくなった分だけ取り直す";

    /// <summary>その註（切ると何が起きるかを 1 行で言う）。</summary>
    public const string SettingsDifferentialUpdateNote =
        "切ると、新しい版に上げるたびに一式を丸ごとダウンロードし直します。";

    /// <summary>詳細 7 行目＝ファイルの場所（旧「データの置き場」）。</summary>
    public const string SettingsPathsLabel = "ファイルの場所";

    /// <summary>その 1 行目（旧「データ」＝据え置き）。</summary>
    public const string SettingsDataDirLabel = "データ";

    /// <summary>その 2 行目（旧「配布樹」）。</summary>
    public const string SettingsAppDirLabel = "アプリ";

    /// <summary>その註（旧「置き場の移動はまだ対応していません（…）」）。</summary>
    public const string SettingsPathsNote = "場所は変えられません。";

    /// <summary>詳細 8 行目＝一時ファイル（旧「取得キャッシュ」）。</summary>
    public const string SettingsCacheLabel = "一時ファイル";

    /// <summary>その註（旧「消すのはこの変種の取得台帳に載っている原檔だけです。…」）。</summary>
    public const string SettingsCacheNote =
        "ダウンロードのときに使った一時ファイルだけを消します。"
        + "消しても、もう一度ダウンロードすることにはなりません。";

    /// <summary>詳細 9 行目＝記録。</summary>
    public const string SettingsLogLabel = "記録";

    /// <summary>詳細 10 行目＝適用の前の 1 行。</summary>
    public const string SettingsApplyNote = "変えた設定は、次にアプリを開いたときから効きます。";

    /// <summary>適用。</summary>
    public const string SettingsApplyButton = "適用";

    /// <summary>取り消し。</summary>
    public const string SettingsRevertButton = "取り消し";

    // ===================== はじめの準備（FirstRunWizard.xaml）=====================

    /// <summary>前の段へ戻る。</summary>
    public const string WizardBackButton = "戻る";

    /// <summary>やめて閉じる。</summary>
    public const string WizardCancelButton = "やめる";

    /// <summary>同意のチェック（決裁 130 Q3＝<b>チェックは残す</b>・初回に 1 度だけ）。</summary>
    public const string WizardAcceptCheck =
        "上の内容を読みました。はじめの準備で、これらをこのパソコンにダウンロードすることに同意します。";

    /// <summary>お知らせの全文を開く。</summary>
    public const string WizardNoticesFullButton = "全文を見る";

    /// <summary>その畳みの見出し。</summary>
    public const string WizardNoticesHeader = "お知らせの全文";

    /// <summary>段 2 の詳細＝自分で選ぶ口（<b>初回の道の上には出さない</b>）。</summary>
    public const string WizardPickVariant = "動かし方を自分で選ぶ";

    /// <summary>段 2 の詳細＝落とす量の内訳。</summary>
    public const string WizardSizeHeader = "落とす量の内訳";

    /// <summary>段 2 の詳細の註（1 檔ずつ確かめる＝<c>sha256</c> の語は出さない）。</summary>
    public const string WizardVerifyNote = "ダウンロードした物は 1 つずつ、壊れていないか確かめます。";

    /// <summary>Windows の許可の窓の予告（本文）。</summary>
    public const string WizardUacBody =
        "Microsoft の部品を 1 つ入れます。"
        + "Windows の許可の窓が 1 度出るので「はい」を押してください。\n"
        + "入れないと、しゃべらせられないことがあります。";

    /// <summary>その窓の題。</summary>
    public const string WizardUacCaption = "Windows の許可について";

    /// <summary>準備の途中で閉じようとしたとき（本文）。</summary>
    public const string WizardCloseBody =
        "準備の途中です。やめて閉じますか。次に開いたときは、続きから始めます。";

    /// <summary>その窓の題。</summary>
    public const string WizardCloseCaption = "はじめの準備";

    // ===================== このアプリについて（AboutView.xaml）=====================

    /// <summary>版の見出し（旧「版」）。</summary>
    public const string AboutVersionLabel = "バージョン";

    /// <summary>透かしの見出し（旧「電子透かし」）。</summary>
    public const string AboutWatermarkLabel = "音に埋め込まれる印";

    /// <summary>但し書きの見出し（旧「参照ボイスの扱い」）。</summary>
    public const string AboutEthicsLabel = "声の元にする音声について";

    /// <summary>ライセンスの見出し（旧「ライセンスの所在」）。</summary>
    public const string AboutLicensesLabel = "ライセンス";

    /// <summary>末尾の註（旧「実行系・モデル・vc_redist は配布物に入っていません。…」）。</summary>
    public const string AboutLicensesNote =
        "動かすための一式と声のデータは、このアプリには入っていません。"
        + "はじめの準備のときに、公式の配布元からこのパソコンへダウンロードします。"
        + "それぞれのライセンス文も一緒に入ります。";

    /// <summary>使い方（<c>docs\</c> を開く）。</summary>
    public const string AboutGuideButton = "使い方を見る";

    /// <summary>報告用のログを保存（在り処を出すだけ）。</summary>
    public const string AboutSaveLogButton = "報告用のログを保存";

    // ===================== 文（ViewModel から出る 1 行）=====================

    // ---- 発話テスト ----

    /// <summary>開いたときに入っている見本の文。</summary>
    public const string TrySampleInput = "こんにちは。文字を打つと、この声でしゃべります。";

    /// <summary>
    /// <b>詳しい字は檔へ</b>（憲章 原則 6・§3-2 の書き方の規則）＝生の記録・番号つきの状態・
    /// 例外の文は画面に出さず、この 1 行で〔ログを開く〕へ送る。
    /// </summary>
    public const string SeeLog = " 詳しくは〔ログを開く〕から記録をご覧ください。";

    /// <summary>撃てないときの既定の 1 行。</summary>
    public const string TryCannotSpeak = "しゃべらせられません。";

    /// <summary>まだ準備が終わっていない（<b>釦の名を文の中で呼ばない</b>＝待てば済む）。</summary>
    public const string TryNotReady = "まだ準備ができていません。準備が終わるまでお待ちください。";

    /// <summary>撃っている間。</summary>
    public const string TryWorking = "作っています…";

    /// <summary>期限切れ。</summary>
    public const string TryTimedOut = "時間がかかりすぎたので、やめました。文を短くしてお試しください。";

    /// <summary>手が落ちた（詳しい字は檔へ）。</summary>
    public const string TryFailed = "うまく作れませんでした。";

    /// <summary>鳴っている。</summary>
    public const string TryPlaying = "再生しています。";

    /// <summary>鳴らせなかった。</summary>
    public const string TryPlayFailed = "再生できませんでした。";

    /// <summary>読み上げの用意が止まった（＋帯の〔もう一度動かす〕）。</summary>
    public const string TryServerDown = "読み上げの用意が止まりました。";

    /// <summary>保存する音がまだ無い。</summary>
    public const string TryNothingToSave = "保存する音がありません。先に〔しゃべらせる〕を押してください。";

    /// <summary>保存した。</summary>
    public const string TrySaved = "保存しました。";

    /// <summary>保存できなかった（詳しい字は檔へ）。</summary>
    public const string TrySaveFailed = "保存できませんでした。";

    /// <summary>檔を選ぶ窓の題。</summary>
    public const string TrySaveDialogTitle = "音声ファイルに保存する";

    /// <summary>檔を選ぶ窓の絞り。</summary>
    public const string TrySaveFilter = "WAV（*.wav）|*.wav";

    /// <summary>保存を押したのに音が無いときの窓の題。</summary>
    public const string TrySaveDialogCaption = "保存";

    // ---- 声 ----

    /// <summary>一覧を読み直せなかった。</summary>
    public const string VoicesRefreshFailed = "一覧を読み直せませんでした。";

    /// <summary>消せなかった。</summary>
    public const string VoicesRemoveFailed = "消せませんでした。";

    /// <summary>下ごしらえを始められなかった。</summary>
    public const string VoicesPrecomputeFailed = "下ごしらえを始められませんでした。";

    /// <summary>一覧が読めず、判っている名前だけを出している。</summary>
    public const string VoicesPartialList = "声の一覧を読み込めませんでした。分かっている名前だけを出しています。";

    /// <summary>まだ組み込まれていない操作。</summary>
    public const string VoicesNotYet = "この操作はまだできません。";

    /// <summary>この声は消せない（既定の理由）。</summary>
    public const string VoicesCannotRemove = "この声は消せません。";

    /// <summary>この声は試聴できない（既定の理由）。</summary>
    public const string VoicesCannotPreview = "この声は試聴できません。";

    /// <summary>下ごしらえの最中は消せない。</summary>
    public const string VoicesPrecomputeRunning = "いま下ごしらえの最中です。終わってから消してください。";

    /// <summary>やり直しが要る声が 1 人も居ない。</summary>
    public const string VoicesNothingStale = "やり直しが要る声はありません。";

    /// <summary>まだ準備ができていないので下ごしらえできない。</summary>
    public const string VoicesPrecomputeNotReady = "まだ準備ができていないので、下ごしらえはできません。";

    /// <summary>声の一覧（台帳）が読めなかった。</summary>
    public const string VoicesTableUnreadable = "声の一覧が読めませんでした。";

    /// <summary>音声ファイルを取り込めなかった。</summary>
    public const string VoicesCopyFailed = "音声ファイルを取り込めませんでした。";

    /// <summary>入れ直す同梱の声が 1 人も居ない。</summary>
    public const string VoicesNothingToRestore = "入れ直す声はありませんでした。";

    /// <summary>同梱を入れ直せなかった。</summary>
    public const string VoicesRestoreFailed = "最初から入っている声を入れ直せませんでした。";

    /// <summary>元にする音声の長さの目安（右の欄の薄字）。</summary>
    public const string VoicesLengthAdvice =
        "10〜30 秒くらいの、その人だけがしゃべっている録音がよく合います";

    /// <summary>檔を選ぶ窓の題。</summary>
    public const string VoicesBrowseDialogTitle = "元にする音声ファイルを選ぶ";

    /// <summary>檔を選ぶ窓の絞り（前半）。</summary>
    public const string VoicesBrowseFilterAudio = "音声ファイル";

    /// <summary>檔を選ぶ窓の絞り（後半）。</summary>
    public const string VoicesBrowseFilterAll = "すべてのファイル（*.*）";

    /// <summary>消す前の確認の題。</summary>
    public const string VoicesRemoveDialogCaption = "声を消す";

    /// <summary>消す前の確認の本文（前半）。</summary>
    public const string VoicesRemoveConfirmTail = "この声のために取り込んだ音声ファイルも消えます。";

    /// <summary>同梱の声を消すときに添える 1 行。</summary>
    public const string VoicesRemoveConfirmPreset =
        "最初から入っている声は〔最初から入っている声を入れ直す〕でいつでも戻せます。";

    /// <summary>いま何人ぶんの声を用意しているか（合計の数は出さない＝憲章 §4 の 3 つだけ）。</summary>
    public const string VoicesSelectedNone = "声を選んでいません。";

    // ---- 但し書き（平易化はしても短縮しない＝`v2-copy.md` §1-8 の逐語）----

    /// <summary>元にする音声の但し書き（声のタブと このアプリについて で同文）。</summary>
    public const string ImpersonationNotice =
        "元にする音声は、使ってよいと許しを得た物だけにしてください。"
        + "実在する人の声を、本人の許しなくまねることは、この音声合成の利用条件"
        + "（Ethical Restrictions 1・No Impersonation）で禁じられています。";

    /// <summary>非公式の断り（このアプリについて の冒頭・<c>decisions.md</c> 1）。</summary>
    public const string Disclaimer =
        "これは非公式のアプリです。"
        + "もとになった音声合成 Irodori-TTS の作者（Aratako 氏）とは関係がありません。"
        + "うまく動かないときも、Aratako 氏へは問い合わせないでください。";

    /// <summary>音に埋め込まれる印（<c>decisions.md</c> 9＝切る経路を持たない）。</summary>
    public const string WatermarkNotice =
        "出した音声には、AI が作った音であるという目印が入ります。人には聞こえません。"
        + "外す方法はありません。";

    // ---- 設定・状態から出る 1 行 ----

    /// <summary>適用したあと（旧「設定を保存しました。変種・GPU・ポートを…」）。</summary>
    public const string SettingsSaved = "設定を保存しました。変えた設定は、次にアプリを開いたときから効きます。";

    /// <summary>取り消したあと。</summary>
    public const string SettingsReverted = "変更を取り消しました。";

    /// <summary>走行中に適用した設定の告知（旧「設定は次回の起動から有効です（…）」）。</summary>
    public const string SettingsPending = "変えた設定は、次にアプリを開いたときから効きます。";

    /// <summary>グラフィックスを探している間。</summary>
    public const string SettingsGpuSearching = "グラフィックスを探しています…";

    /// <summary>グラフィックスが 1 台も無い。</summary>
    public const string SettingsGpuNotFound = "グラフィックスが見つかりませんでした。";

    /// <summary>数え直しが期限内に終わらなかった。</summary>
    public const string SettingsGpuTimedOut = "グラフィックスを探しきれませんでした。";

    /// <summary>まだ組み込まれていない（工事中の個体）。</summary>
    public const string SettingsNotYet = "まだできません。";

    /// <summary>この動かし方の一式が配布物に無い。</summary>
    public const string SettingsVariantUnsupported = "この動かし方には対応していません。";

    /// <summary>つなぎ口の番号の下限・上限（<b>設定の詳細には行として並べない</b>＝手で直したとき用）。</summary>
    public const string SettingsPortRange = "つなぎ口の番号は 1〜65535 です。";

    /// <summary>精度の註（おまかせのとき）。</summary>
    public const string SettingsPrecisionAuto = "おまかせだと、GPU では bf16、CPU では FP32 になります。";

    /// <summary>精度の註（bf16 だけの動かし方）。</summary>
    public const string SettingsPrecisionFixed = "この動かし方は bf16 だけです。";

    /// <summary>一時ファイルを消す釦（空のとき）。</summary>
    public const string SettingsCacheEmpty = "一時ファイルはありません";

    /// <summary>取得が未了（詳しい状態の 1 行）。</summary>
    public const string StatusAcquisitionLine = "まだ準備が終わっていません。";

    /// <summary>入れ直している間（旧「実行系を組み直しています…」）。</summary>
    public const string StatusRebuilding = "動かすための一式を入れ直しています…";

    // ---- 一式の焼き印の食い違い（Services/Ledger/RuntimeStamp.cs の 3 本）--------------
    // 段 C の申し送り ⑵＝この 3 本は段 E の席が触っている檔に在ったので据え置かれていた。
    // 出所を UiStrings に寄せる（画面に出る文は必ずここが綴る）＝Services 側は綴らない。

    /// <summary>配布物の台帳が変わった＝入れ直しの 1 手を出す（旧「…実行系を組み直してください。」）。</summary>
    public const string StatusRebuildLedgerChanged =
        "このアプリが新しくなりました。動かすための一式を入れ直してください。";

    /// <summary>途中までしか組み上がっていない（旧「実行系（…）が途中までしか…」）。</summary>
    public const string StatusRebuildIncomplete =
        "動かすための一式が途中までしか入っていません。入れ直してください。";

    /// <summary>
    /// 台帳は同じで版だけ動いた＝<b>黙って焼き直す</b>ときに記録へ落とす 1 行（画面には出ない）。
    /// </summary>
    public const string StatusAppVersionChangedLog =
        "動かすための一式は前の版のまま使えます（入れ直しは要りません）。";

    // ---- 一時ファイルの掃除が断るとき（Services/Ledger/CacheCleaner.cs の 3 本）--------
    // 段 C の申し送り ⑶＝RuntimeStamp と対で読む文なので同じ回に寄せる。

    /// <summary>一式がまだ無いので消さない。</summary>
    public const string CacheBlockedNotInstalled =
        "動かすための一式がまだ入っていないので、一時ファイルは消しません。";

    /// <summary>何で組んだかが判らないので消さない。</summary>
    public const string CacheBlockedUnknown =
        "何を使って組み上げたかが判らないので、一時ファイルは消しません"
        + "（動かすための一式を入れ直すと判るようになります）。";

    /// <summary>いま在る一式と配布物が食い違うので消さない。</summary>
    public const string CacheBlockedMismatch =
        "いま入っている一式と、このアプリが持っている内容が違うので、一時ファイルは消しません"
        + "（動かすための一式を入れ直してから消してください）。";

    // ---- 差分の取り直し（段 E の RuntimeDiff／ModelDiff を段 F が配線した）------------

    /// <summary>差分の取り直しを始める（帯の 1 行＝状態は「準備しています…」のまま）。</summary>
    public const string DifferentialStarting = "新しくなった分をダウンロードしています…";

    /// <summary>差分の取り直しが通った。</summary>
    public const string DifferentialDone = "新しくなった分を入れました。";

    /// <summary>差分では足りないので丸ごと入れ直す（押す前に告げる 1 行）。</summary>
    public const string DifferentialFallsBack =
        "変わった量が多いので、動かすための一式を丸ごと入れ直します。";

    /// <summary>
    /// 落とす物も入れ替える物も無い（是正・2026-09-11・medium 6）＝
    /// 中身は既に新しい内容と同じで、動いたのは持ち物の一覧の見出しだけ、という回。
    /// </summary>
    public const string DifferentialNothingToDo =
        "新しくする物はありませんでした（いま入っている一式のままで動きます）。";

    /// <summary>
    /// 入れ替えが通らなかった（是正・2026-09-11・medium 4）。
    /// <para>
    /// <see cref="DifferentialFallsBack"/>（<b>始める前</b>に「量が多いから丸ごとにする」と告げる 1 行）
    /// とは<b>別の場面</b>である＝こちらは<b>当て込みを始めたあと</b>で落ちた回で、
    /// 落ちた地点によっては新旧が混ざっている。だから量の話をしない。
    /// </para>
    /// </summary>
    public const string DifferentialApplyFailed =
        "新しくなった分だけでは入れ替えられませんでした。動かすための一式を丸ごと入れ直します。";

    /// <summary>選んだグラフィックスと、実際に使われたグラフィックスの食い違い。</summary>
    public const string StatusGpuMismatch =
        "選んだグラフィックスと、実際に使われたグラフィックスが違います。";

    /// <summary>アプリと、動いている中身の版の食い違い。</summary>
    public const string StatusUpstreamMismatch =
        "アプリと、いま動いている中身の版が食い違っています。入れ直してください。";

    /// <summary>まだ読み込んでいない（詳細の中の数の代わり）。</summary>
    public const string StatusModelNotLoaded = "—（まだ読み込んでいません）";

    /// <summary>まだ準備ができていない（詳細の中の数の代わり）。</summary>
    public const string StatusNotReady = "—（まだ準備ができていません）";

    /// <summary>いま使っている量（旧「torch 使用量」）。</summary>
    public const string StatusMemoryUsed = "いま使っている量";

    /// <summary>確保している量（旧「占有量」）。</summary>
    public const string StatusMemoryReserved = "確保している量";

    /// <summary>必要な部品が足りない（はじめの準備からやり直す）。</summary>
    public const string AcquisitionRestart = "必要な部品が足りないので、はじめの準備からやり直します。";

    /// <summary>まだ準備が終わっていない（主画面の側の 1 行）。</summary>
    public const string NotPreparedYet = "まだ準備が終わっていません。";

    /// <summary>記録がまだ無い（〔ログを開く〕の空振り）。</summary>
    public const string NoLogYet = "まだ記録がありません。";

    /// <summary>動かすための一式がまだ入っていない（GPU の検分・torch の検分の断り）。</summary>
    public const string RuntimeMissingPlain = "動かすための一式がまだ入っていません。";

    /// <summary>動かし方を変える口への案内（門と勧めの 1 手）。</summary>
    public const string SwitchInSettings = "設定の「詳細」で動かし方を変えてください";

    /// <summary>はじめの準備をやり直して動かし方を変える口への案内。</summary>
    public const string SwitchInFirstRun = "はじめの準備をやり直して動かし方を変えてください";

    // ===================== 詳しい状態＝いま動いている場所（`v2-spec.md` §2-2 の 2）=========

    /// <summary>
    /// GPU で動いているが製品名が読めない回（<c>cuda:0</c> の綴りは<b>出さない</b>＝
    /// `v2-copy.md` §1-2 の 40 行目・`v2-spec.md` §2-2 の 2）。
    /// </summary>
    public const string StatusDeviceGpu = "GPU";

    /// <summary>CPU で動いている回（設定の側と同じ 1 行を綴る＝同じ事実に 2 つの名を置かない）。</summary>
    public const string StatusDeviceCpu = "CPU（GPU を使いません）";

    /// <summary>使うグラフィックスが決まっていない回（詳細の中）。</summary>
    public const string StatusGpuUnselected = "未選択";

    // ===================== 起動のときの告知（画面の側・`v2-copy.md` §3-2 E-06）===========
    //
    // **画面に出す文とログに落とす文を分ける**（`v2-copy.md` §1-8 の `VariantGate.cs:102-165`）。
    // ここに在るのは画面の半分だけで、工学の綴り（検分・実行系・裁定の番号）は
    // `GateDecision.Trail` に残り、記録の檔にだけ落ちる。

    /// <summary>動かすための一式が無くてグラフィックスを確かめられなかった（止めずに伝える）。</summary>
    public const string GateProbeUnavailable =
        "このパソコンのグラフィックスを確かめられませんでした。動かすための一式が読めませんでした。"
        + "そのまま始めますが、うまく動かないときは" + SwitchInSettings + "。";

    /// <summary>確かめる手は撃てたが答えが読めなかった（止めずに伝える）。</summary>
    public const string GateProbeUnreadable =
        "このパソコンのグラフィックスを確かめられませんでした。"
        + "グラフィックスドライバか、動かすための一式が読めませんでした。"
        + "そのまま始めますが、うまく動かないときは" + SwitchInSettings + "。";

    /// <summary>まだ動作を確かめていないドライバの帯（前半＝ドライバの版の数字を挟む）。</summary>
    public const string GateUnmeasuredDriverHead = "ドライバ ";

    /// <summary>同・後半（ドライバの版の数字は残す＝憲章 §6-2）。</summary>
    public const string GateUnmeasuredDriverTail =
        " は、まだ動作を確かめていない版です。しゃべらせられますが、うまく動かないときは"
        + SwitchInSettings + "。";

    // ===================== はじめの準備（FirstRunViewModel）=====================

    /// <summary>段 3 の詳細＝起こしてみて動くか見ている間（`v2-copy.md` §1-8 の :1301-1324）。</summary>
    public const string WizardVerifyingRun = "動くか確かめています…";

    /// <summary>
    /// <b>声を読み込んでいる間の 1 行</b>の前半（決裁 135 ⑴＝v2.0.1・言い換えは決裁 137 ⒝）。
    /// <para>
    /// 取得の直後の 1 回目だけ、声の読み込みが実測（RTX 機・段 H 射 2）で 120 秒では終わらず、
    /// 次の起動は 36.62 秒・その次は 12 秒だった。待っている間は「止まりました。」ではなく
    /// <b>まだ働いていること</b>を名乗る。
    /// </para>
    /// <para>
    /// <b>v2.0.1（2）で言い換えた</b>（決裁 136 ⒝・137 ⒝）＝⑴ <b>経過秒を挟む</b>
    /// （1 秒ごとに動く＝決裁 137 の測り方 ⑶）⑵ <b>なぜ待つのかを言う</b>
    /// （パソコンの安全機能が新しいファイルを確認している）。組むのは純関数
    /// <see cref="FirstRunViewModel.LoadingVoicesLine"/> 1 箇所だけである。
    /// </para>
    /// </summary>
    public const string WizardLoadingVoicesHead = "声を読み込んでいます（";

    /// <summary>同・後半（秒の数を挟んだ後ろ）。</summary>
    public const string WizardLoadingVoicesTail =
        " 秒）… 初めてのときは、パソコンの安全機能が新しいファイルを確認するので数分かかります。";

    /// <summary>
    /// <b>新しいファイルを確認している段の 1 行</b>の前半（決裁 136 ⒜・137 ⒜・v2.0.1（2））。
    /// <para>
    /// 組むのは純関数 <see cref="FirstRunViewModel.WarmupPhaseLine"/> 1 箇所だけ＝
    /// 「新しいファイルを確認しています（1,234 / 25,300）」。
    /// <b>この数は「目に見えて動く物」である</b>（決裁 137 の測り方 ⑶）＝
    /// 1 行が 5 秒を越えて変わらない回でも、この数が動いていれば固まってはいない。
    /// </para>
    /// </summary>
    public const string WizardWarmupHead = "新しいファイルを確認しています（";

    /// <summary>同・数と数の間（<c>1,234 / 25,300</c> の区切り）。</summary>
    public const string WizardWarmupSeparator = " / ";

    /// <summary>同・後半。</summary>
    public const string WizardWarmupTail = "）";

    /// <summary>
    /// <b>働いている段に添える経過秒</b>の区切り（決裁 137 ⒜の是正・検分）。
    /// <para>
    /// <b>なぜ要るか</b>＝檔の数だけでは、<b>1 つの檔が大きい間</b>は何も動かない
    /// （モデルの <c>model.safetensors</c> は 3 GB 余りで、その 1 檔の初回走査だけで数十秒に達する）。
    /// 檔の数が <c>25,299 / 25,300</c> で止まっている間も、この秒は 1 秒ごとに動く＝
    /// 決裁 137 の測り方 ⑶ の「<b>目に見えて動く物</b>」を<b>常に</b>切らさないための錠である。
    /// </para>
    /// </summary>
    public const string WizardSecondsJoin = "・";

    /// <summary>同・秒の後ろ（括弧の中に収まる＝「（1,234 / 25,300・63 秒）」）。</summary>
    public const string WizardSecondsTail = " 秒";

    /// <summary>
    /// <b>モデルを落としている間の 1 行</b>の前半（決裁 137 ⒜の是正・検分）。
    /// <para>
    /// この段は数 GB を数分かけて落とすので、秒を添えないと 1 行も数も動かない回がある
    /// （バーは <c>overall_downloaded</c> から動くが、遅い回線では 1 % に 5 秒以上かかる）。
    /// </para>
    /// </summary>
    public const string WizardModelsHead = "声のデータをダウンロードしています（";

    /// <summary>同・後半。</summary>
    public const string WizardModelsTail = " 秒）";

    /// <summary>
    /// <b>起こしている間の 1 行</b>の前半（決裁 137 ⒝の是正・検分）。
    /// <para>
    /// 口が開くまでの間（python.exe の起動と読み込みの始まり）は
    /// <see cref="WizardVerifyingRun"/> のまま 1 行も動かなかった＝実測で 12 秒以上ある。
    /// 口が開いた後は <see cref="WizardLoadingVoicesHead"/> の 1 行に替わるが、
    /// <b>秒はそのまま数え続ける</b>（刻みを止めて始め直さない）。
    /// </para>
    /// </summary>
    public const string WizardStartingHead = "起動しています（";

    /// <summary>同・後半。</summary>
    public const string WizardStartingTail = " 秒）…";

    /// <summary>数え上げている間の前半（総数がまだ判らない＝見つかった数だけを出す）。</summary>
    public const string WizardWarmupCountingHead = "新しいファイルを数えています（";

    /// <summary>同・後半。</summary>
    public const string WizardWarmupCountingTail = "）";

    /// <summary>落とす量が判らない回（推測の数字を出さない・詳しい字は記録へ）。</summary>
    public const string WizardSizeUnknown = "必要な大きさが分かりませんでした。";

    /// <summary>内訳の 1 つ目＝動かすための一式（畳みの中・実数は `GiB` のまま）。</summary>
    public const string WizardSizeRuntime = "動かすための一式 ";

    /// <summary>内訳の 2 つ目＝声のデータ。</summary>
    public const string WizardSizeModels = "・声のデータ ";

    /// <summary>内訳の 3 つ目＝Microsoft の部品（要る機体だけ）。</summary>
    public const string WizardSizeVcRedist = "・Microsoft の部品 ";

    // ===================== 声の名付けと元にする音声（VoiceNameValidator）===============

    /// <summary>名前が空。</summary>
    public const string VoiceNameRequired = "声の名前を入れてください。";

    /// <summary>名前が長すぎる（前半＝上限の数を挟む）。</summary>
    public const string VoiceNameTooLongHead = "名前が長すぎます（上限 ";

    /// <summary>同・後半。</summary>
    public const string VoiceNameTooLongTail = " 字）。";

    /// <summary>名前に制御文字。</summary>
    public const string VoiceNameControlChar = "名前に制御文字は使えません。";

    /// <summary>取ってある名（「◯」に続けて綴る）。</summary>
    public const string VoiceNameReservedTail = "」は、はじめから在る声の名前なので使えません。";

    /// <summary>同じ名前が既にある。</summary>
    public const string VoiceNameTaken = "その名前の声は既にあります。";

    /// <summary>元にする音声ファイルを選んでいない。</summary>
    public const string VoiceSourceRequired = "元にする音声ファイルを選んでください。";

    /// <summary>受けられない形式（前半＝受ける拡張子の列を挟む）。</summary>
    public const string VoiceSourceUnsupportedHead = "この形式は元にする音声ファイルには使えません（";

    /// <summary>同・後半。</summary>
    public const string VoiceSourceUnsupportedTail = "）。";

    /// <summary>元にする音声が短め（止めない＝注意だけ）。</summary>
    public const string VoiceSourceShortHead = "元にする音声が短めです（";

    /// <summary>同・長め。</summary>
    public const string VoiceSourceLongHead = "元にする音声が長めです（";

    /// <summary>同・後半（推奨の秒数を挟む）。</summary>
    public const string VoiceSourceLengthTail = " 秒くらいがよく合います。";

    /// <summary>下ごしらえを片づけられなかったので消せなかった（`v2-copy.md` §1-4 の語彙）。</summary>
    public const string VoicesLatentDetachFailed =
        "この声の下ごしらえを片づけられなかったので、消せませんでした。";

    /// <summary>同・時間内に返らなかった回。</summary>
    public const string VoicesLatentDetachTimedOut =
        "この声の下ごしらえを片づける手が、時間内に終わりませんでした。";

    // ===================== 〔報告用のログを保存〕（`v2-copy.md` §8）=====================

    /// <summary>まとめ終わった 1 行（後ろに檔の路を継ぐ）。</summary>
    public const string AboutReportSaved = "記録をまとめました。";

    /// <summary>まとめられなかった回。</summary>
    public const string AboutReportSaveFailed = "記録をまとめられませんでした。";
}
