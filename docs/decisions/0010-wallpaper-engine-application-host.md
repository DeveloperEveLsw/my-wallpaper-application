# ADR 0010: Wallpaper Engine Application Wallpaper 호스트 수명주기

- 상태: 채택
- 날짜: 2026-07-22

## 결정

Wallpaper Engine 통합은 개인 로컬 `Application` wallpaper package로 제공한다. WPF 앱은
Wallpaper Engine이 전달한 `-parentHWND <decimal HWND>`를 읽고 `SourceInitialized`에서
얻은 실제 WPF HWND를 해당 parent의 child로 배치한다. host adapter가 child/frame style,
`SetParent`, client rect 크기와 Desktop WorkerW 연결 상태를 소유한다.

Wallpaper Engine 2.8.42의 WorkerW가 disabled 및 `WS_EX_TRANSPARENT` 상태이면 표시된 WPF
자식보다 Explorer `SHELLDLL_DefView`가 좌·우클릭을 받는다. 검증된
`Progman → WorkerW → WPEAppIntermediateWorker` 계층에 한해 WorkerW를 활성화하고 투과
style을 제거한다. 다만 WorkerW는 여러 모니터의 wallpaper child가 공유하므로
`SetWindowRgn`으로 자르지 않는다. 포인터 위치 확인과 WorkerW 입력 전환은 월페이퍼
프로세스 내부의 직렬 host poll에서만 수행한다. suspend될 수 있는 월페이퍼 프로세스에는
시스템 전역 저수준 마우스 hook을 설치하지 않는다. 비-suspend watchdog도 앱이 살아 있는
동안 Wallpaper Engine 소유 HWND를 변경하지 않는다. host dispose 시 WorkerW를 다시
비활성화하고 투과 style, 무영역 상태와 Desktop view의 z-order를 복원한다. 주기 polling은
style, parent, rect 또는 WorkerW 입력 상태가 실제로 달라졌을 때만 `SetWindowPos`를 실행해
메뉴와 drag 도중의 native 입력 상태를 흔들지 않는다.

카드 재정렬과 카드 간 파일 이동은 제품 내부 기능이므로 OLE `DoDragDrop`을 사용하지 않는다.
전체 WPF 입력 surface에서 mouse-down, 임계 거리 이동, target hit-test와 mouse-up을 추적하는
내부 drag 상태 머신을 사용한다. 이렇게 하면 foreground top-level HWND가 아닌 Wallpaper
Engine child 창에서도 외부 mouse capture 없이 동일하게 동작한다.

호스트 상태는 `Starting`, `WaitingForParent`, `Active`, `Paused`, `Recovering`, `Faulted`,
`Stopped`로 표현한다. parent가 표시되지 않으면 렌더 수명을 pause하고, parent 또는
Explorer WorkerW 연결이 사라지면 `Recovering`으로 전환한다. WPF HWND가 파괴되거나
Wallpaper Engine이 grace period를 넘어 사라지면 앱 종료를 요청한다.

Wallpaper Engine pause가 앱 프로세스 자체를 OS suspend할 수 있으므로, 실제 호스트
실행에서는 같은 실행 파일의 별도 watchdog 모드를 분리된 프로세스로 시작한다. watchdog은
앱·엔진 PID만 관찰하며 앱이 살아 있는 동안 parent/WorkerW HWND를 변경하지 않는다. 앱 또는
엔진이 먼저 종료되면 새 wallpaper child가 시작 시 검증한 WorkerW를 이미 사용 중인지
확인하고, 그렇지 않을 때 입력 상태를 복원한다. 엔진 종료 시에는 앱 PID도 종료한다. 정상
종료와 reload 스크립트는 watchdog을 포함해 정확히 같은 배포 실행 파일 경로의 프로세스만
종료한다.

Explorer 재시작으로 Wallpaper Engine의 중간 worker가 새 Desktop WorkerW와 재연결되지
않는 경우에는 앱이 단독으로 Explorer 창 계층을 조작하지 않는다. 운용 스크립트의
`-RestartEngine`이 확인된 Wallpaper Engine과 해당 배포 앱을 재시작하고 public
`openWallpaper` 명령으로 package를 다시 연다.

원본 Desktop 아이콘 표시 상태는 Wallpaper Engine 공식 `hideIcons`/`showIcons` 제어
명령이 소유한다. WPF 앱은 Explorer 아이콘 HWND를 직접 숨기거나 복원하지 않는다.

## 이유

Wallpaper Engine의 실제 Application Wallpaper 계약은 일반 top-level WPF 창이 아니라
Wallpaper Engine 소유의 `WPEAppIntermediateWorker` 아래에 application HWND를 두는
구조다. 실행 인자를 정본으로 사용하고 child 배치를 host adapter에 격리하면 ViewModel과
Core가 Wallpaper Engine 창 구조를 알 필요가 없다.

앱 내부 timer만으로 종료를 관찰하면 앱이 suspend된 동안 callback도 멈춰 엔진 종료 후
고아 프로세스가 남는다. 엔진과 함께 suspend되지 않는 최소 watchdog이 이 수명주기
간극을 닫는다. 반대로 앱이 Explorer WorkerW를 임의로 만들거나 재부모화하면 Wallpaper
Engine과 Explorer의 소유권을 침범하므로, 끊어진 계층은 명시적으로 감지하고 제한된
엔진 재시작 경로로 복구한다.

## 결과

- Standalone과 Wallpaper Engine은 동일한 `IWallpaperHost` 경계를 사용한다.
- 설정 패널은 host snapshot을 표시하지만 host별 Win32 구현을 참조하지 않는다.
- visibility는 미래 렌더러의 pause/resume 계약으로 전달된다.
- 정상 reload와 Explorer 복구가 서로 다른 명시적 운영 명령으로 구분된다.
- Application Wallpaper가 OS suspend 상태여도 Wallpaper Engine 종료 뒤 고아 앱을 남기지 않는다.
- 정상·강제 앱 종료와 엔진 종료 뒤 Explorer Desktop 입력 소유권을 복원한다.
- Wallpaper Engine이 앱 프로세스를 suspend해도 시스템 마우스 hook 대기가 발생하지 않는다.
- 비-suspend watchdog이 WorkerW를 변경하지 않아 suspend된 앱과 Wallpaper Engine 사이의
  `AppHangXProcB1` 교차 프로세스 대기를 만들지 않는다.
- 포인터가 대상 parent 밖에 있으면 공유 WorkerW가 입력 투과 상태로 돌아간다.
- 보조 모니터의 wallpaper child는 공유 WorkerW 영역에 의해 시각적으로 잘리지 않는다.
- 원본 Desktop 아이콘 상태의 소유권은 Wallpaper Engine에 유지된다.
- 보조 모니터와 Lively는 별도 capability 검증 전까지 이 계약에 포함하지 않는다.

## 검증과 공식 근거

- [M6 검수 기록](../m6-validation.md)
- [Wallpaper Engine command line controls](https://help.wallpaperengine.io/en/functionality/cli.html)
