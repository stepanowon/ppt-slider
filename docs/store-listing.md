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
검은 화면/흰 화면, 레이저 포인터, 화면 확대, 번호로 이동, 휴식 타이머(Break Time)를 지원합니다.

■ 간단한 편집
텍스트를 제자리에서 고치고(글꼴·크기·색·굵게·기울임꼴·밑줄·정렬), 텍스트 상자와 그림을 넣고, 개체를 옮기거나 크기를 바꿀 수 있습니다.
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

.pptx 프레젠테이션을 열어 간단히 편집하고, 화면 판서와 발표자 보기로 발표하는 가벼운 앱. 판서는 슬라이드별로 기억되고 판서 노트 PDF로 내보낼 수 있습니다.

### 이 버전의 새로운 기능

첫 버전입니다.
- 슬라이드별로 기억되는 화면 판서
- 슬라이드 쇼, 발표자 보기, 레이저 포인터, 화면 확대, 휴식 타이머
- 텍스트·그림 편집, 썸네일 끌어다 놓기로 순서 바꾸기
- 판서 노트 PDF 내보내기
- 한국어·일본어·영어 지원

### 제품 기능

1. 화면 판서: 펜·형광펜·지우개, 7가지 색, 두께 조절
2. 판서를 슬라이드별로 기억해 슬라이드를 넘기면 그 판서가 다시 나타남
3. 편집 화면, 슬라이드 쇼, 다른 앱 위 어디에나 판서
4. 쇼 모니터 선택과 발표자 보기(현재·다음 슬라이드, 노트, 경과 시간)
5. 검은 화면/흰 화면, 레이저 포인터, 화면 확대, 번호로 이동
6. 텍스트 제자리 편집(글꼴·크기·색·굵게·기울임꼴·밑줄·정렬)
7. 텍스트 상자·그림 삽입(붙여넣기, 끌어다 놓기 지원)
8. 썸네일을 끌어다 놓아 슬라이드 순서 바꾸기
9. 판서한 슬라이드만 모은 판서 노트 PDF와 전체 PDF 내보내기
10. 자동 저장·복구, 최근 파일, 단축키 사용자 지정, 휴식 타이머

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
黒い画面/白い画面、レーザー ポインター、画面の拡大、番号でジャンプ、休憩タイマー (Break Time) に対応しています。

■ 手軽な編集
テキストをその場で編集 (フォント・サイズ・色・太字・斜体・下線・配置) し、テキスト ボックスや画像を挿入し、オブジェクトの移動やサイズ変更ができます。
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

.pptx プレゼンテーションを開いて手軽に編集し、画面上の手書きと発表者ツールで発表できる軽量アプリ。手書きはスライドごとに記憶され、手書きノート PDF に出力できます。

### このバージョンの新機能

最初のバージョンです。
- スライドごとに記憶される画面上の手書き
- スライド ショー、発表者ツール、レーザー ポインター、画面の拡大、休憩タイマー
- テキスト・画像の編集、サムネイルのドラッグによる順序の変更
- 手書きノート PDF の出力
- 日本語・韓国語・英語に対応

### 製品の機能

1. 画面上の手書き: ペン・蛍光ペン・消しゴム、7 色、太さの調整
2. 手書きをスライドごとに記憶し、スライドを切り替えると再表示
3. 編集画面、スライド ショー、ほかのアプリの上のどこにでも手書き
4. 表示モニターの選択と発表者ツール (現在と次のスライド、ノート、経過時間)
5. 黒い画面/白い画面、レーザー ポインター、画面の拡大、番号でジャンプ
6. テキストのその場編集 (フォント・サイズ・色・太字・斜体・下線・配置)
7. テキスト ボックス・画像の挿入 (貼り付け、ドラッグ アンド ドロップに対応)
8. サムネイルのドラッグでスライドの順序を変更
9. 手書きしたスライドだけの手書きノート PDF と、全スライドの PDF 出力
10. 自動保存と復元、最近使ったファイル、ショートカットの変更、休憩タイマー

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
Black and white screens, laser pointer, zoom, jump to a slide by number, and a full-screen break timer (Break Time).

■ Quick editing
Edit text in place (font, size, color, bold, italic, underline, alignment), insert text boxes and pictures, and move or resize objects.
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

A lightweight app to open and edit .pptx presentations and present with screen ink and Presenter View. Ink is remembered per slide and can be exported as Ink Notes PDF.

### What's new in this version

First release.
- Screen ink remembered per slide
- Slide show, Presenter View, laser pointer, zoom, break timer
- Text and picture editing, drag thumbnails to reorder slides
- Ink Notes PDF export
- English, Korean and Japanese

### Product features

1. Screen ink: pen, highlighter, eraser, 7 colors, adjustable width
2. Ink remembered per slide and shown again when you return to it
3. Draw anywhere: over the editor, the slide show or other apps
4. Choose the show monitor; Presenter View with current and next slides, notes and elapsed time
5. Black/white screen, laser pointer, zoom, jump to slide by number
6. In-place text editing (font, size, color, bold, italic, underline, alignment)
7. Insert text boxes and pictures (paste or drag and drop)
8. Drag thumbnails to reorder slides
9. Ink Notes PDF of only the slides you drew on, or a PDF of all slides
10. Autosave and recovery, recent files, custom shortcuts, break timer

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
