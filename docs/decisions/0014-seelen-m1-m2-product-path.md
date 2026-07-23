# ADR 0014: Seelen M1·M2 제품 경로와 읽기 전용 프로젝션

- 상태: 구현됨, Windows 실기기 검수 대기
- 결정일: 2026-07-24
- 적용 범위: Seelen 제품 Companion과 Desktop 위젯의 M1·M2

## 결정

M0 스파이크는 회귀 근거로 보존하고 제품 코드는 다음 세 경계로 분리한다.

```text
Wallpaper.Seelen.Widgets
        │ authenticated WebSocket + HTTP visual
        ▼
Wallpaper.Seelen.Companion (Windows)
        │
        ├─ Wallpaper.Seelen (projection orchestration)
        ├─ Wallpaper.Core (shallow scan, ordering)
        └─ Wallpaper.Infrastructure.Windows (watcher, settings, Shell visual)
```

`Wallpaper.Seelen`은 WSL에서 실행 가능한 `net10.0` 경계다. 실제 루트 snapshot, 상태,
폴더 순서, visual URL과 watcher 재스캔을 관리한다. Windows 전용 Companion은 M0에서
검증한 exact Origin/Host, HMAC bootstrap, one-time nonce와 memory-only session을
유지한다.

제품 프로토콜은 버전 2다. `helloAck`에 최초 snapshot과 watcher 상태를 싣고 이후
파일 변경은 `snapshot` 메시지로 push한다. 폴더 순서와 루트 변경은 인증된 WebSocket
명령으로만 받으며 설정 저장 뒤 전체 재스캔한다.

아이콘과 썸네일은 파일 경로를 위젯에 노출하지 않고 인증된
`/visual/{kind}/{fileId}`로 제공한다. Companion은 현재 snapshot에 포함된 ID만 실제
경로로 해석하고 기존 Windows Shell visual 서비스의 제한 캐시를 재사용한다.

위젯 파일 목록은 58px 고정 행과 overscan을 사용해 viewport 주변 행만 DOM에 만든다.
따라서 1,200개 fixture도 전체 DOM 노드나 visual 요청을 한 번에 만들지 않는다.

## M1 범위

- 제품용 Companion/Widget 구조
- 실제 Desktop 또는 저장된 사용자 루트의 얕은 snapshot
- 직접 자식 폴더 Dock, 가상 `…`, 단일 폴더 파일 모달
- 숨김·시스템·재분석 지점 제외와 오류 상태
- 우측 상단 1초 hover 설정 패널, Windows 네이티브 폴더 선택과 루트 변경

## M2 범위

- Windows Shell 아이콘과 이미지 썸네일
- debounce watcher와 모든 변경 뒤 전체 snapshot
- 폴더 카드 drag 순서와 원자적 JSON 저장
- 빈 상태, 경고, 루트 삭제·복구와 설정 손상 복구
- 1,200개 파일 가상 목록

## 검증 경계

WSL에서는 projection, watcher, 설정, 1,200개 항목, 제품 Companion Windows-target
compile과 위젯 bundle을 검증한다. Seelen 표시, Windows Shell visual의 실제 결과,
DPI/input과 Explorer 외부 변경은 Windows 실기기 검수로 분리한다.
