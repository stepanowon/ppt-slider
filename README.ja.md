# PPT Slider

[한국어](README.md) | **日本語** | [English](README.en.md)

PowerPoint がなくても `.pptx` ファイルを開いて簡単に編集し、手書きしながら発表できる Windows デスクトップ アプリです (WPF、.NET 10)。

## 主な機能

- **開く / 保存**: `.pptx` を開く (ドラッグ アンド ドロップ、「プログラムから開く」に対応)、保存、名前を付けて保存。
  編集は OpenXML ドキュメントを直接変更するため、アプリが描画できない要素も保存後そのまま残ります。
  - **最近使ったファイル**: 最近の 10 件を [開く] ボタン横の一覧と空の画面に表示します。
  - **自動保存**: 2 分ごとに編集中のドキュメントのコピーを保管し、正常終了すると削除します。異常終了後に同じファイルを開くと、より新しいコピーから復元するか確認します。
  - **PDF にエクスポート**: スライドごとに 1 ページずつ出力し、手書きを含めるかどうかを選べます。
  - **手書きノート**: 手書きしたスライドだけを手書きと一緒に PDF に出力します (講義ノート)。
- **編集**
  - オブジェクトの選択、ドラッグで移動、右下の角でサイズ変更、削除
  - ダブルクリック (または F2) でテキストをその場で編集 (フォント、サイズ、色、配置など元の書式を維持)
  - 太字 / 斜体 / 下線、フォント・サイズ・色、段落の配置 (テキスト編集中は、太字・斜体・下線・フォント・サイズが選択した文字だけに適用され、フォントとサイズのボックスには選択した文字の値が表示されます)
  - テキスト ボックス・画像の挿入 (画像は [画像] ボタン、Ctrl+V で貼り付け、画像ファイルのドラッグ アンド ドロップ)、新しいスライド・複製・削除、元に戻す
  - サムネイルをドラッグしてスライドの順序を変更
  - 「はみ出す場合だけ自動調整する」(normAutofit) が設定されたテキストは、ボックスに収まるように縮小して描画します。
- **画面上の手書き**: ペン、蛍光ペン、消しゴム、7 色、太さの調整。手書きはスライドではなく画面上の透明なウィンドウに描かれるため、
  編集画面やスライド ショーだけでなく、ほかのアプリの上にも描けます (スライド ショー中はそのモニター、それ以外はエディターのあるモニター)。
  - 手書きはスライドごとに記憶され、スライドを切り替えるとそのスライドで描いた手書きが再び表示されます。
  - 画面上のツールバー: ポインター (手書きを残したまま下の画面をクリック)・ペン・蛍光ペン・消しゴム・色・太さ・元に戻す・消去・非表示。ドラッグで移動できます。
  - Shift を押しながら描くと直線になります。Ctrl+Z は手書きがあれば手書きのストロークから元に戻します。
  - **空白の手書きページ** (Ctrl+Enter): 現在のスライドの後に空のスライドを挿入し、ペンに切り替えます。
  - 手書きは .pptx に保存されず、アプリを閉じると消えます。残すには PDF または手書きノートにエクスポートしてください。
- **スライド ショー**: 最初から (F5) / 現在のスライドから (Shift+F5)。スライド ショーを表示するモニターを選べます (既定: セカンダリ モニター)。
  - 黒い画面 / 白い画面、レーザー ポインター、番号で移動 (数字 + Enter)、画面の拡大 (Z を押して範囲をドラッグ)
  - 右下に現在時刻と経過時間を表示、マウスを 3 秒動かさないとカーソルを非表示
- **発表者ツール**: [発表者ツールを使用] をオンにすると、スライド ショーとは別のモニターに現在と次のスライド、発表者ノート (文字サイズ調整可)、経過時間を表示します。
  発表者ツールで選んだ手書きツールはスライド ショーの画面上に描き、スライド ショーと同じショートカットが使えます。
- **Break Time**: 5 分～2 時間 (5 分単位) の全画面休憩タイマー (T)。
- **言語**: 한국어・日本語・English。既定では Windows の表示言語に従い (韓国語・日本語以外は英語)、[設定] タブの言語一覧で変更すると再起動後に適用されます。選択は `%APPDATA%\PptSlider\language.txt` に保存されます。
- **ショートカットの設定**: [設定] タブの [ショートカットの設定] ですべてのコマンドのショートカットを変更でき、`%APPDATA%\PptSlider\shortcuts.json` に保存されます。F1 で一覧を表示できます。

### 既定のショートカット

| 分類 | コマンド | キー |
|---|---|---|
| 移動 | 次 / 前のスライド | → ↓ PageDown Space Enter N / ← ↑ PageUp Backspace |
| 移動 | 最初 / 最後のスライド | Home / End |
| 移動 | 番号で移動 (スライド ショー) | 数字 + Enter |
| 手書き | ペン / 蛍光ペン / 消しゴム | P / H / E |
| 手書き | レーザー ポインター | Ctrl+L |
| 手書き | 色 1～7 | 1～7 (テンキーを含む) |
| 手書き | 細く / 太く | `[` `-` / `]` `=` |
| 手書き | このスライドの手書きを消去 | C |
| 手書き | 空白の手書きページを挿入 | Ctrl+Enter |
| ショー | 最初から / 現在のスライドから | F5 / Shift+F5 |
| ショー | 黒い画面 / 白い画面 | B `.` / W `,` |
| ショー | 画面の拡大 | Z |
| ショー | Break Time | T |
| ファイル | 開く / 保存 / 名前を付けて保存 | Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| 編集 | 元に戻す (手書きを優先)・テキストの編集・オブジェクトの削除 | Ctrl+Z・F2・Delete |
| 編集 | 太字 / 斜体 / 下線 | Ctrl+B / Ctrl+I / Ctrl+U |
| 編集 | フォント サイズの拡大 / 縮小 | Ctrl+Shift+. / Ctrl+Shift+, |
| スライド | 新しいスライド / 複製 | Ctrl+M / Ctrl+D |
| ヘルプ | ショートカット一覧 | F1 |

スライド ショーでは、数字キーが番号での移動とペンの色で重なっています。数字を 1 つだけ押して 1 秒たつとペンの色、その間に Enter または次の数字が来ると番号の入力として扱います。

Esc は固定キーです: テキスト編集のキャンセル、拡大の解除、選択ツールに戻る、スライド ショーの終了 (前から順に 1 つずつ)。

### まだ描画しない要素

グラフ、効果 (影など)、既定以外の表のスタイル、テキストの自動調整、アニメーションは画面に表示されません。ファイルにはそのまま残ります。
SmartArt は PowerPoint がファイルに保存した図 (drawing.xml) で描画し、その図がないファイルではプレースホルダーの枠だけが表示されます。

## ビルド

必要なもの: Windows、[.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
cd src
dotnet build -c Release
```

出力はリポジトリ ルートの `bin\Release\net10.0-windows\PptSlider.exe` にできます。
ビルド出力 (`bin/`、`obj/`) は `src/Directory.Build.props` でルートに指定しており、Git から除外されます。

### セルフテスト

```powershell
PptSlider.exe --selftest deck.pptx outDir
```

すべてのスライドを PNG にレンダリングし、編集 → 保存 → 再度開く流れと、スライド ショー・発表者ツール・画面上の手書き・画像の挿入・順序の変更・テキストの自動調整・PDF・SmartArt・最近使ったファイル・自動保存・3 言語の UI 文字列を検査します。
スライド ショーと発表者ツールのウィンドウは画面外に表示し、キー入力もそのウィンドウにだけ送るため、実行中に他の作業をしてもかまいません。
結果は `outDir\selftest.txt` に記録され、終了コード 0 なら合格です。アプリ データは `outDir\local` を使うため、実際の最近使ったファイルや自動保存には影響しません。セルフテストは常に韓国語で実行されます。

## MSIX パッケージ

```powershell
.\build-msix.ps1           # ローカル テスト用 (自己署名)
.\build-msix.ps1 -Store    # Store 提出用 (署名なし)
```

self-contained publish → `Package\AppxManifest.xml` と `Package\Assets` のロゴを加えたレイアウト → `makeappx pack` → 署名の順に進み、
結果は `bin\msix\PptSlider_<バージョン>_x64.msix` です。パッケージのバージョンは `src\PptSlider.csproj` の `Version` に従います。
Windows SDK がない場合は、`makeappx`・`signtool` を含む NuGet パッケージ `Microsoft.Windows.SDK.BuildTools` を `obj\tools` にダウンロードして使います。

マニフェストには、完全信頼 (runFullTrust) のデスクトップ アプリ、`.pptx` のファイル関連付け、韓国語・日本語・英語 (ko-KR、ja-JP、en-US) のリソースが含まれています。
Store ではこの 3 言語それぞれのストア掲載情報 (説明・スクリーンショット) が必要です。
ロゴは `Package\make-assets.ps1` で描き直せます。

### ローカル テスト インストール

スクリプトはマニフェストの Publisher と同じ名前の自己署名証明書を `Cert:\CurrentUser\My` に作成し (既にあれば再利用)、`bin\msix\PptSlider-test.cer` にエクスポートします。
この証明書を一度だけ信頼済みとして登録してからインストールします (管理者の PowerShell):

```powershell
Import-Certificate -FilePath bin\msix\PptSlider-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage bin\msix\PptSlider_<バージョン>_x64.msix
```

### Microsoft Store への提出

1. [パートナー センター](https://partner.microsoft.com/dashboard)でアプリ名を予約します。
2. **製品管理 → 製品 ID** ページの値で `Package\AppxManifest.xml` を修正します。
   - `Identity` の `Name` ← Package/Identity/Name
   - `Identity` の `Publisher` ← Package/Identity/Publisher (`CN=XXXXXXXX-...` の形式、そのままコピー)
   - `PublisherDisplayName` ← Package/Properties/PublisherDisplayName
   - `DisplayName` と `uap:VisualElements` の `DisplayName` ← 予約したアプリ名
3. `src\PptSlider.csproj` の `Version` を上げます。Store は同じバージョンを再度受け付けません。
4. `.\build-msix.ps1 -Store` で作成した `.msix` を提出の [パッケージ] にアップロードします。署名は Store が行います。
5. `runFullTrust` は制限付き機能のため、提出時に使用理由を記入する必要があります (例: 「WPF デスクトップ アプリで、ユーザーが開いた .pptx ファイルを読み書きします」)。

> **アプリ名の注意**: 「PPT」は Microsoft PowerPoint を連想させる名前のため、Store の審査で商標の問題を指摘される可能性があります。
> 予約する名前には「PPT」・「PowerPoint」を入れないほうが安全で、説明では「.pptx ファイルを開く」のようにファイル形式としてだけ触れることをお勧めします。

パッケージでインストールしたアプリでは、`%LOCALAPPDATA%`・`%APPDATA%` への書き込みがパッケージ専用フォルダーにリダイレクトされる場合があります。最近使ったファイル・自動保存・ショートカットの設定・言語の選択はその中に保存され、アプリをアンインストールすると一緒に削除されます。

## フォルダー構成

```
build-msix.ps1          MSIX パッケージのビルド
Package/                AppxManifest.xml、Assets (ロゴ)、make-assets.ps1
src/
├─ App.xaml(.cs)        エントリ ポイント (--selftest の処理、起動時にファイルを開く)
├─ Views/               MainWindow (エディター)、ShowWindow (スライド ショー)、PresenterWindow (発表者ツール)、
│                       InkOverlay (画面上の手書き)、BreakWindow (休憩タイマー)、ShortcutsWindow (ショートカットの設定)
├─ Models/              Deck (.pptx ドキュメント・編集・保存・元に戻す)、PenState (手書きツールの状態)、InkHistory (手書きの元に戻す)
├─ Rendering/           SlideRenderer (PPTX → WPF のレンダリング、SmartArt を含む)
├─ Services/            Shortcuts (ショートカット)、Monitors (モニター一覧)、PdfExport (PDF)、
│                       RecentFiles (最近使ったファイル)、AutoSave (自動保存)、AppPaths (アプリ データ フォルダー)、Loc (韓国語・日本語・英語の UI 文字列)
└─ Diagnostics/         SelfTest (編集・スライド ショー・手書き・ビューアーの検査)
```

依存パッケージ: [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml)
