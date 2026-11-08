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
        ["redo"] = ["다시 실행", "やり直し", "Redo"],
        ["nothingToRedo"] = ["다시 실행할 작업이 없습니다", "やり直す操作がありません", "Nothing to redo"],
        ["cut"] = ["잘라내기", "切り取り", "Cut"],
        ["copy"] = ["복사", "コピー", "Copy"],
        ["paste"] = ["붙여넣기", "貼り付け", "Paste"],
        ["pasteTip"] = ["붙여넣기 (복사한 개체, 그림, 텍스트)", "貼り付け (コピーしたオブジェクト、画像、テキスト)", "Paste (copied objects, a picture or text)"],
        ["nothingToPaste"] = ["붙여넣을 내용이 없습니다", "貼り付ける内容がありません", "Nothing to paste"],
        ["selectAll"] = ["모두 선택", "すべて選択", "Select all"],
        ["duplicateShort"] = ["복제 (선택한 개체, 없으면 슬라이드)", "複製 (選択したオブジェクト、なければスライド)", "Duplicate (the selected objects, else the slide)"],
        ["formatPainter"] = ["서식 복사", "書式のコピー", "Format Painter"],
        ["formatPainterTip"] = ["서식 복사: 선택한 개체의 서식을 다음에 클릭하는 개체에 적용", "書式のコピー: 選択したオブジェクトの書式を次にクリックするオブジェクトに適用",
            "Format Painter: give the next object you click the selected object's format"],
        ["painterHint"] = ["서식을 적용할 개체를 클릭하세요 (Esc: 취소)", "書式を適用するオブジェクトをクリックしてください (Esc: キャンセル)", "Click the object to apply the format to (Esc: cancel)"],
        ["copyFormat"] = ["서식 복사", "書式のコピー", "Copy formatting"],
        ["pasteFormat"] = ["서식 붙여넣기", "書式の貼り付け", "Paste formatting"],
        ["formatCopied"] = ["서식을 복사했습니다 (Ctrl+Shift+V로 붙여넣기)", "書式をコピーしました (Ctrl+Shift+V で貼り付け)", "Formatting copied (paste with Ctrl+Shift+V)"],
        ["strike"] = ["취소선", "取り消し線", "Strikethrough"],
        ["highlight"] = ["텍스트 강조 색", "蛍光ペンの色 (テキスト)", "Text highlight color"],
        ["noHighlight"] = ["강조 없음", "色なし", "No color"],
        ["bullets"] = ["글머리 기호", "箇条書き", "Bullets"],
        ["numbering"] = ["번호 매기기", "段落番号", "Numbering"],
        ["indentMore"] = ["목록 수준 늘림 (들여쓰기)", "インデントを増やす", "Increase list level"],
        ["indentLess"] = ["목록 수준 줄임 (내어쓰기)", "インデントを減らす", "Decrease list level"],
        ["alignJustify"] = ["양쪽 맞춤", "両端揃え", "Justify"],
        ["lineSpacing"] = ["줄 간격", "行間", "Line spacing"],
        ["grp.drawing"] = ["그리기", "図形描画", "Drawing"],
        ["shapes"] = ["도형", "図形", "Shapes"],
        ["shapesTip"] = ["도형 삽입: 고른 뒤 슬라이드에서 끌어서 그립니다 (Shift: 정사각형/원)", "図形の挿入: 選んでからスライド上でドラッグします (Shift: 正方形/正円)",
            "Insert a shape: pick one, then drag on the slide (Shift: square / circle)"],
        ["drawShapeHint"] = ["슬라이드에서 끌어서 도형을 그리세요 (Esc: 취소)", "スライド上でドラッグして図形を描いてください (Esc: キャンセル)", "Drag on the slide to draw the shape (Esc: cancel)"],
        ["shape.rect"] = ["사각형", "四角形", "Rectangle"],
        ["shape.roundRect"] = ["모서리가 둥근 사각형", "角丸四角形", "Rounded rectangle"],
        ["shape.ellipse"] = ["타원", "楕円", "Oval"],
        ["shape.triangle"] = ["삼각형", "二等辺三角形", "Triangle"],
        ["shape.rtTriangle"] = ["직각 삼각형", "直角三角形", "Right triangle"],
        ["shape.diamond"] = ["다이아몬드", "ひし形", "Diamond"],
        ["shape.parallelogram"] = ["평행 사변형", "平行四辺形", "Parallelogram"],
        ["shape.trapezoid"] = ["사다리꼴", "台形", "Trapezoid"],
        ["shape.pentagon"] = ["오각형", "正五角形", "Pentagon"],
        ["shape.hexagon"] = ["육각형", "六角形", "Hexagon"],
        ["shape.homePlate"] = ["오각형 화살표", "ホームベース", "Pentagon arrow"],
        ["shape.chevron"] = ["갈매기형 수장", "山形", "Chevron"],
        ["shape.star5"] = ["별: 꼭짓점 5개", "星 5", "Star: 5 points"],
        ["shape.rightArrow"] = ["오른쪽 화살표", "右矢印", "Right arrow"],
        ["shape.leftArrow"] = ["왼쪽 화살표", "左矢印", "Left arrow"],
        ["shape.upArrow"] = ["위쪽 화살표", "上矢印", "Up arrow"],
        ["shape.downArrow"] = ["아래쪽 화살표", "下矢印", "Down arrow"],
        ["shape.line"] = ["선", "直線", "Line"],
        ["shape.arrow"] = ["화살표 선", "矢印", "Arrow"],
        ["arrange"] = ["정렬", "配置", "Arrange"],
        ["arrangeTip"] = ["개체 순서, 맞춤, 회전", "オブジェクトの順序、配置、回転", "Order, align and rotate objects"],
        ["selectShapeFirst"] = ["먼저 개체를 선택하세요", "先にオブジェクトを選択してください", "Select an object first"],

        // ribbon: Insert
        ["tab.insert"] = ["삽입", "挿入", "Insert"],
        ["table"] = ["표", "表", "Table"],
        ["tableTip"] = ["표 삽입: 칸을 골라 행과 열의 수를 정합니다", "表の挿入: マス目で行と列の数を選びます", "Insert a table: pick the number of rows and columns"],
        ["insertTable"] = ["표 삽입", "表の挿入", "Insert Table"],
        ["tableSize"] = ["{0} x {1} 표", "{0} x {1} 表", "{0} x {1} Table"],

        // ribbon: Shape Format
        ["tab.format"] = ["도형 서식", "図形の書式", "Shape Format"],
        ["grp.shapeStyle"] = ["도형 스타일", "図形のスタイル", "Shape Styles"],
        ["shapeFill"] = ["채우기", "塗りつぶし", "Fill"],
        ["shapeFillTip"] = ["도형 채우기 색", "図形の塗りつぶし", "Shape fill color"],
        ["noFill"] = ["채우기 없음", "塗りつぶしなし", "No fill"],
        ["shapeOutline"] = ["윤곽선", "枠線", "Outline"],
        ["shapeOutlineTip"] = ["도형 윤곽선 색, 두께, 대시", "図形の枠線の色、太さ、実線/点線", "Shape outline color, weight and dashes"],
        ["noOutline"] = ["윤곽선 없음", "枠線なし", "No outline"],
        ["outlineWeight"] = ["두께", "太さ", "Weight"],
        ["outlineDash"] = ["대시", "実線/点線", "Dashes"],
        ["dashSolid"] = ["실선", "実線", "Solid"],
        ["dashDot"] = ["점선", "点線", "Dotted"],
        ["dashDash"] = ["파선", "破線", "Dashed"],
        ["dashDashDot"] = ["긴 파선-점선", "長破線と点", "Long dash-dot"],
        ["grp.arrange"] = ["정렬", "配置", "Arrange"],
        ["toFront"] = ["맨 앞으로", "最前面へ", "To Front"],
        ["toBack"] = ["맨 뒤로", "最背面へ", "To Back"],
        ["bringToFront"] = ["맨 앞으로 가져오기", "最前面へ移動", "Bring to front"],
        ["sendToBack"] = ["맨 뒤로 보내기", "最背面へ移動", "Send to back"],
        ["bringForward"] = ["앞으로 가져오기", "前面へ移動", "Bring forward"],
        ["sendBackward"] = ["뒤로 보내기", "背面へ移動", "Send backward"],
        ["align"] = ["맞춤", "配置", "Align"],
        ["alignTip"] = ["개체 맞춤 (여러 개는 서로, 하나는 슬라이드에)", "オブジェクトの配置 (複数なら互いに、1 つならスライドに)", "Align objects (several: to each other, one: to the slide)"],
        ["alignLeftObj"] = ["왼쪽 맞춤", "左揃え", "Align left"],
        ["alignCenterObj"] = ["가운데 맞춤", "左右中央揃え", "Align center"],
        ["alignRightObj"] = ["오른쪽 맞춤", "右揃え", "Align right"],
        ["alignTopObj"] = ["위쪽 맞춤", "上揃え", "Align top"],
        ["alignMiddleObj"] = ["중간 맞춤", "上下中央揃え", "Align middle"],
        ["alignBottomObj"] = ["아래쪽 맞춤", "下揃え", "Align bottom"],
        ["rotate"] = ["회전", "回転", "Rotate"],
        ["rotateTip"] = ["회전 및 대칭 (선택한 개체 위의 원을 끌어도 됩니다)", "回転と反転 (選択したオブジェクトの上の丸をドラッグしても回転します)",
            "Rotate and flip (or drag the circle above the selected object)"],
        ["rotateRight"] = ["오른쪽으로 90° 회전", "右へ 90 度回転", "Rotate right 90°"],
        ["rotateLeft"] = ["왼쪽으로 90° 회전", "左へ 90 度回転", "Rotate left 90°"],
        ["flipV"] = ["상하 대칭", "上下反転", "Flip vertical"],
        ["flipH"] = ["좌우 대칭", "左右反転", "Flip horizontal"],
        ["grp.table"] = ["표", "表", "Table"],
        ["rowAbove"] = ["위에 행", "上に行", "Row Above"],
        ["rowAboveTip"] = ["현재 셀 위에 행 삽입", "現在のセルの上に行を挿入", "Insert a row above the current cell"],
        ["rowBelow"] = ["아래에 행", "下に行", "Row Below"],
        ["rowBelowTip"] = ["현재 셀 아래에 행 삽입", "現在のセルの下に行を挿入", "Insert a row below the current cell"],
        ["colLeft"] = ["왼쪽에 열", "左に列", "Col Left"],
        ["colLeftTip"] = ["현재 셀 왼쪽에 열 삽입", "現在のセルの左に列を挿入", "Insert a column left of the current cell"],
        ["colRight"] = ["오른쪽에 열", "右に列", "Col Right"],
        ["colRightTip"] = ["현재 셀 오른쪽에 열 삽입", "現在のセルの右に列を挿入", "Insert a column right of the current cell"],
        ["deleteRow"] = ["행 삭제", "行の削除", "Delete Row"],
        ["deleteRowTip"] = ["현재 셀의 행 삭제", "現在のセルの行を削除", "Delete the current cell's row"],
        ["deleteCol"] = ["열 삭제", "列の削除", "Delete Col"],
        ["deleteColTip"] = ["현재 셀의 열 삭제", "現在のセルの列を削除", "Delete the current cell's column"],
        ["deleteTable"] = ["표 삭제", "表の削除", "Delete Table"],
        ["selectTableFirst"] = ["먼저 표를 선택하세요 (행·열은 클릭한 셀 기준)", "先に表を選択してください (行・列はクリックしたセルが基準)", "Select a table first (rows and columns go at the cell you clicked)"],

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
        ["inkShowOnly"] = ["판서는 슬라이드 쇼에서만 할 수 있습니다", "手書きはスライドショーでのみ使えます", "Ink can be drawn only in the slide show"],
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
        ["about"] = ["정보", "情報", "About"],
        ["aboutTitle"] = ["PPT Slider 정보", "PPT Slider について", "About PPT Slider"],
        ["versionOf"] = ["버전 {0}", "バージョン {0}", "Version {0}"],
        ["aboutDesc"] = ["슬라이드 쇼 위에 판서하며 발표하는 .pptx 편집·발표 도구입니다.", "スライド ショーに手書きしながら発表できる、軽量な .pptx 編集・発表ツールです。", "A lightweight .pptx editor and presenter for drawing on the slide show."],
        ["aboutLicense"] = ["MIT 라이선스로 배포되는 무료 앱입니다.", "MIT ライセンスで配布されている無料のアプリです。", "Free app released under the MIT License."],
        ["linkHome"] = ["GitHub 저장소", "GitHub リポジトリ", "GitHub repository"],
        ["linkStore"] = ["Microsoft Store", "Microsoft Store", "Microsoft Store"],
        ["linkPrivacy"] = ["개인정보처리방침", "プライバシー ポリシー", "Privacy policy"],
        ["linkNotices"] = ["오픈 소스 고지", "オープンソースの通知", "Third-party notices"],
        ["copyInfo"] = ["정보 복사", "情報をコピー", "Copy info"],
        ["copyInfoTip"] = ["버전과 시스템 정보를 클립보드에 복사(문제 신고용)", "バージョンとシステム情報をクリップボードにコピー (問題の報告用)", "Copy the version and system details to the clipboard (for bug reports)"],
        ["copied"] = ["복사했습니다", "コピーしました", "Copied"],
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
        ["spotlight"] = ["스팟 하이라이트 (마우스 포인터 주위에 원)", "スポット ハイライト (マウス ポインターの周りに円)", "Spot highlight (a circle around the mouse pointer)"],
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
