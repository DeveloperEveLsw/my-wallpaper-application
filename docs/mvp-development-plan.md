# MVP 개발 계획

- 상태: 구현 진행 중
- 현재 단계: M1 read-only snapshot과 첫 WPF 화면

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

현재 상태: WSL build와 Core 테스트 통과, Windows 실화면 검증 대기.

## M2 — 읽기 전용 동기화 완성

- Windows Shell 파일 아이콘과 이미지 썸네일
- `FileSystemWatcher` debounce와 전체 재스캔 복구
- 카드 사용자 순서 저장과 가로 drag 재정렬
- 빈 상태, 다량 파일, 권한·잠금·외부 변경 상태

## M3 — 기본 파일 명령

- 파일 열기와 탐색기에서 위치 열기
- 이름 변경과 Windows 이름 검증
- 휴지통 이동과 Glass 확인 모달
- 파일·폴더 Glass 우클릭 메뉴

모든 변경 명령은 Windows 임시 fixture에서 먼저 검증한다.

## M4 — 내부 drag & drop

- `…` → 폴더, 폴더 → `…`, 폴더 → 폴더 파일 이동
- 유효·무효 대상과 drag preview
- 충돌 시 `파일명 (n).ext` 제안·편집 모달
- 실패·취소·외부 변경 뒤 전체 snapshot 복구

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
- 설정 손상·루트 삭제·권한 오류
- 실제 사용자 카나리 루트 read/write 검증

## M8 — 후속 배경 렌더러

파일 UI MVP 완료 뒤에만 영상, 픽셀 애니메이션, 오디오 반응형 2D/3D 이퀄라이저를
Wallpaper Render Layer에 추가한다.
