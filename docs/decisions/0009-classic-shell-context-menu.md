# ADR 0009: 네이티브 클래식 Shell 메뉴를 기본 표면으로 사용

- 상태: 채택
- 날짜: 2026-07-21

## 결정

파일·폴더의 `Windows 추가 옵션 표시`와 순수 월페이퍼 배경 우클릭은 Windows가 만든
클래식 `HMENU`를 `TrackPopupMenuEx`로 직접 표시한다. Windows 11 현대식 메뉴를 WPF로
재현하지 않으며 별도의 `더 많은 옵션 표시` 중간 단계도 두지 않는다.

파일·폴더 메뉴는 부모 `IShellFolder.GetUIObjectOf(IContextMenu)`에서 얻는다. Desktop
배경 메뉴는 `IShellWindows.FindWindowSW(SWC_DESKTOP)`에서 `SID_STopLevelBrowser`와
`IShellBrowser.QueryActiveShellView`를 거쳐 Explorer의 현재 Desktop `IShellView`를
얻은 뒤 `GetItemObject(SVGIO_BACKGROUND, IContextMenu)`로 요청한다. Explorer Desktop
뷰를 사용할 수 없을 때만 Desktop `IShellFolder.CreateViewObject(IShellView)`로 만든
기본 뷰에 폴백한다. 따라서 NVIDIA 제어판처럼 시스템에 등록된 배경 Shell 확장을 앱이
하드코딩하지 않고 Windows가 메뉴에 합성한다.

메뉴는 WPF 주 UI STA와 실제 `MainWindow` HWND를 owner로 사용한다. 최초 우클릭의 물리
화면 좌표를 유지하고, `IContextMenu2/3`의 owner-drawn 메시지 전달, 선택 명령
`IContextMenu.InvokeCommand`, 모든 종료 경로의 네이티브 자원 해제와 전체 재스캔을
유지한다.

## 이유

Explorer의 Windows 11 XAML 메뉴 전체를 외부 WPF 앱에 호스팅하는 공개 API가 없다.
Shell 명령을 WPF 메뉴로 옮겨 그 외형을 재현하면 owner-drawn 항목과 외부 확장의 동작을
완전히 보존할 수 없다. 실제 클래식 메뉴를 직접 표시하면 Windows가 제공하는 메뉴 구성,
상태, 하위 메뉴, 아이콘과 확장 호환성을 그대로 사용한다.

Desktop 폴더 자체의 `IContextMenu`, 새로 만든 기본 뷰의 메뉴와 Explorer가 현재 호스팅
중인 Desktop 뷰 배경 메뉴는 같은 결과를 보장하지 않는다. `_SVGIO` 문서는
`SVGIO_BACKGROUND`와 `IID_IContextMenu`의 조합을 뷰 배경 바로 가기 메뉴를 얻는 용도로
명시한다. 실제 Windows 11 검수에서 기본 뷰에는 없던 NVIDIA 제어판 항목이 Explorer의
현재 Desktop 뷰 메뉴에는 포함되는 것을 확인했으므로 Explorer와 동등한 확장 공급 범위를
위해 활성 뷰의 배경 계약을 우선 사용한다.

## 결과

- WPF 현대식 메뉴 presenter와 Shell 명령 열거 모델을 제거한다.
- 모든 Shell 메뉴는 한 번의 동작으로 네이티브 클래식 표면에 열린다.
- 등록된 외부 배경 확장은 Windows의 `IContextMenu` 구현을 통해 제공된다.
- Windows 10/11에서 동일한 Win32 메뉴 호스팅 경로를 사용한다.
- HWND·STA·포커스와 owner-drawn 메시지 정책은 [ADR 0007](0007-native-shell-menu-hosting.md)을 따른다.

## 공식 API 근거

- [IShellView::GetItemObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-getitemobject)
- [SVGIO_BACKGROUND](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)
- [IShellBrowser::QueryActiveShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-queryactiveshellview)
- [IContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icontextmenu)
- [TrackPopupMenuEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenuex)
