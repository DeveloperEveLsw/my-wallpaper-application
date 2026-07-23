[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$LoadDevelopmentResource
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Warning 'prepare-seelen-m1-m2.ps1은 호환용 이름입니다. 이후에는 prepare-seelen.ps1을 사용하세요.'
& (Join-Path $PSScriptRoot 'prepare-seelen.ps1') @PSBoundParameters
