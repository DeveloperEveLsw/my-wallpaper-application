# Seelen M1·M2 검수 기록

- 구현 상태: M1·M2 코드 완료
- WSL/자동 검증: 통과
- Windows Seelen 실기기 검수: 사용자 검수 대기
- 기준 프로토콜: 2

## 자동 검증 범위

`Wallpaper.Seelen.Tests`는 다음을 자동 검증한다.

- 폴더와 루트 파일의 얕은 projection
- icon/thumbnail URL 계약
- watcher 변경 뒤 snapshot 갱신
- 폴더 순서 저장과 재시작 복원
- 손상된 설정 복구
- 사용자 루트 변경 검증
- 1,200개 파일 누락 없는 projection
- 루트 삭제 상태와 복귀 후 자동 회복

2026-07-24 전체 솔루션 Release build는 경고·오류 없이 통과했으며 기존 회귀를 포함한
자동 테스트는 **131/131** 통과했다. 제품 위젯 bundle과 생성된 JavaScript 구문 검사도
통과했다.

전체 저장소 검사는 WSL에서 다음 명령으로 실행한다.

```bash
./scripts/check.sh
```

Linux용 `dotnet`이 PATH에 없다면 설치하거나 `WALLPAPER_DOTNET`을 지정한다. Windows
SDK를 WSL interop으로 사용할 때는 개별 build/test에 Windows 경로의 `dotnet.exe`를
쓸 수 있지만, 최종 publish는 반드시 Windows 로컬 checkout에서 수행한다.

## WPF UI 정합성

2026-07-24 Seelen 제품 위젯의 표시 구조를 기존 WPF `WallpaperView` 기준으로
정렬했다.

- 위젯 창은 현재 모니터의 작업 영역 전체를 사용한다.
- 시계는 상단 중앙 44px, Dock은 하단 중앙 96px 안전 영역에 표시한다.
- Dock은 카드 배경과 파일 수를 제거하고 WPF 폴더 아이콘, 이름, 열린 상태 표시만
  사용한다.
- 실제 폴더 영역만 가로 스크롤하고 구분선과 가상 `…` 카드는 오른쪽에 고정한다.
- 파일 모달은 Dock 위의 760×470 Glass 패널이며, 파일은 WPF와 같은 128×122 타일
  격자로 표시한다.
- 1,200개 파일 검수 경계는 타일 행 단위 DOM 가상화로 유지한다.
- 설정은 우측 상단 72×72 진입 영역과 WPF의 루트·다시 스캔·상태 구성을 사용한다.

`@wallpaper/desktop`은 작은 독립 위젯이 아니라 WPF 화면 구성을 재현하는 전체 작업
영역 위젯이므로 창 자체의 사용자 drag 위치를 저장하지 않는다. 내부 배치는
`desktop/index.css`의 `.desktop-clock`, `.dock`, `.file-modal`,
`.settings-panel`에서 조정한다.

일반적인 Seelen `Desktop` 위젯은 위젯이 제공한 drag handle에서
`widget.window.startDragging()`을 호출해 이동한다. `Desktop` preset은 이동 뒤 마지막
위치와 크기를 자동 저장·복원한다. 위젯에 drag handle이 없다면 Seelen 설정만으로
콘텐츠 영역을 잡아 이동할 수 없으므로 위젯 구현에 handle을 추가해야 한다.

## Windows 배치

Windows 로컬 checkout에서 PowerShell로 실행한다.

```powershell
git pull --ff-only origin main
./scripts/check.ps1
./scripts/prepare-seelen-m1-m2.ps1
$fixture = ./scripts/new-m2-fixture.ps1
```

준비 스크립트는 다음을 배치한다.

- `%LOCALAPPDATA%\WallpaperSeelen\Wallpaper.Seelen.Companion.exe`
- `%APPDATA%\com.seelen.seelen-ui\widgets\wallpaper-desktop`

기본 실행은 영구 사용자 리소스만 설치하므로 Seelen을 재시작한다. 현재 세션에 개발
리소스를 바로 load하려면 다음을 사용한다.

```powershell
./scripts/prepare-seelen-m1-m2.ps1 -LoadDevelopmentResource
```

M0 Desktop 리소스는 제품 위젯과 ID가 다르지만 화면 중복을 피하기 위해 비활성화한다.

## M1 실기기 체크리스트

1. `@wallpaper/desktop`을 Desktop preset으로 활성화한다.
2. 우측 상단에 1초 hover하여 설정 패널을 연다.
3. `폴더 선택`이 Windows 네이티브 폴더 선택기를 여는지 확인하고 fixture를 선택한다.
4. `Work`, `Photos`, `Empty`, `Bulk` 카드와 `…` 카드가 보이는지 확인한다.
5. `…`를 열어 `loose-file.txt`만 보이는지 확인한다.
6. `Work`를 열어 `report.txt`, `notes.md`는 보이고 `Nested-Ignored` 내용은 보이지
   않는지 확인한다.
7. 빈 `Empty` 모달이 0개 상태로 정상 표시되는지 확인한다.
8. 모달 외부 클릭과 `Esc`로 닫히는지 확인한다.

## M2 실기기 체크리스트

1. `Photos`의 PNG가 썸네일로, TXT/MD가 Windows Shell 아이콘으로 표시되는지 확인한다.
2. `Bulk`의 1,200개 파일을 빠르게 스크롤하고 입력 정지나 전체 동시 이미지 요청이
   없는지 확인한다.
3. Dock 폴더 카드를 drag 재정렬하고 Seelen 재시작 후 순서가 유지되는지 확인한다.
4. Explorer에서 루트 파일을 생성·이름 변경·삭제하고 약 1초 안에 전체 snapshot이
   반영되는지 확인한다.
5. 하위 폴더 파일의 외부 변경도 해당 카드 모달에 반영되는지 확인한다.
6. fixture 루트 폴더 이름을 임시 변경해 `root-missing` 상태가 표시되는지 확인하고,
   원래 이름으로 복구했을 때 자동 회복하는지 확인한다.
7. `%LOCALAPPDATA%\MyWallpaperApplication\settings.json`을 백업한 뒤 잘못된 JSON으로
   바꾸고 Companion을 재시작하여 기본 Desktop과 설정 복구 메시지가 표시되는지
   확인한다.
8. Companion 프로세스를 종료하고 위젯이 자동 재실행·재연결되는지 확인한다.

## 실패 기록 시 필요한 정보

- 화면에 표시된 상태와 메시지
- `Get-Process Wallpaper.Seelen.Companion` 결과
- `Get-NetTCPConnection -State Listen`의 43127~43135 결과
- 실패한 루트 경로와 파일 수
- Seelen 로그 시각

실기기 검수가 끝나기 전에는 M1·M2의 Windows 통과를 선언하지 않는다.
