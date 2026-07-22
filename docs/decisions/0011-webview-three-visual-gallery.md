# ADR 0011: WebView2/three.js Visual Gallery

- 상태: 채택
- 날짜: 2026-07-23

## 배경

Wallpaper Render Layer는 WPF Dock, 파일 모달과 네이티브 Shell 입력을 가리지 않는 3D 렌더
경로가 필요하다. 구체적인 작품을 설계하기 전에 composition, 배포 자산, resize와 호스트
pause/resume이 함께 동작하는지 작은 장면으로 먼저 검증한다. 이후에는 같은 경계 안에서 여러
작품을 선택하는 Visual Gallery로 확장할 수 있어야 한다.

## 결정

- WPF 합성 surface로 일반 `WebView2`가 아닌 `WebView2CompositionControl`을 사용한다.
- `WebView2CompositionControl`의 WinRT projection이 publish에 포함되도록 WPF 프로젝트는
  `net10.0-windows10.0.17763.0`을 대상으로 한다.
- 3D scene은 three.js `WebGLRenderer`로 구현한다.
- scene은 registry에 descriptor와 factory를 등록한다. WPF는 scene id만 선택한다.
- 현재 배포에 필요한 three.js module을 앱에 vendor한다. CDN에 의존하지 않는다.
- renderer의 pause/resume은 기존 `IWallpaperRenderLifecycle`을 WebView message로 전달한다.
- 초기 `Baseline` 장면은 기본 조명, 회전 cube와 바닥만 사용한다. 특정 작품의 시각 언어와
  오디오 반응 계약은 이 검증 범위에 포함하지 않는다.

## 결과와 검증 경계

- WebView2 초기화, 오프라인 자산 로딩, resize와 pause/resume을 작은 장면으로 검증할 수 있다.
- 새 작품은 WPF와 Wallpaper Engine host를 변경하지 않고 registry에 추가할 수 있다.
- WPF 상위 레이어의 hit testing과 native menu 소유권이 유지된다.
- WebView2 process와 WebGL context는 native Direct3D surface보다 관리 부담이 낮다.
- `WebView2CompositionControl`의 capture 비용과 4K 성능은 Windows 실기기 검수 항목이다.
- 작품 선택 UI, 시각 연출과 오디오 반응은 요구사항을 구체화한 뒤 별도 결정으로 추가한다.

## 공식 근거

- [WPF의 WebView2CompositionControl](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf)
- [three.js WebGLRenderer](https://threejs.org/docs/pages/WebGLRenderer.html)
