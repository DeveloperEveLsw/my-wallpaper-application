# My Wallpaper Application

실제 Windows 파일과 폴더를 감성적인 라이브 월페이퍼 안에 정돈해 보여주는
WPF 기반 데스크톱 애플리케이션이다.

화면 하단에는 macOS Dock에서 영감을 받은 글래스 카드가 배치된다. 사용자가 지정한
루트 폴더의 직접 자식 폴더는 카드로 표시하고, 루트 바로 아래의 파일은 가상 `…`
카드에 모아 표시한다. 카드에서 수행한 파일 이동은 실제 파일 시스템에 반영된다.

## 현재 상태

- MVP 첫 read-only vertical slice 구현
- WPF 애플리케이션이 제품 본체
- Wallpaper Engine과 Lively는 선택 가능한 실행 호스트
- Wallpaper Engine은 로컬 Application Wallpaper로 사용하며 공개 Workshop 배포는 범위 밖
- 로컬 루트 얕은 snapshot, Dock, `…`, 단일 파일 모달과 숨은 설정 패널 구현
- 파일 변경·Shell 메뉴·Wallpaper Engine 호스팅은 아직 비활성

## 기준 문서

- [제품 명세](docs/product-spec.md)
- [기술 아키텍처](docs/architecture.md)
- [미결정 사항](docs/open-questions.md)
- [WPF 네이티브 런타임 결정](docs/decisions/0001-wpf-native-runtime.md)
- [MVP 개발 계획](docs/mvp-development-plan.md)

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

첫 실행에서는 설정 패널이 자동으로 열린다. `new-mvp-fixture.ps1`이 출력한 경로를
`폴더 선택`에서 지정한다. 루트 설정 후에는 화면 우측 상단의 보이지 않는 영역을 1초간
hover하면 설정 패널을 다시 열 수 있다.

현재 vertical slice는 읽기 전용이다. fixture 또는 실제 루트에서 파일 이동·이름 변경·
삭제를 시도하지 않는다.

## 개발·검증 흐름

1. WSL의 Linux 파일 시스템에서 소스 작성과 자동 테스트를 수행한다.
2. 작은 기능 단위로 커밋하고 GitHub에 푸시한다.
3. Windows 로컬 작업 폴더에서 pull한다.
4. Windows에서 WPF 실행, 호스트 통합, 실제 입력과 파일 시스템 동작을 검증한다.
5. 검증 결과를 문서와 회귀 테스트에 반영한다.

WPF는 Windows에서만 실행된다. WSL에서는 플랫폼 비종속 Core 테스트와 가능한 빌드
검사를 수행하고, 최종 실행 검증은 반드시 Windows에서 수행한다.
