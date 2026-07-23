# M5 검수 기록

- 구현 상태: 완료
- 자동 검증: 통과
- Windows Standalone UI 재검수: 완료
- 검수 환경: Windows 11 Pro 10.0.26200
- 기준일: 2026-07-21

## 최종 합의

Explorer의 Windows 11 현대식 메뉴 전체를 외부 WPF 앱에서 여는 공개 API가 없으므로
현대식 외형을 WPF로 재현하지 않는다. 파일·폴더의 `Windows 추가 옵션 표시`와 순수 배경
우클릭은 Windows가 만든 이전 형식의 클래식 Shell 메뉴를 직접 연다.

## 구현된 범위

- 파일과 실제 폴더 Glass 메뉴의 다섯 번째 `Windows 추가 옵션 표시`
- 명령 직전 항목 존재·루트 경계·깊이·종류·재분석 지점 재검증
- 파일·폴더 PIDL의 부모 `IShellFolder.GetUIObjectOf(IContextMenu)` 기반 실제 Shell 메뉴
- `IShellWindows.FindWindowSW(SWC_DESKTOP)`와 `SID_STopLevelBrowser`를 통한 Explorer의
  현재 Desktop `IShellView` 획득
- `IShellView.GetItemObject(SVGIO_BACKGROUND, IContextMenu)` 기반 실제 뷰 배경 메뉴
- Explorer Desktop 뷰를 사용할 수 없을 때 기본 Desktop `IShellView` 배경 메뉴 폴백
- `TrackPopupMenuEx` 네이티브 클래식 메뉴의 직접 표시와 `IContextMenu.InvokeCommand`
- 최초 우클릭 물리 화면 좌표 저장과 Glass 버튼 클릭 뒤 동일 좌표 재사용
- `IContextMenu2/3` owner-drawn·하위 메뉴 메시지 전달
- 입력 mouse-up 뒤 Dispatcher idle에서 메뉴를 열어 즉시 닫히는 경합 방지
- 메뉴 성공·취소·실패 뒤 전체 snapshot 재스캔
- 파일·실제 폴더, 순수 배경, Dock·모달 빈 영역과 설정 hotspot의 명시적 hit-test 경계
- WPF 현대식 메뉴 presenter와 Shell 명령 열거용 표시 모델 제거

Wallpaper Engine에서의 우클릭 중복, 포커스 복귀와 parent HWND 배치는 합의에 따라 M6의
첫 통합 게이트로 유지한다.

## 자동 검증

Windows .NET 10 SDK로 다음을 확인했다.

- Release 전체 빌드 경고 0, 오류 0
- Core 테스트 42개 통과
- Windows Infrastructure 테스트 28개 통과
- 오래된 Shell 메뉴 대상은 owner HWND나 Shell COM에 접근하기 전에 거부
- 유효한 대상과 Desktop 메뉴 요청도 유효한 owner HWND 없이는 실행하지 않음
- 실제 STA owner에서 파일 메뉴와 Desktop 뷰 배경 메뉴가 네이티브 항목을 포함해 생성됨

## Windows Standalone 재검수 결과

격리된 M5 fixture와 WSL Release 산출물을 Windows에서 실행해 다음을 확인했다.

| 항목 | 결과 |
|---|---|
| 파일·폴더 추가 옵션 | 현대식 중간 메뉴 없이 설치된 확장과 하위 메뉴가 포함된 클래식 메뉴가 바로 표시됨 |
| Shell 메뉴 원본성 | WPF 메뉴가 아니라 앱 프로세스가 소유한 네이티브 `#32768` 팝업 메뉴로 확인됨 |
| 원래 우클릭 위치 | Glass 버튼으로 커서를 옮긴 뒤에도 최초 항목 우클릭 좌표에서 메뉴가 열림 |
| 외부 확장 호환성 | Visual Studio, Git, 7-Zip, WinRAR 등 설치된 항목과 하위 메뉴가 표시됨 |
| 배경 실제 우클릭 | 빈 배경 우클릭 좌표에 Desktop 클래식 메뉴가 직접 표시됨 |
| NVIDIA 제어판 | Explorer의 클래식 메뉴와 앱의 Desktop 메뉴 양쪽에 동일하게 표시됨 |
| Desktop 기본 명령 | 보기, 정렬 기준, 새로 고침, 새로 만들기, 디스플레이 설정, 개인 설정이 표시됨 |
| 키보드 진입 | `Shift+F10`으로도 동일한 Desktop 클래식 메뉴가 표시됨 |
| owner-drawn 메시지 | 아이콘과 외부 확장 항목이 정상 렌더링됨 |
| 취소·포커스 | `Esc`로 메뉴를 취소하고 앱 포커스와 UI가 정상 복귀함 |

검수 PC에는 `Directory\\Background\\shellex\\ContextMenuHandlers\\NvCplDesktopContext`가
등록되어 있었다. 앱은 이 키나 NVIDIA 명령을 직접 읽거나 삽입하지 않으며 Explorer의
현재 Desktop 뷰가 반환한 `IContextMenu`를 그대로 사용한다.

## 재현 절차

Windows PowerShell에서 다음을 실행한다.

```powershell
./scripts/check.ps1
$fixture = ./scripts/new-m5-fixture.ps1
./scripts/run-dev-window.ps1 -RootPath $fixture.RootPath -Configuration Release
```

### 항목 메뉴

- `Work/item-shell-menu.txt`와 `Work` 폴더 카드를 우클릭해 다섯 Glass 명령을 확인한다.
- `Windows 추가 옵션 표시`를 선택해 파일과 폴더의 클래식 Shell 메뉴가 중간 단계 없이
  바로 열리는지 확인한다.
- 최초 우클릭 뒤 커서를 Glass 메뉴 버튼으로 이동해도 Shell 메뉴가 최초 우클릭 위치를
  기준으로 열리는지 확인한다.
- `속성`처럼 내용을 바꾸지 않는 명령을 실행하고 메뉴 종료 뒤 UI가 다시 동기화되는지
  확인한다.
- 메뉴가 열린 상태에서 `Esc`를 눌러 취소하고 포커스가 앱으로 돌아오는지 확인한다.

### 배경과 hit-test

- 순수 배경을 우클릭해 `NVIDIA 제어판`, `디스플레이 설정`, `개인 설정`이 표시되는지
  확인한다. 같은 PC의 Explorer 클래식 메뉴에 없는 확장은 앱에도 없을 수 있다.
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
