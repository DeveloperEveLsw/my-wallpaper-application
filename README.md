# My Wallpaper Application

실제 Windows 파일과 폴더를 감성적인 라이브 월페이퍼 안에 정돈해 보여주는
WPF 기반 데스크톱 애플리케이션이다.

화면 하단에는 macOS Dock에서 영감을 받은 글래스 카드가 배치된다. 사용자가 지정한
루트 폴더의 직접 자식 폴더는 카드로 표시하고, 루트 바로 아래의 파일은 가상 `…`
카드에 모아 표시한다. 카드에서 수행한 파일 이동은 실제 파일 시스템에 반영된다.

## 현재 상태

- M5 Windows Shell 메뉴와 hit-test 입력 소유권 구현·Standalone 검수 완료
- WPF 애플리케이션이 제품 본체
- Wallpaper Engine과 Lively는 선택 가능한 실행 호스트
- Wallpaper Engine은 로컬 Application Wallpaper로 사용하며 공개 Workshop 배포는 범위 밖
- 로컬 루트 얕은 snapshot, Dock, `…`, 단일 파일 모달과 숨은 설정 패널 구현
- Windows Shell 파일 아이콘, 비동기 이미지 썸네일 캐시와 대량 파일 가상화 구현
- `FileSystemWatcher` debounce·전체 재스캔과 루트 삭제·복구 처리 구현
- Dock 카드 drag 재정렬과 사용자 순서 저장 구현
- 파일 선택·더블클릭 실행과 파일·폴더 Glass 우클릭 메뉴 구현
- Windows 이름 검증을 거치는 파일·폴더 이름 변경 구현
- Glass 확인 모달과 `IFileOperation`을 이용한 Windows 휴지통 이동 구현
- `…` ↔ 폴더와 폴더 ↔ 폴더 파일 이동, drag preview와 유효·무효 대상 표시 구현
- 덮어쓰기 없는 충돌 이름 제안·편집 모달과 이동 뒤 전체 재스캔 복구 구현
- 실제 Shell 명령 기반 Windows 11 Fluent 추가 옵션과 바탕화면 메뉴 구현
- 추가 옵션은 최초 우클릭 위치에 표시하고 `더 많은 옵션 표시`로 클래식 메뉴도 제공
- Wallpaper Engine 호스팅과 호스트 상태의 우클릭 중복·포커스·HWND 검증은 M6 범위

## 기준 문서

- [제품 명세](docs/product-spec.md)
- [기술 아키텍처](docs/architecture.md)
- [미결정 사항](docs/open-questions.md)
- [WPF 네이티브 런타임 결정](docs/decisions/0001-wpf-native-runtime.md)
- [네이티브 Shell 메뉴 HWND 결정](docs/decisions/0007-native-shell-menu-hosting.md)
- [Windows 11 현대식 Shell 명령 표면 결정](docs/decisions/0008-modern-shell-command-surface.md)
- [MVP 개발 계획](docs/mvp-development-plan.md)
- [M5 검수 기록](docs/m5-validation.md)

## 요구 환경

- .NET SDK 10.0.302 이상 10.0 feature band
- WSL2 Ubuntu 24.04 또는 동등한 Linux 개발 환경
- 실제 UI 검증용 Windows 10/11

## 개발 명령

WSL:

```bash
./scripts/check.sh
```

Windows PowerShell:

```powershell
./scripts/check.ps1
$fixture = ./scripts/new-mvp-fixture.ps1
./scripts/run-standalone.ps1
```

M2 기능을 함께 검증할 때는 다음 fixture를 사용한다.

```powershell
$fixture = ./scripts/new-m2-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

M3 파일 명령은 실제 사용자 파일 대신 전용 fixture에서 검수한다.

```powershell
$fixture = ./scripts/new-m3-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

세부 항목은 [M3 검수 체크리스트](docs/m3-validation.md)를 따른다.

M4 내부 파일 이동은 전용 충돌·잠금 fixture에서 검수한다.

```powershell
$fixture = ./scripts/new-m4-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

세부 항목은 [M4 검수 체크리스트](docs/m4-validation.md)를 따른다.

M5 Windows Shell 메뉴와 입력 경계는 전용 fixture에서 검수한다.

```powershell
$fixture = ./scripts/new-m5-fixture.ps1
./scripts/run-standalone.ps1 -RootPath $fixture.RootPath -Configuration Release
```

세부 결과와 항목은 [M5 검수 기록](docs/m5-validation.md)을 따른다.

첫 실행에서는 설정 패널이 자동으로 열린다. `new-mvp-fixture.ps1`이 출력한 경로를
`폴더 선택`에서 지정한다. 루트 설정 후에는 화면 우측 상단의 보이지 않는 영역을 1초간
hover하면 설정 패널을 다시 열 수 있다.

현재 vertical slice는 파일·폴더 열기, 탐색기 위치 열기, 이름 변경, 휴지통 이동,
카드 사이 내부 파일 drag & drop, Windows 11 Fluent 추가 옵션과 실제 Shell 명령 기반
바탕화면 메뉴를 제공한다. 모든 변경과 Shell 메뉴 종료 뒤에는 실제 파일 시스템을 전체
재스캔한다.

## 개발·검증 흐름

1. WSL의 Linux 파일 시스템에서 소스 작성과 자동 테스트를 수행한다.
2. 작은 기능 단위로 커밋하고 GitHub에 푸시한다.
3. Windows 로컬 작업 폴더에서 pull한다.
4. Windows에서 WPF 실행, 호스트 통합, 실제 입력과 파일 시스템 동작을 검증한다.
5. 검증 결과를 문서와 회귀 테스트에 반영한다.

WPF는 Windows에서만 실행된다. WSL에서는 플랫폼 비종속 Core 테스트와 가능한 빌드
검사를 수행하고, 최종 실행 검증은 반드시 Windows에서 수행한다.
