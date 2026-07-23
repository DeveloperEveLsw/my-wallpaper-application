# Seelen M1·M2 검수 기록

- 구현 상태: M1·M2 완료
- WSL/자동 검증: 통과
- Windows Seelen 실기기 검수: 통과
- 완료 판정일: 2026-07-24
- 기준 프로토콜: 3

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
자동 테스트는 **132/132** 통과했다. 제품 위젯 bundle과 생성된 JavaScript 구문 검사도
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
- 지속 설정은 Seelen 위젯 설정 GUI가 소유하고, 화면에는 오류 전용 재시도 패널만
  표시한다.

`@wallpaper/desktop`은 작은 독립 위젯이 아니라 WPF 화면 구성을 재현하는 전체 작업
영역 위젯이므로 창 자체의 사용자 drag 위치를 저장하지 않는다. 내부 배치는
`desktop/index.css`의 `.desktop-clock`, `.dock`, `.file-modal`,
`.error-panel`에서 조정한다.

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
2. Seelen 위젯 설정에서 `기본 Desktop 폴더 사용`을 끈다.
3. `사용자 지정 루트 경로`에 fixture의 절대 Windows 경로를 입력한다.
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

이 체크리스트와 아래 후속 수정 재검수를 완료해 M1·M2의 Windows 통과를 선언했다.

## 2026-07-24 사용자 검수 후속 수정

첫 Windows 사용자 검수에서 확인된 6개 항목은 다음과 같이 반영했다.

- 모달 재진입 때 캐시된 visual을 DOM 연결 전에 적용해 fallback만 남던 순서를
  수정했다. 행을 DOM에 연결한 다음 icon/thumbnail을 적용한다.
- 전체 작업 영역 Desktop 위젯은 유지하되 Seelen의 전역 마우스 좌표와
  `setIgnoreCursorEvents`를 사용해 Dock, 열린 파일 모달, 오류 전용 재시도 패널에서만
  입력을 받는다. 그 밖의 시계와 빈 배경은 Windows 바탕화면으로 입력을 통과시킨다.
- `UIColors`의 현재 강조색과 변경 이벤트를 폴더, `…`, 열린 상태 표시에 적용한다.
  위젯 `metadata.yml`에도 사용자 지정 폴더 색 switch/color를 선언했으므로 Seelen
  설정 GUI에서 기본 시스템 강조색 추적을 끄고 별도 색을 선택할 수 있다.
- `…` glyph의 시각 중심을 폴더 glyph에 맞게 아래로 보정하고, 세 점 묶음을 카드
  중앙에 정렬했다. 아이콘 아래 라벨은 가상 컬렉션의 의미가 드러나는 `루트`로
  표시한다.
- 인증된 `openFile` 명령을 Companion에 추가했다. 파일 ID를 현재 projection에서 다시
  검증한 뒤 Windows 기본 연결 프로그램으로 열며, 타일 더블클릭과 키보드 `Enter`가
  이 명령을 호출한다.
- 파일 타일의 기본 표시는 마지막 확장자를 숨기되, 전체 실제 이름은 tooltip과
  projection에 그대로 유지한다. Shell이 별도 표시 이름을 반환하는 바로가기 파일은
  그 이름을 우선한다.

이 후속 수정은 자동 build/test 뒤 Windows Seelen에서 다음을 다시 확인한다.

1. 같은 폴더를 닫았다 다시 열어도 icon/thumbnail이 유지된다.
2. 빈 배경 우클릭은 Windows 바탕화면 메뉴를 열고 Dock/모달 우클릭은 바탕화면으로
   전달되지 않는다.
3. Windows/Seelen 강조색 변경이 폴더에 반영되고 Seelen 설정의 사용자 지정 색이
   우선한다.
4. `…` glyph와 폴더 glyph의 시각 중심이 맞는다.
5. TXT, 이미지, 바로가기를 더블클릭하면 각각 기본 연결 프로그램으로 열린다.
6. 표시 이름에는 확장자가 없고 tooltip에는 실제 전체 파일 이름이 남는다.

## 2026-07-24 Seelen 설정 통합

- 우측 상단 1초 hover 진입 영역과 위젯 내부 설정 패널을 제거했다.
- `기본 Desktop 폴더 사용`과 `사용자 지정 루트 경로`를 Seelen 위젯 설정에 추가했다.
- 폴더 색상 설정은 기존처럼 Seelen 설정에 유지한다.
- 정상 연결·감시 상태는 숨기고 연결, watcher, 루트와 명령 오류만 재시도 패널에
  표시한다.
- watcher가 정상일 때는 수동 `다시 스캔`을 노출하지 않고 오류 패널의 `재시도`에서만
  인증된 `refresh` 명령을 보낸다.
- Dock 폴더 순서는 기존 drag 직접 조작과 Companion의 원자적 JSON 저장을 유지한다.

Windows Seelen에서 다음을 추가로 확인한다.

1. 기본 스위치가 켜져 있으면 Windows Desktop을 표시한다.
2. 스위치를 끄고 fixture 절대 경로를 입력하면 해당 루트로 전환한다.
3. 존재하지 않는 사용자 경로를 입력하면 오류 패널만 나타나고 `재시도`가 동작한다.
4. 정상 루트로 고치면 오류 패널이 사라진다.
5. Companion 종료와 루트 삭제 때 오류 패널이 나타나며 복구 후 자동으로 사라진다.

## 최종 판정

2026-07-24 사용자 완료 확인에 따라 M1·M2를 종료한다. 이후 회귀 기준선은 다음과 같다.

- Release build 경고·오류 0
- 전체 자동 테스트 132/132
- M0 및 제품 위젯 bundle·JavaScript 구문 검사 통과
- 위 M1·M2 체크리스트와 사용자 검수 후속 수정 재검수 통과

후속 결과는 [Seelen M3·M4 검수 기록](m3-m4-seelen-validation.md)을 따른다.
