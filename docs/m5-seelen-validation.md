# Seelen M5 검수 기록

- 구현 상태: 코드 완료
- WSL/자동 검증: 통과
- Windows Seelen 실기기 검수: 대기
- 기준일: 2026-07-24
- 기준 프로토콜: 5
- 제품 위젯 버전: 0.5.0

## 구현 범위

- 파일과 실제 폴더 Glass 메뉴의 다섯 번째 `Windows 추가 옵션 표시`
- 최초 항목 우클릭의 물리 화면 좌표와 Seelen widget HWND 전달
- 현재 projection 항목과 루트·깊이·종류·재분석 지점 재검증
- 위젯에는 실제 경로 대신 10초 수명의 256-bit 일회용 ticket만 반환
- `SeelenCommand.RequestFocus` 뒤 같은 Companion 실행 파일의 transient broker 시작
- current-user named pipe에서 ticket을 한 번만 실제 target으로 교환
- `[STAThread]` broker와 투명한 1×1 top-level owner HWND
- 기존 `IContextMenu/IContextMenu2/3`, `TrackPopupMenuEx`와 owner-drawn 메시지 경로 재사용
- 메뉴 취소 때 widget 포커스 복귀, 명령 실행 때 새 창 포커스 보존
- 성공·취소·실패·broker 연결 끊김 뒤 전체 projection 재스캔
- pending ticket 경합·중복·명시적 취소·능동 만료 처리

순수 배경 우클릭은 Seelen 위젯의 기존 입력 투과를 통해 Explorer가 직접 처리한다.
Companion Desktop 배경 메뉴는 M5에 추가하지 않는다.

## 자동 검증 결과

WSL에서 Windows .NET 10 SDK를 명시해 다음을 실행했다.

```bash
WALLPAPER_DOTNET='/mnt/c/Program Files/dotnet/dotnet.exe' ./scripts/check.sh
```

| 범위 | 결과 |
|---|---|
| 전체 Release build | 경고 0, 오류 0 |
| 전체 .NET 테스트 | 162/162 통과 |
| `Wallpaper.Seelen.Tests` | 38/38 통과 |
| 제품 위젯 Node 테스트 | 9/9 통과 |
| M0·제품 위젯 bundle | 통과 |
| 생성 JavaScript 구문 검사 | 통과 |
| npm audit | 취약점 0 |

자동 테스트는 다음 경계를 직접 확인한다.

- 음수 좌표를 포함한 물리 화면 좌표, positive owner HWND와 request 형식
- 준비 응답에 ticket만 있고 실제 root/relative path가 없는 계약
- 현재 projection target 재해석과 stale 항목 거부·재스캔
- 같은 pending 요청의 ticket 재사용과 동시 다른 메뉴 거부
- ticket 단일 교환, 잘못된 ticket 거부, 완료 뒤 다음 메뉴 허용
- pending ticket 취소와 만료 뒤 다음 요청 복구
- 위젯 다섯 번째 Glass 옵션과 프로토콜 5 DOM/bundle 계약
- `RequestFocus`, transient broker 인자, 물리 좌표와 owner HWND 전달

STA COM, 실제 top-level foreground, `#32768` 네이티브 메뉴 내용과 설치된 Shell 확장은
Windows Seelen 실기기 검수로 분리한다.

## Windows 배치

Windows 로컬 checkout에서 다음을 실행한다.

```powershell
git pull --ff-only origin main
./scripts/check.ps1
./scripts/prepare-seelen.ps1 -LoadDevelopmentResource
$fixture = ./scripts/new-m5-fixture.ps1
```

Seelen에서 `@wallpaper/desktop`을 활성화한다. 위젯 설정의 `기본 Desktop 폴더 사용`을
끄고 `사용자 지정 루트 경로`에 `$fixture.RootPath`를 입력한다.

## Windows Seelen 실기기 체크리스트

1. `Work` 폴더와 `Work/item-shell-menu.txt`의 Glass 메뉴에 다섯 번째
   `Windows 추가 옵션 표시`가 한 번만 나타난다.
2. 파일과 폴더에서 이 옵션을 누르면 Windows 11 현대식 중간 메뉴를 흉내 낸 UI가 아니라
   설치된 확장과 하위 메뉴가 포함된 클래식 네이티브 메뉴가 바로 열린다.
3. Glass 버튼으로 커서를 이동한 뒤에도 메뉴가 최초 항목 우클릭 좌표에서 열린다.
4. 주 모니터와 배율이 다른 보조 모니터에서 메뉴 위치가 커서와 맞고 화면 밖으로
   치우치지 않는다.
5. 마우스 hover 하위 메뉴, 아이콘·owner-drawn 항목, 방향키, `Enter`와 `Esc`가 정상
   동작한다.
6. `Esc`로 취소하면 위젯 입력과 포커스가 복귀하고 다음 우클릭이 즉시 동작한다.
7. `속성`을 실행하면 속성 창의 포커스를 위젯이 다시 빼앗지 않는다.
8. fixture에서만 이름 변경 같은 Shell 명령을 실행해 메뉴 종료 뒤 Dock·모달 snapshot이
   실제 파일 시스템과 다시 일치하는지 확인한다.
9. 빠르게 반복 클릭해도 네이티브 메뉴가 동시에 두 개 열리지 않고
   `Wallpaper.Seelen.Companion` broker가 고아 프로세스로 남지 않는다.
10. 순수 배경 우클릭은 broker를 시작하지 않고 Explorer Desktop 메뉴를 그대로 연다.
11. 가상 `루트` 카드에는 항목 Shell 메뉴가 생기지 않고 기존 배경 입력 투과가 유지된다.
12. 메뉴 준비 중 Companion 또는 Seelen을 재시작한 뒤 10초 이내 오류가 정리되고 재연결
    후 다음 메뉴가 정상적으로 열린다.

## 실패 기록

- 대상이 파일인지 폴더인지와 최초 우클릭 화면 좌표
- 모니터 배율, 작업 영역과 주/보조 모니터 여부
- 표시된 오류 메시지와 발생 시각
- `Get-Process Wallpaper.Seelen.Companion` 결과
- Seelen 로그와 Companion 종료 코드
- 같은 항목의 Explorer 클래식 메뉴에 표시되는 확장 비교

Windows 체크리스트와 발견 결함 재검수까지 끝나면 M5 완료로 판정한다.
