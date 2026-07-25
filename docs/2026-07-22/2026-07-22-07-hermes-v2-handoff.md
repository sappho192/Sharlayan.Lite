# Hermes v2 / Sharlayan.Lite 9.1.2 handoff

작성일: 2026-07-23

최종 갱신: 2026-07-25

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
- currentTalk 구현은 아직 working tree 상태다. commit 후 그 SHA를 새 verifier commit으로 사용한다.
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

Hermes 저장소는 `D:\REPO\ffxiv-hermes`이며 새 currentTalk candidate가 `main`에 병합되었다.

- Hermes main commit: `746414d55919677c79f6c3709f839ace556551aa`
- FCS commit: `8ff04195c4e77ef0b85d15c6fd1c67785378f0fb`
- Generator commit: `f44e33479dbf50699b44b1b8932b2b1b21c31f54`
- Candidate revision:
  `sha256:cbf5e08e2bcfe214f5ee42f634dd6cf23939afcb056904bfd838bf0fd9a186af`
- Minimum Sharlayan version: `9.1.2`
- Candidate file:
  `D:\REPO\ffxiv-hermes\v2\candidates\8ff04195c4e77ef0b85d15c6fd1c67785378f0fb.json`

Candidate는 기존 CHATLOG/LastTalk 계약에 `currentStandardTalk` addon traversal 계약을
추가했고, LastTalk UTF-8 길이 원천을 `bufferUsedMinusNull`로 명시한다.

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
- unknown top-level/typed property와 예상하지 않은 resource 거부
- 같은 manifest에서 CHATLOG, LastTalk 및 currentTalk layout 매핑
- candidate별 Scanner를 사용하고 성공 시 structures/scanner/resource info를 원자적으로 반영
- remote candidate scan 실패 시 cache/embedded candidate로 계속 fallback
- source, revision, FCS/generator commit, validation status 및 fallback reason 진단

### Talk API

- `Reader.CanGetTalk()` / `Reader.GetTalk()`: current-first, last-fallback
- `Reader.CanGetCurrentTalk()` / `Reader.GetCurrentTalk()`
- `Reader.CanGetLastTalk()`
- `Reader.GetLastTalk()` / `TalkResult`
- `TalkResult.Source=Current|Last` 및 `IsVisible`
- current Talk addon의 visibility/readiness/type 검증과 stable pair snapshot
- LastTalk의 `BufUsed - 1` 길이, strict UTF-8, 최대 16 KiB 및 null terminator 검증
- pointer/layout/string race 발생 시 snapshot 재확인 후 1회 재시도

`LastTalkName`과 `LastTalkText`는 마지막 표준 Talk 값을 보존하며 현재 Talk 창 활성 상태를
의미하지 않는다. Consumer는 attach 직후 값을 baseline으로 처리해야 한다.

### 9.1.2 정합성

- `Sharlayan.csproj` package version: `9.1.2`
- file version: `9.1.2.0`
- assembly version은 binary compatibility를 위해 기존 `8.0.0.0` 유지
- LiveSmoke는 더 이상 client version `8.1.0`을 hardcode하지 않고 실제 Sharlayan assembly
  informational version을 사용
- manifest/provider 테스트 입력은 `9.1.2`로 정렬

현재 embedded manifest는 새 candidate의 canonical LF byte와 정확히 일치한다. 이는 pre-release
검증용이며, 최초 live-verified Hermes manifest가 배포된 뒤 정확한 production byte로 다시
교체해야 한다.

## 확인된 로컬 검증

마지막 검증 결과:

```text
Release multi-target build: PASS
  net462, net48, net6.0, net7.0, net8.0, net10.0
Warnings: 0
Sharlayan.Tests: 66 PASS on net8.0 and net10.0, 0 FAIL
Sharlayan.Lite 9.1.2 pack: PASS
Package verification: PASS, 598,040 bytes combined
```

재현 명령:

```powershell
dotnet build Sharlayan.sln -c Release --no-restore
dotnet test Sharlayan.Tests\Sharlayan.Tests.csproj -c Release --no-build
dotnet pack Sharlayan\Sharlayan.csproj -c Release --no-build `
  -o artifacts\packages-v2-912-current-talk
.\tools\Verify-Package.ps1 `
  -PackageDirectory .\artifacts\packages-v2-912-current-talk `
  -ExpectedPackageVersion 9.1.2
```

## 2026-07-25 live 진단 결과

FFXIV가 실행 중인 Windows 환경에서 candidate와 verifier `4d38d25`를 사용해 실제 메모리를
검증했다.

확인된 정상 항목:

- `ffxiv_dx11` CHATLOG signature match: 정확히 1개
- module scan failed read: 0개
- Hermes resource: local candidate, FCS `d25004c582d2c5d78118830d79ffd1479fe650ee`
- resolved location: CHATLOG, LastTalkName, LastTalkText 총 3개
- handler 초기화: 약 120~138 ms
- 60초 CHATLOG smoke: 신규 entry 1개, code `000A`, cursor 진행, `LIVE SMOKE PASS`
- Talk name/text 주소와 `StringPtr`는 readable

기존 `--require-talk`은 표준 NPC Talk를 여러 번 진행해도 실패했다. 개인정보 비출력을 전제로
한 초기 verifier 진단만으로는 사용자가 Talk를 열지 않은 것으로 오인할 수 있으므로, 다음
raw header를 확인하는 임시 계측을 수행했다.

```text
name: BufSize=64,  BufUsed=9,   StringLength(+0x18)=0
text: BufSize=256, BufUsed=140, StringLength(+0x18)=0
```

`BufUsed - 1` 바이트를 strict UTF-8로 읽으면 실제 값은 다음과 같이 정상이다.

```text
name: Estinien
text: Having been to Thavnair before, I can travel by aetheryte, but what of the rest
      of you? Another sea voyage would waste time we do not have.
```

진단 당시 화면에는 다음 단계인 Thancred의 Talk가 표시 중이었다. 따라서 `LastTalkName`과
`LastTalkText`는 현재 표시 중인 창과 동기화된 값이 아니라 직전에 확정된 Talk를 보존한다.
이 동작은 smoke에서 값의 가독성을 입증하는 데는 충분하지만, consumer가 현재 활성 대사를
판별하는 용도로 사용하면 안 된다.

### 현재 표시 중인 Talk 취득 가능성

같은 게임 세션에서 `RaptureAtkUnitManager.AllLoadedUnitsList`를 외부 메모리로 순회하고 이름이
`Talk`인 활성 addon을 찾아 현재 값을 직접 확인했다.

```text
Talk AtkValue[0], type=0x28 ManagedString:
  Krile was of the same mind, and has already secured the aid of the good folk
  of the Confluence. We'll take ourselves there.
Talk AtkValue[1], type=0x28 ManagedString:
  Thancred
```

이는 화면에 표시 중인 이름과 본문에 정확히 일치했다. `AddonTalk`의 text node `+0x238`에서도
이름, `+0x240`에서도 본문을 읽을 수 있었지만, 본문 node에는 줄바꿈용 SeString control payload가
포함되었다. 반면 `AtkValues[0]`과 `[1]`은 이 표본에서 plain UTF-8이므로 consumer 입력으로 더
적합하다.

권장 설계:

1. manifest에 기존 `lastStandardTalk`와 별도로 `currentStandardTalk` resource를 추가한다.
2. FCS metadata에서 UIModule → RaptureAtkModule → RaptureAtkUnitManager →
   AllLoadedUnitsList, AtkUnitList entries/count, AtkUnitBase name/visibility/readiness 및
   AtkValues pointer/count/type offset을 추출한다.
3. addon 이름이 `Talk`이고 `IsReady=true`, `IsVisible=true`, `AtkValuesCount>=2`일 때만
   현재 값으로 인정한다.
4. `AtkValues[0]`을 text, `[1]`을 name으로 읽되 두 값 모두 string 계열 type인지 확인하고,
   bounded null-terminated strict UTF-8 및 before/after snapshot 일관성을 검증한다.
5. public 결과에는 최소 `Source=Current|Last`, `IsVisible`을 노출한다. Consumer 기본 정책은
   `Current` 우선, 활성 Talk가 없거나 순간적으로 unreadable일 때만 `Last` fallback으로 한다.
6. `AtkValue` index 의미는 현재 FCS가 이름 붙인 field가 아니므로 단순 offset 추출만으로
   안전하다고 보지 않는다. Candidate마다 live smoke에서 실제 현재 화면과 일치하는지 검증한다.

추가 live gate:

- Talk가 열린 동안 화면의 name/text와 `Source=Current` 결과가 같은지 확인
- 다음 대사로 넘겼을 때 polling 결과가 같은 프레임 또는 허용 지연 내 새 값으로 바뀌는지 확인
- Talk를 닫았을 때 `Current`가 사라지고 필요 시 `Last` fallback으로 전환되는지 확인
- inline/heap managed string, 다국어 및 SeString payload 포함 대사 확인
- addon close/update와 동시에 읽어도 partial pair를 반환하지 않는지 확인

### 확정된 실패 원인

`TalkMemoryReader`가 manifest의 `StringLengthOffset=0x18` 값을 실제 byte length로 강제한다.
하지만 실제 `LastTalk` 객체에서는 이 값이 0이고 `BufUsed`만 유효하다. 정확한 candidate FCS
commit의 `Utf8String` 구현도 공개 `Length`와 `AsSpan()`을 `BufUsed - 1` 기준으로 계산한다.

실험적으로 `TalkMemoryReader`의 effective length를 `BufUsed - 1`로 바꾸자 다음 결과를 얻었다.

```text
Talk: available, nameUtf16Length=8, textUtf16Length=139
LIVE ATTACH PASS
TalkMemoryReaderTests: net8.0 3 PASS, net10.0 3 PASS
```

이 실험 수정은 진단 당시 원복했지만, 아래 요구사항은 2026-07-25 currentTalk 구현에 정식으로
반영되었다. 자동 검증은 통과했으나 아직 commit 및 실제 게임 live smoke 전이므로 production
verifier SHA로 사용할 수는 없다.

### Sharlayan 구현 결과

1. effective byte length를 FCS와 동일하게 `BufUsed - 1`로 계산한다.
2. `StringPtr != 0`, `1 <= BufUsed <= MaximumStringBytes + 1`을 검증한다.
3. 마지막 byte의 null을 확인하고, null을 제외한 정확히 `BufUsed - 1` 바이트만 strict UTF-8
   decode한다.
4. race 검증에 `StringPtr`와 `BufUsed`의 before/after 동일성을 포함한다.
   `StringLength(+0x18)`가 0이라는 이유로 유효한 값을 거부하지 않는다.
5. `StringLength=0`, `BufUsed>1`인 실제 관측 형태와 inline/heap 양쪽을 unit test fixture에
   반영했다. null 누락, oversize, invalid UTF-8 및 header race gate도 유지한다.
6. runtime plan과 API 설명에서 “exact StringLength” 의존 표현을 실제 규칙에 맞게 고쳤다.
7. multi-target build/test/package verification은 통과했다. 새 verifier commit을 만든 뒤
   attach-only와 60초 CHATLOG+Talk smoke를 다시 실행하는 일만 남았다.

### Windows candidate byte 주의

이전 Hermes candidate에서는 Git blob은 LF지만 Windows checkout이 CRLF가 되어 local
override가 다음과 같이 서로 다른 revision을 보고했다.

```text
canonical Git blob revision:
  sha256:7c97d2962f6cbf52ccfa34a97ae6879d780e8ba0a1aa3f881763ab5a6de4fa0c
Windows working-copy revision:
  sha256:3fff6f6e42a87e288dc04c0de995dba07dbf5a8344b3f10d88087471a31b6f65
git ls-files --eol:
  i/lf w/crlf attr/
```

Local candidate override는 candidate smoke만을 위해 임의 revision을 허용하므로 위 실험은
가능했지만, production revision 증거에는 working-copy hash를 사용하지 않는다. 새 Hermes
candidate와 Sharlayan embedded fixture에는 LF 고정 규칙이 적용되었다. 현재 embedded SHA-256은
canonical candidate와 같은
`cbf5e08e2bcfe214f5ee42f634dd6cf23939afcb056904bfd838bf0fd9a186af`이다.

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
  --manifest D:\REPO\ffxiv-hermes\v2\candidates\8ff04195c4e77ef0b85d15c6fd1c67785378f0fb.json `
  --attach-only `
  --require-current-talk `
  --print-talk
```

필수 확인:

- CHATLOG signature match 정확히 1개
- module scan failed read 0개
- handler initialization 성공
- CHATLOG address readable
- `CurrentTalk: available, source=Current, visible=True` 출력
- `--print-talk` name/text가 실제 화면과 일치
- `LIVE ATTACH PASS`

### 3. Full CHATLOG + Talk smoke

NPC Talk가 열린 상태를 유지하고 60초 동안 새 chat entry가 생기도록 게임 내 채팅 또는 시스템
메시지를 발생시킨다.

```powershell
dotnet run --project tools\Sharlayan.LiveSmoke\Sharlayan.LiveSmoke.csproj `
  --configuration Release -- `
  --manifest D:\REPO\ffxiv-hermes\v2\candidates\8ff04195c4e77ef0b85d15c6fd1c67785378f0fb.json `
  --poll-seconds 60 `
  --require-current-talk
```

필수 확인:

- 첫 poll은 historical entry를 반환하지 않음
- polling cursor가 진행됨
- 신규 entry 1개 이상
- `OnException` 없음
- `LIVE SMOKE PASS`

공유할 full smoke 로그에는 실제 이름과 대사 문자열을 출력하지 않고 source, visibility 및
UTF-16 length만 기록한다. 대사창을 닫은 뒤 `--attach-only --require-last-talk`도 별도로 실행해
`Source=Last`, `IsVisible=False` 폴백 값을 확인한다.

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
fcs_commit: 8ff04195c4e77ef0b85d15c6fd1c67785378f0fb
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
- FCS commit `8ff04195c4e77ef0b85d15c6fd1c67785378f0fb` 일치
- 66개 이상의 전체 test 통과
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
