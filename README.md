# My Wallpaper Application

실제 Windows 파일과 폴더를 감성적인 라이브 월페이퍼 안에 정돈해 보여주는
WPF 기반 데스크톱 애플리케이션이다.

화면 하단에는 macOS Dock에서 영감을 받은 글래스 카드가 배치된다. 사용자가 지정한
루트 폴더의 직접 자식 폴더는 카드로 표시하고, 루트 바로 아래의 파일은 가상 `…`
카드에 모아 표시한다. 카드에서 수행한 파일 이동은 실제 파일 시스템에 반영된다.

## 현재 상태

- 제품 요구사항과 네이티브 아키텍처를 정의하는 단계
- WPF 애플리케이션이 제품 본체
- Wallpaper Engine과 Lively는 선택 가능한 실행 호스트
- Wallpaper Engine은 로컬 Application Wallpaper로 사용하며 공개 Workshop 배포는 범위 밖
- 아직 애플리케이션 코드는 생성하지 않음

## 기준 문서

- [제품 명세](docs/product-spec.md)
- [기술 아키텍처](docs/architecture.md)
- [미결정 사항](docs/open-questions.md)
- [WPF 네이티브 런타임 결정](docs/decisions/0001-wpf-native-runtime.md)

## 개발·검증 흐름

1. WSL의 Linux 파일 시스템에서 소스 작성과 자동 테스트를 수행한다.
2. 작은 기능 단위로 커밋하고 GitHub에 푸시한다.
3. Windows 로컬 작업 폴더에서 pull한다.
4. Windows에서 WPF 실행, 호스트 통합, 실제 입력과 파일 시스템 동작을 검증한다.
5. 검증 결과를 문서와 회귀 테스트에 반영한다.

WPF는 Windows에서만 실행된다. WSL에서는 플랫폼 비종속 Core 테스트와 가능한 빌드
검사를 수행하고, 최종 실행 검증은 반드시 Windows에서 수행한다.
