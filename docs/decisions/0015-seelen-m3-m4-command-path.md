# ADR 0015: Seelen M3·M4 파일 명령과 내부 이동 경로

- 상태: 구현·자동 검증·Windows Seelen 실기기 검수 완료
- 결정일: 2026-07-24
- 적용 범위: Seelen 제품 Companion과 Desktop 위젯의 M3·M4
- Windows Seelen 실기기 검수: 2026-07-24 통과

## 결정

M3·M4는 보존된 WPF 파일 엔진을 다시 구현하지 않고 Seelen 제품 경로에 안전한 명령
오케스트레이션을 추가한다.

```text
Seelen Widget
  └─ protocol 4 itemCommand(requestId, action, itemId, ...)
       ▼
DesktopCommandService
  ├─ 현재 projection item/destination 재해석
  ├─ 명령 직렬화와 requestId 재실행 방지
  └─ WindowsFileCommandService
       ├─ open / showInExplorer / rename / recycle
       └─ prepareMove / move
```

위젯은 명령에 실제 경로와 항목 종류를 보내지 않는다. Companion은 현재 projection이
보유한 `itemId`만 파일 또는 실제 폴더로 해석하고, 가상 `루트` 카드는 M3 항목 명령에서
제외한다. M4 목적지는 별도 레지스트리로 관리해 실제 폴더와 가상 `루트`를 허용한다.

모든 실제 작업은 기존 `FileCommandTargetValidator`를 통해 루트 경계, 지원 깊이, 항목
종류와 재분석 지점을 다시 검증한다. 이동은 `File.Move(..., overwrite: false)` 경로를
유지하며 recycle은 Windows Shell `IFileOperation`만 사용한다.

## 프로토콜 4

M3·M4 명령은 다음 공통 envelope를 사용한다.

```json
{
  "type": "itemCommand",
  "requestId": "widget-generated-id",
  "action": "open | showInExplorer | rename | recycle | prepareMove | move",
  "itemId": "current-projection-id",
  "destinationId": "prepareMove와 move에서만 사용",
  "newName": "rename, move와 재제안 prepareMove에서 사용"
}
```

응답은 같은 `requestId`를 가진 `itemCommandAck`다. 최근 요청과 결과를 제한 캐시에
보존하므로 같은 요청의 재전송은 이름 변경, recycle 또는 move를 두 번 실행하지 않는다.
같은 `requestId`를 다른 명령에 재사용하면 거부한다. 변경 명령은 Companion 전체에서
직렬 실행한다.

## M3 결정

- 파일 단일 선택, 더블클릭과 `Enter` 열기를 유지한다.
- 파일과 실제 폴더 우클릭은 네 명령의 Glass 메뉴를 연다.
- 이름 변경과 recycle은 별도 Glass 확인 대화상자를 사용한다.
- 위젯의 Windows 이름 검사는 빠른 피드백이며 Companion 검사가 최종 판정이다.
- 폴더 이름 변경은 저장된 Dock 순서의 이전 ID를 새 ID로 이관한다.
- 폴더 recycle은 저장 순서에서 사라진 ID를 제거한다.
- 변경 성공과 실패 뒤 전체 projection을 다시 만든다.

`Windows 추가 옵션 표시`와 Desktop 배경 네이티브 메뉴는 M5 범위로 남긴다.

## M4 결정

- 루트 → 폴더, 폴더 → 루트, 폴더 → 다른 폴더의 단일 파일 이동을 허용한다.
- Seelen UI 2.8의 위젯 WebView 호스트가 HTML5 drag보다 먼저 네이티브 drag 입력을
  소유하므로 HTML5 `draggable`, `dragstart`와 `drop`에 의존하지 않는다.
- Seelen 호스트를 fork하지 않고 `pointerdown`/`pointermove`/`pointerup`과 pointer
  capture를 사용하는 공통 상태 머신으로 파일 drag와 Dock 폴더 순서 drag를 구분한다.
- 5px 이동 전에는 클릭 후보로 유지하고, 활성 drag가 끝난 직후 발생하는 합성 click은
  한 번 억제한다.
- 후보 또는 활성 drag 중에는 Seelen 위젯의 커서 이벤트 투과를 해제해 빈 배경 위에서도
  pointer capture를 유지하고, 완료·취소·capture 손실 뒤 마지막 커서 위치로 복구한다.
- 출발 카드는 무효, 다른 카드는 유효 대상으로 표시한다.
- drag preview는 실제 파일 이름을 사용한다.
- 충돌이 없으면 준비 응답 뒤 바로 이동한다.
- 충돌이 있으면 가장 작은 `파일명 (n).ext`를 제안하는 편집 대화상자를 연다.
- 충돌 대화상자 중 외부에서 이름이 생기면 실행 직전에 다시 거부하고 새 이름을 제안한다.
- drag, 이름 변경, recycle 또는 충돌 대화상자 중 받은 snapshot은 최신 한 건만 지연
  적용해 작업 중 DOM이 교체되지 않게 한다.
- drag 취소, 잘못된 drop과 충돌 대화상자 취소도 전체 재스캔으로 복구한다.

폴더 이동, 복사, 다중 선택, 실행 취소와 외부 OLE drag & drop은 범위 밖이다.

## 검증 경계

WSL에서 전체 Release build, 기존 Windows 파일 서비스 회귀, Seelen 명령 오케스트레이션,
프로토콜 직렬화, 세 이동 경로, 충돌·stale·중복 요청과 위젯 정책/DOM 계약을 자동
검증한다.

실제 Seelen 렌더링, 전역 입력 투과, pointer capture, 네이티브 Explorer 실행, Windows
휴지통 UI, drag preview와 Glass 배치는 Windows Seelen 실기기 검수로 분리한다. 세부 항목은
[Seelen M3·M4 검수 기록](../m3-m4-seelen-validation.md)을 따른다.
