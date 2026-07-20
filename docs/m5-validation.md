# M5 검수 기록

- 구현 상태: 완료
- 자동 검증: 통과
- Windows Standalone UI 검수: 완료
- 검수 환경: Windows 11 Pro 10.0.26200
- 기준일: 2026-07-21

## 구현된 범위

- 파일과 실제 폴더 Glass 메뉴의 다섯 번째 `Windows 추가 옵션 표시`
- 명령 직전 항목 존재·루트 경계·깊이·종류·재분석 지점 재검증
- 파일·폴더 PIDL의 부모 `IShellFolder.GetUIObjectOf(IContextMenu)` 기반 실제 Shell 메뉴
- Desktop `IShellFolder.CreateViewObject(IContextMenu)` 기반 실제 배경 메뉴
- 실제 Shell 명령을 표시하는 Windows 11 Fluent WPF 메뉴와 시스템 light/dark 테마
- 공통 명령 아이콘 행, Shell 아이콘·상태·하위 메뉴와 선택 명령 `InvokeCommand`
- `더 많은 옵션 표시`의 `TrackPopupMenuEx` 클래식 호환 메뉴
- 최초 우클릭 물리 화면 좌표 저장과 Glass 버튼 클릭 뒤 동일 좌표 재사용
- 클래식 메뉴의 `IContextMenu2/3` owner-drawn 메시지 전달
- 입력 mouse-up 뒤 Dispatcher idle에서 메뉴를 열어 즉시 닫히는 경합 방지
- 메뉴 성공·취소·실패 뒤 전체 snapshot 재스캔
- 파일·실제 폴더, 순수 배경, Dock·모달 빈 영역과 설정 hotspot의 명시적 hit-test 경계

Wallpaper Engine에서의 우클릭 중복, 포커스 복귀와 parent HWND 배치는 합의에 따라 M6의
첫 통합 게이트로 이동했다.

## 자동 검증

Windows .NET 10 SDK로 다음을 확인했다.

- Release 전체 빌드 경고 0, 오류 0
- Core 테스트 42개 통과
- Windows Infrastructure 테스트 28개 통과
- 오래된 Shell 메뉴 대상은 owner HWND나 Shell COM에 접근하기 전에 거부
- 유효한 대상과 Desktop 메뉴 요청도 유효한 owner HWND 없이는 실행하지 않음
- 실제 STA owner에서 파일과 Desktop Shell 명령 세션 열거 확인

## Windows Standalone 검수 결과

격리된 임시 fixture와 WSL Release 산출물을 Windows `dotnet.exe`로 실행해 확인했다.

| 항목 | 결과 |
|---|---|
| 파일 Glass 메뉴 | 다섯 명령이 합의된 순서로 표시됨 |
| 파일 추가 옵션 | 설치된 shell extension과 하위 메뉴를 포함한 Windows 11 Fluent 메뉴가 표시됨 |
| 폴더 추가 옵션 | 공통 명령 아이콘 행과 폴더 전용 Fluent 메뉴가 표시됨 |
| Shell 명령 실행 | 폴더 `속성` 명령을 선택해 실제 Windows 속성창이 열림 |
| 원래 우클릭 위치 | 현재 커서와 무관하게 저장된 기준점 X=996, Y=988에서 메뉴가 위로 정렬됨 |
| 클래식 호환성 | `더 많은 옵션 표시`로 owner-drawn 확장용 클래식 메뉴에 접근 가능 |
| 배경 우클릭 | `디스플레이 설정`, `개인 설정`을 포함한 Desktop Fluent 메뉴가 표시됨 |
| 열린 모달에서 배경 우클릭 | 모달을 닫은 뒤 Desktop 메뉴 하나만 표시됨 |
| 모달 빈 영역 | 우클릭해도 Windows 메뉴가 열리지 않고 모달이 유지됨 |
| Dock 빈 영역·구분선 | 우클릭해도 Windows 메뉴가 열리지 않고 모달이 유지됨 |
| 설정 hotspot 우클릭 | hover 설정을 열지 않고 Desktop 메뉴가 표시됨 |
| Shell 메뉴 취소 | `Esc` 뒤 WPF 포커스와 열린 파일 모달이 정상 복귀함 |
| 중복 입력 | Standalone에서 한 번의 우클릭에 메뉴 하나만 표시됨 |

## 재현 절차

Windows PowerShell에서 다음을 실행한다.

```powershell
./scripts/check.ps1
$fixture = ./scripts/new-m5-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

### 항목 메뉴

- `Work/item-shell-menu.txt`와 `Work` 폴더 카드를 우클릭해 다섯 Glass 명령을 확인한다.
- `Windows 추가 옵션 표시`를 선택해 파일과 폴더의 Windows 11 Fluent 메뉴가 열리는지
  확인한다.
- 최초 우클릭 뒤 커서를 Glass 메뉴 버튼으로 이동해도 Fluent 메뉴가 최초 우클릭 위치를
  기준으로 열리는지 확인한다.
- `속성`처럼 내용을 바꾸지 않는 명령을 실행하고 메뉴 종료 뒤 UI가 다시 동기화되는지
  확인한다.
- `더 많은 옵션 표시`에서 레거시 owner-drawn 확장이 필요한 클래식 메뉴도 확인한다.
- 메뉴가 열린 상태에서 `Esc`를 눌러 취소하고 포커스가 앱으로 돌아오는지 확인한다.

### 배경과 hit-test

- 순수 배경을 우클릭해 Desktop 메뉴의 `디스플레이 설정`과 `개인 설정`을 확인한다.
- 파일 모달을 연 상태에서 모달 바깥 배경을 우클릭하면 모달이 닫히고 메뉴 하나만
  표시되는지 확인한다.
- 모달 내부 빈 영역, Dock padding·구분선과 가상 `…` 카드를 우클릭해 Windows 메뉴가
  열리지 않는지 확인한다.
- 우측 상단 hotspot을 우클릭하면 설정 패널이 아니라 Desktop 메뉴가 열리는지 확인한다.

### 회귀

- 파일·폴더의 기존 네 Glass 명령, 파일 선택·더블클릭, Dock 순서 drag와 내부 파일
  drag & drop이 유지되는지 확인한다.
- 네이티브 메뉴에서 이름 변경·삭제 같은 외부 명령을 시험할 때는 실제 사용자 파일이
  아니라 이 fixture만 사용한다.
