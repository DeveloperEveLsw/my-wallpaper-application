# MVP 개발 계획

- 상태: 구현 진행 중
- 현재 단계: M4 구현 완료, Windows 사용자 검수 준비

## M0 — 기준선

- 제품 명세와 ADR
- WSL → GitHub → Windows pull 검증 흐름
- .NET 10 솔루션, Release build와 단위 테스트

통과 상태: 완료.

## M1 — 읽기 전용 수직 경로

- WPF Standalone 실행
- WPF 네이티브 루트 선택과 앱 로컬 설정
- 루트 직접 자식 폴더와 파일의 얕은 snapshot
- 폴더 Dock, 고정 `| …`, 단일 파일 모달
- 숨김·시스템·재분석 지점 제외
- 우측 상단 1초 hover 설정 패널

통과 상태: WSL build·Core 테스트와 Windows 실화면 검증 완료.

## M2 — 읽기 전용 동기화 완성

- Windows Shell 파일 아이콘과 이미지 썸네일
- `FileSystemWatcher` debounce와 전체 재스캔 복구
- 카드 사용자 순서 저장과 가로 drag 재정렬
- 빈 상태, 다량 파일, 루트 삭제·복구, 권한·잠금·외부 변경 상태

통과 상태: 완료. Shell 아이콘·썸네일, debounce 재스캔, 카드 순서 재시작 복원,
1,200개 파일 가상화와 루트·권한 오류 복구를 Windows에서 검증했다.

검증 기록: [M2 검증 기록](m2-validation.md)

## M3 — 기본 파일 명령

- 파일 열기와 탐색기에서 위치 열기
- 이름 변경과 Windows 이름 검증
- 휴지통 이동과 Glass 확인 모달
- 파일·폴더 Glass 우클릭 메뉴

모든 변경 명령은 Windows 임시 fixture에서 먼저 검증한다.

구현 상태: 완료. 명령 직전 루트 경계·깊이·항목 종류·재분석 지점을 다시 검증하며,
이름 변경과 실제 Windows 휴지통 이동을 임시 fixture 자동 테스트로 검증했다.

사용자 검수: 완료. 문제 없음. [M3 검수 기록](m3-validation.md)

## M4 — 내부 drag & drop

- `…` → 폴더, 폴더 → `…`, 폴더 → 폴더 파일 이동
- 유효·무효 대상과 drag preview
- 충돌 시 `파일명 (n).ext` 제안·편집 모달
- 실패·취소·외부 변경 뒤 전체 snapshot 복구

구현 상태: 완료. 세 파일 이동 경로, drag preview와 유효·무효 대상, 충돌 이름 제안·편집,
이동 직전 안전 검증과 모든 종료 경로의 전체 재스캔을 구현했다.

사용자 검수: [M4 검수 체크리스트](m4-validation.md)

## M5 — Windows Shell과 입력

- `Windows 추가 옵션 표시`
- 빈 배경의 실제 Windows Desktop 컨텍스트 메뉴
- Dock·모달·배경 hit-test 경계
- Wallpaper Engine 적용 상태의 우클릭 중복·포커스 검증

## M6 — Wallpaper Engine 로컬 호스트

- 로컬 Application Wallpaper package
- WPF HWND 탐지·배치·종료
- pause/resume, reload와 Explorer 재시작 복구
- 원본 Desktop 아이콘 숨김은 Wallpaper Engine 설정 사용

## M7 — 안정화

- 성능·DPI·작업 표시줄 work area
- 다량 카드와 파일
- 설정 손상과 반복 복구 회귀
- 실제 사용자 카나리 루트 read/write 검증

## M8 — 후속 배경 렌더러

파일 UI MVP 완료 뒤에만 영상, 픽셀 애니메이션, 오디오 반응형 2D/3D 이퀄라이저를
Wallpaper Render Layer에 추가한다.
