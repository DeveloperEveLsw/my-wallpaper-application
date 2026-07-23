[CmdletBinding()]
param(
    [ValidateRange(43127, 43135)]
    [int]$Port = 43127
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$listener = [System.Net.Sockets.TcpListener]::new(
    [System.Net.IPAddress]::Loopback,
    $Port)

try {
    $listener.Start()
    Write-Host "127.0.0.1:$Port 충돌을 유지합니다."
    Write-Host 'M0 Desktop 위젯에서 재연결을 누르고 다음 포트로 연결되는지 확인하세요.'
    Read-Host '검증을 마쳤으면 Enter'
}
finally {
    $listener.Stop()
}
