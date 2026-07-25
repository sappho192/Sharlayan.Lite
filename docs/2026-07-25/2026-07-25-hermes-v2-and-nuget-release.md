# Hermes v2 연동 및 Sharlayan.Lite 9.1.2 배포 기록

## 문서 목적

이 문서는 이번 대화 세션에서 수행한 Hermes v2 runtime 연동, 현재 NPC Talk 읽기, 실제 게임
smoke, production manifest 동기화, release gate 보강 및 Sharlayan.Lite `9.1.2` NuGet
배포를 정리한다. 2026-07-22 문서는 당시의 계획과 시점별 증거를 보존하며, 이 문서는 이후
완료된 작업과 공개 배포 결정을 추가한다.

## 최종 결과

- Hermes v2의 remote → verified cache → embedded resource provider가 구현됐다.
- CHATLOG, LastTalk 및 current Talk layout을 같은 manifest revision에서 원자적으로 적용한다.
- 화면에 현재 표시 중인 표준 NPC Talk를 읽는 current-first API가 구현됐다.
- Hermes live-verified production manifest를 package embedded fallback으로 동기화했다.
- multi-target build, net8.0/net10.0 test 및 package verification을 통과했다.
- GitHub Actions release를 NuGet Trusted Publishing OIDC 방식으로 전환했다.
- Sharlayan.Lite `9.1.2`와 symbol package를 NuGet.org에 공개했다.
- 신규 임시 NuGet cache에서 `9.1.2` restore와 net10.0 소비자 build를 확인했다.

## Hermes v2 runtime 구현

### Resource 선택

Resource provider의 우선순위:

1. public Hermes v2 latest와 immutable manifest
2. 마지막으로 검증된 로컬 cache
3. NuGet package에 포함된 embedded manifest

각 source는 같은 parser와 validator를 사용한다. schema version, compatibility floor,
revision hash, validation metadata 및 resource layout을 모두 통과한 manifest만 적용한다.
Remote 장애는 handler 초기화 실패로 이어지지 않으며 cache와 embedded fallback을 계속
시도한다.

현재 handler가 선택한 manifest revision은 handler 생명주기 동안 고정한다. polling 중
latest가 바뀌어도 CHATLOG signature와 structure 일부만 혼합해 교체하지 않는다.

### LastTalk 진단과 수정

실제 게임에서 NPC Talk를 여러 번 진행했지만 기존 reader는 Talk를 찾지 못했다. raw
`Utf8String` header를 확인한 결과 실제 LastTalk 값은 다음 형태였다.

- `StringLength(+0x18) == 0`
- `BufUsed > 1`
- `StringPtr`와 `BufUsed - 1` byte는 정상

따라서 FCS와 동일하게 유효 byte 길이를 `BufUsed - 1`로 계산하도록 수정했다. strict UTF-8,
null terminator, 최대 16 KiB, before/after snapshot 및 1회 race retry 검증은 유지했다.

`LastTalkName`과 `LastTalkText`는 현재 보이는 창과 동기화된 값이 아니라 마지막으로 확정된
표준 Talk다. 현재 창 활성 상태로 해석하면 안 된다.

### 현재 표시 중인 Talk

실제 게임에서 `RaptureAtkUnitManager.AllLoadedUnitsList`를 순회해 활성 `Talk` addon을
찾았고 다음 값이 화면과 일치함을 확인했다.

- `AtkValues[0]`: 현재 본문
- `AtkValues[1]`: 현재 화자
- type: `ManagedString(0x28)`

`CurrentTalkMemoryReader`는 addon name, readiness, visibility, value count/type, bounded strict
UTF-8 및 stable pair snapshot을 검증한다.

Public API:

- `Reader.CanGetCurrentTalk()` / `Reader.GetCurrentTalk()`
- `Reader.CanGetLastTalk()` / `Reader.GetLastTalk()`
- `Reader.CanGetTalk()` / `Reader.GetTalk()`
- `TalkResult.Source=Current|Last`
- `TalkResult.IsVisible`

`GetTalk()`은 current-first이며 활성 current Talk가 없거나 일시적으로 unreadable할 때만
LastTalk로 fallback한다.

## Hermes production 동기화

Embedded manifest 식별자:

| 항목 | 값 |
| --- | --- |
| resource revision | `sha256:419248bf2ef93aa64e72723ea9e97d5503163178dab63e90a8155b359ebcf96d` |
| FCS commit | `8ff04195c4e77ef0b85d15c6fd1c67785378f0fb` |
| verifier commit | `3e27261f82851e1e88c413a25461e6ca0ad551e8` |
| validation status | `live-verified` |
| minimum Sharlayan version | `9.1.2` |

`Sharlayan/Resources/HermesV2/embedded.json`은 Hermes public immutable object와 canonical LF
byte가 일치한다. Candidate를 embedded final로 사용하지 않는다.

## 검증 결과

### Build, test 및 package

```text
Release target frameworks:
  net462, net48, net6.0, net7.0, net8.0, net10.0
Build warnings/errors:
  0 / 0
Sharlayan.Tests:
  net8.0  66 PASS, 0 FAIL
  net10.0 66 PASS, 0 FAIL
Package:
  Sharlayan.Lite 9.1.2 pack PASS
  Verify-Package.ps1 PASS
```

Assembly version은 binary compatibility를 위해 `8.0.0.0`을 유지하고 package/file version은
`9.1.2`/`9.1.2.0`으로 정렬했다.

### current Talk 및 CHATLOG live smoke

Candidate와 verifier commit `3e27261`을 사용한 실제 게임 결과:

- CHATLOG signature match 정확히 1개
- module scan failed read 0개
- 첫 current Talk의 speaker/text가 실제 화면과 일치
- 다음 Talk로 진행했을 때 current 결과가 즉시 전환되고 화면과 일치
- Talk 종료 후 이전 값이 `Source=Last`, `IsVisible=False`로 보존
- 60초 CHATLOG polling 신규 entry 2개, cursor 진행, wrap 0회
- `LIVE SMOKE PASS`

실제 Talk 문자열은 일시적인 로컬 에이전트 진단에서만 화면과 대조했고 public 문서에는
보존하지 않는다. 일반 공유 로그에는 source, visibility, 길이 및 화면 일치 여부만 기록한다.

### Release gate 보강

Production resource를 사용해 추가로 확인한 항목:

- embedded attach PASS, non-admin process
- remote attach PASS
- remote 실패 후 verified cache fallback PASS
- 8 threads × 25 live `GetChatLog` calls PASS

10분을 목표로 한 single-consumer run은 사용자의 요청으로 420.1초에 중단했다.

- 신규 entry 78개
- cursor `41:3640` → `119:9721`
- wrap 0회
- `OnException` 및 stderr 없음

이 결과는 약 7분의 안정적인 polling 증거지만 10분 gate PASS가 아니다. 다음 증거는 여전히
미충족이다.

- 10분 이상 single-consumer polling
- 1,000-entry 또는 실제 wrap 전후 중복·누락
- Say, Tell, Party, Alliance, Novice, System의 사용자 동작과 code를 연결한 명시적 fixture
- elevated process attach
- upstream Sharlayan 9.1.2와 동일 process 결과 비교

## 9.1.2 공개 결정

사용자는 미충족 증거를 숨기지 않는 조건으로, 다른 지인에게 실제 테스트를 맡기기 위해
Sharlayan.Lite `9.1.2`를 공개하도록 명시적으로 결정했다.

이 결정은 `9.1.2`에 대한 release waiver다.

- 미충족 gate를 PASS로 바꾸지 않는다.
- 약 7분 run을 10분 run으로 기록하지 않는다.
- package 공개는 지인 테스트 범위를 넓히기 위한 결정이다.
- 같은 waiver가 이후 version에 자동 적용되지 않는다.

`docs/2026-07-22/2026-07-22-05-live-smoke.md`의 “package 공개 gate는 아직 열리지 않았다”는
문장은 해당 문서 commit 시점의 사실이다. 이후 사용자가 위 release waiver를 승인했고 이
문서가 최종 공개 결정을 기록한다.

## NuGet Trusted Publishing

저장된 장기 API key 대신 GitHub OIDC로 수명이 짧은 NuGet API key를 발급하도록
`.github/workflows/release.yml`을 변경했다.

NuGet.org policy:

| 항목 | 값 |
| --- | --- |
| policy owner | `sappho192` |
| repository owner | `sappho192` |
| repository | `Sharlayan.Lite` |
| workflow file | `release.yml` |
| GitHub environment | `production` |

GitHub repository 설정:

- 기본 브랜치: `min-chat`
- protected environment: `production`
- reviewer와 deployment branch policy는 GitHub API로 확인
- NuGet.org profile identity는 protected Environment 입력으로 제공
- workflow는 저장된 장기 NuGet API key를 사용하지 않음

Profile identity 입력은 NuGet.org profile name이며 실제 credential은 아니다. API key는
`NuGet/login@v1`이 OIDC token과 교환해 실행 중에만 제공한다. Public 문서에는 Environment
입력 식별자나 reviewer 세부값을 복사하지 않고 다음 명령으로 현재 설정을 확인한다.

```powershell
rg -n 'environment:|id-token:|secrets\.' .github/workflows/release.yml
gh secret list --repo sappho192/Sharlayan.Lite --env production
gh api repos/sappho192/Sharlayan.Lite/environments/production
gh api repos/sappho192/Sharlayan.Lite/environments/production/deployment-branch-policies
```

Workflow 구조:

1. `build` job이 resource generation, diff, restore, multi-target build, test, pack,
   package verification 및 artifact upload를 수행한다.
2. `publish=false`이면 `publish` job은 실행하지 않는다.
3. `publish=true`이면 build 성공 후 `production` 승인을 기다린다.
4. `publish` job에만 `id-token: write`를 부여한다.
5. verified artifact를 내려받고 exactly one `.nupkg`를 확인한다.
6. `NuGet/login`으로 임시 API key를 받은 직후 `.nupkg`와 `.snupkg`를 push한다.

Action은 commit SHA로 고정했다.

- `NuGet/login@8d196754b4036150537f80ac539e15c2f1028841`
- `actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093`

## NuGet 배포 증거

OIDC workflow commit:

```text
99f8a90497fc7c4a80884950066502884090ecb2
```

Dry run:

- URL:
  `https://github.com/sappho192/Sharlayan.Lite/actions/runs/30160920273`
- inputs: `package_version=9.1.2`, `publish=false`
- build/test/pack/verify/artifact: success
- publish job: expected대로 skipped

Production run:

- URL:
  `https://github.com/sappho192/Sharlayan.Lite/actions/runs/30161079520`
- inputs: `package_version=9.1.2`, `publish=true`
- build job: success
- `production` approval: 완료
- OIDC exchange: `Successfully exchanged OIDC token for NuGet API key.`
- `.nupkg` push: HTTP Created
- `.snupkg` push: HTTP Created
- workflow conclusion: success

NuGet.org 결과:

- package page: `https://www.nuget.org/packages/Sharlayan.Lite/9.1.2`
- V3 flat-container와 registration index에 `9.1.2` 노출
- 신규 임시 package/cache directory에서 정확히 `9.1.2` restore
- net10.0 소비자 project build: warning 0, error 0

Publish 중 유일한 비차단 경고는 package에 README가 포함되지 않았다는 점이다. 다음 package
version 전에 embedded package README 추가를 검토한다.

## 다음 릴리스 절차

1. version과 changelog를 정렬한다.
2. generated chat resource를 다시 만들고 Git diff가 없는지 확인한다.
3. multi-target build, net8/net10 test, pack, final repository commit 검증 및
   `Verify-PackageConsumer.ps1` clean-cache consumer build를 실행한다.
4. 필요한 실제 게임 live smoke를 수행하고 미충족 항목을 그대로 기록한다.
5. 같은 version이 NuGet.org에 없는지 확인한다. NuGet version은 덮어쓸 수 없다.
6. `release.yml`을 `publish=false`로 실행한다.
7. dry run artifact와 workflow conclusion을 확인한다.
8. `publish=true`로 실행하고 `production`을 승인한다.
9. OIDC exchange와 package/symbol push를 확인한다.
10. V3 index 노출 후 신규 cache에서 restore/build한다.

기본 브랜치가 `min-chat`이고 remote에 upstream도 있으므로 모든 GitHub CLI 명령은 repository를
명시한다.

```powershell
gh ... --repo sappho192/Sharlayan.Lite
```

`gh repo view`를 repository 인자 없이 실행하면 upstream `FFXIVAPP/sharlayan`을 선택할 수
있으므로 release workflow를 잘못 조회하거나 실행하지 않도록 주의한다.

## 관련 문서

- `docs/2026-07-22/2026-07-22-05-live-smoke.md`
- `docs/2026-07-22/2026-07-22-06-hermes-v2-runtime-plan.md`
- `docs/2026-07-22/2026-07-22-07-hermes-v2-handoff.md`
- `docs/2026-07-26/2026-07-26-sharlayan-9.1.3-release-preparation.md`
- `sappho192/ffxiv-hermes`의
  `docs/2026-07-25/2026-07-25-v2-release-session.md`
- Microsoft Learn:
  `https://learn.microsoft.com/nuget/nuget-org/trusted-publishing`
