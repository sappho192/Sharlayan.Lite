# Hermes v2 / Sharlayan.Lite 9.1.2 handoff

작성일: 2026-07-23

## 목적

이 문서는 별도 Sharlayan 작업 세션이 검증된 verifier commit을 이어받아 실제 게임 live smoke,
Hermes production 승격 및 Sharlayan.Lite 9.1.2 패키지 준비를
완료하기 위한 handoff다.

IronworksTranslator 코드는 이 범위에 포함하지 않는다. 해당 저장소에는 upstream 진행 상태만
문서로 전달한다.

## 시작 상태

- 저장소: `D:\REPO\sharlayan`
- branch: `min-chat`
- 구현 기준 base: `ced3652 Add live chat smoke validation`
- Hermes v2 구현, strict parser 보강 및 release gate 확정은 이 문서를 포함하는 verifier commit에 있다.
- smoke 전에 `git status --short`가 비어 있는지 확인하고 `git rev-parse HEAD`를 verifier SHA로 사용한다.
- 기존 사용자 변경을 버리거나 `git reset --hard`, `git checkout --`로 되돌리지 않는다.
- `artifacts/`의 로컬 package 결과는 source commit 대상이 아니다.

첫 확인 명령:

```powershell
cd D:\REPO\sharlayan
git status --short
git diff --check
```

## Hermes upstream 상태

Hermes 저장소는 `D:\REPO\ffxiv-hermes`이며 `main`은 현재 clean 상태다.

- Hermes main commit: `c2412206a404c8975b586cf1c092db3ef57619cd`
- Candidate workflow run: `29937911406` (success)
- Candidate PR: `sappho192/ffxiv-hermes#4` (merged)
- FCS commit: `d25004c582d2c5d78118830d79ffd1479fe650ee`
- Generator commit: `2eadb5222f43b1e1968f155ddc32f30cab41aeb4`
- Candidate revision:
  `sha256:7c97d2962f6cbf52ccfa34a97ae6879d780e8ba0a1aa3f881763ab5a6de4fa0c`
- Minimum Sharlayan version: `9.1.2`
- Candidate file:
  `D:\REPO\ffxiv-hermes\v2\candidates\d25004c582d2c5d78118830d79ffd1479fe650ee.json`

Candidate는 bootstrap fixture와 비교해 `source.fcsCommit` 및 `source.generatorCommit`만
변경되었다. `roots`와 `resources`의 CHATLOG/Talk pattern 및 offset은 동일하다.

아직 Hermes production `v2/latest.json`은 배포하지 않았다. Candidate의
`validation.status`는 `candidate`이므로 Sharlayan의 일반 remote/cache 경로에서는 거부되며,
live-smoke 전용 `--manifest` override에서만 사용할 수 있다.

## Working tree에 구현된 내용

### Resource acquisition

- `ResourceMode.EmbeddedOnly` 기본값
- opt-in `ResourceMode.RemotePreferred`
- remote → verified cache → embedded fallback
- HTTPS 및 same-origin redirect 제한
- timeout, response size, content type 및 HTTP status 검증
- latest ETag conditional request
- immutable manifest SHA-256 검증
- Windows-safe cache filename, atomic write 및 corrupt quarantine
- remote/cache에는 `live-verified` manifest만 허용
- local live-smoke override와 embedded에는 candidate 허용

### Manifest와 초기화

- Hermes v2 strict DTO/parser/semantic validator
- schema, source repository, platform, pointer resolver, semver 및 offset 검증
- unknown top-level/typed property 거부, optional resource extension 허용
- 같은 manifest에서 CHATLOG signature/structure와 Talk layout 매핑
- candidate별 Scanner를 사용하고 성공 시 structures/scanner/resource info를 원자적으로 반영
- remote candidate scan 실패 시 cache/embedded candidate로 계속 fallback
- source, revision, FCS/generator commit, validation status 및 fallback reason 진단

### Talk API

- `Reader.CanGetLastTalk()`
- `Reader.GetLastTalk()` / `TalkResult`
- name/text pair snapshot
- strict UTF-8, 최대 16 KiB, null terminator 및 exact length 처리
- pointer/length race 발생 시 header 재확인 후 1회 재시도

`LastTalkName`과 `LastTalkText`는 마지막 표준 Talk 값을 보존하며 현재 Talk 창 활성 상태를
의미하지 않는다. Consumer는 attach 직후 값을 baseline으로 처리해야 한다.

### 9.1.2 정합성

- `Sharlayan.csproj` package version: `9.1.2`
- file version: `9.1.2.0`
- assembly version은 binary compatibility를 위해 기존 `8.0.0.0` 유지
- LiveSmoke는 더 이상 client version `8.1.0`을 hardcode하지 않고 실제 Sharlayan assembly
  informational version을 사용
- manifest/provider 테스트 입력은 `9.1.2`로 정렬

현재 embedded manifest는 아직 이전 bootstrap candidate다. 최초 live-verified Hermes manifest가
배포된 뒤 정확한 production byte로 교체해야 한다.

## 확인된 로컬 검증

마지막 검증 결과:

```text
Release multi-target build: PASS
  net462, net48, net6.0, net7.0, net8.0
Warnings: 0
Sharlayan.Tests: 60 PASS, 0 FAIL
Sharlayan.Lite 9.1.2 pack: PASS
Package verification: PASS, 457,954 bytes combined
```

재현 명령:

```powershell
dotnet build Sharlayan.sln -c Release --no-restore
dotnet test Sharlayan.Tests\Sharlayan.Tests.csproj -c Release --no-build
dotnet pack Sharlayan\Sharlayan.csproj -c Release --no-build `
  -o artifacts\packages-v2-912-verifier
.\tools\Verify-Package.ps1 `
  -PackageDirectory .\artifacts\packages-v2-912-verifier `
  -ExpectedPackageVersion 9.1.2
```

## 다음 세션의 권장 순서

### 1. Verifier commit 확인

working tree가 clean인지 확인하고 현재 HEAD를 기록한다. smoke 전에 코드가 변경되면 전체 자동
검증을 다시 실행하고 새 commit을 verifier SHA로 사용한다.

다음 값을 기록한다.

```powershell
git rev-parse HEAD
```

이 SHA가 Hermes publish의 `verifier_commit` 입력이다. Push는 별도 세션 사용자와 scope를
확인한 뒤 수행한다.

### 2. Attach-only Talk smoke

FFXIV에 로그인하고 표준 NPC Talk 창을 연 상태에서 실행한다. 게임 로그인에는 2FA와 GPU
환경이 필요하므로 이 검증은 수동으로 유지한다.

```powershell
dotnet run --project tools\Sharlayan.LiveSmoke\Sharlayan.LiveSmoke.csproj `
  --configuration Release -- `
  --manifest D:\REPO\ffxiv-hermes\v2\candidates\d25004c582d2c5d78118830d79ffd1479fe650ee.json `
  --attach-only `
  --require-talk
```

필수 확인:

- CHATLOG signature match 정확히 1개
- module scan failed read 0개
- handler initialization 성공
- CHATLOG address readable
- `Talk: available` 출력
- `LIVE ATTACH PASS`

### 3. Full CHATLOG + Talk smoke

NPC Talk가 열린 상태를 유지하고 60초 동안 새 chat entry가 생기도록 게임 내 채팅 또는 시스템
메시지를 발생시킨다.

```powershell
dotnet run --project tools\Sharlayan.LiveSmoke\Sharlayan.LiveSmoke.csproj `
  --configuration Release -- `
  --manifest D:\REPO\ffxiv-hermes\v2\candidates\d25004c582d2c5d78118830d79ffd1479fe650ee.json `
  --poll-seconds 60 `
  --require-talk
```

필수 확인:

- 첫 poll은 historical entry를 반환하지 않음
- polling cursor가 진행됨
- 신규 entry 1개 이상
- `OnException` 없음
- `LIVE SMOKE PASS`

실제 이름과 대사 문자열은 로그에 출력하지 않고 UTF-16 length만 기록한다.

### 4. Publish 입력값 수집

```powershell
$ffxivProcess = Get-Process ffxiv_dx11 | Select-Object -First 1
$ffxivExe = $ffxivProcess.Path
$executableSha256 = (Get-FileHash -LiteralPath $ffxivExe -Algorithm SHA256).Hash.ToLowerInvariant()
$gameVersion = (Get-Content -LiteralPath (Join-Path (Split-Path $ffxivExe) 'ffxivgame.ver') -Raw).Trim()
$verifierCommit = git rev-parse HEAD

$gameVersion
$executableSha256
$verifierCommit
```

`executableSha256`은 64자리 lowercase hex, `verifierCommit`은 Sharlayan의 40자리 lowercase
commit SHA여야 한다.

### 5. Hermes production promote

`ffxiv-hermes`의 Actions에서 `Publish Hermes v2`를 `main` ref로 수동 실행한다.

```text
mode: promote
fcs_commit: d25004c582d2c5d78118830d79ffd1479fe650ee
game_version: <ffxivgame.ver 값>
executable_sha256: <위에서 계산한 lowercase SHA-256>
verifier_commit: <Sharlayan smoke commit SHA>
```

`main` environment에서 `sappho192`가 배포를 승인한다. `prevent_self_review=false` 및
deployment branch `main`이 설정되어 있다. Publish workflow는 immutable object를 먼저 올리고
read-back checksum을 확인한 다음 latest pointer를 마지막에 교체한다.

### 6. Embedded production manifest 동기화

Promote 성공 후 Hermes가 기록한 live-verified immutable manifest byte를
`Sharlayan/Resources/HermesV2/embedded.json`으로 복사한다. Candidate를 embedded final로
사용하지 않는다.

동기화 후 다음을 다시 확인한다.

- embedded manifest `validation.status == live-verified`
- embedded byte revision과 Hermes production revision 일치
- FCS commit `d25004c582d2c5d78118830d79ffd1479fe650ee` 일치
- 60개 이상의 전체 test 통과
- multi-target build 및 package verification 통과

그 뒤 Sharlayan.Lite 9.1.2 package release를 진행한다.

## 아직 하지 말아야 할 작업

- Live smoke 없이 Hermes `publish-v2` promote 실행
- `2026-07-22-05-live-smoke.md`의 9.1.2 release gate를 모두 통과하기 전 package 공개 배포
- Candidate manifest를 remote/cache production으로 취급
- 검증 전 Sharlayan 9.1.2 package 공개 배포
- embedded manifest와 remote manifest의 CHATLOG/Talk 일부를 혼합
- `LastTalk` 값을 현재 창 활성 상태로 추측
- IronworksTranslator source 직접 변경

## 관련 문서

- `docs/2026-07-22/2026-07-22-05-live-smoke.md`
- `docs/2026-07-22/2026-07-22-06-hermes-v2-runtime-plan.md`
- `D:\REPO\ffxiv-hermes\docs\V2_IMPLEMENTATION_PLAN.md`
- `D:\REPO\ffxiv-hermes\docs\V2_GITHUB_AND_CACHE_SETUP.md`
