using System.Globalization;
using System.IO;
using System.Windows.Markup;
using System.Windows.Media;

namespace PptSlider;

/// <summary>
/// UI text in Korean, Japanese and English. The language follows Windows unless the user picked one
/// (saved to %APPDATA%\PptSlider\language.txt); a change applies after a restart.
/// </summary>
public static class L
{
    public static readonly string[] Languages = ["ko", "ja", "en"];

    /// <summary>Each language's name in itself, for the language menu.</summary>
    public static readonly string[] Names = ["한국어", "日本語", "English"];

    public static string Lang { get; private set; } = "ko";

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PptSlider", "language.txt");

    /// <summary>The language the user picked, or null to follow Windows.</summary>
    public static string? Saved
    {
        get
        {
            try { return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() is var s && Languages.Contains(s) ? s : null; }
            catch (Exception) { return null; }
        }
    }

    public static bool Save(string? lang)
    {
        try
        {
            if (lang == null) File.Delete(FilePath);
            else { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, lang); }
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Korean or Japanese Windows get their own language, everything else English.</summary>
    public static string WindowsLang => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is var l && l is "ko" or "ja" ? l : "en";

    /// <summary>Before any window is made. <paramref name="lang"/> forces one (the self-test checks Korean text).</summary>
    public static void Init(string? lang = null) => Lang = lang ?? Saved ?? WindowsLang;

    /// <summary>The UI font: Japanese text must not fall back to a Korean font's kanji.</summary>
    public static FontFamily UiFont => new(Lang == "ja" ? "Segoe UI, Yu Gothic UI, Meiryo UI, Malgun Gothic" : Lang == "ko" ? "Segoe UI, Malgun Gothic, Yu Gothic UI" : "Segoe UI, Yu Gothic UI, Malgun Gothic");

    public static string T(string key) => T(key, Lang);

    public static string T(string key, string lang) =>
        Table.TryGetValue(key, out var v) ? v[Math.Max(0, Array.IndexOf(Languages, lang))] : $"[{key}]";

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    /// <summary>key → [Korean, Japanese, English]. <c>{0}</c> etc. are <see cref="F"/> arguments.</summary>
    internal static readonly Dictionary<string, string[]> Table = new()
    {
        // ribbon: Home
        ["tab.home"] = ["홈", "ホーム", "Home"],
        ["grp.file"] = ["파일", "ファイル", "File"],
        ["open"] = ["열기", "開く", "Open"],
        ["recent"] = ["최근 파일", "最近使ったファイル", "Recent files"],
        ["save"] = ["저장", "保存", "Save"],
        ["saveAsShort"] = ["다른 이름", "名前を付けて", "Save As"],
        ["saveAs"] = ["다른 이름으로 저장", "名前を付けて保存", "Save As"],
        ["exportPdf"] = ["PDF로 내보내기", "PDF にエクスポート", "Export to PDF"],
        ["exportPdfTip"] = ["PDF로 내보내기 (판서 포함 여부 선택)", "PDF にエクスポート (手書きを含めるか選択)", "Export to PDF (with or without ink)"],
        ["inkNotes"] = ["판서 노트", "手書きノート", "Ink Notes"],
        ["inkNotesTip"] = ["판서한 슬라이드만 판서와 함께 PDF로 (강의 노트)", "手書きしたスライドだけを手書きと一緒に PDF に (講義ノート)", "Only the slides you drew on, with their ink, as a PDF (lecture notes)"],
        ["inkNotesSuffix"] = ["_판서노트", "_手書きノート", "_ink-notes"],
        ["noInkedSlides"] = ["판서한 슬라이드가 없습니다", "手書きしたスライドがありません", "No slide has ink"],
        ["grp.edit"] = ["편집", "編集", "Edit"],
        ["undo"] = ["실행 취소", "元に戻す", "Undo"],
        ["grp.slides"] = ["슬라이드", "スライド", "Slides"],
        ["newSlide"] = ["새 슬라이드", "新しいスライド", "New Slide"],
        ["newSlideTip"] = ["빈 슬라이드 추가", "空のスライドを追加", "Add a blank slide"],
        ["duplicate"] = ["복제", "複製", "Duplicate"],
        ["duplicateSlide"] = ["슬라이드 복제", "スライドの複製", "Duplicate slide"],
        ["delete"] = ["삭제", "削除", "Delete"],
        ["deleteSlide"] = ["슬라이드 삭제", "スライドの削除", "Delete slide"],
        ["grp.font"] = ["글꼴", "フォント", "Font"],
        ["fontSizeTip"] = ["글꼴 크기 (Enter로 적용)", "フォント サイズ (Enter で適用)", "Font size (Enter to apply)"],
        ["grow"] = ["글꼴 크기 크게", "フォント サイズの拡大", "Increase font size"],
        ["shrink"] = ["글꼴 크기 작게", "フォント サイズの縮小", "Decrease font size"],
        ["bold"] = ["굵게", "太字", "Bold"],
        ["italic"] = ["기울임꼴", "斜体", "Italic"],
        ["underline"] = ["밑줄", "下線", "Underline"],
        ["fontColor"] = ["글꼴 색", "フォントの色", "Font color"],
        ["grp.paragraph"] = ["단락", "段落", "Paragraph"],
        ["alignLeft"] = ["왼쪽 맞춤", "左揃え", "Align left"],
        ["alignCenter"] = ["가운데 맞춤", "中央揃え", "Center"],
        ["alignRight"] = ["오른쪽 맞춤", "右揃え", "Align right"],
        ["grp.insert"] = ["삽입", "挿入", "Insert"],
        ["textBox"] = ["텍스트 상자", "テキスト ボックス", "Text Box"],
        ["picture"] = ["그림", "画像", "Picture"],
        ["pictureTip"] = ["그림 삽입 (붙여넣기 Ctrl+V, 파일을 끌어다 놓아도 됩니다)", "画像の挿入 (Ctrl+V で貼り付け、ファイルのドラッグも可)", "Insert a picture (or paste with Ctrl+V, or drop a file)"],
        ["pictureFilter"] = ["그림 (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp", "画像 (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
            "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp"],
        ["pictureFail"] = ["그림을 넣지 못했습니다.\n{0}", "画像を挿入できませんでした。\n{0}", "Could not insert the picture.\n{0}"],
        ["deleteShape"] = ["개체 삭제", "オブジェクトの削除", "Delete object"],
        ["deleteShapeTip"] = ["선택한 개체 삭제", "選択したオブジェクトを削除", "Delete the selected object"],

        // ribbon: Draw
        ["tab.draw"] = ["그리기", "描画", "Draw"],
        ["grp.tools"] = ["도구", "ツール", "Tools"],
        ["select"] = ["선택", "選択", "Select"],
        ["selectTip"] = ["선택/편집 (Esc)", "選択/編集 (Esc)", "Select/edit (Esc)"],
        ["pen"] = ["펜", "ペン", "Pen"],
        ["highlighter"] = ["형광펜", "蛍光ペン", "Highlighter"],
        ["eraser"] = ["지우개", "消しゴム", "Eraser"],
        ["laser"] = ["레이저 포인터", "レーザー ポインター", "Laser pointer"],
        ["grp.color"] = ["색", "色", "Color"],
        ["grp.width"] = ["두께", "太さ", "Width"],
        ["penColor"] = ["펜/형광펜 색", "ペン/蛍光ペンの色", "Pen/highlighter color"],
        ["grp.undoClear"] = ["되돌리기 · 지우기", "元に戻す · 消去", "Undo · Clear"],
        ["undoInkShort"] = ["되돌리기", "元に戻す", "Undo"],
        ["undoInk"] = ["판서 되돌리기", "手書きを元に戻す", "Undo ink"],
        ["thisSlide"] = ["이 슬라이드", "このスライド", "This Slide"],
        ["clearInk"] = ["현재 슬라이드 판서 지우기", "このスライドの手書きを消去", "Clear ink on this slide"],
        ["allInk"] = ["모든 판서", "すべての手書き", "All Ink"],
        ["inkShown"] = ["판서 보기", "手書きを表示", "Show Ink"],
        ["inkShownTip"] = ["화면 위 판서 보이기/숨기기", "画面上の手書きの表示/非表示", "Show or hide the ink on the screen"],
        ["hideInk"] = ["판서 숨기기 (펜을 고르면 다시 보입니다)", "手書きを隠す (ペンを選ぶと再表示)", "Hide the ink (pick a pen to show it again)"],
        ["inkPointer"] = ["포인터 — 판서는 그대로 두고 아래 화면을 클릭 (Esc)", "ポインター — 手書きを残したまま下の画面をクリック (Esc)", "Pointer — keep the ink and click the screen underneath (Esc)"],
        ["moveBar"] = ["끌어서 도구 모음 옮기기", "ドラッグでツールバーを移動", "Drag to move the toolbar"],
        ["grp.page"] = ["페이지", "ページ", "Page"],
        ["blankPage"] = ["빈 판서 페이지", "空白の手書きページ", "Blank Ink Page"],
        ["blankPageTip"] = ["현재 슬라이드 뒤에 빈 슬라이드를 넣고 이동", "現在のスライドの後に空のスライドを挿入して移動", "Insert a blank slide after this one and go to it"],
        ["thinner"] = ["가늘게", "細く", "Thinner"],
        ["thicker"] = ["굵게", "太く", "Thicker"],

        // ribbon: Slide Show
        ["tab.show"] = ["슬라이드 쇼", "スライド ショー", "Slide Show"],
        ["grp.startShow"] = ["슬라이드 쇼 시작", "スライド ショーの開始", "Start Slide Show"],
        ["fromStart"] = ["처음부터", "最初から", "From Beginning"],
        ["fromCurrent"] = ["현재 슬라이드부터", "現在のスライドから", "From Current Slide"],
        ["grp.monitor"] = ["모니터", "モニター", "Monitor"],
        ["monitorLabel"] = ["슬라이드 쇼를 표시할 모니터", "スライド ショーを表示するモニター", "Monitor for the slide show"],
        ["refreshMonitors"] = ["모니터 목록 새로 고침", "モニター一覧を更新", "Refresh the monitor list"],
        ["monitor"] = ["모니터 {0}", "モニター {0}", "Monitor {0}"],
        ["primaryMonitor"] = [" (주 모니터)", " (メイン)", " (primary)"],
        ["presenterView"] = ["발표자 보기", "発表者ツール", "Presenter View"],
        ["usePresenter"] = ["발표자 보기 사용", "発表者ツールを使用", "Use Presenter View"],
        ["usePresenterTip"] = ["쇼 모니터가 아닌 모니터에 현재·다음 슬라이드, 발표자 노트, 시간을 표시합니다. 모니터가 하나면 표시하지 않습니다.",
            "スライド ショーとは別のモニターに、現在と次のスライド、発表者ノート、時間を表示します。モニターが 1 台の場合は表示しません。",
            "Shows the current and next slide, speaker notes and time on a monitor other than the show's. Not shown with a single monitor."],
        ["onOtherMonitor"] = ["다른 모니터에 표시", "別のモニターに表示", "On another monitor"],
        ["grp.break"] = ["휴식", "休憩", "Break"],
        ["breakTip"] = ["휴식 타이머", "休憩タイマー", "Break timer"],
        ["grp.help"] = ["도움말", "ヘルプ", "Help"],
        ["shortcuts"] = ["단축키", "ショートカット", "Shortcuts"],
        ["shortcutList"] = ["단축키 목록", "ショートカット一覧", "Shortcut list"],
        ["shortcutSettings"] = ["단축키 설정", "ショートカットの設定", "Shortcut Settings"],
        ["shortcutSettingsTip"] = ["단축키 바꾸기", "ショートカットを変更", "Change shortcuts"],
        ["tab.settings"] = ["설정", "設定", "Settings"],
        ["language"] = ["언어", "言語", "Language"],
        ["languageSystem"] = ["Windows 설정 따르기 ({0})", "Windows の設定に従う ({0})", "Same as Windows ({0})"],
        ["languageNote"] = ["다시 시작하면 적용됩니다", "再起動後に適用されます", "Applies after a restart"],
        ["languageRestart"] = ["언어는 앱을 다시 시작하면 바뀝니다. 지금 다시 시작할까요?", "言語はアプリの再起動後に切り替わります。今すぐ再起動しますか?",
            "The language changes when the app restarts. Restart now?"],
        ["languageSaveFail"] = ["언어 설정을 저장하지 못했습니다.", "言語の設定を保存できませんでした。", "Could not save the language setting."],

        // editor: messages and status
        ["recover"] = ["{0}\n\n지난번에 저장하지 못한 편집 내용이 자동 저장되어 있습니다(파일보다 최근).\n복구할까요?\n\n[예]  자동 저장된 내용으로 열기\n[아니요]  파일 그대로 열기 (자동 저장본은 지웁니다)",
            "{0}\n\n前回保存されなかった編集内容が自動保存されています (ファイルより新しい)。\n復元しますか?\n\n[はい]  自動保存された内容で開く\n[いいえ]  ファイルをそのまま開く (自動保存は削除します)",
            "{0}\n\nEdits you did not save last time were autosaved (newer than the file).\nRecover them?\n\n[Yes]  Open the autosaved copy\n[No]  Open the file as it is (the autosaved copy is deleted)"],
        ["recoverTitle"] = ["자동 저장 복구", "自動保存の復元", "Recover autosave"],
        ["deleteSlideAsk"] = ["슬라이드 {0}을(를) 삭제할까요? 실행 취소할 수 없습니다.", "スライド {0} を削除しますか? 元に戻すことはできません。", "Delete slide {0}? This cannot be undone."],
        ["openFail"] = ["파일을 열 수 없습니다.\n{0}\n\n.pptx 형식만 지원합니다.", "ファイルを開けません。\n{0}\n\n.pptx 形式のみ対応しています。", "Cannot open the file.\n{0}\n\nOnly .pptx files are supported."],
        ["recovered"] = ["자동 저장된 사본에서 복구했습니다. 저장하면 원래 파일에 씁니다.", "自動保存されたコピーから復元しました。保存すると元のファイルに書き込みます。",
            "Recovered from the autosaved copy. Saving writes to the original file."],
        ["pptxFilter"] = ["PowerPoint 프레젠테이션 (*.pptx)|*.pptx", "PowerPoint プレゼンテーション (*.pptx)|*.pptx", "PowerPoint Presentation (*.pptx)|*.pptx"],
        ["inkSuffix"] = ["_판서", "_手書き", "_ink"],
        ["saved"] = ["저장됨: {0}", "保存しました: {0}", "Saved: {0}"],
        ["saveFail"] = ["저장하지 못했습니다.\n{0}", "保存できませんでした。\n{0}", "Could not save.\n{0}"],
        ["pdfWithInk"] = ["판서한 내용이 있습니다. PDF에 판서를 포함할까요?\n\n[예]  판서 포함\n[아니요]  판서 없이\n[취소]  내보내지 않음",
            "手書きがあります。PDF に手書きを含めますか?\n\n[はい]  手書きを含める\n[いいえ]  手書きなし\n[キャンセル]  エクスポートしない",
            "There is ink on the slides. Include it in the PDF?\n\n[Yes]  With the ink\n[No]  Without the ink\n[Cancel]  Don't export"],
        ["pdfFilter"] = ["PDF 문서 (*.pdf)|*.pdf", "PDF ドキュメント (*.pdf)|*.pdf", "PDF Document (*.pdf)|*.pdf"],
        ["pdfDone"] = ["PDF로 내보냄: {0}", "PDF にエクスポートしました: {0}", "Exported to PDF: {0}"],
        ["pdfFail"] = ["PDF를 만들지 못했습니다.\n{0}", "PDF を作成できませんでした。\n{0}", "Could not create the PDF.\n{0}"],
        ["unsaved"] = ["저장하지 않은 변경 내용이 있습니다. 저장할까요?", "保存されていない変更があります。保存しますか?", "You have unsaved changes. Save them?"],
        ["openAFile"] = ["파일을 열어 주세요", "ファイルを開いてください", "Open a file"],
        ["slideOf"] = ["슬라이드 {0} / {1}", "スライド {0} / {1}", "Slide {0} of {1}"],
        ["editingText"] = ["텍스트 편집 중 — 바깥을 클릭하면 적용, Esc는 취소, Shift+Enter는 줄 바꿈", "テキストを編集中 — 外側をクリックで適用、Esc でキャンセル、Shift+Enter で改行",
            "Editing text — click outside to apply, Esc to cancel, Shift+Enter for a line break"],
        ["selectTextFirst"] = ["먼저 슬라이드에서 텍스트 개체를 선택하세요", "先にスライドでテキスト オブジェクトを選択してください", "Select a text object on the slide first"],
        ["newTextBox"] = ["텍스트를 입력하세요", "テキストを入力してください", "Type your text"],
        ["lastSlideStays"] = ["마지막 슬라이드는 삭제할 수 없습니다", "最後のスライドは削除できません", "The last slide cannot be deleted"],
        ["inkUndone"] = ["판서를 되돌렸습니다", "手書きを元に戻しました", "Ink stroke undone"],
        ["nothingToUndo"] = ["더 이상 실행 취소할 내용이 없습니다", "これ以上元に戻せる操作はありません", "Nothing more to undo"],
        ["noInkToUndo"] = ["되돌릴 판서가 없습니다", "元に戻す手書きがありません", "No ink to undo"],
        ["penStatus"] = ["펜 · 두께 {0:0}", "ペン · 太さ {0:0}", "Pen · width {0:0}"],
        ["highlighterStatus"] = ["형광펜 · 두께 {0:0}", "蛍光ペン · 太さ {0:0}", "Highlighter · width {0:0}"],
        ["clearAllAsk"] = ["모든 슬라이드의 판서를 지울까요?", "すべてのスライドの手書きを消去しますか?", "Clear the ink on all slides?"],
        ["clearInkTitle"] = ["판서 지우기", "手書きの消去", "Clear ink"],
        ["helpFixed"] = ["""
            [고정]
                Esc : 텍스트 편집 취소 / 선택 도구로 (쇼에서는 한 번 더 누르면 종료)
                쇼에서 숫자 + Enter : 해당 번호 슬라이드로 이동 (숫자만 누르면 1초 뒤 펜 색)
                Shift + 드래그 : 판서를 직선으로    쇼에서 확대 중 Esc / 클릭 : 원래 크기
                더블클릭 : 텍스트 편집    드래그 : 이동, 오른쪽 아래 모서리 : 크기
                Ctrl + V : 그림 붙여넣기    썸네일 드래그 : 슬라이드 순서 바꾸기

            단축키는 [설정] 탭의 [단축키 설정]에서 바꿀 수 있습니다.
            """, """
            [固定]
                Esc : テキスト編集のキャンセル / 選択ツールに戻る (スライド ショーではもう一度押すと終了)
                スライド ショーで数字 + Enter : その番号のスライドへ移動 (数字だけなら 1 秒後にペンの色)
                Shift + ドラッグ : 手書きを直線に    スライド ショーの拡大中に Esc / クリック : 元のサイズ
                ダブルクリック : テキストの編集    ドラッグ : 移動、右下の角 : サイズ変更
                Ctrl + V : 画像の貼り付け    サムネイルのドラッグ : スライドの順序の変更

            ショートカットは [設定] タブの [ショートカットの設定] で変更できます。
            """, """
            [Fixed]
                Esc : cancel text editing / back to Select (in the show, press again to end it)
                Number + Enter in the show : go to that slide (a number alone sets the pen color after 1 s)
                Shift + drag : straight ink line    Esc / click while zoomed in the show : normal size
                Double-click : edit text    Drag : move, bottom-right corner : resize
                Ctrl + V : paste a picture    Drag a thumbnail : change the slide order

            Shortcuts can be changed in [Settings] tab → [Shortcut Settings].
            """],
        ["emptyHint"] = ["PPTX 파일을 열거나 이곳으로 끌어다 놓으세요", "PPTX ファイルを開くか、ここにドラッグしてください", "Open a PPTX file or drag it here"],
        ["notShown"] = ["[차트/SmartArt는 표시되지 않습니다]", "[グラフ/SmartArt は表示されません]", "[Charts/SmartArt are not displayed]"],

        // slide show and presenter view
        ["endShow"] = ["쇼 마침 (Esc)", "スライド ショーの終了 (Esc)", "End show (Esc)"],
        ["prev"] = ["이전", "前へ", "Previous"],
        ["next"] = ["다음", "次へ", "Next"],
        ["pointer"] = ["포인터 (Esc)", "ポインター (Esc)", "Pointer (Esc)"],
        ["black"] = ["검은 화면", "黒い画面", "Black screen"],
        ["white"] = ["흰 화면", "白い画面", "White screen"],
        ["currentSlide"] = ["현재 슬라이드", "現在のスライド", "Current slide"],
        ["nextSlide"] = ["다음 슬라이드", "次のスライド", "Next slide"],
        ["isLastSlide"] = ["마지막 슬라이드입니다", "最後のスライドです", "This is the last slide"],
        ["notesBigger"] = ["노트 글자 크게", "ノートの文字を大きく", "Larger notes text"],
        ["notesSmaller"] = ["노트 글자 작게", "ノートの文字を小さく", "Smaller notes text"],
        ["notes"] = ["발표자 노트", "発表者ノート", "Speaker notes"],
        ["noNotes"] = ["(노트 없음)", "(ノートなし)", "(No notes)"],
        ["goToSlide"] = ["슬라이드 {0} 로 이동 (Enter)", "スライド {0} へ移動 (Enter)", "Go to slide {0} (Enter)"],
        ["slideN"] = ["슬라이드 {0}", "スライド {0}", "Slide {0}"],
        ["audienceBlack"] = ["청중 화면: 검은 화면", "聴衆の画面: 黒い画面", "Audience screen: black"],
        ["audienceWhite"] = ["청중 화면: 흰 화면", "聴衆の画面: 白い画面", "Audience screen: white"],
        ["zoomHint"] = ["확대할 영역을 드래그하세요   (Esc: 취소)", "拡大する範囲をドラッグしてください   (Esc: キャンセル)", "Drag the area to zoom in   (Esc: cancel)"],
        ["clock"] = ["{0:t}   경과 {1}", "{0:t}   経過 {1}", "{0:t}   Elapsed {1}"],

        // break time
        ["minutesShort"] = ["{0}분", "{0} 分", "{0} min"],
        ["hoursShort"] = ["{0}시간", "{0} 時間", "{0} h"],
        ["hoursMinutes"] = ["{0}시간 {1}분", "{0} 時間 {1} 分", "{0} h {1} min"],
        ["start"] = ["시작", "開始", "Start"],
        ["breakSetupKeys"] = ["← → ↑ ↓ : 5분씩 조정    Enter : 시작    Esc : 닫기", "← → ↑ ↓ : 5 分ずつ調整    Enter : 開始    Esc : 閉じる", "← → ↑ ↓ : ±5 min    Enter : start    Esc : close"],
        ["breakHeading"] = ["쉬는 시간입니다", "休憩時間です", "Break time"],
        ["breakOver"] = ["휴식 끝! 수업을 시작합니다", "休憩終了! 再開します", "Break is over! Let's get started"],
        ["resumeAt"] = ["{0:HH:mm}에 다시 시작합니다", "{0:HH:mm} に再開します", "Back at {0:HH:mm}"],
        ["paused"] = ["일시 정지됨", "一時停止中", "Paused"],
        ["breakRunKeys"] = ["Space : 일시 정지/계속    Esc : 닫기", "Space : 一時停止/再開    Esc : 閉じる", "Space : pause/resume    Esc : close"],

        // shortcut settings
        ["shortcutsHelp"] = ["기능을 선택하고 [추가] 또는 [바꾸기]를 누른 뒤 원하는 키 조합을 누르세요. 한 기능에 여러 키를 지정할 수 있습니다. Esc는 편집 취소·포인터·쇼 종료용으로 예약되어 있어 지정할 수 없습니다.",
            "機能を選択して [追加] または [変更] を押し、割り当てるキーの組み合わせを押してください。1 つの機能に複数のキーを割り当てられます。Esc は編集のキャンセル・ポインター・スライド ショーの終了用に予約されているため割り当てられません。",
            "Select a command, press [Add] or [Replace], then press the key combination you want. A command can have several keys. Esc is reserved for canceling edits, the pointer and ending the show, so it cannot be assigned."],
        ["resetAll"] = ["모두 기본값으로", "すべて既定値に戻す", "Reset all"],
        ["ok"] = ["확인", "OK", "OK"],
        ["cancel"] = ["취소", "キャンセル", "Cancel"],
        ["add"] = ["추가", "追加", "Add"],
        ["replace"] = ["바꾸기", "変更", "Replace"],
        ["clear"] = ["지우기", "消去", "Clear"],
        ["resetOne"] = ["이 기능 기본값", "この機能を既定値に", "Reset this one"],
        ["colGroup"] = ["분류", "分類", "Group"],
        ["colCommand"] = ["기능", "機能", "Command"],
        ["pressKeyToAdd"] = ["‘{0}’에 추가할 키를 누르세요…   (Esc : 취소)", "「{0}」に追加するキーを押してください…   (Esc : キャンセル)", "Press the key to add to “{0}”…   (Esc : cancel)"],
        ["pressKeyToSet"] = ["‘{0}’에 지정할 키를 누르세요…   (Esc : 취소)", "「{0}」に割り当てるキーを押してください…   (Esc : キャンセル)", "Press the key for “{0}”…   (Esc : cancel)"],
        ["escReserved"] = ["Esc는 지정할 수 없습니다.", "Esc は割り当てられません。", "Esc cannot be assigned."],
        ["keyTaken"] = ["{0}은(는) 이미 ‘{1}’에 지정되어 있습니다.\n‘{2}’(으)로 옮길까요?", "{0} は既に「{1}」に割り当てられています。\n「{2}」に移しますか?", "{0} is already assigned to “{1}”.\nMove it to “{2}”?"],
        ["keyTakenTitle"] = ["단축키 중복", "ショートカットの重複", "Shortcut in use"],
        ["resetAllAsk"] = ["모든 단축키를 기본값으로 되돌릴까요?", "すべてのショートカットを既定値に戻しますか?", "Reset all shortcuts to their defaults?"],
        ["settingsSaveFail"] = ["설정 파일을 저장하지 못했습니다. 이번 실행 동안만 적용됩니다.", "設定ファイルを保存できませんでした。今回の実行中のみ適用されます。",
            "Could not save the settings file. The changes apply until the app is closed."],

        // shortcut commands
        ["g.move"] = ["이동", "移動", "Navigate"],
        ["g.ink"] = ["판서", "手書き", "Ink"],
        ["g.show"] = ["쇼", "ショー", "Show"],
        ["g.file"] = ["파일", "ファイル", "File"],
        ["g.edit"] = ["편집", "編集", "Edit"],
        ["g.slides"] = ["슬라이드", "スライド", "Slides"],
        ["g.help"] = ["도움말", "ヘルプ", "Help"],
        ["firstSlide"] = ["첫 슬라이드", "最初のスライド", "First slide"],
        ["lastSlide"] = ["마지막 슬라이드", "最後のスライド", "Last slide"],
        ["prevSlide"] = ["이전 슬라이드", "前のスライド", "Previous slide"],
        ["selectPointer"] = ["선택/포인터", "選択/ポインター", "Select/pointer"],
        ["colorN"] = ["색 {0} ({1})", "色 {0} ({1})", "Color {0} ({1})"],
        ["colorNames"] = ["검정,빨강,파랑,초록,노랑,흰색,주황", "黒,赤,青,緑,黄,白,オレンジ", "Black,Red,Blue,Green,Yellow,White,Orange"],
        ["insertBlankPage"] = ["빈 판서 페이지 삽입", "空白の手書きページを挿入", "Insert blank ink page"],
        ["showFromStart"] = ["처음부터 슬라이드 쇼", "最初からスライド ショー", "Slide show from the beginning"],
        ["showFromCurrent"] = ["현재 슬라이드부터 슬라이드 쇼", "現在のスライドからスライド ショー", "Slide show from the current slide"],
        ["breakTimer"] = ["Break Time 타이머", "Break Time タイマー", "Break Time timer"],
        ["zoom"] = ["화면 확대 (영역 드래그)", "画面の拡大 (範囲をドラッグ)", "Zoom in (drag an area)"],
        ["undoInkFirst"] = ["실행 취소 (판서 우선)", "元に戻す (手書きを優先)", "Undo (ink first)"],
        ["editText"] = ["텍스트 편집", "テキストの編集", "Edit text"],
    };
}

/// <summary><c>{local:Loc key}</c> in XAML: the text for <see cref="L.Lang"/>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
