# ADR 0003: 파일 입력, Dock 순서와 Windows Shell 메뉴

- 상태: 채택
- 날짜: 2026-07-19

## 결정

### 파일 입력

- 한 번 클릭은 파일 선택이다.
- 더블클릭은 Windows 기본 연결 프로그램으로 파일을 연다.
- 카드 간 파일 이동은 drag & drop으로만 제공하고 우클릭 메뉴에 중복 명령을 두지 않는다.
- 한 번에 하나의 폴더 모달만 연다.
- 같은 카드를 다시 클릭하거나 모달 바깥 월페이퍼를 좌클릭하거나 `Esc`를 누르면 닫는다.
- 이름 충돌 시 `파일명 (n).확장자` 형식의 사용 가능한 이름을 제안하고 사용자가 모달에서
  편집한 뒤 이동한다. 기존 파일 덮어쓰기는 제공하지 않는다.

### 커스텀 우클릭 메뉴

MVP 메뉴는 다음 순서를 사용한다.

1. 열기
2. 탐색기에서 위치 열기
3. 이름 변경
4. 휴지통으로 이동
5. Windows 추가 옵션 표시

휴지통 이동은 `IFileOperation`의 recycle 동작을 사용하고 영구 삭제로 폴백하지 않는다.
`Windows 추가 옵션 표시`는 실제 Shell object의 `IContextMenu`를 이용해 Windows
네이티브 메뉴를 연다. 네이티브 메뉴와 외부 shell extension 실행 후에는 실제 파일
시스템을 다시 스캔한다.

휴지통 이동 전에는 Glass 확인 모달을 표시한다. 폴더는 현재 화면에 표시되지 않는 하위
내용도 함께 이동한다는 사실을 경고한다.

### Windows 바탕화면 메뉴

순수 월페이퍼 배경을 우클릭하면 Windows Explorer의 실제 바탕화면 배경 메뉴를 연다.
파일·폴더 항목은 Glass 메뉴를 열고 Dock·모달의 빈 패널은 Windows 메뉴로 전달하지
않는다. Desktop Shell folder의 배경 `IContextMenu`를 우선 사용하며 Windows 10/11과
Wallpaper Engine 적용 상태에서 실제 메뉴 내용과 중복 입력을 검증한다.

### Dock 순서

- 초기 폴더 카드 순서는 자연어 이름순이다.
- 사용자가 바꾼 표시 순서는 앱 설정에 보존한다.
- 카드 순서 변경은 실제 폴더 위치를 바꾸지 않는다.
- 가상 `…` 카드는 항상 마지막에 고정한다.
- 실제 폴더 그룹과 `…` 사이에 얇은 세로 구분선과 여백을 둔다.

## 결과

- 선택과 실행이 분리되어 drag 시작과 단일 클릭 실행이 충돌하지 않는다.
- 자주 사용하는 명령은 Glass 메뉴에서 일관되게 제공하면서 전체 Windows Shell 기능에도
  접근할 수 있다.
- 실제 파일 이동과 가상 Dock 정렬의 의미가 분리된다.
- 외부 shell extension은 앱이 완전히 통제할 수 없으므로 별도 STA와 후속 재스캔이
  필요하다.
- 입력 위치별 hit-test 경계가 제품 동작의 일부가 되며 native host 환경 검증이 필요하다.

## 공식 API 근거

- [IContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icontextmenu)
- [IFileOperation operation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
- [IShellFolder::CreateViewObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)
- [SHGetDesktopFolder](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetdesktopfolder)
