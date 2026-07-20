# 기술 아키텍처

- 상태: 초기 기준
- 원칙: WPF 애플리케이션 중심, 호스트 비종속, 실제 파일 시스템 우선

## 1. 구성 개요

```text
Wallpaper Engine / Lively / Standalone
                │
                │ 실행·배치·pause/resume·종료
                ▼
┌──────────────────────────────────────────────┐
│ WPF Application                             │
│ Dock · Modal · Context Menu · Drag & Drop   │
└──────────────────────┬───────────────────────┘
                       │ application commands
┌──────────────────────▼───────────────────────┐
│ Core                                         │
│ snapshot · move planning · validation        │
└──────────────────────┬───────────────────────┘
                       │ interfaces
┌──────────────────────▼───────────────────────┐
│ Windows Infrastructure                       │
│ FileSystemWatcher · Shell · Win32 · hosts    │
└──────────────────────┬───────────────────────┘
                       ▼
             Windows File System
```

### 1.1 렌더링 레이어

WPF 화면 내부의 합성 순서는 다음과 같다.

1. `Wallpaper Render Layer`: 미래의 영상, 픽셀 애니메이션, 2D/3D 오디오 시각화
2. `Desktop UI Layer`: Dock, 폴더 모달, 시계, 영상·음악 재생 정보 카드
3. `Transient Layer`: drag preview, context menu, 충돌·삭제 확인, 설정 패널

Desktop UI와 Transient Layer는 이벤트 기반 WPF visual tree로 유지한다. Wallpaper
Render Layer는 독립적인 렌더 수명과 FPS 정책을 갖고, 합성 가능한 surface 또는 WPF
visual로 결과만 제공한다.

미래 렌더러를 별도 child HWND인 `HwndHost`로 화면 아래에 삽입하는 것을 기본 구조로
삼지 않는다. `HwndHost`는 같은 top-level window의 WPF 요소 위에 표시되는 airspace
제약과 별도 입력 영역을 만들기 때문에 Dock·모달 overlay를 가릴 수 있다. DirectX를
도입할 때는 `D3DImage` 또는 당시 검증된 composition-compatible surface처럼 WPF
레이어와 합성할 수 있는 방식을 먼저 비교한다. 구체 2D/3D 엔진은 현재 확정하지 않는다.

오디오·미디어 수집 서비스는 렌더러와 정보 카드가 공유할 수 있는 immutable snapshot을
발행한다. renderer가 WPF ViewModel을 참조하거나 UI가 renderer의 내부 상태를 직접
수정하지 않는다.

## 2. 프로젝트 경계

초기 솔루션은 다음 책임으로 분리한다.

| 프로젝트 | 책임 |
|---|---|
| `Wallpaper.App` | WPF 화면, ViewModel, 입력, 애니메이션과 앱 생명주기 |
| `Wallpaper.Core` | 플랫폼 비종속 모델, snapshot, 이동 계획, 경계 정책 |
| `Wallpaper.Infrastructure.Windows` | 실제 파일 시스템, watcher, Windows Shell 및 Win32 연동 |
| `Wallpaper.Hosts` | Standalone, Lively, Wallpaper Engine 실행 계약 어댑터 |
| `Wallpaper.Rendering.Abstractions` | 미래 배경 렌더러의 수명·surface·pause 계약 |
| `Wallpaper.Core.Tests` | WSL에서도 실행 가능한 순수 로직 회귀 테스트 |
| `Wallpaper.Windows.Tests` | Windows 임시 폴더를 사용하는 통합 테스트 |

구체 프로젝트 수는 첫 vertical slice에서 불필요한 추상화가 확인되면 줄일 수 있지만,
Core가 WPF 및 특정 호스트를 참조하지 않는 의존 방향은 유지한다.

`Wallpaper.Rendering.Abstractions`는 현재 placeholder 구현만 둔다. 영상·오디오 분석,
2D/3D 엔진과 픽셀 애니메이션 구현은 파일 UI 목표가 완료될 때까지 시작하지 않는다.

## 3. 기술 기준

- C#과 WPF
- .NET 10 LTS
- SDK 스타일 프로젝트와 nullable reference types 활성화
- MVVM 기반 상태·명령 분리
- Windows API는 별도 infrastructure 경계에 격리
- 단위 테스트는 플랫폼 비종속 Core에 집중
- Windows 전용 동작은 임시 디렉터리와 실제 HWND를 사용하는 통합 테스트로 검증

MVP는 주 모니터 하나와 일반 로컬 디렉터리만 지원한다. 네트워크·UNC·OneDrive 및
다른 클라우드 동기화 루트는 지원·검증 범위에서 제외한다. 디렉터리 심볼릭 링크와
재분석 지점은 경계 우회 가능성이 있으므로 따라가지 않는다.

.NET 10은 현재 활성 지원 중인 LTS이며 2028-11-14까지 지원된다. WPF는 .NET에 포함된
Windows 전용 UI 프레임워크이므로 실행 검증은 Windows에서 수행한다.

## 4. 호스트 경계

WPF 프로세스가 제품의 상태와 파일 기능을 소유한다. Wallpaper Engine과 Lively는
다음 정보만 전달하거나 제어한다.

- 시작과 종료
- 대상 모니터 또는 배치 영역
- pause/resume
- 선택적인 루트·프리셋 인자
- 호스트가 제공하는 경우 성능 또는 오디오 상태

호스트별 설정 형식, IPC와 창 재배치 방식은 어댑터 내부에 둔다. ViewModel, 파일 시스템
서비스와 Core 모델은 실행 호스트의 존재를 알지 않는다. 호스트가 없어도 Standalone
모드에서 동일한 UI와 파일 동작을 검증할 수 있어야 한다.

루트 설정은 WPF 애플리케이션이 소유하고 앱 로컬 설정에 저장한다. 변경은 WPF 내부
설정 패널과 네이티브 폴더 선택기로만 수행한다. Wallpaper Engine/Lively 속성, 시스템
트레이 또는 호스트 내부 파일은 설정 경로로 사용하지 않는다.

Wallpaper Engine 운용 시 Windows Explorer 원본 바탕화면 아이콘 숨김은 Wallpaper
Engine 자체 기능을 사용한다. WPF 애플리케이션은 Explorer 아이콘 표시 상태를 직접
변경하지 않는다.

호스트 구현 순서는 Standalone, Wallpaper Engine 로컬 Application Wallpaper, Lively다.
Wallpaper Engine은 공개 Workshop 배포가 아니라 개인 로컬 실행만 대상으로 한다.

우측 상단 숨은 설정 진입 영역은 WPF hit testing 계층의 별도 영역이다. 포인터 dwell은
1초짜리 취소 가능한 타이머로 구현하고, 우클릭은 설정 동작으로 소비하지 않고 Desktop
배경 메뉴 경계로 전달한다. 유효한 루트가 없는 최초 실행과 설정 손상 상태에서는 설정
패널을 자동 표시해 숨은 진입점 때문에 복구할 수 없는 상태가 생기지 않게 한다.

## 5. 파일 작업 모델

화면 항목은 스캔 시점의 실제 파일 시스템 엔트리를 나타낸다. UI가 오래된 엔트리에
명령을 내릴 수 있으므로 작업 직전에 다음을 다시 검증한다.

1. 항목이 여전히 존재하는가.
2. 정규화된 실제 경로가 현재 루트 경계 안에 있는가.
3. 지원하는 깊이와 항목 종류인가.
4. 대상 폴더가 현재 루트의 허용된 직접 자식인가.
5. 같은 이름 충돌이나 재분석 지점 우회가 없는가.

파일 이동은 임시 UI 상태로 진행 중임을 표시할 수 있지만, 완료 상태는 실제 이동과
후속 스캔이 성공한 뒤에만 반영한다.

휴지통 이동은 Windows Shell `IFileOperation`과 `FOFX_RECYCLEONDELETE`를 사용하는
Windows 전용 서비스로 구현한다. 영구 삭제로 폴백하지 않는다.

`Windows 추가 옵션 표시`는 검증된 항목의 Shell object에서 `IContextMenu`를 얻어 실제
명령·상태·아이콘·하위 메뉴를 세션으로 열거한다. 앱은 이 명령을 .NET WPF의 Windows 11
Fluent `ContextMenu`로 표시하고 선택된 command id를 원래 `IContextMenu.InvokeCommand`로
실행한다. `더 많은 옵션 표시`를 선택한 경우에만 `TrackPopupMenuEx` 클래식 메뉴로
전환한다. 실제 WPF HWND와 같은 UI STA를 owner로 사용하고 클래식 메뉴가 열린 동안에만
owner-drawn 메시지를 `IContextMenu2/3`로 전달한다. 외부 shell extension은 앱 내부
명령과 다른 신뢰 경계이므로 실패를 사용자 오류로 변환하고, 메뉴 종료 후 항상 관련
디렉터리를 다시 스캔한다. 세부 결정은 [ADR 0007](decisions/0007-native-shell-menu-hosting.md)과
[ADR 0008](decisions/0008-modern-shell-command-surface.md)을 따른다.

순수 월페이퍼 배경 우클릭은 Desktop `IShellFolder`의 view object에서 배경
`IContextMenu`를 요청하고 같은 Fluent 명령 표면으로 표시한다. 항목 메뉴는 최초
`MouseRightButtonDown`의 물리 화면 좌표를 보존하므로 Glass 메뉴 안의 버튼을 누르는 동안
커서가 이동해도 Shell 메뉴 기준점은 바뀌지 않는다. Wallpaper Engine이 호스트인 상태의
중복 메뉴·포커스·parent HWND는 M6 첫 통합 게이트에서 검증한다.

WPF hit testing은 파일·폴더 항목, Dock·모달 패널과 순수 월페이퍼 배경을 명시적으로
구분한다. 항목 우클릭은 앱이 소유하고, 순수 배경 우클릭만 Windows Desktop 메뉴 경계로
전달한다.

## 6. WSL과 Windows 역할

WSL:

- 코드 작성, 검색, 정적 분석
- Core 빌드와 단위 테스트
- Git commit과 push
- Windows 전용 코드의 가능한 범위까지 컴파일 검사

Windows:

- WPF 실제 실행
- DPI, 다중 모니터, 포커스와 입력 검증
- Wallpaper Engine/Lively 프로세스 생명주기 검증
- 임시 루트 파일 이동과 watcher 통합 검증
- Glass/Blur 렌더링 및 성능 확인

## 7. 검증 순서

1. Standalone 창에서 고정 snapshot 렌더링
2. Windows 임시 루트 read-only snapshot
3. watcher와 전체 재스캔
4. 임시 루트 내부 파일 이동
5. 커스텀 우클릭과 입력 소유권
6. Wallpaper Engine 로컬 Application Wallpaper 호스트
7. Lively 호스트
8. 사용자가 명시적으로 선택한 실제 루트

주 모니터 외 배치, 모니터별 상태와 OneDrive placeholder는 MVP 이후 별도 capability
검증을 거친 뒤 지원한다.

실제 사용자 파일을 변경하는 검증은 임시 루트 자동 테스트를 통과한 뒤 수동 카나리
파일로만 진행한다.

## 8. 공식 근거

- [.NET 공식 지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Microsoft WPF 개요](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [Lively 공식 저장소](https://github.com/rocksdanister/lively)
- [Wallpaper Engine 공식 CLI](https://help.wallpaperengine.io/en/functionality/cli.html)
