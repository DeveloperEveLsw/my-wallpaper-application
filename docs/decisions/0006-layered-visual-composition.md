# ADR 0006: 동적 배경과 데스크톱 UI의 레이어 분리

- 상태: 채택
- 날짜: 2026-07-19

## 배경

Dock, 시계, 폴더 모달과 미디어 정보 카드는 파일·상태 이벤트에 반응하는 비교적 정적인
UI다. 미래에는 그 뒤에 영상, 픽셀 애니메이션, 음성 데이터 반응형 2D/3D 이퀄라이저처럼
지속적으로 프레임을 생성하는 배경이 추가될 수 있다.

두 종류를 하나의 렌더 루프와 구현에 결합하면 배경 FPS마다 파일 UI가 갱신되고, 미래
렌더링 기술 선택이 현재 WPF 입력과 레이아웃을 다시 만들게 할 수 있다.

## 결정

화면을 다음 순서의 독립 레이어로 구성한다.

1. Wallpaper Render Layer
2. Desktop UI Layer
3. Transient Layer

Wallpaper Render Layer는 독립적인 수명, FPS, pause/resume과 device recovery 계약을
갖는다. Desktop UI Layer는 Dock, 모달, 시계와 미디어 정보 카드를 소유한다. Transient
Layer는 drag preview, context menu, 확인창과 설정 패널을 소유한다.

현재 구현은 배경 placeholder와 추상 계약만 포함한다. 영상, 오디오 분석, 2D/3D
이퀄라이저와 픽셀 애니메이션은 파일 UI 목표가 완료된 뒤 별도로 설계한다.

## 합성 제약

배경 renderer는 WPF 상위 레이어와 합성 가능한 결과를 제공해야 한다. 같은 top-level
window 안에 별도 child HWND를 두는 `HwndHost`를 기본 renderer surface로 사용하지 않는다.
이는 WPF airspace 및 입력 제약 때문에 Dock과 모달보다 위에 나타날 수 있기 때문이다.

DirectX가 필요해지면 `D3DImage` 또는 당시 검증된 composition-compatible surface를
비교한다. 이 ADR은 Direct3D 버전이나 최종 렌더링 엔진을 선택하지 않는다.

## 데이터 경계

미래의 오디오·미디어 수집 서비스는 immutable snapshot을 발행한다. 배경 renderer와
미디어 정보 카드는 같은 snapshot을 소비할 수 있지만 서로의 내부 상태를 참조하지 않는다.

## 공식 근거

- [WPF와 Win32 interop의 airspace 제약](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation)
- [WPF `D3DImage`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.d3dimage)
