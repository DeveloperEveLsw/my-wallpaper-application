# ADR 0012: Wallpaper Engine 우선 프레젠테이션과 개발 창

- 상태: 채택
- 날짜: 2026-07-24
- 대체 범위: ADR 0001의 다중 호스트·Standalone 정책, ADR 0010의 사후 `SetParent` 정책

## 결정

Wallpaper Engine을 유일한 제품 실행 호스트로 사용한다. 제품 프로세스는
`-parentHWND <decimal HWND>` 없이 암묵적으로 일반 창으로 실행되지 않는다.

Wallpaper Engine 경로에서는 일반 WPF `Window`를 먼저 만든 뒤 재배치하지 않는다.
전달받은 parent HWND를 `HwndSourceParameters.ParentWindow`에 지정해 `WS_CHILD` WPF HWND를
처음부터 만들고, 그 `RootVisual`에 공용 `WallpaperView`를 넣는다. host adapter는 이 HWND가
전달된 parent의 자식인지 검증하고 parent client rect에 맞추기만 한다.

WebView2는 `WebView2CompositionControl`로 공용 WPF visual tree 안에 존재한다. WebView2
내부 `Chrome_WidgetWin_0`을 열거하거나 `SetParent`하지 않는다. 따라서 배경 렌더러와
폴더 UI의 z-order 및 hit testing은 WPF가 단일 visual tree에서 소유한다.

로컬 디버깅에는 `--dev-window`를 명시해야 한다. 이 경로는 별도 제품 호스트가 아니라 같은
`WallpaperView`를 일반 WPF `Window`에 넣는 얇은 프레젠테이션이다. 파일·설정·렌더링·입력
코드는 두 프레젠테이션이 공유한다.

## 이유

top-level WPF 창을 사후에 다른 프로세스의 child로 바꾸면 WPF와 WebView2가 생성한 내부
HWND의 소유·z-order 관계가 갈라질 수 있다. 내부 WebView HWND를 다시 부모 변경하는 보정은
WPF hit testing을 우회해 WebView가 폴더 UI 위에서 클릭을 가로채는 문제를 만든다.

제품 경로와 개발 경로를 host mode로 동등하게 취급하면 실제 배포 조건이 없는 실행도
정상 동작처럼 보이게 한다. 반대로 제품 경로는 parent HWND를 필수 계약으로 두고 개발
창만 명시적으로 열면 실패가 빠르고 재현 조건이 분명하다.

## 결과

- Wallpaper Engine parent 아래에는 생성 시점부터 올바른 부모를 가진 WPF HWND 하나만 둔다.
- WebView2 내부 HWND를 탐색·재배치하는 코드와 renderer-ready 보정 이벤트가 사라진다.
- 제품 실행은 `-parentHWND`, 개발 실행은 `--dev-window`라는 두 개의 명시적 진입만 갖는다.
- 공용 `WallpaperView` 덕분에 개발 창에서도 제품 UI와 기능을 대부분 빠르게 검증할 수 있다.
- parent/WorkerW 입력 초기화, visibility 기반 pause/resume, watchdog 복구는 기존 계약을
  유지한다.
