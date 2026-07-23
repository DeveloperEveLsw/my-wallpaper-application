# M0 Seelen UI + .NET Companion 검수 기록

- 구현 상태: 자동 검증 가능한 스파이크 구현 완료
- Windows Seelen 실기기 검수: 대기
- M1 판정: **보류**
- 기준일: 2026-07-24
- 기준 Seelen SDK: `@seelen-ui/lib` 2.8.0
- 기준 Seelen 소스: `b2fc871c41a1e947e47793e6514080b33f2d781d`

## 1. 판정 요약

M0는 인식 가능한 독립 게이트로 시작했다. Desktop/Popup 위젯, Companion 싱글턴,
nonce 부트스트랩, loopback WebSocket/HTTP 보안 경계와 자동 정책 테스트는 구현되어
있다. 실제 Seelen의 Origin, 입력, 재시작, 절전, DPI, 다중 모니터, 방화벽 및 설치 모드
검수는 아직 수행하지 않았으므로 M0 통과나 M1 진행 가능으로 판정하지 않는다.

M1은 아래 표의 모든 필수 항목이 실제 Windows에서 통과하고 증거 열이 채워진 뒤에만
진행한다. 하나라도 실패하면 기존 Wallpaper Engine/WPF/WebView2/three.js 경로를
보존한 채 M0 안에서 수정·재검증한다.

## 2. 구현 경계

| 구성 | 위치 | 역할 |
|---|---|---|
| Desktop 위젯 | `spikes/seelen-m0/widgets/desktop` | Run, 연결, Blob, 클릭·drag, Popup trigger |
| Popup 위젯 | `spikes/seelen-m0/widgets/popup` | 실제 focus와 text/Enter/Esc 입력 |
| .NET Companion | `spikes/seelen-m0/companion` | 싱글턴, Kestrel, 인증, Desktop root |
| Windows 준비 | `scripts/prepare-seelen-m0.ps1` | self-contained Companion 배치와 위젯 load |
| 포트 충돌 | `scripts/hold-seelen-m0-port-collision.ps1` | 첫 후보 포트 점유 |

기존 `src` 프로젝트는 M0 프로젝트를 참조하지 않으며 기존 구현 파일은 삭제하지 않았다.

## 3. 보안 계약

1. Desktop 위젯은 Web Crypto로 32바이트 nonce를 생성한다.
2. Seelen `Run`은 `elevated: false`로
   `%LOCALAPPDATA%\WallpaperSeelenM0\Wallpaper.Seelen.M0.Companion.exe`를 실행한다.
3. 최초 프로세스는 `Local\Wallpaper.Seelen.M0.Companion.v1` mutex를 소유한다.
4. 후속 프로세스는 `PipeOptions.CurrentUserOnly` named pipe로 새 nonce를 전달하고
   종료한다.
5. Kestrel은 `127.0.0.1`의 `43127..43135` 중 첫 가용 포트에 HTTP/1로만 bind한다.
6. 포트 탐색은 nonce를 보내지 않고 HMAC bootstrap proof를 먼저 검증한다.
7. WebSocket handshake의 Origin은 `http://tauri.localhost`와 exact match해야 한다.
8. 모든 요청의 Host는 선택된 `127.0.0.1:<port>`와 exact match해야 한다.
9. hello nonce는 30초 안에 한 번만 소비할 수 있다.
10. hello 성공 시 새 256-bit 세션 토큰을 발급하며 위젯과 Companion 메모리에만 둔다.
11. HTTP Blob은 같은 Origin과 bearer session token을 모두 만족해야 한다.
12. CORS는 요청 Origin 하나를 그대로 반환하며 `*`를 사용하지 않는다.

실제 Seelen Origin이 예상값과 다르면 화면에 실제값과 허용값을 표시하고 연결을
중단한다. 확인 없이 allowlist를 넓히지 않는다.

## 4. 자동 검증 결과

2026-07-24에 Windows .NET SDK 10.0.302와 WSL Node.js 22.21.0에서 수행했다.

| 항목 | 결과 | 근거 |
|---|---|---|
| Companion Release 빌드 | 통과 | 경고 0, 오류 0 |
| 보안·옵션·loopback 통합 테스트 | 통과 | 16/16 |
| 위젯 bundle | 통과 | esbuild 완료 |
| Desktop/Popup JS 구문 | 통과 | `node --check` |
| Seelen resource bundle | 통과 | 설치된 `slu.exe` 2.8.0으로 두 metadata bundle 완료 |
| exact Origin 거부 사례 | 통과 | case, trailing slash, localhost, wildcard 거부 |
| exact Host 거부 사례 | 통과 | localhost, 무포트, 다른 포트, suffix 거부 |
| nonce 크기·일회성·만료 | 통과 | 256-bit, replay 거부, 30초 후 거부 |
| bearer 형식·Origin binding | 통과 | 잘못된 scheme/길이/Origin 거부 |
| Kestrel hello/Blob E2E | 통과 | 실제 loopback WebSocket 및 HTTP 검증 |
| 포트 충돌 fallback | 통과 | 첫 loopback 포트 점유 시 다음 포트 bind |

전체 자동 검사는 다음 명령으로 반복한다.

```powershell
./scripts/check.ps1
```

## 5. 현재 Windows 환경 확인

2026-07-24 읽기 전용 확인 결과 Seelen UI Appx `2.8.0.0`과 패키지 내부 `slu.exe`가
설치되어 있었다. 확인 시 Seelen 프로세스는 실행 중이 아니었다. PowerShell의 `slu`
별칭은 `Set-LocalUser`와 충돌하므로 준비·정리 스크립트는 `slu.exe` 애플리케이션만
찾고, PATH에 없으면 Seelen Appx 설치 폴더를 사용한다.

이 확인은 설치 존재 여부만 확인한 것이며 위젯 로드나 실제 UI 검수 통과로 계산하지
않는다.

## 6. Windows 실기기 사전 조건

- Windows 10/11 로컬 파일 시스템 checkout
- .NET SDK 10.0.302 이상 10.0 feature band
- Node.js 22 이상
- Seelen UI와 `slu` CLI
- `@seelen-ui/lib` 2.8.0과 호환되는 Seelen 빌드
- 최소 한 번의 단일 모니터 검수, 이후 DPI가 다른 두 모니터 검수

WSL UNC checkout에서 Windows publish를 수행하지 않는다.

## 7. 최초 실행 재현 절차

Windows PowerShell에서 실행한다.

```powershell
./scripts/prepare-seelen-m0.ps1
```

예상 결과:

1. Companion이 `%LOCALAPPDATA%\WallpaperSeelenM0`에 self-contained single-file로 배치된다.
2. Desktop/Popup 위젯이 build되고 현재 Seelen 세션에 load된다.
3. Desktop 위젯이 모니터에 나타난다.
4. 최초 `Run`에서 Seelen 권한 대화상자가 한 번 나타난다.
5. 실행을 허용하면 상태가 `hello + Blob 검증 통과`로 바뀐다.
6. Origin은 `http://tauri.localhost`, Host는 `127.0.0.1:43127` 또는 허용 범위의
   다른 포트로 표시된다.
7. Desktop 값은 아래 PowerShell 결과와 정확히 일치한다.

```powershell
[Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory,
    [Environment+SpecialFolderOption]::DoNotVerify)
```

Explorer 바탕 화면 아이콘은 사용자가 Windows 기능으로 직접 숨긴다. 검수 전후 아이콘
상태가 Companion 때문에 바뀌면 실패다.

## 8. 필수 실기기 매트릭스

각 행의 환경, 시각 증거 또는 로그 시각, 실제 결과를 `증거` 열에 기록한다.

| 요구사항 | 통과 기준 | 현재 결과 | 증거 |
|---|---|---|---|
| 최소 Desktop | 화면 뒤 Desktop preset으로 표시 | 대기 | |
| 최소 Popup | trigger 뒤 input focus, 문자·한글·Enter·Esc 수신 | 대기 | |
| Companion 싱글턴 | 반복 재연결 뒤 Companion 프로세스 1개 | 대기 | |
| Seelen Run | 비상승 실행, 최초 권한 허용 후 실행 | 대기 | |
| nonce/hello | 256-bit nonce, helloAck, replay 거부 | 자동 통과·실기기 대기 | |
| 메모리 토큰 | 파일·설정·localStorage·로그에 토큰 없음 | 코드 통과·실기기 대기 | |
| HTTP Blob | icon/image status·URL·type·length·PNG·decode 통과 | 대기 | |
| 실제 Origin | 표시값이 exact allowlist와 일치 | 대기 | |
| 실제 Host | 선택 포트의 exact IPv4 Host만 수락 | 자동 통과·실기기 대기 | |
| 클릭 | 버튼 count가 클릭마다 1 증가 | 대기 | |
| drag | handle drag로 위치 이동·복원 | 대기 | |
| Popup 포커스 | 열릴 때 input caret, focus loss 때 숨김 | 대기 | |
| 위젯 reload | 새 nonce로 기존 Companion에 재연결 | 대기 | |
| Seelen 재시작 | 새 위젯이 기존 또는 새 Companion에 재연결 | 대기 | |
| Companion 재시작 | socket 종료 감지 후 자동 Run·복구 | 대기 | |
| 절전 복귀 | resume 뒤 15초 안에 연결·입력 복구 | 대기 | |
| DPI | 100/125/150/200%에서 크기·hit test 정상 | 대기 | |
| 다중 모니터 | 모니터별 Desktop replica, Popup 위치·입력 정상 | 대기 | |
| 포트 충돌 | 43127 점유 시 다음 포트에서 인증 연결 | 대기 | |
| 방화벽 | 최초·재실행에 Windows 방화벽 prompt 없음 | 대기 | |
| 설치 방식 | 배치된 경로를 Run하고 재부팅/재시작 후 재연결 | 대기 | |
| 실제 Desktop root | redirected Desktop 포함 실제 경로 일치 | 대기 | |
| Explorer 아이콘 비소유 | 설정 변경·감시·복원 없음 | 코드 통과·실기기 대기 | |

## 9. 입력과 수명주기 절차

### 클릭·drag·Popup

1. `클릭 검증`을 5회 누르고 count가 정확히 5 증가하는지 확인한다.
2. `이동` handle로 위젯을 두 지점에 drag한다.
3. 위젯을 reload하고 마지막 위치가 복원되는지 확인한다.
4. `키보드 Popup 열기`를 누른 즉시 caret가 input에 있는지 확인한다.
5. 영문, 한글, 공백을 입력하고 Enter 결과를 확인한다.
6. 다시 열어 Esc와 외부 클릭에서 각각 숨는지 확인한다.

### 재시작·재연결

1. `재연결`을 5회 누른다.
2. `Get-Process Wallpaper.Seelen.M0.Companion` 결과가 1개인지 확인한다.
3. Desktop widget resource를 unload/load하여 새 nonce 연결을 확인한다.
4. Seelen UI를 정상 종료·재실행하고 연결 복구를 확인한다.
5. Companion 프로세스를 종료하고 위젯이 자동으로 다시 실행·연결하는지 확인한다.
6. Windows 절전 후 복귀하고 15초 안에 status와 Popup 입력이 복구되는지 확인한다.

### 포트 충돌

별도 PowerShell에서 다음을 실행한 채 Desktop 위젯의 `재연결`을 누른다.

```powershell
./scripts/hold-seelen-m0-port-collision.ps1 -Port 43127
```

Host가 `127.0.0.1:43128` 이상으로 바뀌고 hello/Blob이 모두 통과해야 한다. 충돌
프로세스에 nonce를 보내지 않는 HMAC proof 경로가 유지되어야 한다.

### DPI와 다중 모니터

1. 100%, 125%, 150%, 200% 배율에서 click target과 글자 clipping을 확인한다.
2. 서로 다른 DPI의 두 모니터 사이로 Desktop 위젯을 이동한다.
3. `ReplicaByMonitor` 인스턴스가 각 모니터에 한 개씩인지 확인한다.
4. 각 인스턴스에서 Popup이 현재 pointer 근처에 열리고 키보드를 받는지 확인한다.
5. 보조 모니터 분리·재연결 후 인스턴스 수와 연결을 확인한다.

### 방화벽

최초 배치, 최초 Run, Companion 강제 재시작과 Seelen 재시작 각각에서 Windows Defender
Firewall prompt가 없어야 한다. Kestrel endpoint가 `127.0.0.1`에만 존재하는지 함께
확인한다.

```powershell
Get-NetTCPConnection -State Listen |
    Where-Object {
        $_.LocalPort -ge 43127 -and $_.LocalPort -le 43135
    } |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

`LocalAddress`가 `127.0.0.1` 이외이면 실패다.

## 10. 설치 방식 검수

`prepare-seelen-m0.ps1`의 `slu resource load`는 현재 세션 개발 검수다. 설치 방식
검수에서는 두 widget을 `slu resource bundle widget <path>`로 bundle하고 사용 중인
Seelen 빌드의 Settings 리소스 설치 흐름으로 설치한다. 그 뒤 Seelen과 Windows를
재시작해 다음을 확인한다.

- widget이 저장소 경로의 session load 없이 생성된다.
- `Run`이 LocalAppData에 배치한 정확한 Companion 경로를 실행한다.
- 권한 결정과 싱글턴이 예상대로 동작한다.
- Companion은 관리자 권한으로 실행되지 않는다.

Seelen 빌드별 영구 로컬 리소스 설치 UI가 다르거나 제공되지 않으면 이를 M0 실패 또는
배포 blocker로 기록한다. session load 통과를 설치 방식 통과로 대체하지 않는다.

## 11. 정리

현재 세션의 개발 리소스만 unload한다.

```powershell
./scripts/unload-seelen-m0.ps1
```

이 명령은 `%LOCALAPPDATA%\WallpaperSeelenM0`의 Companion 파일이나 기존 제품 코드를
삭제하지 않는다.

## 12. M1 진행 조건

다음을 모두 만족해야 `진행 가능`으로 변경한다.

- 필수 실기기 매트릭스 전 행 통과
- 실제 Origin/Host 값을 문서에 기록
- 권한 및 설치 모드 통과
- firewall prompt 없음
- Desktop root와 Explorer 아이콘 비소유 정책 확인
- 재현 가능한 실패가 0개이거나 M1과 무관한 명시적 known limitation으로 승인

현재 판정은 **보류**다.
