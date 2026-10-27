# PPT Slider

PowerPoint 없이 `.pptx` 파일을 열어 간단히 편집하고, 판서하면서 발표할 수 있는 Windows 데스크톱 앱입니다 (WPF, .NET 10).

## 주요 기능

- **열기 / 저장** — `.pptx` 열기(파일 끌어다 놓기, "연결 프로그램" 지원), 저장, 다른 이름으로 저장.
  편집은 OpenXML 문서를 직접 고치므로 앱이 그리지 못하는 요소도 저장 후 그대로 유지됩니다.
- **편집**
  - 개체 선택 · 드래그 이동 · 오른쪽 아래 모서리로 크기 조절 · 삭제
  - 더블클릭(또는 F2)으로 텍스트 제자리 편집 — 글꼴, 크기, 색, 정렬 등 원래 서식 유지
  - 굵게 / 기울임꼴 / 밑줄, 글꼴 · 크기 · 색, 단락 정렬
  - 텍스트 상자 삽입, 새 슬라이드 · 복제 · 삭제, 실행 취소
- **판서** — 펜, 형광펜, 지우개, 7가지 색, 두께 조절. 편집 화면과 슬라이드 쇼 모두에서 사용할 수 있습니다.
  판서가 있을 때 저장하면 판서 포함/미포함 버전을 고를 수 있으며, 포함하면 슬라이드마다 투명 그림으로 들어갑니다.
- **슬라이드 쇼** — 처음부터(F5) / 현재 슬라이드부터(Shift+F5). 쇼를 띄울 모니터를 선택할 수 있습니다(기본값: 보조 모니터).
- **Break Time** — 5분 ~ 2시간(5분 단위) 전체 화면 휴식 타이머(T).
- **단축키 설정** — 모든 명령의 단축키를 바꿀 수 있으며 `%APPDATA%\PptSlider\shortcuts.json`에 저장됩니다. F1로 목록을 볼 수 있습니다.

### 기본 단축키

| 분류 | 명령 | 키 |
|---|---|---|
| 이동 | 다음 / 이전 슬라이드 | → ↓ PageDown Space Enter N / ← ↑ PageUp Backspace |
| 이동 | 첫 / 마지막 슬라이드 | Home / End |
| 판서 | 펜 / 형광펜 / 지우개 | P / H / E |
| 판서 | 색 1~7 | 1~7 (숫자 패드 포함) |
| 판서 | 가늘게 / 굵게 | `[` `-` / `]` `=` |
| 판서 | 현재 슬라이드 판서 지우기 | C |
| 쇼 | 처음부터 / 현재부터 | F5 / Shift+F5 |
| 쇼 | Break Time | T |
| 파일 | 열기 / 저장 / 다른 이름으로 저장 | Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| 편집 | 실행 취소 · 텍스트 편집 · 개체 삭제 | Ctrl+Z · F2 · Delete |
| 편집 | 굵게 / 기울임꼴 / 밑줄 | Ctrl+B / Ctrl+I / Ctrl+U |
| 편집 | 글꼴 크기 크게 / 작게 | Ctrl+Shift+. / Ctrl+Shift+, |
| 슬라이드 | 새 슬라이드 / 복제 | Ctrl+M / Ctrl+D |
| 도움말 | 단축키 목록 | F1 |

Esc는 고정 키입니다: 텍스트 편집 취소, 선택 도구로 돌아가기, 슬라이드 쇼 종료.

### 아직 그리지 않는 요소

차트, SmartArt, 효과(그림자 등), 기본값 외의 표 스타일, 애니메이션은 화면에 표시되지 않습니다. 파일에는 그대로 남아 있습니다.

## 빌드

필요: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
cd src
dotnet build -c Release
```

결과물은 저장소 루트의 `bin\Release\net10.0-windows\PptSlider.exe`에 생깁니다.
빌드 출력(`bin/`, `obj/`)은 `src/Directory.Build.props`에서 루트로 지정하며 Git에서 제외됩니다.

### 셀프 테스트

```powershell
PptSlider.exe --selftest deck.pptx outDir
```

모든 슬라이드를 PNG로 렌더링하고, 편집 → 저장(판서 포함/미포함) → 다시 열기 과정을 검사합니다.
결과는 `outDir\selftest.txt`에 기록되며 종료 코드 0이면 통과입니다.

## 폴더 구조

```
src/
├─ App.xaml(.cs)        진입점 (--selftest 처리, 시작 시 파일 열기)
├─ Views/               MainWindow(편집기), ShowWindow(슬라이드 쇼), BreakWindow(휴식 타이머), ShortcutsWindow(단축키 설정)
├─ Models/              Deck(.pptx 문서 · 편집 · 저장 · 실행 취소), PenState(판서 도구 상태)
├─ Rendering/           SlideRenderer(PPTX → WPF 렌더링)
├─ Services/            Shortcuts(단축키 매핑 · 저장), Monitors(모니터 목록)
└─ Diagnostics/         SelfTest
```

의존 패키지: [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml)
