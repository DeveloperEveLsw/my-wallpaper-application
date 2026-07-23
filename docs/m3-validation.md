# M3 검수 체크리스트

- 구현 상태: 완료
- 자동 검증: 통과
- Windows 세부 UI 검수: 통과, 문제 없음
- 기준일: 2026-07-20

## 구현된 범위

- 파일 단일 선택과 Windows 기본 연결 프로그램 더블클릭 실행
- 파일·실제 폴더의 Glass 우클릭 메뉴
- 파일·폴더의 탐색기 위치 열기
- Windows 이름 규칙과 동일 위치 충돌을 검증하는 파일·폴더 이름 변경
- 파일·폴더 휴지통 이동 전 Glass 확인 모달
- `IFileOperation`, `FOFX_RECYCLEONDELETE` 기반 휴지통 이동
- 작업 직전 루트 경계·허용 깊이·종류·재분석 지점 재검증
- 성공·실패·외부 변경 뒤 전체 snapshot 재스캔
- 폴더 이름 변경 뒤 저장된 Dock 순서 ID 이관

M3 메뉴는 `열기`, `탐색기에서 위치 열기`, `이름 변경`, `휴지통으로 이동`만 제공한다.
`Windows 추가 옵션 표시`와 순수 배경의 Windows 메뉴는 M5 범위다. 가상 `…` 카드는
실제 파일 시스템 경로가 아니므로 우클릭 파일 명령을 제공하지 않는다.

## 자동 검증 범위

Release build와 전체 테스트에서 다음 항목을 확인한다.

- 루트 직접 파일, 직접 자식 폴더와 그 안의 직접 파일만 명령 대상으로 허용
- 루트 밖 경로, 지원 깊이 밖 경로, 사라진 항목과 종류가 바뀐 항목 거부
- Windows 예약 이름, 금지 문자, 끝 공백·마침표와 255자 초과 이름 거부
- 파일·폴더 이름 변경과 폴더의 중첩 내용 보존
- 동일 이름 충돌, 잠긴 파일과 stale target에서 원본 상태 보존
- 대소문자만 바꾸는 Windows 이름 변경
- Windows 임시 fixture 폴더를 실제 휴지통으로 이동하고 루트는 보존
- WPF Release 빌드 경고 0, 오류 0

휴지통 서비스는 영구 삭제 API로 폴백하지 않는다. 자동 recycle 테스트 역시 매번 생성한
Windows 임시 fixture만 대상으로 한다.

## 사용자 검수 준비

Windows PowerShell에서 다음을 실행한다.

```powershell
./scripts/check.ps1
$fixture = ./scripts/new-m3-fixture.ps1
./scripts/run-dev-window.ps1 -RootPath $fixture.RootPath -Configuration Release
```

### 파일 입력과 메뉴

- `Work` 카드를 열고 파일을 한 번 클릭하면 해당 타일만 선택 표시되는지 확인한다.
- `open-with-notepad.txt`를 더블클릭하면 Windows 기본 연결 프로그램이 열리는지 확인한다.
- 파일과 실제 폴더를 우클릭하면 네 개의 Glass 명령이 순서대로 표시되는지 확인한다.
- 가상 `…` 카드를 우클릭해도 파일·폴더 메뉴가 열리지 않는지 확인한다.
- 메뉴가 화면 가장자리 밖으로 잘리지 않고 `Esc`와 바깥 클릭으로 닫히는지 확인한다.

### 탐색기에서 위치 열기

- `Work/open-with-notepad.txt`에서 실행하면 Explorer가 해당 파일을 선택하는지 확인한다.
- 실제 폴더 카드에서 실행하면 Explorer가 루트 안의 해당 폴더를 선택하는지 확인한다.

### 이름 변경

- `rename-file.txt`를 `renamed-file.txt`로 변경하고 열린 모달이 새 snapshot을 표시하는지 확인한다.
- `collision.txt`, `CON.txt`, `bad?.txt`, 끝 공백·마침표를 입력하면 원본이 유지되는지 확인한다.
- `RenameFolder`를 다른 이름으로 변경한 뒤 `keep-after-folder-rename.txt`가 보존되는지 확인한다.
- Dock을 먼저 재정렬한 뒤 폴더 이름을 변경하고 앱을 재시작해 카드 위치가 유지되는지 확인한다.
- 외부에서 대상을 삭제한 뒤 열린 이름 변경창을 확정하면 stale 오류와 재스캔이 표시되는지 확인한다.

### 휴지통 이동

- `recycle-file.txt`의 확인창에서 취소하면 파일이 그대로 남는지 확인한다.
- 다시 실행해 확인하면 파일이 사라지고 Windows 휴지통에서 찾을 수 있는지 확인한다.
- `RecycleFolder` 확인창에 화면에 보이지 않는 하위 내용도 함께 이동된다는 경고가 표시되는지 확인한다.
- 폴더 이동을 확인한 뒤 `Nested-Not-Visible/nested-file.txt`까지 휴지통으로 이동했는지 확인한다.
- 파일 잠금이나 권한 오류가 발생해도 영구 삭제되지 않고 재스캔으로 복구되는지 확인한다.

### M2 회귀

- Shell 아이콘과 이미지 썸네일, 대량 파일 가상화가 그대로 동작하는지 확인한다.
- 외부 생성·이름 변경·삭제가 watcher debounce 뒤 열린 모달에 반영되는지 확인한다.
- 루트 삭제·복구와 Dock 카드 drag 재정렬·재시작 복원이 유지되는지 확인한다.
