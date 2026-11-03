# Microsoft Store 목록 (Partner Center)

Partner Center → 제출 → **Store 목록** 에서 언어(한국어 · 日本語 · English)마다 아래 항목을 붙여 넣습니다.
글자 수 제한: 설명 10,000자 · 새로운 기능 1,500자 · 제품 기능 항목당 200자(최대 20개) · 스크린샷 캡션 200자 · 키워드 7개(각 30자 이하) · 짧은 설명 1,000자.

스크린샷은 `screen/ko`, `screen/ja`, `screen/en` 의 4장을 순서대로 올립니다.

Store 로고(Store 목록 → Store 로고): `Package/StoreArt`의 9:16 포스터 아트(720×1080, 1440×2160)와 1:1 박스 아트(1080×1080, 2160×2160). 다시 그리려면 `Package/make-store-art.ps1`.
1:1 앱 타일 아이콘(300×300, 150×150, 71×71)은 `Package/StoreArt/StoreIcon-*.png`이며 앱 로고와 같은 그림입니다. 다시 그리려면 `Package/make-assets.ps1`.

개인정보처리방침 URL(제품 관리 → 속성): https://github.com/stepanowon/ppt-slider/blob/main/PRIVACY.md

> 앱 이름에 "PPT"가 들어가므로, 설명에는 "PowerPoint"라는 상표를 쓰지 않고 ".pptx 파일"이라는 파일 형식으로만 적었습니다.

---

## 한국어 (ko-KR)

### 한 줄 소개

.pptx를 열어 간단히 고치고, 슬라이드별로 기억되는 화면 판서로 발표하는 가벼운 발표 도구

### 설명

PPTSlider는 .pptx 프레젠테이션을 열어 간단히 고치고, 화면에 판서하면서 발표할 수 있는 가벼운 Windows 앱입니다.
강의, 수업, 세미나처럼 슬라이드 위에 직접 쓰고 그리며 설명하는 발표에 맞춰 만들었습니다.

■ 화면 판서
펜, 형광펜, 지우개와 7가지 색, 두께 조절을 지원합니다. 판서는 화면 위의 투명한 층에 그려지므로 편집 화면, 슬라이드 쇼는 물론 다른 앱 위에도 그릴 수 있습니다.
판서는 슬라이드별로 기억되어, 슬라이드를 넘겼다가 돌아오면 그 슬라이드에서 그렸던 판서가 다시 나타납니다.
Shift를 누른 채 그리면 직선, Ctrl+Z로 판서 되돌리기, Ctrl+Enter로 빈 판서 페이지를 바로 추가할 수 있습니다.

■ 슬라이드 쇼와 발표자 보기
쇼를 띄울 모니터를 고를 수 있고, 다른 모니터에는 현재·다음 슬라이드, 발표자 노트, 경과 시간을 보여 주는 발표자 보기를 띄울 수 있습니다.
검은 화면/흰 화면, 레이저 포인터, 포인터 주변을 원으로 비추는 스팟 하이라이트, 화면 확대, 번호로 이동, 휴식 타이머(Break Time)를 지원합니다.

■ 편집
텍스트를 제자리에서 고칩니다. 글꼴·크기·색·굵게·기울임꼴·밑줄·취소선·텍스트 강조 색, 글머리 기호·번호·목록 수준, 정렬·줄 간격을 바꿀 수 있고, 편집 중에는 선택한 글자에만 적용됩니다.
사각형·원·화살표·별·선 등 도형 19종, 표, 텍스트 상자, 그림을 넣을 수 있습니다. 도형은 채우기·윤곽선 색과 두께·대시, 회전·대칭, 순서(맨 앞·맨 뒤), 맞춤을 바꿀 수 있습니다.
표는 셀을 바로 편집하고(Tab으로 다음 셀), 행·열을 넣거나 지울 수 있습니다. 병합된 셀도 처리합니다.
여러 개체를 한꺼번에 선택해 옮기거나 크기를 바꾸고 회전할 수 있으며, 잘라내기·복사·붙여넣기(다른 슬라이드로, 그림 포함), 복제, 서식 복사, 실행 취소·다시 실행을 지원합니다.
슬라이드 추가·복제·삭제와 썸네일 끌어다 놓기로 순서 바꾸기를 지원합니다.
앱이 표시하지 못하는 요소도 파일 안에 그대로 남겨 두므로, 저장해도 원래 내용이 손상되지 않습니다.

■ 판서 노트와 PDF
판서한 슬라이드만 판서와 함께 PDF로 내보내 강의 노트로 나눠 줄 수 있습니다. 모든 슬라이드를 PDF로 내보낼 수도 있습니다.

■ 그 밖에
자동 저장과 복구, 최근 파일, 모든 단축키 바꾸기, 한국어·일본어·영어 화면을 지원합니다.

※ 차트, 애니메이션, 일부 효과(그림자 등)는 화면에 표시되지 않지만 파일에는 그대로 유지됩니다.
※ 판서는 .pptx 파일에 저장되지 않습니다. 남기려면 PDF 또는 판서 노트로 내보내세요.
※ 개인 정보를 수집하거나 인터넷으로 보내지 않습니다.

### 짧은 설명

.pptx 프레젠테이션을 열어 텍스트·도형·표를 편집하고, 화면 판서와 발표자 보기로 발표하는 가벼운 앱. 판서는 슬라이드별로 기억되고 판서 노트 PDF로 내보낼 수 있습니다.

### 이 버전의 새로운 기능

버전 0.2.4
- 슬라이드 쇼를 시작하고 끝낼 때마다 메모리가 조금씩 늘어나던 문제를 고쳤습니다. 강의 중 쇼를 여러 번 열고 닫아도 메모리가 쌓이지 않습니다.
- 오래 편집해도 메모리가 계속 늘지 않도록, 실행 취소는 최근 100번의 편집(판서는 슬라이드마다 최근 200번)까지 기억합니다.
- 내부 구조를 정리해 안정성을 높였습니다.

버전 0.2.3
- 슬라이드가 PowerPoint에서 보는 것과 더 가깝게 표시됩니다. 줄 간격, 첫 문단 간격, 글머리 기호의 내어쓰기, 글자 간격이 PowerPoint와 같아져 텍스트가 상자 밖으로 넘치거나 줄이 다르게 바뀌던 문제가 줄었습니다.
- 한글 줄바꿈을 PowerPoint 규칙대로 처리합니다. 단어 단위와 글자 단위 설정을 따르고, 영문 단어에 붙은 조사는 떼지 않습니다.
- 슬라이드 번호가 표시됩니다.
- 표의 글꼴, 첫 열 강조, 밝게·어둡게 한 색이 PowerPoint와 같게 나옵니다.
- 아주 가는 선과 구분선이 사라지지 않고 표시됩니다.
- 파일을 열지 않은 상태에서 언어를 바꿔 다시 시작할 때 "The path is empty" 오류가 나던 문제를 고쳤습니다.

### 제품 기능

1. 화면 판서: 펜·형광펜·지우개, 7가지 색, 두께 조절
2. 판서를 슬라이드별로 기억해 슬라이드를 넘기면 그 판서가 다시 나타남
3. 편집 화면, 슬라이드 쇼, 다른 앱 위 어디에나 판서
4. 쇼 모니터 선택과 발표자 보기(현재·다음 슬라이드, 노트, 경과 시간)
5. 검은 화면/흰 화면, 레이저 포인터, 스팟 하이라이트, 화면 확대, 번호로 이동
6. 텍스트 제자리 편집(글꼴·크기·색·굵게·기울임꼴·밑줄·취소선·강조 색·정렬)
7. 글머리 기호·번호, 목록 수준, 줄 간격
8. 도형 19종 삽입(사각형·원·화살표·별·선 등), 채우기·윤곽선·회전·대칭
9. 표 삽입과 셀 편집, 행·열 삽입·삭제(병합된 셀 포함)
10. 텍스트 상자·그림 삽입(붙여넣기, 끌어다 놓기 지원)
11. 여러 개체 선택, 크기·회전 핸들, 순서와 맞춤
12. 잘라내기·복사·붙여넣기, 복제, 서식 복사, 실행 취소·다시 실행
13. 썸네일을 끌어다 놓아 슬라이드 순서 바꾸기
14. 판서한 슬라이드만 모은 판서 노트 PDF와 전체 PDF 내보내기
15. 자동 저장·복구, 최근 파일, 단축키 사용자 지정, 휴식 타이머

### 스크린샷 캡션

1. `1-editor.png` — 슬라이드 목록과 리본으로 .pptx를 바로 편집합니다.
2. `2-editor-ink.png` — 편집 화면 위에 바로 판서하고, 도구 모음은 원하는 곳으로 옮깁니다.
3. `3-show-ink.png` — 슬라이드 쇼에서 펜과 형광펜으로 설명합니다. 판서는 슬라이드별로 기억됩니다.
4. `4-presenter.png` — 발표자 보기에서 다음 슬라이드, 노트, 경과 시간을 확인합니다.

### 추가 정보

**키워드** (하나씩 입력하고 Enter)

프레젠테이션 · 판서 · 슬라이드 쇼 · 발표자 보기 · pptx 뷰어 · 강의 · 화면 펜

**저작권 및 상표 정보**

© 2026 StephenWon. MIT License. 이 앱은 Microsoft와 관련이 없으며, 언급된 상표는 각 소유자의 것입니다.

**추가 사용 조건**

이 앱은 MIT 라이선스(https://github.com/stepanowon/ppt-slider/blob/main/LICENSE)에 따라 무료로 제공되며, 명시적이거나 묵시적인 어떠한 보증도 없이 "있는 그대로" 제공됩니다. 개발자는 이 앱의 사용으로 인한 데이터 손실이나 손해에 책임을 지지 않습니다. 중요한 파일은 편집하기 전에 백업하세요. 판서는 .pptx 파일에 저장되지 않으므로, 남기려면 PDF로 내보내야 합니다. 개인정보 처리에 대해서는 개인정보처리방침을 참고하세요.

**개발자**

StephenWon

---

## 日本語 (ja-JP)

### ひとこと紹介

.pptx を開いて手軽に編集し、スライドごとに記憶される画面上の手書きで発表できる軽量プレゼン ツール

### 説明

PPTSlider は、.pptx プレゼンテーションを開いて手軽に編集し、画面に手書きしながら発表できる軽量な Windows アプリです。
講義、授業、セミナーなど、スライドに直接書き込みながら説明する発表のために作りました。

■ 画面上の手書き
ペン、蛍光ペン、消しゴム、7 色、太さの調整に対応しています。手書きは画面上の透明なレイヤーに描かれるため、編集画面やスライド ショーだけでなく、ほかのアプリの上にも描けます。
手書きはスライドごとに記憶され、スライドを切り替えて戻ると、そのスライドで描いた手書きが再び表示されます。
Shift を押しながら描くと直線、Ctrl+Z で手書きを元に戻し、Ctrl+Enter で空白の手書きページをすぐに追加できます。

■ スライド ショーと発表者ツール
スライド ショーを表示するモニターを選べ、別のモニターには現在と次のスライド、発表者ノート、経過時間を表示する発表者ツールを表示できます。
黒い画面/白い画面、レーザー ポインター、ポインターの周りを円で照らすスポットハイライト、画面の拡大、番号でジャンプ、休憩タイマー (Break Time) に対応しています。

■ 編集
テキストをその場で編集できます。フォント・サイズ・色・太字・斜体・下線・取り消し線・蛍光ペンの色、箇条書き・段落番号・リストのレベル、配置・行間を変更でき、編集中は選択した文字だけに適用されます。
四角形・円・矢印・星・線など 19 種類の図形、表、テキスト ボックス、画像を挿入できます。図形は塗りつぶしと枠線の色・太さ・破線、回転・反転、順序 (最前面・最背面)、配置を変更できます。
表はセルをそのまま編集し (Tab で次のセル)、行・列の挿入と削除ができます。結合セルにも対応しています。
複数のオブジェクトをまとめて選択して移動・サイズ変更・回転でき、切り取り・コピー・貼り付け (別のスライドへ、画像を含む)、複製、書式のコピー、元に戻す・やり直しに対応しています。
スライドの追加・複製・削除、サムネイルのドラッグによる順序の変更に対応しています。
アプリが表示できない要素もファイル内にそのまま残すため、保存しても元の内容は損なわれません。

■ 手書きノートと PDF
手書きしたスライドだけを手書きと一緒に PDF に出力し、講義ノートとして配布できます。すべてのスライドを PDF に出力することもできます。

■ その他
自動保存と復元、最近使ったファイル、すべてのショートカットの変更、日本語・韓国語・英語の画面に対応しています。

※ グラフ、アニメーション、一部の効果 (影など) は画面に表示されませんが、ファイルにはそのまま保持されます。
※ 手書きは .pptx ファイルには保存されません。残すには PDF または手書きノートに出力してください。
※ 個人情報を収集したり、インターネットに送信したりすることはありません。

### 短い説明

.pptx プレゼンテーションを開いてテキスト・図形・表を編集し、画面上の手書きと発表者ツールで発表できる軽量アプリ。手書きはスライドごとに記憶され、手書きノート PDF に出力できます。

### このバージョンの新機能

バージョン 0.2.4
- スライド ショーを開始・終了するたびにメモリが少しずつ増えていた問題を修正しました。講義中にショーを何度開いて閉じても、メモリがたまらなくなりました。
- 長時間編集してもメモリが増え続けないよう、元に戻すは直近 100 回の編集 (手書きはスライドごとに直近 200 回) まで記憶します。
- 内部構造を整理し、安定性を高めました。

バージョン 0.2.3
- スライドが PowerPoint での表示により近くなりました。行間、最初の段落の間隔、箇条書きのぶら下げインデント、文字間隔が PowerPoint と同じになり、テキストがボックスからはみ出したり改行位置が変わったりする問題が減りました。
- 韓国語の改行を PowerPoint の規則どおりに処理します。単語単位と文字単位の設定に従い、英単語に続く助詞は切り離しません。
- スライド番号が表示されるようになりました。
- 表のフォント、最初の列の強調、明るく・暗くした色が PowerPoint と同じになりました。
- ごく細い線や区切り線が消えずに表示されるようになりました。
- ファイルを開いていない状態で言語を変えて再起動すると "The path is empty" エラーが出る問題を修正しました。

### 製品の機能

1. 画面上の手書き: ペン・蛍光ペン・消しゴム、7 色、太さの調整
2. 手書きをスライドごとに記憶し、スライドを切り替えると再表示
3. 編集画面、スライド ショー、ほかのアプリの上のどこにでも手書き
4. 表示モニターの選択と発表者ツール (現在と次のスライド、ノート、経過時間)
5. 黒い画面/白い画面、レーザー ポインター、スポットハイライト、画面の拡大、番号でジャンプ
6. テキストのその場編集 (フォント・サイズ・色・太字・斜体・下線・取り消し線・蛍光ペンの色・配置)
7. 箇条書き・段落番号、リストのレベル、行間
8. 19 種類の図形の挿入 (四角形・円・矢印・星・線など)、塗りつぶし・枠線・回転・反転
9. 表の挿入とセルの編集、行・列の挿入と削除 (結合セルにも対応)
10. テキスト ボックス・画像の挿入 (貼り付け、ドラッグ アンド ドロップに対応)
11. 複数のオブジェクトの選択、サイズ・回転ハンドル、順序と配置
12. 切り取り・コピー・貼り付け、複製、書式のコピー、元に戻す・やり直し
13. サムネイルのドラッグでスライドの順序を変更
14. 手書きしたスライドだけの手書きノート PDF と、全スライドの PDF 出力
15. 自動保存と復元、最近使ったファイル、ショートカットの変更、休憩タイマー

### スクリーンショットのキャプション

1. `1-editor.png` — スライド一覧とリボンで .pptx をそのまま編集できます。
2. `2-editor-ink.png` — 編集画面の上に直接手書きでき、ツールバーは好きな場所に移動できます。
3. `3-show-ink.png` — スライド ショーでペンと蛍光ペンを使って説明。手書きはスライドごとに記憶されます。
4. `4-presenter.png` — 発表者ツールで次のスライド、ノート、経過時間を確認できます。

### 追加情報

**キーワード** (1 つずつ入力して Enter)

プレゼンテーション · 手書き · スライドショー · 発表者ツール · pptx ビューアー · 講義 · 画面ペン

**著作権と商標の情報**

© 2026 StephenWon. MIT License. 本アプリは Microsoft とは関係がなく、記載された商標はそれぞれの所有者に帰属します。

**追加使用条件**

本アプリは MIT ライセンス (https://github.com/stepanowon/ppt-slider/blob/main/LICENSE) に基づいて無料で提供され、明示または黙示を問わず、いかなる保証もなく「現状のまま」提供されます。開発者は、本アプリの使用によって生じたデータの損失や損害について責任を負いません。重要なファイルは編集する前にバックアップしてください。手書きは .pptx ファイルに保存されないため、残すには PDF に出力する必要があります。個人情報の取り扱いについては、プライバシー ポリシーをご覧ください。

**開発者**

StephenWon

---

## English (en-US)

### One-line summary

Open and quickly edit .pptx files, then present with screen ink that is remembered per slide.

### Description

PPTSlider is a lightweight Windows app for opening .pptx presentations, making quick edits, and presenting while you draw on the screen.
It is built for lectures, classes and seminars, where you explain by writing directly over your slides.

■ Screen ink
Pen, highlighter and eraser, 7 colors and adjustable width. Ink is drawn on a transparent layer over the screen, so you can draw over the editor, the slide show, and even other apps.
Ink is remembered per slide: move to another slide and back, and the ink you drew there comes back.
Hold Shift for a straight line, press Ctrl+Z to undo a stroke, and Ctrl+Enter to add a blank ink page instantly.

■ Slide show and Presenter View
Choose which monitor shows the slide show, and open Presenter View on another one with the current and next slides, speaker notes and elapsed time.
Black and white screens, laser pointer, a spotlight that circles the mouse pointer, zoom, jump to a slide by number, and a full-screen break timer (Break Time).

■ Editing
Edit text in place: font, size, color, bold, italic, underline, strikethrough and highlight color, bullets, numbering and list levels, alignment and line spacing. While editing, formatting applies only to the selected characters.
Insert 19 shapes (rectangles, ellipses, arrows, stars, lines and more), tables, text boxes and pictures. Change a shape's fill and outline color, weight and dashes, rotate and flip it, and set its order (bring to front, send to back) and alignment.
Edit table cells in place (Tab to the next cell) and insert or delete rows and columns, merged cells included.
Select several objects at once to move, resize or rotate them. Cut, copy and paste (across slides, pictures included), duplicate, Format Painter, undo and redo.
Add, duplicate and delete slides, and drag thumbnails to change their order.
Elements the app does not display are kept in the file as they are, so saving never damages your original content.

■ Ink notes and PDF
Export only the slides you drew on, with their ink, as a PDF to hand out as lecture notes. You can also export every slide to PDF.

■ And more
Autosave and recovery, recent files, fully customizable shortcuts, and an English, Korean and Japanese interface.

Note: charts, animations and some effects (such as shadows) are not displayed, but they are kept in the file.
Note: ink is not saved into the .pptx file. To keep it, export to PDF or Ink Notes.
Note: the app does not collect personal data or send anything over the internet.

### Short description

A lightweight app to open .pptx presentations, edit text, shapes and tables, and present with screen ink and Presenter View. Ink is remembered per slide and can be exported as Ink Notes PDF.

### What's new in this version

Version 0.2.4
- Fixed memory growing a little every time a slide show started and ended. Opening and closing the show many times during a lecture no longer adds up.
- So that memory doesn't keep growing during long editing sessions, undo now remembers the last 100 edits (and the last 200 ink changes on each slide).
- Reorganized the internals for better stability.

Version 0.2.3
- Slides now look much closer to PowerPoint. Line spacing, the spacing of the first paragraph, hanging indents for bullets and character spacing now match PowerPoint, so text no longer overflows its box or wraps differently as often.
- Korean text wraps by PowerPoint's rules: it follows the word or character wrapping setting and keeps a particle attached to the English word before it.
- Slide numbers are shown.
- Table fonts, first-column emphasis and lightened or darkened colors now match PowerPoint.
- Very thin lines and separators are no longer missing.
- Fixed the "The path is empty" error when restarting after changing the language with no file open.

### Product features

1. Screen ink: pen, highlighter, eraser, 7 colors, adjustable width
2. Ink remembered per slide and shown again when you return to it
3. Draw anywhere: over the editor, the slide show or other apps
4. Choose the show monitor; Presenter View with current and next slides, notes and elapsed time
5. Black/white screen, laser pointer, spotlight, zoom, jump to slide by number
6. In-place text editing (font, size, color, bold, italic, underline, strikethrough, highlight, alignment)
7. Bullets and numbering, list levels, line spacing
8. Insert 19 shapes (rectangles, ellipses, arrows, stars, lines and more) with fill, outline, rotate and flip
9. Insert tables, edit cells, insert and delete rows and columns (merged cells included)
10. Insert text boxes and pictures (paste or drag and drop)
11. Multiple selection, resize and rotation handles, order and alignment
12. Cut, copy and paste, duplicate, Format Painter, undo and redo
13. Drag thumbnails to reorder slides
14. Ink Notes PDF of only the slides you drew on, or a PDF of all slides
15. Autosave and recovery, recent files, custom shortcuts, break timer

### Screenshot captions

1. `1-editor.png` — Edit a .pptx right away with the slide list and the ribbon.
2. `2-editor-ink.png` — Draw directly over the editor and move the toolbar wherever you like.
3. `3-show-ink.png` — Explain with pen and highlighter during the slide show. Ink is remembered per slide.
4. `4-presenter.png` — Presenter View shows the next slide, your notes and the elapsed time.

### Additional information

**Keywords** (type each one and press Enter)

presentation · screen ink · slide show · presenter view · pptx viewer · lecture · annotate

**Copyright and trademark info**

© 2026 StephenWon. MIT License. This app is not affiliated with Microsoft; trademarks mentioned belong to their respective owners.

**Additional license terms**

This app is provided free of charge under the MIT License (https://github.com/stepanowon/ppt-slider/blob/main/LICENSE), "as is", without warranty of any kind, express or implied. The developer is not liable for any data loss or damage arising from its use. Back up important files before editing them. Ink is not saved into the .pptx file; export to PDF to keep it. See the privacy policy for how your information is handled.

**Developed by**

StephenWon
