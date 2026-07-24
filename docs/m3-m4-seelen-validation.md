# Seelen M3·M4 검수 기록

- 구현 상태: M3·M4 코드 완료
- WSL/자동 검증: 통과
- Windows Seelen 실기기 검수: 통과
- 기준일: 2026-07-24
- 기준 프로토콜: 4
- 제품 위젯 버전: 0.4.1

## 구현 범위

### M3 기본 파일 명령

- 파일 단일 선택과 더블클릭·`Enter` 기본 연결 프로그램 실행
- 파일·실제 폴더의 Glass 우클릭 메뉴
- 파일·폴더 열기와 탐색기에서 위치 열기
- Windows 이름 규칙과 같은 위치 충돌을 검증하는 이름 변경
- 폴더 이름 변경 뒤 저장된 Dock 순서 ID 이관
- 파일·폴더 recycle 전 Glass 확인
- 폴더의 표시되지 않는 하위 내용 경고
- Windows Shell recycle만 사용하고 영구 삭제 폴백 금지
- stale target과 명령 실패 뒤 전체 snapshot 복구

가상 `루트` 카드는 실제 항목이 아니므로 M3 우클릭 명령을 제공하지 않는다.
`Windows 추가 옵션 표시`는 M5 범위다.

### M4 내부 파일 이동

- 루트 파일 → 실제 폴더
- 실제 폴더 파일 → 루트
- 실제 폴더 파일 → 다른 실제 폴더
- HTML5 drag 대신 Seelen WebView 호환 pointer capture 상태 머신
- 5px 이동 임계값과 Dock 순서 drag·파일 drag 상태 분리
- 파일명 drag preview
- 출발 카드 무효 표시와 이동 가능한 카드 유효 표시
- 대소문자 무시 충돌 검사와 표시되지 않는 하위 폴더 이름 포함
- 확장자를 보존한 가장 작은 `파일명 (n).ext` 제안
- 제안 이름 편집과 Windows 이름 검증
- 사용자 확인 없는 덮어쓰기 금지
- 이동 직전 출발·목적지·충돌 재검증
- 동시 충돌 발생 시 새 이름 재제안
- 성공·실패·취소·외부 변경 뒤 전체 snapshot 복구

M4는 단일 파일 이동만 제공한다. 폴더 이동, 복사, 다중 선택, 실행 취소와 외부
애플리케이션 drag & drop은 범위 밖이다.

## 안전 경계

- 위젯은 실제 경로나 항목 종류를 명령 인자로 보내지 않는다.
- Companion은 현재 projection의 `itemId`와 `destinationId`만 해석한다.
- 실제 파일 명령 직전에 루트 경계, 깊이, 종류와 재분석 지점을 다시 검증한다.
- 변경 명령은 한 번에 하나씩 실행한다.
- `requestId` 결과 캐시로 같은 변경 요청의 이중 실행을 막는다.
- 같은 이름이 있으면 이동을 거부하며 `overwrite: false`를 유지한다.
- recycle은 `IFileOperation` 경로를 유지하고 영구 삭제 API로 폴백하지 않는다.
- 실제 파일 시스템과 UI가 다르면 재스캔한 파일 시스템 상태를 우선한다.

## WSL 자동 검증 결과

WSL에서 Windows .NET 10 SDK를 명시해 다음을 실행했다.

```bash
WALLPAPER_DOTNET='/mnt/c/Program Files/dotnet/dotnet.exe' ./scripts/check.sh
```

| 범위 | 결과 |
|---|---|
| 전체 Release build | 경고 0, 오류 0 |
| 전체 .NET 테스트 | 151/151 통과 |
| `Wallpaper.Seelen.Tests` | 27/27 통과 |
| 제품 위젯 Node 테스트 | 9/9 통과 |
| M0·제품 위젯 bundle | 통과 |
| 생성 JavaScript 구문 검사 | 통과 |
| npm audit | 취약점 0 |
| Seelen 배치·fixture·검사 PowerShell 구문 | 통과 |
| M3·M4 fixture 생성과 고유 경로 | 통과, 임시 fixture 3개 정리 |

자동 테스트는 다음 M3·M4 경계를 직접 확인한다.

- 파일·실제 폴더 명령 대상과 루트·폴더 이동 목적지 분리
- 가상 `루트`의 M3 항목 명령 거부
- 프로토콜 4 메시지 필드, 잘못된 명령 거부와 ack 직렬화
- 열기·탐색기 위치 명령의 파일/폴더 종류 전달
- 파일·폴더 이름 변경과 중첩 내용 보존
- 폴더 이름 변경 뒤 Dock 위치와 설정 ID 보존
- 폴더 recycle 뒤 projection과 저장 순서 정리
- 예약 이름, 금지 문자, 끝 공백·마침표, 255자 경계
- stale target 거부와 snapshot 복구
- 루트 → 폴더, 폴더 → 루트, 폴더 → 폴더 이동과 내용 보존
- 같은 카드와 가상 출발 항목 거부
- 파일·폴더를 포함한 충돌과 가장 작은 접미사 제안
- 준비 뒤 외부에서 생긴 충돌 재검증과 원본 보존
- 같은 `requestId` 재전송의 단일 실행
- 실행 중인 파일 명령이 끝날 때까지 루트 변경을 직렬 대기
- Glass 메뉴 정적 DOM 계약, 작업 영역 배치와 유효·무효 drop 정책
- 5px 포인터 drag 임계값, Dock 순서 계산과 HTML5 drag API 비사용 계약

기존 `Wallpaper.Infrastructure.Windows.Tests`는 실제 Windows 임시 fixture의 이름 변경,
세 이동 경로, 잠긴 파일, 동시 충돌과 Windows recycle 경계를 계속 검증한다.

## Windows 배치

Windows 로컬 checkout에서 다음을 실행한다.

```powershell
git pull --ff-only origin main
./scripts/check.ps1
./scripts/prepare-seelen.ps1
```

현재 Seelen 세션에 개발 리소스를 바로 다시 싣고 싶으면 다음을 사용한다.

```powershell
./scripts/prepare-seelen.ps1 -LoadDevelopmentResource
```

그렇지 않으면 Seelen을 재시작하고 `@wallpaper/desktop`을 활성화한다. 각 fixture를 만든
뒤 Seelen 위젯 설정에서 `기본 Desktop 폴더 사용`을 끄고 `사용자 지정 루트 경로`에
해당 `$fixture.RootPath`를 입력한다.

## M3 Windows 실기기 체크리스트

```powershell
$fixture = ./scripts/new-m3-fixture.ps1
```

1. `Work` 파일을 한 번 클릭하면 해당 타일만 선택된다.
2. 파일 더블클릭과 `Enter`가 기본 연결 프로그램을 연다.
3. 파일과 실제 폴더 우클릭에 `열기`, `탐색기에서 위치 열기`, `이름 변경`,
   `휴지통으로 이동`만 나타난다.
4. 가상 `루트` 카드에는 항목 메뉴가 나타나지 않는다.
5. 메뉴가 화면 밖으로 잘리지 않고 바깥 클릭과 `Esc`로 닫힌다.
6. 탐색기 위치 열기가 파일 또는 폴더를 선택한다.
7. 파일을 이름 변경하면 열린 모달과 실제 경로가 갱신된다.
8. 폴더 이름 변경 뒤 중첩 내용과 기존 Dock 위치가 보존된다.
9. `collision.txt`, `CON.txt`, `bad?.txt`, 끝 공백·마침표를 거부한다.
10. recycle 확인을 취소하면 원본이 유지된다.
11. recycle 확인 뒤 파일 또는 폴더가 Windows 휴지통에 존재한다.
12. 폴더 recycle 확인문이 표시되지 않는 하위 내용도 함께 이동한다고 알린다.
13. 외부에서 대상을 삭제한 뒤 확인하면 stale 오류와 최신 snapshot을 표시한다.

## M4 Windows 실기기 체크리스트

```powershell
$fixture = ./scripts/new-m4-fixture.ps1
```

1. 파일 타일을 5px 미만 움직이면 클릭으로 남고, 5px 이상 움직이면 파일 이름 preview가
   포인터를 따라간다.
2. 출발 카드는 붉은 이동 불가, 다른 폴더와 `루트`는 초록 이동 가능 상태다.
3. `root-to-work.txt`를 `루트`에서 `Work`로 이동한다.
4. `work-to-root.txt`를 `Work`에서 `루트`로 이동한다.
5. `work-to-archive.txt`를 `Work`에서 `Archive`로 이동한다.
6. 세 이동에서 실제 경로와 내용이 보존되고 snapshot에 중복·유령 항목이 없다.
7. `collision.txt`를 `Archive`로 끌면 `collision (3).txt`를 제안한다.
8. 제안을 `moved-collision.txt`로 바꿔 이동하고 기존 파일·폴더 세 항목을 보존한다.
9. 충돌 대화상자에서 `CON.txt`, `bad?.txt`, 끝 공백·마침표의 이동 버튼이 비활성화된다.
10. 충돌 대화상자를 취소하면 출발과 목적 항목을 모두 보존한다.
11. 대화상자 중 제안 이름을 외부에서 만들면 덮어쓰지 않고 새 이름을 제안한다.
12. 잠긴 파일 이동은 실패하고 출발 파일을 보존한다.
13. 출발 파일 또는 목적 폴더를 외부에서 변경하면 stale 오류와 최신 snapshot을 표시한다.
14. Dock 폴더 자체 drag는 5px 뒤 시작하며 파일 이동이 아니라 기존 순서 재정렬로
    동작한다.
15. 파일 또는 폴더를 빈 배경 위로 이동했다가 유효 대상으로 가져와도 drag가 끊기지
    않는다.
16. `Esc`, 포인터 취소 또는 위젯 밖 해제로 drag 표시가 남지 않고 이후 클릭과 배경 입력
    투과가 정상 동작한다.

## M1·M2 회귀

1. 빈 배경 입력은 Windows Desktop으로 통과하고 Dock·모달·메뉴·대화상자는 입력을 받는다.
2. Shell 아이콘과 이미지 썸네일이 유지된다.
3. 1,200개 파일 가상 목록이 입력 정지 없이 스크롤된다.
4. Explorer 외부 변경이 watcher debounce 뒤 반영된다.
5. 루트 삭제·복구, Companion 재연결과 설정 손상 복구가 유지된다.
6. Dock 순서 변경과 Seelen 재시작 뒤 복원이 유지된다.

## 실패 기록

- 화면에 표시된 명령과 오류 메시지
- 출발·목적 카드와 실제 fixture 경로
- `Get-Process Wallpaper.Seelen.Companion` 결과
- `Get-NetTCPConnection -State Listen`의 43127~43135 결과
- Seelen 로그 시각
- 재현 직전 외부 파일 변경 또는 잠금 명령

Windows 실기기 체크리스트는 2026-07-24 사용자 완료 보고를 기준으로 통과 처리했다.
M3·M4 종료 게이트를 닫고 후속 단계는
[Seelen M5 검수 기록](m5-seelen-validation.md)에서 관리한다.
