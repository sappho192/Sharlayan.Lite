# Hermes v2 런타임 전환 계획

## 1. 문서 역할

이 문서는 Sharlayan.Lite가 ffxiv-hermes v2 manifest를 안전하게 소비하고 CHATLOG와 표준 NPC Talk를 고수준 API로 제공하기 위한 저장소별 구현 계획이다.

Hermes v2 스키마, 생성, 검증, 배포 및 rollback의 기준 문서는 다음 파일이다.

```text
D:\REPO\ffxiv-hermes\V2_IMPLEMENTATION_PLAN.md
```

공개 저장소 기준 문서:

```text
https://github.com/sappho192/ffxiv-hermes/blob/main/V2_IMPLEMENTATION_PLAN.md
```

이 문서는 Hermes 스키마를 별도로 재정의하지 않는다. 구현 중 계약이 달라지면 Hermes 기준 문서를 먼저 변경하고 fixture와 schema를 갱신한 뒤 Sharlayan을 변경한다.

## 2. 목표

- Hermes v2의 CHATLOG와 표준 Talk 메타데이터를 런타임에 적용한다.
- 원격 리소스가 실패하거나 호환되지 않아도 package에 포함된 embedded 리소스로 동작한다.
- CHATLOG signature와 structure를 반드시 같은 manifest revision에서 가져온다.
- `UIModule.LastTalkName`과 `UIModule.LastTalkText`를 안전하게 읽는 API를 제공한다.
- IronworksTranslator가 `Signature`, pointer path 및 raw memory layout을 직접 다루지 않게 한다.
- 현재 지원하는 `net462`, `net48`, `net6.0`, `net7.0`, `net8.0`을 유지하고 `net10.0`을 추가한다.
- public API와 기존 초기화 event의 호환성을 가능한 한 유지한다.
- 선택된 resource source와 revision을 진단할 수 있게 한다.

## 3. 비목표

- FCS assembly를 Sharlayan.Lite runtime package에 포함하지 않는다.
- Sharlayan이 실행 중 FCS repository를 직접 조회하지 않는다.
- handler가 실행 중인 동안 manifest를 hot reload하지 않는다.
- BattleTalk, TalkSubtitle, NpcYell 및 말풍선을 초기 API에 포함하지 않는다.
- 표준 Talk의 대화창 활성 상태를 추측하지 않는다.
- Hermes manifest에서 임의의 메서드 호출이나 범용 메모리 읽기 명령을 실행하지 않는다.
- 원격 manifest를 검증 없이 기존 embedded 리소스보다 우선하지 않는다.

## 4. 현재 상태

현재 CHATLOG 리소스는 다음 흐름으로 고정되어 있다.

- `tools/Sharlayan.ChatResources.Generator`가 pinned FCS commit에서 C# source를 생성한다.
- `Sharlayan/Resources/GeneratedChatResources.g.cs`가 signature와 structure를 포함한다.
- `MemoryHandler` 생성자는 embedded structure를 즉시 설정한다.
- `MemoryHandler.InitializationTask`는 generated CHATLOG signature scan을 나타낸다.
- `Signatures.Resolve`와 `APIHelper`는 generated 리소스를 반환한다.
- HTTP와 JSON cache 관련 기존 configuration은 obsolete no-op이다.

관련 파일:

```text
Sharlayan/MemoryHandler.cs
Sharlayan/Signatures.cs
Sharlayan/Utilities/APIHelper.cs
Sharlayan/SharlayanConfiguration.cs
Sharlayan/Resources/GeneratedChatResources.g.cs
tools/Sharlayan.ChatResources.Generator/Program.cs
tools/Sharlayan.LiveSmoke/Program.cs
```

Hermes v2에서는 generated C# 값을 유일한 리소스로 사용하지 않고 동일 스키마의 embedded manifest를 최종 fallback으로 사용한다.

## 5. 호환성 원칙

### 기본 모드

Sharlayan.Lite 자체의 기본값은 `EmbeddedOnly`로 둔다. 기존 소비자가 package update만으로 예기치 않은 네트워크 요청을 시작하지 않게 하기 위함이다.

IronworksTranslator처럼 동적 갱신이 필요한 소비자는 `RemotePreferred`를 명시적으로 설정한다.

향후 major release에서 기본값을 바꾸려면 별도의 호환성 검토와 release note가 필요하다.

### 기존 configuration

- obsolete인 `APIBaseURL`, `PatchVersion`, `UseLocalCache`, `JSONCacheDirectory` 의미를 다시 살리지 않는다.
- Hermes v2에는 의미가 분명한 새 configuration property를 사용한다.
- 기존 property는 현재 no-op 동작을 유지하여 binary 및 source compatibility를 보존한다.
- package validation baseline과 public API diff를 CI에서 확인한다.

### 기존 API

- `Reader.GetChatLog` 동작과 반환 모델을 유지한다.
- `OnMemoryLocationsFound`는 최종 선택된 manifest의 scan이 끝난 뒤 한 번만 발생시킨다.
- `InitializationTask`는 manifest 선택과 signature scan 전체를 포함하도록 의미를 확장한다.
- `IsInitialized`는 최종 초기화 task가 성공한 경우에만 true가 된다.
- `Signatures.Resolve` 및 obsolete `APIHelper`의 장기 동작은 별도 호환 shim으로 유지하되 선택된 handler revision을 표현하는 API로 사용하지 않는다.

## 6. 제안 코드 구조

예상 파일 구조:

```text
Sharlayan/
  Resources/
    HermesV2/
      embedded.json
  Models/
    Resources/
      ResourceMode.cs
      ResourceSource.cs
      ResourceInfo.cs
      HermesV2Manifest.cs
  Models/
    ReadResults/
      TalkResult.cs
  Resources/
    HermesV2ManifestParser.cs
    HermesV2ManifestValidator.cs
    HermesV2ResourceProvider.cs
    HermesV2ResourceCache.cs
    HermesV2ResourceMapper.cs
  Reader.Talk.cs
```

실제 구현에서는 작은 DTO를 한 파일에 함께 둘 수 있다. 재사용되지 않는 type을 불필요하게 분리하지 않는다.

책임:

- parser는 JSON을 DTO로 변환한다.
- validator는 schema version, 호환 버전, hash, pattern 및 offset을 검사한다.
- provider는 remote, cache 및 embedded 후보 순서를 결정한다.
- cache는 atomic write와 ETag metadata를 관리한다.
- mapper는 검증된 manifest를 기존 `Signature` 및 `StructuresContainer`로 변환한다.
- Reader는 선택된 Talk memory location에서 `Utf8String`을 읽는다.

## 7. Public configuration 초안

정확한 이름은 public API 검토에서 확정하되 다음 의미를 제공한다.

```csharp
public enum ResourceMode {
    EmbeddedOnly,
    RemotePreferred,
}

public sealed class SharlayanConfiguration {
    public ResourceMode ResourceMode { get; set; }
    public Uri HermesV2LatestUri { get; set; }
    public string ResourceCacheDirectory { get; set; }
    public TimeSpan ResourceRequestTimeout { get; set; }
}
```

기본값:

```text
ResourceMode = EmbeddedOnly
HermesV2LatestUri = `https://hermes.sapphosound.com/v2/latest.json` (production v2 latest URL)
ResourceRequestTimeout = 짧고 제한된 값
ResourceCacheDirectory = 명시되지 않으면 cache 비활성
```

규칙:

- URI는 HTTPS만 허용한다. 테스트용 localhost 허용은 internal seam으로 분리한다.
- timeout은 최소 및 최대 범위를 적용한다.
- cache 경로가 없거나 쓸 수 없으면 cache 없이 계속한다.
- configuration에 credential이나 임의 header를 넣지 않는다.
- library 내부 테스트를 위한 transport 주입은 internal constructor 또는 `InternalsVisibleTo`로 해결한다.

## 8. Manifest 선택 정책

### EmbeddedOnly

1. package의 embedded manifest를 읽는다.
2. embedded byte hash와 DTO를 검증한다.
3. CHATLOG와 Talk 리소스를 구성한다.

embedded manifest 검증 실패는 package 손상 또는 개발 오류이므로 초기화를 실패시킨다.

### RemotePreferred

1. `v2/latest.json`을 ETag conditional request로 요청한다.
2. latest pointer의 schema version, revision 및 relative manifest path를 검증한다.
3. 동일 revision의 검증된 cache가 있으면 immutable manifest 재다운로드를 생략한다.
4. 새 revision이면 immutable manifest를 다운로드한다.
5. exact response bytes의 SHA-256이 latest pointer의 revision과 일치하는지 확인한다.
6. manifest schema와 `minimumSharlayanVersion`을 검증한다.
7. 검증 성공 후 cache에 atomic write한다.
8. remote가 실패하면 마지막으로 검증된 cache를 시도한다.
9. cache가 실패하면 embedded manifest를 사용한다.

동일 revision을 remote와 cache에서 중복 후보로 시도하지 않는다.

`minimumSharlayanVersion` 비교에는 고정된 `AssemblyVersion`이 아니라 package의 semantic version을 나타내는 informational version 또는 build-generated version constant를 사용한다. prerelease precedence도 semantic version 규칙으로 비교한다.

### 런타임 scan fallback

정적 검증 성공만으로 현재 게임에서 signature가 일치한다고 보장할 수 없다.

권장 정책:

1. remote 또는 cache manifest로 required CHATLOG signature를 scan한다.
2. required location이 없거나 signature scan이 실패하면 embedded revision이 다를 때 embedded를 한 번 시도한다.
3. 최종 성공한 manifest를 handler revision으로 고정한다.
4. Talk만 읽을 수 없고 CHATLOG는 정상인 경우 handler 전체를 실패시키지 않고 Talk를 unavailable로 둔다.
5. 모든 CHATLOG 후보가 실패하면 `InitializationTask`를 실패시키고 기존 exception event 정책을 따른다.

fallback scan 중간 결과로 `OnMemoryLocationsFound`를 발생시키지 않는다.

## 9. HTTP 정책

- handler 생성자에서 동기 HTTP 요청을 수행하지 않는다.
- manifest 획득은 `InitializationTask` 안에서 비동기로 수행한다.
- target framework별 API 차이를 감싼 최소 transport helper를 둔다.
- response status를 명시적으로 검사한다.
- redirect 횟수와 response body 최대 크기를 제한한다.
- `latest.json`과 manifest에 서로 다른 작은 최대 크기를 적용한다.
- `application/json`을 기대하되 CDN의 안전한 호환 content type 정책을 문서화한다.
- static 또는 재사용 가능한 `HttpClient` 생명주기를 사용한다.
- cancellation과 timeout을 구분하여 진단한다.
- 실패한 응답 body에 사용자 데이터가 포함될 수 있으므로 전체 body를 로그에 남기지 않는다.

## 10. Cache 구조

권장 경로:

```text
<ResourceCacheDirectory>/hermes-v2/
  latest.json
  latest.etag
  manifests/
    <resource-revision>.json
```

규칙:

- immutable manifest 파일명은 검증된 SHA-256 revision에서만 만든다.
- path traversal이 가능한 manifest path를 허용하지 않는다.
- 다운로드는 동일 디렉터리의 임시 파일에 기록한다.
- SHA-256과 manifest validation 성공 후 atomic rename한다.
- 기존 정상 cache를 새 다운로드로 먼저 덮어쓰지 않는다.
- 손상된 cache는 삭제하거나 격리하고 다음 fallback으로 진행한다.
- cache 쓰기 실패는 remote manifest 사용 자체를 막지 않는다.
- ETag가 없으면 revision 비교만 사용한다.
- cache cleanup은 현재 revision과 embedded revision을 보호한 뒤 오래된 immutable 파일만 대상으로 한다.

## 11. Embedded manifest

`Sharlayan/Resources/HermesV2/embedded.json`에 release 시점의 마지막 live-verified manifest를 포함한다.

규칙:

- NuGet build 중 네트워크에서 manifest를 다운로드하지 않는다.
- 승인된 Hermes immutable manifest를 명시적으로 갱신하고 review한다.
- embedded 파일의 expected revision을 unit test에 기록한다.
- package에 실제 embedded resource가 포함되었는지 pack test로 확인한다.
- source tree의 embedded manifest와 generated C#이 서로 다른 값을 제공하는 기간을 최소화한다.

전환 순서:

1. 현재 generated C# 값을 표현하는 최초 v2 fixture를 만든다.
2. manifest mapper 결과와 `GeneratedChatResources` 결과가 동일한지 테스트한다.
3. `MemoryHandler`를 manifest mapper 사용으로 전환한다.
4. 회귀 검증 후 generated C# runtime 경로를 제거한다.
5. 기존 generator는 Hermes generator와의 fixture 검증 도구로 축소하거나 제거한다.

## 12. 초기화 흐름

현재 constructor의 초기화 코드를 private async 초기화 메서드로 이동한다.

개념적 흐름:

```csharp
private async Task InitializeAsync() {
    foreach (var candidate in await resourceProvider.GetCandidatesAsync()) {
        var mapped = resourceMapper.Map(candidate.Manifest);
        var scan = await TryScanAsync(mapped.Signatures);
        if (!scan.HasRequiredChatLog) {
            continue;
        }

        Structures = mapped.Structures;
        ResourceInfo = candidate.ResourceInfo;
        CommitScan(scan);
        return;
    }

    throw new InvalidOperationException("No compatible CHATLOG resource could be initialized.");
}
```

구현 요구사항:

- `Structures`는 해당 signature scan을 최종 채택할 때 함께 commit한다.
- Reader가 초기화 중간 structure를 관찰하지 않게 한다.
- scanner의 기존 public instance와 event 계약을 보존한다.
- fallback candidate scan 결과가 최종 scanner dictionary에 섞이지 않게 한다.
- scan 중 process가 종료되면 다른 manifest를 시도하지 않고 종료 오류로 처리한다.
- initialization 완료 전 Reader 호출은 기존처럼 unavailable 결과를 반환한다.
- handler가 dispose되면 진행 중 HTTP와 scan을 취소한다.

## 13. Manifest 매핑

### Framework signature

Hermes root의 pattern, relative follow offset 및 `isPointer`를 기존 Sharlayan `Signature` semantics로 변환한다.

현재 변환 규칙:

```text
patternLength = pattern byte length
rewindOffset = -(patternLength - relativeFollowOffset)
```

CHATLOG pointer path:

```text
[rewindOffset, 0, uiModuleOffset, raptureLogModuleOffset]
```

Talk name pointer path:

```text
[rewindOffset, 0, uiModuleOffset, nameOffset]
```

Talk text pointer path:

```text
[rewindOffset, 0, uiModuleOffset, textOffset]
```

Talk path는 `Utf8String` 구조 주소에서 끝낸다. 기존 `GetString`을 재사용하기 위해 마지막 `0`으로 `StringPtr`를 따라가지 않는다. 새 Reader가 header와 정확한 string length를 검증해서 읽는다.

### CHATLOG structure

manifest의 index 및 data vector offset에서 기존 `ChatLogPointers` 값을 만든다.

```text
OffsetArrayStart = indexVectorOffset
OffsetArrayPos   = indexVectorOffset + pointerSize
OffsetArrayEnd   = indexVectorOffset + pointerSize * 2
LogStart         = dataVectorOffset
LogNext          = dataVectorOffset + pointerSize
LogEnd           = dataVectorOffset + pointerSize * 2
```

v2 platform은 x64만 허용하므로 pointer size는 8이다. manifest 값이 겹치거나 정렬 및 범위를 위반하면 거부한다.

### Pattern 중복 scan

CHATLOG, Talk name 및 Talk text는 같은 Framework pattern을 공유한다.

초기 구현에서 기존 Scanner 계약을 유지하기 위해 세 logical location을 만들 수 있다. 성능 문제가 확인되면 동일 pattern과 relative follow 규칙을 한 번만 scan한 뒤 여러 pointer path에 공유하도록 Scanner 내부를 개선한다.

중복 scan 최적화는 정확성 구현과 분리하고 별도 benchmark 없이 먼저 복잡성을 추가하지 않는다.

## 14. Talk API

### 결과 모델

다중 target framework 호환성을 고려하여 단순 immutable 또는 read-only 모델을 추가한다.

개념적 API:

```csharp
public sealed class TalkResult {
    public bool IsAvailable { get; }
    public string Name { get; }
    public string Text { get; }
}
```

Reader API:

```csharp
public bool CanGetLastTalk();
public TalkResult GetLastTalk();
```

`IsAvailable`은 메모리 읽기 가능 여부를 의미한다. 대화창이 현재 열려 있다는 의미로 사용하지 않는다.

### Utf8String 읽기

읽기 순서:

1. `Utf8String` header에서 `StringPtr`, `BufUsed` 및 `StringLength`를 local buffer로 읽는다.
2. pointer가 null인지 확인한다.
3. length가 음수 또는 configured maximum을 초과하지 않는지 확인한다.
4. `BufUsed`와 `StringLength` 관계를 sanity check한다.
5. 실제 length만큼 별도 local buffer로 읽는다.
6. null terminator와 UTF-8 경계를 처리한다.
7. header를 다시 읽어 pointer와 length가 바뀌었으면 한 번 재시도한다.
8. 두 번째 snapshot도 불안정하면 unavailable 결과를 반환한다.

이 구현은 현재 `GetString(..., 2048)`의 전체 범위 read 실패, 임의 truncation 및 UTF-8 중간 절단 문제를 제거한다.

### 동시성

- CHATLOG timer와 Talk timer가 동시에 Reader를 호출할 수 있다.
- Talk header와 text buffer는 호출별 local array 또는 안전한 pool을 사용한다.
- 기존 handler-wide scratch buffer를 lock 없이 공유하지 않는다.
- `ArrayPool`을 사용하면 예외 경로에서도 반환하고 민감한 text bytes를 지운다.
- 이름과 대사는 가능한 한 한 번의 logical snapshot으로 읽는다.

### 문자열 의미

- Sharlayan은 FFXIV payload를 임의의 번역용 문자열로 정제하지 않는다.
- 기존 CHATLOG parser와 일관된 최소 decoding만 수행한다.
- control payload 제거와 표시 정책은 IronworksTranslator가 담당한다.
- 빈 text는 정상적인 값일 수 있으므로 메모리 읽기 실패와 구분한다.

## 15. Resource 진단 API

handler에서 다음 read-only 정보를 제공한다.

```text
Source: Remote, Cache 또는 Embedded
ResourceRevision
FcsCommit
GeneratorCommit
SchemaVersion
FallbackReason
```

정확한 public model 이름은 API review에서 확정한다.

로그 정책:

- source와 revision은 initialization당 한 번 기록한다.
- remote 실패 시 URL 전체 query나 response body를 기록하지 않는다.
- fallback 이유는 exception type과 검증 단계 중심으로 기록한다.
- NPC 이름, 대사 및 CHATLOG 본문은 resource 진단 로그에 기록하지 않는다.

## 16. 오류 정책

| 상황 | 동작 |
| --- | --- |
| latest HTTP 실패 | cache, embedded 순서로 fallback |
| immutable manifest HTTP 실패 | cache, embedded 순서로 fallback |
| revision hash 불일치 | 해당 remote 거부 후 fallback |
| schema version 불일치 | 해당 manifest 거부 후 fallback |
| minimum library version 불일치 | 해당 manifest 거부 후 fallback |
| cache 손상 | cache 격리 후 embedded fallback |
| remote CHATLOG scan 실패 | revision이 다른 embedded scan 시도 |
| Talk location만 실패 | CHATLOG 유지, Talk unavailable |
| 모든 CHATLOG scan 실패 | initialization 실패 및 exception 통지 |
| process 종료 | 즉시 취소, 다른 manifest 시도 안 함 |

## 17. 보안 검토

- manifest는 허용된 typed resource만 표현한다.
- 임의 pointer path 배열을 무제한으로 실행하는 범용 명령 스키마를 만들지 않는다.
- pattern 길이, wildcard 수, offset 및 response 크기를 제한한다.
- HTTPS endpoint만 허용한다.
- latest가 가리키는 manifest path는 같은 configured origin의 v2 base와 그 아래
  `manifests/` 디렉터리로 제한한다.
- manifest exact bytes의 SHA-256을 latest revision과 비교한다.
- cache file path는 revision에서만 만들고 사용자 제공 path segment를 사용하지 않는다.
- manifest 서명이 도입되면 hash 검증 다음에 signature trust 검증을 추가한다.
- HTTP 처리와 JSON parsing 오류가 게임 process memory write로 이어질 경로를 만들지 않는다.
- Sharlayan은 계속 read-only process access를 기본으로 한다.

## 18. 테스트 계획

### DTO 및 validation

- Hermes의 valid fixture 역직렬화
- 필수 field 누락 및 known field type 불일치 거부
- 같은 schema version의 unknown additive optional field 허용
- schema version 불일치
- minimum version 불일치
- 잘못된 SHA와 source commit
- 잘못된 pattern 문자와 홀수 길이
- 음수 및 과도한 field offset
- 지원되지 않는 platform과 pointer resolver version

### Provider 및 HTTP

- EmbeddedOnly에서 네트워크 호출 없음
- RemotePreferred 정상 다운로드
- latest ETag 304와 cache hit
- latest timeout 후 cache fallback
- manifest 404 후 cache fallback
- response size 초과 거부
- redirect origin 변경 거부
- revision mismatch 거부
- cache write 실패 후 현재 remote 계속 사용
- remote와 cache 실패 후 embedded fallback

### Cache

- 임시 파일에서 atomic rename
- 기존 정상 cache 보존
- 손상된 cache 격리
- 동일 revision idempotent write
- path traversal 입력 거부
- concurrent handler cache 접근

### Mapper 및 Scanner

- Framework relative follow 변환
- CHATLOG pointer path 생성
- Talk name 및 text pointer path 생성
- vector offset에서 `ChatLogPointers` 생성
- required CHATLOG location 판정
- remote scan 실패 후 embedded scan
- 중간 fallback scan event 미발생
- 최종 event 한 번 발생

### Reader Talk

- synthetic `Utf8String` heap buffer
- inline storage를 가리키는 `StringPtr`
- 빈 이름과 빈 text
- 다국어 UTF-8
- null pointer
- length 상한 초과
- header 변경 후 한 번 재시도
- 반복 변경 시 unavailable
- page boundary 근처의 짧은 문자열
- CHATLOG와 Talk 동시 호출

### 기존 회귀

- 전체 target framework build
- 기존 Scanner tests
- 기존 CHATLOG parser 및 ring cursor tests
- API compatibility 및 package validation
- NuGet package embedded resource 포함
- package에 FCS runtime assembly 미포함

## 19. Live smoke 확장

`tools/Sharlayan.LiveSmoke`에 다음 option을 추가한다.

```text
--resource-mode embedded|remote
--manifest <local-path-or-url>
--verify-talk
```

검증 항목:

- 선택된 resource source와 revision
- Framework signature unique match
- CHATLOG 최종 주소
- CHATLOG 신규 entry polling
- Talk name 및 text location 읽기 가능 여부
- 사용자가 화면에서 확인한 Talk와 읽은 값의 일치 여부
- 대화창 종료 후 LastTalk 잔존 여부
- 동일 대사의 닫기 및 재열기

도구는 실제 이름과 대사 본문을 기본 로그에 출력하지 않는다. 길이, 변경 횟수 및 사용자의 명시적인 일치 확인만 기록한다.

## 20. 구현 단계

### Phase 1: 계약과 embedded baseline

- [ ] Hermes v2 schema와 fixture를 test input으로 추가
- [ ] DTO와 strict validator 구현
- [ ] 현재 generated CHATLOG와 동등한 embedded manifest 추가
- [ ] manifest mapper와 generated C# 동등성 테스트 추가
- [ ] exact byte revision 검증 구현

완료 조건:

- embedded manifest에서 현재와 동일한 CHATLOG signature 및 structure가 생성된다.
- 모든 target framework가 build된다.
- runtime package에 FCS assembly가 포함되지 않는다.

### Phase 2: Provider와 cache

- [ ] 새 resource configuration 추가
- [ ] EmbeddedOnly provider 구현
- [ ] RemotePreferred HTTP provider 구현
- [ ] immutable cache와 ETag 처리 구현
- [ ] remote, cache, embedded fallback 테스트 추가
- [ ] resource diagnostics 모델 추가

완료 조건:

- EmbeddedOnly는 네트워크를 사용하지 않는다.
- RemotePreferred는 유효한 새 revision을 적용한다.
- 네트워크 및 cache 오류에서 embedded로 복구한다.

### Phase 3: MemoryHandler 초기화 전환

- [ ] `InitializationTask`에 manifest 획득 포함
- [ ] signature와 structure atomic commit 구현
- [ ] runtime scan fallback 구현
- [ ] event 한 번 발생 보장
- [ ] generated C# runtime 경로 제거 또는 fallback 전용 축소
- [ ] public API compatibility 검사

완료 조건:

- CHATLOG signature와 structure가 한 revision에서만 설정된다.
- remote scan 실패 시 embedded revision으로 복구할 수 있다.
- 기존 `Reader.GetChatLog` 소비자는 코드 변경 없이 동작한다.

### Phase 4: Talk API

- [ ] Talk name 및 text location 매핑
- [ ] safe `Utf8String` reader 구현
- [ ] `TalkResult`, `CanGetLastTalk`, `GetLastTalk` 추가
- [ ] snapshot retry와 length 제한 구현
- [ ] Talk 단위 테스트 추가
- [ ] CHATLOG와 Talk 동시 호출 테스트 추가

완료 조건:

- 이름과 대사를 고정 길이 2048바이트 read 없이 가져온다.
- invalid pointer와 변경 중인 string에서 예외를 외부로 누출하지 않는다.
- Talk failure가 정상 CHATLOG polling을 중단하지 않는다.

### Phase 5: Live 검증과 package release

- [ ] LiveSmoke manifest option 추가
- [ ] 글로벌 및 한국 client 검증
- [ ] remote, cache 및 embedded 각각 검증
- [ ] pack과 package validation 실행
- [ ] README와 CHANGELOG 갱신
- [ ] IronworksTranslator가 사용할 package version 배포

완료 조건:

- CHATLOG와 표준 Talk live smoke가 통과한다.
- 네트워크 차단 상태에서 embedded fallback이 통과한다.
- 새 public API와 configuration이 release note에 기록된다.

## 21. 검증 명령

구현 후 repository root에서 실행한다.

```powershell
dotnet build Sharlayan.sln --configuration Release
dotnet test Sharlayan.Tests/Sharlayan.Tests.csproj --configuration Release
dotnet pack Sharlayan/Sharlayan.csproj --configuration Release --no-build
```

현재 repository CI와 release workflow의 전체 target framework, stale generated output 및 package validation 단계도 통과해야 한다.

Live smoke는 사용자가 실행 중인 게임 process와 명시적으로 선택한 manifest에서 별도로 실행한다.

## 22. Release 및 rollback

Release 순서:

1. Hermes v2 schema와 최초 live-verified manifest를 확정한다.
2. 동일 manifest를 Sharlayan embedded fallback으로 포함한다.
3. Sharlayan prerelease를 IronworksTranslator에서 통합 검증한다.
4. Sharlayan stable package를 배포한다.
5. IronworksTranslator가 `RemotePreferred`로 opt-in한다.
6. Hermes production latest 갱신을 활성화한다.

Rollback:

- remote 데이터 문제는 Hermes `v2/latest.json`을 이전 immutable revision으로 되돌린다.
- client는 ETag 갱신 후 이전 revision을 다시 선택한다.
- remote와 cache가 모두 문제면 IronworksTranslator에서 EmbeddedOnly를 임시 설정할 수 있게 한다.
- Sharlayan 코드 회귀는 정상 package version으로 IronworksTranslator dependency를 되돌리고 새 release로 배포한다.

## 23. 완료 기준

- Hermes v2 manifest를 strict validation 후 적용한다.
- default EmbeddedOnly에서 기존 no-network 동작이 유지된다.
- RemotePreferred에서 remote, cache 및 embedded fallback이 동작한다.
- CHATLOG signature와 structure가 같은 revision에서 atomic하게 설정된다.
- Talk 이름과 text를 safe `Utf8String` reader로 제공한다.
- IronworksTranslator가 raw pointer와 `Signature` 없이 API를 사용할 수 있다.
- 모든 target framework, unit test, live smoke 및 package validation이 통과한다.
- package에 FCS runtime dependency가 포함되지 않는다.
- 선택된 revision과 fallback 이유를 대사 내용 없이 진단할 수 있다.

## 24. 구현 전 확정 사항

- 최초 Sharlayan package version
- public configuration type과 property 이름
- production Hermes v2 URL
- runtime JSON validator를 수동 구현할지 별도 dependency를 사용할지 여부
- remote CHATLOG scan 실패 시 embedded 재scan의 timeout 상한
- Talk text 최대 byte 길이
- `TalkResult`가 빈 값과 unavailable을 표현하는 최종 방식
- resource diagnostics의 public API 노출 범위
