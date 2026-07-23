# ADR 0013: Seelen UI 위젯 + .NET Companion M0 스파이크

- 상태: 승인됨
- 결정일: 2026-07-24
- 적용 범위: M0 기술 검증 코드와 판정 게이트

## 배경

Wallpaper Engine Application Wallpaper 안에 WPF/WebView2/three.js 화면을 배치하는 기존
제품 경로를 Seelen UI 위젯과 별도 .NET Companion 구조로 바꾸려 한다. 다만 Seelen의
Desktop/Popup 입력, 제3자 위젯 `Run` 권한, loopback 통신, 재시작 복구와 Windows 실기기
경계가 검증되기 전에는 기존 제품 코드를 제거할 수 없다.

## 결정

M0를 기존 `src/Wallpaper.Core`, `src/Wallpaper.Infrastructure.Windows`,
`src/Wallpaper.Hosts` 및 WPF 렌더링 프로젝트와 참조 관계가 없는 `spikes/seelen-m0`에
구현한다.

실험 경계는 다음과 같다.

```text
Seelen Desktop widget
  ├─ 256-bit one-time nonce 생성
  ├─ Seelen Run (elevated: false)
  ├─ HMAC bootstrap proof로 loopback 포트 식별
  ├─ WebSocket hello → memory-only session token
  ├─ HTTP icon/image Blob 검증
  └─ Popup trigger
                  │
                  ▼
.NET Companion singleton
  ├─ Local named mutex
  ├─ current-user-only named pipe로 새 nonce 전달
  ├─ Kestrel 127.0.0.1:43127-43135
  ├─ exact Origin + exact Host 검사
  ├─ nonce one-time consume
  └─ Windows DesktopDirectory를 read-only root로 보고
```

Companion은 `0.0.0.0`, IPv6 any, LAN 주소에 bind하지 않는다. 허용 Origin은 M0 SDK
기준 실제 예상값인 `http://tauri.localhost` 하나이며 ordinal exact match만 허용한다.
Host는 Companion이 선택한 `127.0.0.1:<port>` 하나만 허용한다. 실제 설치된 Seelen에서
Origin이 다르면 wildcard 또는 suffix match로 넓히지 않고 M0 실패로 기록한 뒤 명시적인
allowlist 변경을 다시 검토한다.

위젯이 여러 후보 포트를 찾을 때 nonce를 미확인 서비스에 보내지 않는다. 먼저
`SHA-256(nonce)` 식별자와 임의 challenge를 보내고 Companion이 반환한
`HMAC-SHA256(nonce, challenge)`를 검증한다. 증명이 맞는 포트에만 WebSocket hello와
nonce를 전송한다.

첫 Companion 프로세스가 Kestrel과 현재 사용자 전용 named pipe를 소유한다. 위젯
재로드나 Seelen 재시작으로 실행된 후속 프로세스는 새 nonce와 Origin만 기존 프로세스에
전달하고 종료한다. nonce와 세션 토큰은 파일, 설정, localStorage 및 로그에 저장하지
않는다.

실제 루트는 `Environment.SpecialFolder.DesktopDirectory`를 통해 확인한다. Companion은
Explorer 바탕 화면 아이콘 표시 설정을 변경·감시·복원하지 않는다.

## M0 한계와 인수한 위험

- 파일 스캔, watcher, 파일 명령과 제품 UI 이식은 M1 이후 범위다.
- M0 HTTP 응답은 통신 경계 검증용 내장 PNG 두 개뿐이다.
- 포트 범위는 기술 스파이크용 고정 allowlist이며 제품 확정값이 아니다.
- 현재 사용자 전용 named pipe는 동일 사용자 세션 내부 부트스트랩 전달만 담당한다.
- 절전 복귀, 전 DPI 조합, 실제 포트 충돌, 방화벽 prompt와 redirected Desktop은
  M0 종료 시점에 미수행 위험으로 남아 있으며 제품화 전에 다시 검증한다.
- Desktop 위젯은 최종 M0 결정에 따라 모니터별 복제가 아닌 `Single` 인스턴스를 쓴다.

## 결과

2026-07-24 M0를 통과로 종료했다. Desktop/Popup 입력, Seelen `Run`, 싱글턴 Companion,
인증된 loopback WebSocket/HTTP Blob, exact Origin/Host, 실제 Desktop root, 수동
재연결과 사용자 리소스 폴더 영구 설치를 Windows Seelen 2.8에서 확인했다. 초기 손상
PNG와 세션 전용 리소스 등록 문제는 수정·재검증했다.

M1에서는 기존 Core 및 Windows 파일 서비스의 Companion 쪽 재사용 경계를 설계할 수
있다. 기존 Wallpaper Engine, WPF WebView2 및 three.js 파일 삭제는 이번 M0 종료에
포함되지 않으며 별도 변경으로만 수행한다.

검수 항목과 현재 결과는 [M0 검수 기록](../m0-seelen-validation.md)에 둔다.

## SDK 근거

- [Seelen widget guidelines](https://github.com/eythaann/Seelen-UI/blob/main/documentation/widget-guidelines.md)
- [Seelen widget JS API](https://github.com/eythaann/Seelen-UI/blob/main/documentation/widget-js-api.md)
- [Seelen resource load/bundle workflow](https://github.com/eythaann/Seelen-UI/blob/main/documentation/resource-guidelines.md)
- 기준 소스: Seelen UI `b2fc871c41a1e947e47793e6514080b33f2d781d`
- 위젯 라이브러리: `@seelen-ui/lib` 2.8.0
