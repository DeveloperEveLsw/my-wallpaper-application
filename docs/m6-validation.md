# M6 검수 기록

- 구현 상태: 완료
- 자동 검증: 통과
- 실제 Wallpaper Engine 통합 검수: 완료
- 검수 환경: Windows 11 Pro 10.0.26200, Wallpaper Engine 2.8.42
- 배포 형식: 로컬 비공개 Application Wallpaper, self-contained `win-x64`
- 기준일: 2026-07-23

## 완료 판정

M6의 첫 통합 게이트와 호스트 수명주기를 실제 Wallpaper Engine에서 통과했다. WPF 창은
Wallpaper Engine이 `-parentHWND`로 전달한 중간 worker HWND 안에 자식 창으로 배치되고,
호스트의 표시 상태에 따라 렌더 수명이 pause/resume 된다. Wallpaper Engine 2.8.42가
비활성·입력 투과 상태로 만든 WorkerW는 표시 중인 주 모니터 영역에서만 상호작용하도록
보정했다. 공유 WorkerW 자체의 window region은 제한하지 않아 보조 모니터 wallpaper를
함께 자르지 않는다. 일반 reload, Explorer 재시작 뒤의 복구, 앱·Wallpaper Engine 강제 종료 뒤
입력 복원과 원본 Desktop 아이콘 숨김도 각각 검증했다.

| M6 요구사항 | 결과 | 근거 |
|---|---|---|
| 우클릭 중복 | 통과 | 순수 배경에서 Windows 11 Desktop 메뉴가 논리적으로 하나만 표시되고 `Esc` 뒤 모든 팝업 surface가 사라짐 |
| 포커스 복귀 | 통과 | 메뉴 취소 뒤 foreground HWND가 Explorer Desktop `Progman`으로 복귀 |
| parent HWND 배치 | 통과 | WPF `HwndWrapper`의 parent가 전달된 `WPEAppIntermediateWorker`이고 client rect 전체를 채움 |
| 로컬 Application Wallpaper package | 통과 | private `project.json`과 self-contained WPF publish 산출물을 `projects\myprojects`에 배포 |
| WPF HWND 탐지·배치·종료 | 통과 | 실제 HWND attach, child style·크기 보정, WPF HWND 파괴 또는 호스트 종료 시 앱 종료 |
| 실제 좌·우클릭 입력 | 통과 | 폴더 좌클릭 모달, 항목 Glass 우클릭 메뉴, 빈 배경 Windows 메뉴가 모두 WPF 경계에서 열림 |
| 입력 범위 격리 | 자동 검증 통과·실기기 재검증 필요 | 포인터가 주 모니터 parent rect 안에 있을 때만 공유 WorkerW 입력 활성화 |
| 보조 모니터 렌더 보존 | 수정 완료·실기기 재검증 필요 | 공유 WorkerW region 제한 제거로 다른 wallpaper child의 렌더 영역 보존 |
| pause/resume | 통과 | Wallpaper Engine pause 동안 앱 프로세스가 OS suspend되고 resume 뒤 정상 재개 |
| reload | 통과 | 배포된 실행 파일 경로와 `-parentHWND` 실행 인스턴스만 종료한 뒤 새 HWND로 재실행 |
| Explorer 재시작 복구 | 통과 | 끊어진 WorkerW 계층을 `Recovering`으로 감지하고 엔진 재시작 복구 명령으로 새 WorkerW에 재배치 |
| 비정상 종료 입력 복원 | 통과 | 앱 및 엔진 강제 종료 뒤 WorkerW 비활성화, `WS_EX_TRANSPARENT`·무영역·Explorer 입력 소유권 복원 |
| 원본 Desktop 아이콘 숨김 | 통과 | WPF가 Explorer 상태를 건드리지 않고 Wallpaper Engine의 `hideIcons` 제어 명령만 사용 |

## 실제 호스트 계약

Wallpaper Engine 2.8.42는 Application Wallpaper 실행 파일을 다음 형태로 시작했다.

```text
Wallpaper.App.exe -WINDOWED -parentHWND <decimal HWND>
```

관측하고 검증한 창·프로세스 관계는 다음과 같다.

```text
Explorer Progman
├─ WorkerW                               전체 가상 Desktop 렌더 영역 유지
│  └─ WPEAppIntermediateWorker           wallpaper64.exe 소유
│     └─ HwndWrapper[Wallpaper.App;…]    Wallpaper.App.exe 소유
└─ SHELLDLL_DefView                      Explorer Desktop 입력 표면
```

`HostLaunchOptions`는 대소문자를 구분하지 않고 `-parentHWND`의 10진수 값을 읽는다.
`WallpaperEngineHost`는 WPF `SourceInitialized` 뒤 실제 `HwndSource`를 attach하고, 전달된
parent HWND를 우선 사용한다. WPF top-level HWND에는 `WS_CHILD`를 적용하고 frame style을
제거한 뒤 `SetParent`와 parent client rect 크기 보정을 수행한다. parent가 Explorer의
Desktop/WorkerW 계층에서 끊어지면 화면을 계속 활성 상태로 오판하지 않고
`Recovering`으로 전환한다.

기본 WorkerW는 disabled이고 `WS_EX_TRANSPARENT`이어서 WPF가 보여도 모든 클릭이
`SHELLDLL_DefView`로 향했다. 호스트는 검증한 `Progman → WorkerW →
WPEAppIntermediateWorker` 계층에서만 WorkerW를 활성화하고 입력 투과 style을 제거한다.
저수준 마우스 입력 라우터는 포인터가 전달된 parent rect 안에 있을 때만 이 상태를
유지하고, 다른 모니터로 이동하면 WorkerW를 다시 disabled·입력 투과 상태로 만든다.
WorkerW window region은 설정하지 않으므로 같은 WorkerW의 다른 모니터 wallpaper child는
계속 렌더링된다. 정상 dispose와 별도 watchdog은 이 변경을 역순으로 복원한다.

세부 수명주기 결정은 [ADR 0010](decisions/0010-wallpaper-engine-application-host.md)을
따른다.

## 자동 검증

Release 전체 검사에서 다음을 확인했다.

- 전체 빌드 경고 0, 오류 0
- Core 테스트 42개 통과
- Windows Infrastructure 테스트 28개 통과
- Host 테스트 29개 통과
- 합계 99개 테스트 통과
- Standalone/Wallpaper Engine 선택, 환경 변수와 실제 실행 인자 parsing
- 유효하지 않은 parent HWND 거부와 실제 전달 parent 우선 사용
- 표시/비표시 전환에 따른 렌더 pause/resume
- parent 소실·재연결, Desktop 계층 단절과 WPF HWND 파괴 처리
- 입력 라우팅 적용·정상 dispose 복원 호출
- watchdog의 parent/WorkerW HWND 인자 검증과 호스트 종료 후 앱·Desktop 입력 복원

## Windows 실기기 결과

### 배치와 표시

- WPF HWND parent와 `-parentHWND` 값이 일치했다.
- WPF child rect는 parent client 기준 `(0, 0, 1920, 1080)`을 채웠다.
- taskbar 항목이나 활성화 가능한 일반 top-level 창을 만들지 않고 월페이퍼로 표시됐다.

### 좌클릭·우클릭과 모니터 경계

- 주 모니터의 폴더 카드 좌클릭으로 `2026_py` 모달과 파일 6개가 표시됐다.
- 같은 카드 우클릭으로 `ItemWindowsOptionsMenu`를 포함한 Glass 항목 메뉴가 표시됐다.
- 빈 배경 우클릭으로 앱 프로세스 소유의 Windows 네이티브 메뉴 창(`#32768`)이 하나
  표시됐다.
- 주 모니터 세 지점의 `WindowFromPoint`는 WPF HWND를 반환했다. 음수 좌표의 보조
  모니터 지점은 WPF/WorkerW가 아니라 해당 모니터의 기존 Chrome 창을 반환했다.
- 당시 WorkerW region을 주 모니터 rect로 제한한 것은 입력 격리만 확인했으나, 이후 같은
  WorkerW를 공유하는 보조 모니터 wallpaper 렌더까지 잘라 회색 화면을 만드는 회귀로 확인됐다.

### pause/resume과 reload

- 재생 중 표본 CPU delta는 62.5 ms, pause 중은 0 ms였다.
- pause 중 23/23 표본에서 앱이 OS suspended 상태였고, resume 뒤 0/표본으로 복귀했다.
- reload 전 앱 PID가 종료되고 새 PID와 새 parent HWND 인자로 다시 실행되는 것을 확인했다.

### Explorer와 엔진 종료 복구

Explorer Shell을 강제 재시작하면 기존 `WPEAppIntermediateWorker`와 새 WorkerW의 연결이
끊어지는 Wallpaper Engine 2.8.42 동작을 관측했다. 앱은 이 상태를 `Recovering`으로
표시하고 렌더를 pause한다. `-RestartEngine` 복구 명령은 이 프로젝트의 정확한 배포
실행 파일과 확인된 `wallpaper64.exe`만 종료한 뒤 엔진과 wallpaper를 다시 열어 새
WorkerW 계층을 만들며, 복구 뒤 WPF 화면·입력·배치를 다시 확인했다.

Wallpaper Engine은 pause 시 Application Wallpaper 프로세스 자체를 OS suspend한다.
따라서 앱 내부 timer만으로는 엔진 종료를 감지할 수 없다. 실제 앱은 짧게 분리된
watchdog 프로세스를 함께 시작한다. watchdog은 suspend되지 않은 채 엔진 PID와 시작
시점의 parent/WorkerW HWND를 관찰하고, 엔진이 종료되면 정확한 앱 PID만 종료한 뒤 입력
상태를 복원한다. 앱 강제 종료와 엔진 강제 종료를 각각 시험했을 때 WorkerW는 disabled,
extended style `0x80000A0`, window region 없음으로 돌아갔고 `WindowFromPoint`는 다시
Explorer `SHELLDLL_DefView`를 반환했다. 기존 suspend 시험에서도 앱 11/11 thread는
suspended, watchdog 0/14 thread는 suspended 상태였으며 엔진 종료 뒤 둘 다 8초 제한보다
일찍 종료됐다.

### 우클릭, 포커스와 아이콘

- 빈 WPF 배경 우클릭에서 `보기`, `정렬 기준`, `새로 고침`, `새로 만들기`,
  `디스플레이 설정`, `개인 설정`, `터미널`, `더 많은 옵션 표시`가 포함된 Desktop 메뉴
  하나를 확인했다.
- Windows 11 XAML 메뉴는 두 개의 `PopupWindowSiteBridge` surface를 사용했지만 한 개의
  논리 메뉴였으며 `Esc` 뒤 둘 다 닫혔다.
- foreground HWND는 메뉴 전 `Progman`, 메뉴 중 Explorer XAML host, 메뉴 취소 뒤 다시
  `Progman`으로 복귀했다.
- Wallpaper Engine 공식 CLI의 `hideIcons`로 원본 Explorer 아이콘을 숨긴 화면을
  확인했다. WPF 코드는 `Progman`/`SysListView32` 표시 상태를 직접 변경하지 않는다.

## 배포와 운용

아래 명령은 Wallpaper Engine이 설치된 Windows의 로컬 파일 시스템 checkout에서
PowerShell로 실행한다. WPF publish의 Windows SDK 경로 해석 때문에 WSL UNC 경로를
Windows PowerShell의 작업 폴더로 사용하지 않는다.

```powershell
# self-contained win-x64 로컬 패키지 배포
./scripts/deploy-wallpaper-engine.ps1

# 주 모니터에서 열고 원본 Desktop 아이콘 숨김
./scripts/run-wallpaper-engine.ps1 -DesktopIcons Hide

# 현재 M6 앱만 교체 실행
./scripts/run-wallpaper-engine.ps1 -Reload -DesktopIcons Hide

# Explorer Shell 재시작 뒤 worker 계층 복구
./scripts/run-wallpaper-engine.ps1 -RestartEngine -DesktopIcons Hide

# 원본 Desktop 아이콘 다시 표시
./scripts/run-wallpaper-engine.ps1 -DesktopIcons Show
```

다른 Steam library에 설치된 경우 `-WallpaperEnginePath`를 지정할 수 있다. 배포 스크립트는
Steam registry, `libraryfolders.vdf`와 고정 드라이브의 일반 설치 위치도 탐색한다. 삭제와
종료 대상은 검증된 project 이름 아래의 정확한 앱 경로로 제한한다.

## 범위 경계

- M6 기능 범위는 주 모니터 하나지만, 보조 모니터의 Wallpaper Engine 렌더를 방해하지 않는
  것은 필수 비회귀 조건이다.
- 포인터 위치 기반 입력 전환과 보조 모니터 wallpaper 렌더 보존은 Windows 실기기에서
  재검증해야 한다.
- package는 개인 로컬용이며 공개 Workshop 배포를 지원하지 않는다.
- Lively 호스트는 후속 범위다.
- 영상·픽셀·오디오 반응형 배경 렌더러는 파일 UI 안정화 뒤 M8에서 다룬다.

## 공식 근거

- [Wallpaper Engine command line controls](https://help.wallpaperengine.io/en/functionality/cli.html)
