# My Wallpaper Application

실제 Windows 파일과 폴더를 감성적인 라이브 월페이퍼 안에 정돈해 보여주는
WPF 기반 데스크톱 애플리케이션이다.

화면 하단에는 macOS Dock에서 영감을 받은 글래스 카드가 배치된다. 사용자가 지정한
루트 폴더의 직접 자식 폴더는 카드로 표시하고, 루트 바로 아래의 파일은 가상 `…`
카드에 모아 표시한다. 카드에서 수행한 파일 이동은 실제 파일 시스템에 반영된다.

## 현재 상태

- M6 Wallpaper Engine 로컬 Application Wallpaper 구현·Windows 실기기 검수 완료
- WebView2/three.js 렌더 경로 검증용 `Baseline` 기본 장면 구현
- WPF 애플리케이션이 제품 본체
- Wallpaper Engine 호스트 구현 완료, Lively 호스트는 후속 범위
- Wallpaper Engine은 로컬 Application Wallpaper로 사용하며 공개 Workshop 배포는 범위 밖
- 로컬 루트 얕은 snapshot, Dock, `…`, 단일 파일 모달과 숨은 설정 패널 구현
- Windows Shell 파일 아이콘, 비동기 이미지 썸네일 캐시와 대량 파일 가상화 구현
- `FileSystemWatcher` debounce·전체 재스캔과 루트 삭제·복구 처리 구현
- Dock 카드 drag 재정렬과 사용자 순서 저장 구현
- 파일 선택·더블클릭 실행과 파일·폴더 Glass 우클릭 메뉴 구현
- Windows 이름 검증을 거치는 파일·폴더 이름 변경 구현
- Glass 확인 모달과 `IFileOperation`을 이용한 Windows 휴지통 이동 구현
- `…` ↔ 폴더와 폴더 ↔ 폴더 파일 이동, drag preview와 유효·무효 대상 표시 구현
- 덮어쓰기 없는 충돌 이름 제안·편집 모달과 이동 뒤 전체 재스캔 복구 구현
- 실제 Windows 클래식 Shell 추가 옵션과 바탕화면 배경 메뉴 구현
- 추가 옵션은 최초 우클릭 위치에 네이티브 메뉴로 직접 표시
- Wallpaper Engine `-parentHWND` child 배치, host 상태와 렌더 pause/resume 구현
- Wallpaper Engine WorkerW 좌·우클릭 라우팅과 포인터 위치 기반 주 모니터 입력 격리 구현
- 앱 reload, Explorer worker 복구와 앱·엔진 강제 종료 입력 복원 watchdog 구현
- Wallpaper Engine 공식 제어 명령을 통한 Windows 원본 Desktop 아이콘 숨김·복원
- 다음 단계는 M7 성능·DPI·대량 항목·반복 복구 안정화

## 기준 문서

- [제품 명세](docs/product-spec.md)
- [기술 아키텍처](docs/architecture.md)
- [미결정 사항](docs/open-questions.md)
- [WPF 네이티브 런타임 결정](docs/decisions/0001-wpf-native-runtime.md)
- [네이티브 Shell 메뉴 HWND 결정](docs/decisions/0007-native-shell-menu-hosting.md)
- [클래식 Shell 메뉴 기본 표면 결정](docs/decisions/0009-classic-shell-context-menu.md)
- [Wallpaper Engine 호스트 수명주기 결정](docs/decisions/0010-wallpaper-engine-application-host.md)
- [WebView2/three.js Visual Gallery 결정](docs/decisions/0011-webview-three-visual-gallery.md)
- [MVP 개발 계획](docs/mvp-development-plan.md)
- [M5 검수 기록](docs/m5-validation.md)
- [M6 검수 기록](docs/m6-validation.md)

## 요구 환경

- .NET SDK 10.0.302 이상 10.0 feature band
- Node.js 22 이상(visualizer 스크립트 검사에만 필요, 앱 실행에는 불필요)
- WSL2 Ubuntu 24.04 또는 동등한 Linux 개발 환경
- 실제 UI 검증용 Windows 10/11

## 개발 명령

WSL:

```bash
./scripts/check.sh
```

Windows PowerShell:

```powershell
./scripts/check.ps1
$fixture = ./scripts/new-mvp-fixture.ps1
./scripts/run-standalone.ps1
```

M2 기능을 함께 검증할 때는 다음 fixture를 사용한다.

```powershell
$fixture = ./scripts/new-m2-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

M3 파일 명령은 실제 사용자 파일 대신 전용 fixture에서 검수한다.

```powershell
$fixture = ./scripts/new-m3-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

세부 항목은 [M3 검수 체크리스트](docs/m3-validation.md)를 따른다.

M4 내부 파일 이동은 전용 충돌·잠금 fixture에서 검수한다.

```powershell
$fixture = ./scripts/new-m4-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

세부 항목은 [M4 검수 체크리스트](docs/m4-validation.md)를 따른다.

M5 Windows Shell 메뉴와 입력 경계는 전용 fixture에서 검수한다.

```powershell
$fixture = ./scripts/new-m5-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

세부 결과와 항목은 [M5 검수 기록](docs/m5-validation.md)을 따른다.

M6 로컬 Wallpaper Engine package는 Wallpaper Engine이 설치된 Windows의 로컬 파일
시스템 checkout에서 배포하고 실행한다.

```powershell
# self-contained win-x64 package 배포
./scripts/deploy-wallpaper-engine.ps1

# 주 모니터에서 실행하고 Explorer 원본 아이콘 숨김
./scripts/run-wallpaper-engine.ps1 -DesktopIcons Hide

# 앱만 다시 로드
./scripts/run-wallpaper-engine.ps1 -Reload -DesktopIcons Hide

# Explorer Shell 재시작 뒤 worker 계층 복구
./scripts/run-wallpaper-engine.ps1 -RestartEngine -DesktopIcons Hide

# Explorer 원본 아이콘 복원
./scripts/run-wallpaper-engine.ps1 -DesktopIcons Show
```

Wallpaper Engine 경로를 자동 탐색하지 못하면 `-WallpaperEnginePath`를 지정한다. 앱은
실행 인자 `-parentHWND` 또는 `--host=wallpaper-engine`으로 호스트 모드를 선택하고,
Standalone 진단은 `--host=standalone` 또는 기존 `run-standalone.ps1`을 사용한다.
Windows PowerShell에서 WSL UNC 경로를 직접 publish 경로로 사용하지 않는다. 자세한
운영·실기기 결과는 [M6 검수 기록](docs/m6-validation.md)을 따른다.

첫 실행에서는 설정 패널이 자동으로 열린다. `new-mvp-fixture.ps1`이 출력한 경로를
`폴더 선택`에서 지정한다. 루트 설정 후에는 화면 우측 상단의 보이지 않는 영역을 1초간
hover하면 설정 패널을 다시 열 수 있다.

현재 vertical slice는 파일·폴더 열기, 탐색기 위치 열기, 이름 변경, 휴지통 이동,
카드 사이 내부 파일 drag & drop, 네이티브 클래식 추가 옵션과 실제 Desktop 뷰 배경
메뉴를 제공한다. 모든 변경과 Shell 메뉴 종료 뒤에는 실제 파일 시스템을 전체
재스캔한다.

## 개발·검증 흐름

1. WSL의 Linux 파일 시스템에서 소스 작성과 자동 테스트를 수행한다.
2. 작은 기능 단위로 커밋하고 GitHub에 푸시한다.
3. Windows 로컬 작업 폴더에서 pull한다.
4. Windows에서 WPF 실행, 호스트 통합, 실제 입력과 파일 시스템 동작을 검증한다.
5. 검증 결과를 문서와 회귀 테스트에 반영한다.

WPF는 Windows에서만 실행된다. WSL에서는 플랫폼 비종속 Core 테스트와 가능한 빌드
검사를 수행하고, 최종 실행 검증은 반드시 Windows에서 수행한다.
