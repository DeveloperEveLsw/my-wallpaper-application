# ADR 0007: 네이티브 Shell 메뉴의 HWND와 STA 소유권

- 상태: 채택
- 날짜: 2026-07-21

## 결정

이 결정의 HWND·STA·클래식 메뉴 호환성 부분은 유지한다. [ADR 0009](0009-classic-shell-context-menu.md)에
따라 아래 `TrackPopupMenuEx` 경로를 파일·폴더와 Desktop 배경 메뉴의 기본 표면으로 사용한다.

네이티브 파일·폴더 및 Desktop 배경 메뉴는 WPF 주 UI STA에서 실제 `MainWindow` HWND를
owner로 사용해 표시한다. Wallpaper Engine에서 이 HWND가 child이면 `GetAncestor(GA_ROOT)`로
찾은 top-level HWND를 foreground로 만든 뒤, owner-drawn 메시지는 원래 WPF child HWND로
전달한다. COM·PIDL·메뉴 interop은 `Wallpaper.Infrastructure.Windows`의 전용 서비스에
격리한다.

메뉴를 표시하는 동안에만 owner HWND에 Win32 subclass hook을 설치하고
`WM_INITMENUPOPUP`, `WM_DRAWITEM`, `WM_MEASUREITEM`, `WM_MENUCHAR`를 지원 가능한
`IContextMenu2` 또는 `IContextMenu3`로 전달한다. 메뉴 종료 시 hook, `HMENU`, PIDL과 COM
포인터를 `finally`에서 해제한다.

마우스로 연 메뉴는 `MouseRightButtonUp`에서 표시해 같은 클릭의 mouse-up이 새 메뉴에
전달되지 않게 한다. 키보드와 Glass 버튼 진입은 해당 입력 완료 이벤트에서 바로 표시한다.
`TrackPopupMenuEx`가 동작 중 WPF 입력을 modal하게 소유하며 종료 뒤 실제 파일 시스템을
다시 스캔한다.

## 이유

Shell 메뉴와 owner-drawn shell extension은 올바른 foreground HWND, 해당 HWND의 메시지
전달과 포커스 복귀가 필요하다. WPF 창과 다른 STA의 숨은 owner를 사용하면 Standalone과
향후 Wallpaper Engine 배치에서 메뉴 위치·활성화·포커스 소유권이 갈라질 수 있다.

실제 WPF HWND의 STA에서 일시적인 subclass를 사용하는 방식은 Windows 11 Standalone에서
파일·폴더 메뉴, 하위 메뉴, `속성` 명령, Desktop 배경 메뉴와 `Esc` 포커스 복귀로 검증했다.

## 결과

- 외부 shell extension은 메뉴가 열린 동안 애플리케이션 UI STA에서 실행될 수 있다.
- 실패는 Windows 전용 서비스에서 사용자 메시지로 변환하고 후속 재스캔으로 복구한다.
- 실제 환경에서 특정 shell extension의 중단·충돌이 확인되면 별도 helper process 격리를
  추가 검토한다. 확인되지 않은 위험 때문에 owner HWND를 숨은 스레드로 분리하지 않는다.
- Wallpaper Engine의 parent HWND, 단일 메뉴와 Desktop 포커스 복귀도 M6 통합 게이트에서
  검증했다.
