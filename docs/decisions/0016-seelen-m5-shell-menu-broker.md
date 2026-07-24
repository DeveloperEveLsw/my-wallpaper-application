# ADR 0016: Seelen M5 transient Shell Menu Broker

- 상태: 구현됨, 자동 검증 통과
- 결정일: 2026-07-24
- 적용 범위: Seelen 제품 Companion과 Desktop 위젯의 M5
- Windows Seelen 실기기 검수: 대기

## 결정

Seelen 위젯 항목 메뉴의 다섯 번째 명령으로 `Windows 추가 옵션 표시`를 제공한다.
Windows Shell 메뉴는 WebView나 Seelen 소유 HWND에 주입하지 않고, 같은
`Wallpaper.Seelen.Companion.exe`를 일회성 STA broker 모드로 실행해 표시한다.

```text
Seelen Widget
  ├─ protocol 5 prepareShellMenu(itemId, physical point, widget HWND)
  ▼
Primary Companion
  ├─ current projection + FileCommandTargetValidator 재검증
  └─ 10초 수명, 1회 교환 ticket 발급
       │
       └──────── ticket only ────────┐
                                     ▼
SeelenCommand.RequestFocus + Run(--shell-menu-ticket ...)
                                     ▼
Transient STA Broker
  ├─ current-user named pipe에서 ticket 교환
  ├─ 실제 경로와 종류 수신
  ├─ 투명한 1×1 top-level owner HWND 생성
  └─ IContextMenu/IContextMenu2/3 + TrackPopupMenuEx
                                     ▼
Primary Companion completion + 전체 projection 재스캔
```

## 이유

- Shell context menu는 STA COM과 메뉴 메시지를 받을 top-level owner HWND가 필요하다.
- Seelen widget HWND는 다른 프로세스가 소유하므로 subclass하거나 Shell 메뉴 owner로
  재사용하지 않는다.
- transient broker는 메뉴 수명 동안 독립적인 foreground/top-level 경계를 제공하고
  기존 Windows Shell 구현을 그대로 재사용한다.
- 별도 설치 파일을 늘리지 않고 같은 Companion 실행 파일의 명시적 인자 모드만 추가한다.
- 위젯에 실제 파일 경로를 공개하지 않고 기존 projection ID 신뢰 경계를 유지한다.

## 프로토콜과 보안 경계

1. 위젯은 인증된 WebSocket으로 `itemId`, 물리 화면 좌표와 자신의 HWND를 보낸다.
2. Primary Companion은 현재 projection에서 항목을 다시 찾고 실제 파일 경계를 검증한다.
3. 응답에는 256-bit 무작위 ticket만 포함하며 파일 경로와 종류를 넣지 않는다.
4. ticket은 10초 안에 같은 Windows 사용자 전용 named pipe에서 한 번만 교환할 수 있다.
5. 한 번에 네이티브 메뉴 하나만 허용하며, 중복 준비는 같은 ticket을 돌려주고 다른
   요청은 `shell_menu_busy`로 거부한다.
6. 포커스 또는 프로세스 시작 실패 때 위젯이 pending ticket을 취소한다. broker가 pipe
   교환 전에 사라지면 ticket이 능동 만료된다.
7. broker 완료·연결 끊김·명령 실패 뒤 Primary Companion이 projection을 전체 재스캔하고
   완료 결과를 위젯에 보낸다.

## 입력과 포커스

- 메뉴 위치는 Glass 메뉴 버튼 위치가 아니라 최초 항목 우클릭의 물리 화면 좌표다.
- 위젯은 실행 직전에 `SeelenCommand.RequestFocus`를 호출한다.
- broker는 보이지 않는 top-level owner HWND로 `TrackPopupMenuEx`와 owner-drawn 메시지를
  소유한다.
- 메뉴 취소 또는 표시 실패 때만 Seelen widget HWND로 foreground를 복귀한다. Shell
  명령을 실행했으면 새로 열린 속성 창이나 애플리케이션의 포커스를 빼앗지 않는다.

## 범위

M5 종료 게이트는 파일과 실제 폴더의 `Windows 추가 옵션 표시`다. 가상 `루트` 카드는
실제 Shell 항목이 아니므로 제외한다. 순수 배경 우클릭은 위젯의 기존 cursor-event
투과를 통해 Explorer가 직접 처리하므로 Companion이 Desktop 배경 메뉴를 대신 열지
않는다. Windows 11 현대식 메뉴를 복제하지 않고 Windows가 반환한 클래식 Shell 메뉴를
직접 표시한다.

실기기 항목은
[Seelen M5 검수 기록](../m5-seelen-validation.md)을 따른다.
