# ADR 0008: Windows 11 현대식 Shell 명령 표면

- 상태: 채택
- 날짜: 2026-07-21

## 결정

파일·폴더와 Desktop `IContextMenu`가 제공하는 실제 명령을 UI STA 세션에서 열거하고,
.NET WPF의 공식 Fluent 테마 `ContextMenu`로 Windows 11 형식의 기본 메뉴를 표시한다.
공통 잘라내기·복사·삭제 명령은 상단 아이콘 행으로 배치하고 Shell 아이콘, 활성 상태,
체크 상태와 하위 메뉴를 유지한다. 선택한 명령은 원래 `IContextMenu.InvokeCommand`로
실행한다.

메뉴 마지막의 `더 많은 옵션 표시`는 owner-drawn 또는 레거시 확장과의 완전한 호환성이
필요할 때 기존 `TrackPopupMenuEx` 메뉴를 연다.

항목의 최초 `MouseRightButtonDown` 좌표를 즉시 물리 화면 좌표로 저장한다. Glass 메뉴의
`Windows 추가 옵션 표시` 버튼을 누를 때 현재 커서를 다시 읽지 않고 저장한 좌표를 Fluent
메뉴와 클래식 폴백 모두의 기준점 및 명령 호출 위치로 사용한다.

## 이유

Windows의 공개 `IContextMenu` API는 Shell 명령의 구성과 실행을 제공하지만 Explorer의
Windows 11 XAML 메뉴 자체를 외부 WPF 창에 호스팅하는 API는 제공하지 않는다.
`IExplorerCommand` 문서는 Explorer에 현대식 메뉴 확장을 등록하는 계약이지 외부 앱에서
Explorer 메뉴 전체를 여는 계약이 아니다. Shell 명령을 정적으로 복제하면 설치된 확장과
실제 상태가 유실되므로 명령 공급자는 계속 Windows Shell로 유지한다.

.NET 9 이상 WPF Fluent 테마는 Windows 11 디자인, 시스템 light/dark 모드와 accent 지원을
공식 제공하므로 현재 .NET 10 앱의 표시 계층에 적합하다.

## 결과

- 기본 메뉴는 Windows 11 Fluent 외형과 상단 공통 명령 행을 사용한다.
- 설치된 Shell extension, 하위 메뉴와 실제 명령 실행은 `IContextMenu` 세션에서 유지한다.
- 시스템 앱 테마에 따라 밝은/어두운 배경을 선택한다.
- 최초 우클릭과 메뉴 표시 사이에 커서가 이동해도 기준점은 변하지 않는다.
- 모든 종료 경로에서 COM·HMENU를 해제하고 snapshot을 다시 스캔한다.

## 참고

- [WPF Fluent theme](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90#fluent-theme)
- [IContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icontextmenu)
- [Windows 11 File Explorer context-menu extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)
