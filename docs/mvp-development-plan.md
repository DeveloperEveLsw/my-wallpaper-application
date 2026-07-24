# MVP 개발 계획

> 현재 제품 경로는 Seelen UI Desktop 위젯 + .NET Companion이다. Wallpaper Engine/WPF
> 경로의 M0~M8 기록은 재사용 가능한 구현과 회귀 근거로 아래에 보존한다.

- 상태: Seelen M3·M4 구현 및 WSL 자동 검증 완료
- 현재 단계: M3·M4 Windows Seelen 사용자 검수 준비 완료
- 현재 기준선: 프로토콜 4, 위젯 0.4.1, Release build 경고·오류 0,
  자동 테스트 151/151, 제품 위젯 테스트 9/9
- 현재 검수: [Seelen M3·M4 검수 기록](m3-m4-seelen-validation.md)

## 현재 Seelen 로드맵

| 단계 | 목표 | 상태/종료 게이트 |
|---|---|---|
| M0 | Seelen 위젯, 로컬 Companion, 인증 통신과 배치 가능성 검증 | 완료 |
| M1 | 얕은 snapshot, 폴더 Dock, `루트` 파일 모달과 Seelen 루트 설정 | 완료 |
| M2 | Shell visual, watcher, 순서 저장, 오류 복구와 1,200개 가상화 | 완료 |
| M3 | 열기, 탐색기 위치, 이름 변경, 휴지통과 Glass 항목 메뉴 | 구현·자동 검증 완료, Windows 검수 대기 |
| M4 | `루트` ↔ 폴더, 폴더 ↔ 폴더 내부 파일 이동과 충돌 처리 | 구현·자동 검증 완료, Windows 검수 대기 |
| M5 | Windows 추가 옵션, Desktop 배경 메뉴와 입력 경계 | 다음 단계 |
| M6 | Companion 설치·업데이트·시작·복구·진단 등 제품 운용 경로 | 대기 |
| M7 | 성능, DPI/work area, 반복 복구와 실제 사용자 카나리 안정화 | 대기 |
| M8 | 영상·픽셀·오디오 반응형 2D/3D 배경 렌더러 | 파일 UI MVP 후속 |

### 단계 운영 원칙

1. 각 단계는 WSL/Windows 자동 검증을 먼저 통과한다.
2. 파일을 변경하는 M3 이후 검수는 생성형 fixture에서만 시작한다.
3. Windows Seelen 실기기 체크리스트와 발견 결함 재검수까지 끝나야 완료로 판정한다.
4. 실제 사용자 루트의 쓰기 검수는 M7에서 명시적으로 선택한 카나리 항목으로 제한한다.
5. M8은 M7까지의 파일 UI와 운영 경로가 안정화된 뒤 시작한다.

## 보존된 Wallpaper Engine/WPF 이력

이하 단계는 현재 실행 계획이 아니라 이미 구현·검증한 이전 제품 경로다. Core,
Windows 파일 서비스, fixture와 검수 결과는 같은 번호의 Seelen 단계에서 재사용한다.

### M0 — 기준선

- 제품 명세와 ADR
- WSL → GitHub → Windows pull 검증 흐름
- .NET 10 솔루션, Release build와 단위 테스트

통과 상태: 완료.

### M1 — 읽기 전용 수직 경로

- WPF Standalone 실행
- WPF 네이티브 루트 선택과 앱 로컬 설정
- 루트 직접 자식 폴더와 파일의 얕은 snapshot
- 폴더 Dock, 고정 `| …`, 단일 파일 모달
- 숨김·시스템·재분석 지점 제외
- 우측 상단 1초 hover 설정 패널

통과 상태: WSL build·Core 테스트와 Windows 실화면 검증 완료.

### M2 — 읽기 전용 동기화 완성

- Windows Shell 파일 아이콘과 이미지 썸네일
- `FileSystemWatcher` debounce와 전체 재스캔 복구
- 카드 사용자 순서 저장과 가로 drag 재정렬
- 빈 상태, 다량 파일, 루트 삭제·복구, 권한·잠금·외부 변경 상태

통과 상태: 완료. Shell 아이콘·썸네일, debounce 재스캔, 카드 순서 재시작 복원,
1,200개 파일 가상화와 루트·권한 오류 복구를 Windows에서 검증했다.

검증 기록: [M2 검증 기록](m2-validation.md)

### M3 — 기본 파일 명령

- 파일 열기와 탐색기에서 위치 열기
- 이름 변경과 Windows 이름 검증
- 휴지통 이동과 Glass 확인 모달
- 파일·폴더 Glass 우클릭 메뉴

모든 변경 명령은 Windows 임시 fixture에서 먼저 검증한다.

구현 상태: 완료. 명령 직전 루트 경계·깊이·항목 종류·재분석 지점을 다시 검증하며,
이름 변경과 실제 Windows 휴지통 이동을 임시 fixture 자동 테스트로 검증했다.

사용자 검수: 완료. 문제 없음. [M3 검수 기록](m3-validation.md)

### M4 — 내부 drag & drop

- `…` → 폴더, 폴더 → `…`, 폴더 → 폴더 파일 이동
- 유효·무효 대상과 drag preview
- 충돌 시 `파일명 (n).ext` 제안·편집 모달
- 실패·취소·외부 변경 뒤 전체 snapshot 복구

구현 상태: 완료. 세 파일 이동 경로, drag preview와 유효·무효 대상, 충돌 이름 제안·편집,
이동 직전 안전 검증과 모든 종료 경로의 전체 재스캔을 구현했다.

사용자 검수: [M4 검수 체크리스트](m4-validation.md)

### M5 — Windows Shell과 입력

- `Windows 추가 옵션 표시`
- 빈 배경의 실제 Windows Desktop 컨텍스트 메뉴
- Dock·모달·배경 hit-test 경계

통과 상태: 완료. 파일·폴더와 Desktop 뷰 배경의 실제 클래식 Shell 메뉴를 직접
표시하고, owner-drawn 확장 호환성, 최초 우클릭 좌표 보존,
명시적 hit-test 경계와 메뉴 종료 뒤 재스캔을 구현했다. Windows 11 Standalone에서 메뉴
내용·명령 실행·취소·포커스와 입력 소유권을 검증했다.

검증 기록: [M5 검증 기록](m5-validation.md)

### M6 — Wallpaper Engine 로컬 호스트

- 첫 통합 게이트: 우클릭 중복·포커스 복귀·parent HWND 배치 검증
- 로컬 Application Wallpaper package
- WPF HWND 탐지·배치·종료
- pause/resume, reload와 Explorer 재시작 복구
- 원본 Desktop 아이콘 숨김은 Wallpaper Engine 설정 사용

통과 상태: 완료. 실제 Wallpaper Engine 2.8.42에서 전달된 `-parentHWND` 아래에 WPF
HWND를 배치하고, 우클릭 단일 메뉴·Desktop 포커스 복귀·pause/resume·reload를 검증했다.
Explorer 재시작으로 끊어진 worker 계층은 감지 후 제한된 엔진 재시작으로 복구하며,
WorkerW를 자르지 않는다. Wallpaper Engine 소유 HWND는 초기 attach에서만 입력 가능
상태로 전환하고 실행 중 host poll은 창을 반복 변경하지 않는다.
엔진 종료는 suspend되지 않는
watchdog이 고아 WPF 프로세스 없이 처리하고 앱·엔진 강제 종료 뒤 Explorer 입력 상태도
복원한다. 원본 Desktop 아이콘은 Wallpaper Engine 공식 제어 명령만 사용한다.

검증 기록: [M6 검수 기록](m6-validation.md)

### M7 — 안정화

- 성능·DPI·작업 표시줄 work area
- 다량 카드와 파일
- 설정 손상과 반복 복구 회귀
- 실제 사용자 카나리 루트 read/write 검증

### M8 — 후속 배경 렌더러

파일 UI MVP 완료 뒤에만 영상, 픽셀 애니메이션, 오디오 반응형 2D/3D 이퀄라이저를
Wallpaper Render Layer에 추가한다.
