# Upstream main 채팅 기능 반영 계획

## 1. 문서 목적

이 문서는 채팅 기능만 유지하는 `Sharlayan.Lite`의 `min-chat` 브랜치에 `FFXIVAPP/sharlayan:main`의 최근 변경을 어떤 방식으로 반영할지 정리한다.

결론은 다음과 같다.

- upstream 전체를 병합하거나 주요 커밋을 그대로 cherry-pick하지 않는다.
- 더 이상 최신 상태로 유지되지 않는 `sharlayan-resources` JSON 의존성은 제거한다.
- FFXIVClientStructs(FCS)는 빌드 시 채팅 시그니처와 구조 오프셋을 생성하는 도구 전용 의존성으로 사용한다.
- 런타임 패키지에는 FCS 전체와 Lumina를 포함하지 않는다.
- upstream 9.1.2의 채팅 파서, 메모리 접근, 스캐너 안전성 수정은 파일 단위로 수동 이식한다.
- 현재 공개 API와 대상 프레임워크는 첫 번째 마이그레이션 릴리스에서 가능한 한 유지하고, 정리는 별도 major 릴리스로 미룬다.

이 접근은 upstream의 패치 대응 방식을 따르면서도 이 포크의 핵심 목표인 작은 런타임 패키지와 채팅 전용 범위를 보존한다.

## 2. 조사 기준

조사일은 2026-07-22이다.

| 구분 | 기준 |
| --- | --- |
| 로컬 브랜치 | `min-chat` |
| 로컬 HEAD | `9a03add7da26723e1313e75032f7fec2ea89efa6` |
| 원격 포크 HEAD | `origin/min-chat`의 `9a03add7da26723e1313e75032f7fec2ea89efa6` |
| upstream 기준 이전 커밋 | `c92845a08facd5ebdbe3382afd0bca17601fc3e6` |
| upstream 최신 `main` | `7bc42ce9dd312a87837bf60dae16a088e260218f` |
| upstream 패키지 버전 | `9.1.2` |
| upstream FCS pin | `15ae1806b0c175d1e2dd2ae845e4c853f332fd07` |

조사 직후 로컬 `min-chat`을 `origin/min-chat`의 `9a03add7...`까지 fast-forward했다. 반영된 3개 커밋은 dependency checker, Dependabot 설정, README 오탈자 수정이며 채팅 런타임 변경은 아니다.

upstream ref는 조사 중 `upstream/main`까지 fetch했다. 조사 단계에서는 이 문서 외의 작업 트리 파일을 변경하지 않았다.

## 3. 현재 포크 상태

### 3.1 유지된 핵심 경로

- 프로세스 연결과 메모리 읽기: `Sharlayan/MemoryHandler.cs`, `Sharlayan/UnsafeNativeMethods.cs`
- 시그니처 검색과 포인터 해석: `Sharlayan/Scanner.cs`, `Sharlayan/MemoryLocation.cs`, `Sharlayan/Models/Signature.cs`
- 채팅 ring buffer 읽기: `Sharlayan/Reader.ChatLog.cs`, `Sharlayan/Utilities/ChatLogReader.cs`
- 채팅 파싱과 정제: `Sharlayan/Utilities/ChatEntry.cs`, `Sharlayan/Utilities/ChatCleaner.cs`
- 채팅 결과 모델: `Sharlayan/Core/ChatLogItem.cs`, `Sharlayan/Models/ReadResults/ChatLogResult.cs`
- JSON 리소스 로딩: `Sharlayan/Utilities/APIHelper.cs`

### 3.2 현재 리소스 경로의 문제

현재 코드는 다음 URL의 `latest/x64.json`을 런타임에 읽는다.

- `https://raw.githubusercontent.com/FFXIVAPP/sharlayan-resources/master/signatures/latest/x64.json`
- `https://raw.githubusercontent.com/FFXIVAPP/sharlayan-resources/master/structures/latest/x64.json`

두 URL은 조사 시점에도 응답하지만 마지막 관련 갱신은 2025-01-02의 7.1 대응 커밋 `9721dc5`이다. 따라서 서비스가 완전히 중단된 것은 아니지만 최신 게임 패치용 리소스로 간주할 수 없다. 특히 최신 upstream이 사용하는 채팅 주소 해석은 기존 JSON의 직접 시그니처와 포인터 경로가 아니라 다음 FCS 체인이다.

```text
Framework [StaticAddress]
  -> Framework.UIModule
  -> UIModule.RaptureLogModule
  -> LogModule.LogMessageIndex / LogMessageData
```

현재 FCS pin 기준 주요 채팅 오프셋은 다음과 같다.

| 항목 | 값 또는 산출 방식 |
| --- | --- |
| `Framework.UIModule` | `0x2B68` |
| `UIModule.RaptureLogModule` | `0x1AC0` |
| `OffsetArrayStart/Pos/End` | `LogMessageIndex` 필드 위치에 `0/8/16`을 더함 |
| `LogStart/Next/End` | `LogMessageData` 필드 위치에 `0/8/16`을 더함 |

`RaptureLogModule` 오프셋은 이전 `0x19E0`에서 `0x1AC0`으로 변경된 이력이 있다. 하드코딩되거나 갱신되지 않은 JSON에 계속 의존하면 패치 시 채팅 읽기가 조용히 중단될 가능성이 높다.

### 3.3 현재 확인된 별도 위험

- 구조 정보 로딩과 시그니처 검색이 서로 다른 fire-and-forget 작업에서 실행되어 초기화 순서가 보장되지 않는다.
- 초기화 작업 실패가 관찰되지 않아 `UnobservedTaskException`으로 이어질 수 있다.
- `GetChatLog`가 공유 `ChatLogReader` 상태를 동기화 없이 변경한다.
- 잘린 payload와 구분자 없는 공개 채팅이 파서 예외를 유발할 수 있다.
- `GetUInt64`가 실제로 `TryToUInt32`를 호출한다.
- `GetStructure<T>`가 저장된 `ProcessHandle` 대신 `Process.Handle`을 사용하며 unmanaged 메모리 해제를 `finally`로 보장하지 않는다.
- `OpenProcess`가 처음부터 전체 접근 권한을 요청한다.
- 테스트 프로젝트와 CI 테스트 단계가 없다.
- release workflow는 기본 브랜치가 `min-chat`인데도 `main` push를 대상으로 한다.
- actor/current-player 해석이 제거되어 `ChatLogItem.PlayerCharacterName`은 일반적으로 `UNRESOLVED`가 된다.

## 4. upstream 주요 변경 요약

### 4.1 Sharlayan 9 리소스 재구축

[PR #93](https://github.com/FFXIVAPP/sharlayan/pull/93), 커밋 [`6b9e675`](https://github.com/FFXIVAPP/sharlayan/commit/6b9e6753d66e0d7fe8ad44b90e7521c981b2cbc1)은 가장 중요한 변경이다.

- `sharlayan-resources` JSON provider와 ETL을 제거했다.
- FCS의 `[StaticAddress]`와 `[FieldOffset]` 메타데이터에서 시그니처와 구조 오프셋을 산출한다.
- `ChatLogPointersMapper`를 추가했다.
- 전체 기능용 DB 데이터는 설치된 게임 파일을 Lumina로 읽도록 변경했다.
- FCS와 `InteropGenerator.Runtime`을 최종 DLL에 ILRepack한다.
- 대상 프레임워크를 `net10.0`으로 변경했다.
- 빌드에 .NET 10, PowerShell 7, git submodule 초기화가 필요해졌다.

이 포크에는 FCS 기반 채팅 메타데이터 산출만 필요하다. Lumina, 전체 provider, 전체 FCS 런타임 병합은 필요하지 않다.

### 4.2 후속 패치 대응

| 커밋 | PR | 내용 | 채팅 포크 판단 |
| --- | --- | --- | --- |
| `93f4ed9` | [#104](https://github.com/FFXIVAPP/sharlayan/pull/104) | Lumina `GameData` 공유와 sheet cache | 제외 |
| `86feed5` | [#105](https://github.com/FFXIVAPP/sharlayan/pull/105) | BGM, sound, loading 상태 | 제외 |
| `65386b1` | [#106](https://github.com/FFXIVAPP/sharlayan/pull/106) | FCS/Lumina 갱신, status 수정 | FCS pin 갱신 방식만 반영 |
| `fac1b5e` | [#111](https://github.com/FFXIVAPP/sharlayan/pull/111) | 백그라운드 작업 오류 관찰 | 수동 반영 |
| `6162361` | [#112](https://github.com/FFXIVAPP/sharlayan/pull/112) | FCS 7.51, login-state latch | FCS 갱신 방식만 반영 |
| `dff01c5` | [#115](https://github.com/FFXIVAPP/sharlayan/pull/115) | FCS 갱신, enmity harness | FCS 갱신 방식만 반영 |
| `7bc42ce` | [#116](https://github.com/FFXIVAPP/sharlayan/pull/116) | 전체 감사, 74개 수정, 9.1.2 | 채팅 및 공통 메모리 수정 선별 반영 |

### 4.3 9.1.2에서 직접 유용한 수정

- `GetChatLog` 호출을 lock으로 직렬화한다.
- 채팅 entry 목록을 매 호출마다 만들지 않고 재사용한다.
- ring buffer wrap 시 index 메모리를 중복으로 읽지 않는다.
- entry를 임대 버퍼에 읽은 뒤 복사하지 않고 결과 배열에 직접 읽는다.
- timestamp와 chat code 추출에서 LINQ 임시 배열을 제거한다.
- 공개 채팅에 `:`가 없을 때 음수 길이 `Substring`을 호출하지 않는다.
- 잘린 특수 payload를 읽을 때 배열 경계를 확인한다.
- primitive 메모리 읽기용 1/2/4/8바이트 배열을 스레드별로 재사용한다.
- `GetUInt64`, `GetStructure<T>`, process handle 정리를 수정한다.
- 스캔 패턴을 영역별로 반복 변환하지 않고 시그니처별 한 번만 변환한다.
- 스캔 작업 실패 시 `IsScanning`을 확실히 해제하고 오류를 관찰한다.
- `OpenProcess`에서 먼저 read/query 최소 권한을 요청한다.
- `CloseHandle` P/Invoke 반환형을 Win32 `BOOL`과 일치시킨다.

이 변경은 chat-only 코드와 직접 겹치므로 cherry-pick 대신 현재 포크 파일에 맞춰 수동 이식한다.

## 5. 반영 범위 결정

| 우선순위 | 항목 | 결정 | 이유 |
| --- | --- | --- | --- |
| P0 | JSON 리소스 제거 | 필수 | 7.1 이후 최신성이 보장되지 않음 |
| P0 | FCS 기반 `CHATLOG` 시그니처와 오프셋 생성 | 필수 | 최신 패치 대응의 핵심 |
| P0 | `Reader.ChatLog`, `ChatLogReader`, `ChatEntry`, `ChatCleaner` 수정 | 필수 | 예외, 경쟁 상태, 할당 문제 수정 |
| P0 | `MemoryHandler`, `Scanner`, `UnsafeNativeMethods` 안전성 수정 | 필수 | 채팅 경로가 직접 의존함 |
| P0 | 단위 테스트와 CI | 필수 | 현재 자동 회귀 검증이 없음 |
| P1 | 동적 ring buffer capacity 사용 | 권장 | 고정 1,000 entry 가정의 위험 축소 |
| P1 | 명시적 초기화 완료 상태 또는 task | 권장 | 구조/시그니처 초기화 race 제거 |
| P1 | `CharacterName`을 채팅 결과에 사용 | 권장 | actor 기능 없이 `UNRESOLVED` 문제 해결 |
| P1 | dependency/version 중앙 관리 | 선택 | 릴리스 유지보수 개선, 채팅과 독립적 |
| P2 | target framework 정리 | 별도 결정 | 소비자 호환성에 직접 영향 |
| 제외 | Lumina와 XIV DB | 제외 | 채팅 decoding에 필요 없음 |
| 제외 | actor, inventory, party, target, enmity, job gauge | 제외 | 포크 범위 밖 |
| 제외 | BGM, weather, sound, game-state | 제외 | 포크 범위 밖 |
| 제외 | 전체 FCS DLL의 ILRepack | 기본안에서 제외 | 패키지 크기와 `net10.0` 결합을 증가시킴 |
| 제외 | upstream sample/harness 전체 | 제외 | 라이브 채팅 smoke test만 별도 최소 도구로 대체 |

## 6. 권장 리소스 아키텍처

### 6.1 기본안: 빌드 시 채팅 리소스 생성

FCS를 런타임 의존성이 아닌 source-of-truth로 사용한다.

```text
external/FFXIVClientStructs (고정된 submodule commit)
  -> net10.0 생성 도구
  -> Framework StaticAddress + 두 개의 field chain 추출
  -> LogModule의 두 StdVector 필드 오프셋 추출
  -> GeneratedChatResources.g.cs 생성
  -> Sharlayan.Lite 런타임은 생성된 상수만 사용
```

생성 도구의 책임은 다음으로 제한한다.

- FCS `Framework`의 `[StaticAddress]` pattern, relative-follow offset, `isPointer`를 읽는다.
- upstream의 `FFXIVClientStructsSignatureExtractor.BuildSignature`와 같은 규칙으로 `Signature`을 만든다.
- `Framework.UIModule`과 `UIModule.RaptureLogModule`의 `[FieldOffset]`을 읽는다.
- `LogModule.LogMessageIndex`와 `LogModule.LogMessageData`의 위치를 읽는다.
- 생성 파일에 FCS commit SHA와 생성 도구 버전을 기록한다.
- 출력 순서와 형식을 고정해 같은 입력에서 byte-for-byte 동일한 파일을 만든다.

런타임 구현은 generic `IResourceProvider` 계층 전체를 복사하지 않는다. 채팅 리소스 하나만 반환하는 작은 내부 provider 또는 정적 factory로 충분하다.

이 방식의 장점은 다음과 같다.

- 패키지에 FCS 전체를 포함하지 않는다.
- Lumina를 추가하지 않는다.
- 게임 실행 시 네트워크 요청이나 로컬 JSON cache가 필요 없다.
- 기존 multi-target 라이브러리와 net10.0 FCS 도구를 분리할 수 있다.
- FCS를 갱신할 때 생성 파일 차이로 채팅 주소 변경을 리뷰할 수 있다.
- upstream과 동일하게 패치마다 FCS pin과 패키지를 갱신하는 명시적 배포 절차를 갖는다.

### 6.2 대안: 런타임 FCS direct provider

생성 도구 prototype이 안정적으로 FCS 메타데이터를 추출하지 못할 때만 upstream 방식을 축소해 사용한다.

- `Framework`, `UIModule`, `LogModule` 관련 타입만 참조하는 chat-only direct provider를 만든다.
- FCS와 `InteropGenerator.Runtime`을 ILRepack한다.
- Lumina provider와 다른 mapper는 포함하지 않는다.
- 패키지 크기, build 시간, `net10.0` 강제 여부를 기본안과 비교한다.

대안이 기본안보다 유리하다고 판단하려면 실제 패키지 크기와 소비자 호환성 측정 결과가 있어야 한다. 단순히 upstream과 코드가 같다는 이유만으로 선택하지 않는다.

### 6.3 사용하지 않을 대안

- 기존 JSON URL을 fallback으로 유지하지 않는다. 최신성이 확인되지 않은 데이터가 실패를 숨길 수 있다.
- FCS 소스 일부를 포크 코드에 직접 복사하지 않는다. 동기화 지점과 라이선스 관리 비용이 늘어난다.
- `RaptureLogModule` 오프셋을 사람이 직접 갱신하는 방식으로 돌아가지 않는다.

## 7. 구현 단계

### Phase 0. 기준선 고정

목표는 변경 전 상태와 호환성 기준을 재현 가능하게 만드는 것이다.

- 로컬 `min-chat`을 `origin/min-chat`에 fast-forward한다. 완료: `9a03add7...`
- 별도 작업 브랜치를 만든다.
- 현재 NuGet의 압축 크기, DLL 크기, 공개 API 목록, 모든 target framework 빌드 결과를 기록한다.
- 현재 JSON의 `CHATLOG` signature, pointer path, `ChatLogPointers` 값을 fixture로 보존한다. fallback으로 사용하지 않고 비교 자료로만 둔다.
- implementation PR마다 대응 upstream commit/PR을 설명에 기록한다.

완료 조건:

- 현재 `Sharlayan.sln`이 모든 대상 프레임워크에서 빌드된다.
- baseline package와 공개 API 목록을 CI artifact로 만들 수 있다.

### Phase 1. 테스트 안전망 추가

upstream에는 mapper와 signature extractor 테스트가 있지만 chat parser와 ring buffer 전용 테스트가 없다. 이 포크에서는 채팅이 전 기능이므로 먼저 보완한다.

- `Sharlayan.Tests`를 추가하고 우선 `net8.0`에서 실행한다.
- `ChatEntry` 정상 메시지, 공개 채팅, 구분자 없는 공개 채팅, 8바이트 미만 입력, 일본어/국제 문자열을 검증한다.
- `ChatCleaner`의 잘린 marker, `length == 0/1`, control character, 개행, HTML entity를 검증한다.
- fake memory seam을 통해 첫 poll, 연속 poll, wrap, 빈 entry, 잘못된 offset을 검증한다.
- signature pattern 정규화, RIP-relative rewind, pointer dereference chain을 검증한다.
- `ChatLogPointers`의 vector 필드 순서와 `+0/+8/+16` 규칙을 검증한다.
- 기존 public API를 snapshot 또는 API compatibility 도구로 고정한다.

완료 조건:

- 게임 프로세스 없이 parser, cleaner, ring cursor, signature 변환을 재현할 수 있다.
- 현재 구현에서 실패하는 회귀 테스트는 원인을 명시하고 다음 phase의 수정과 함께 활성화한다.

### Phase 2. 9.1.2 채팅 및 공통 수정 수동 이식

대상 파일:

- `Sharlayan/Reader.ChatLog.cs`
- `Sharlayan/Utilities/ChatLogReader.cs`
- `Sharlayan/Utilities/ChatEntry.cs`
- `Sharlayan/Utilities/ChatCleaner.cs`
- `Sharlayan/MemoryHandler.cs`
- `Sharlayan/Scanner.cs`
- `Sharlayan/UnsafeNativeMethods.cs`

작업 내용:

- `GetChatLog`의 공유 cursor와 재사용 buffer를 하나의 lock으로 보호한다.
- poll당 index 배열을 한 번만 읽는다.
- entry 결과 배열에 process memory를 직접 읽는다.
- LINQ 기반 byte slicing과 불필요한 배열 복사를 제거한다.
- 구분자 없는 공개 채팅과 잘린 control payload의 경계를 검사한다.
- `GetUInt64` 변환과 `GetStructure<T>` handle/해제를 수정한다. Public `CloseHandle`의 기존 `int` 반환은 Win32 `BOOL`과 같은 32비트 폭이며 minor 릴리스 ABI 유지를 위해 변경하지 않는다.
- 먼저 `PROCESS_VM_READ | PROCESS_QUERY_INFORMATION`으로 연결하고 필요할 때만 기존 권한으로 fallback한다.
- primitive read buffer 재사용과 count 제한 `Peek` overload를 반영한다.
- scanner의 pattern 변환을 시그니처당 한 번 수행한다.
- scanner와 초기화 task의 예외를 `OnException`으로 전달하고 `IsScanning` 복구를 보장한다.

추가 검토:

- upstream 코드를 그대로 복사하기보다 이 포크에 남은 코드만 적용한다.
- silent `catch`를 새로 늘리지 않는다.
- lock 내부에서 consumer callback을 호출하지 않는다.
- thread-static buffer는 동일 스레드의 중첩 호출이 없는 primitive read에만 사용한다.

완료 조건:

- Phase 1 테스트가 모두 통과한다.
- 병렬 `GetChatLog` 호출에서 cursor와 buffer가 섞이지 않는다.
- malformed payload가 예외 없이 결정적인 결과를 반환한다.
- 연결/해제 반복에서 process handle이 증가하지 않는다.

### Phase 3. FCS 기반 채팅 리소스 생성

예상 구성:

- `external/FFXIVClientStructs`: 고정 commit의 submodule
- `tools/Sharlayan.ChatResources.Generator`: `net10.0` 생성 도구
- `Sharlayan/Resources/GeneratedChatResources.g.cs`: 리뷰 가능한 생성 결과
- `Sharlayan/Resources/ChatResourceProvider.cs`: 생성 결과를 런타임 모델로 노출
- `THIRD-PARTY-NOTICES.md`: FCS 출처, commit, 라이선스 기록

작업 내용:

- upstream FCS pin `15ae1806b...`로 첫 생성 결과를 만든다.
- 생성 도구가 `CHATLOG` 하나만 내보내도록 제한한다.
- `StructuresContainer`에는 `ChatLogPointers`만 채운다.
- 구조 정보는 handler 생성 중 동기적으로 설정해 현재의 구조/스캔 race를 제거한다.
- signature scan 완료는 관찰 가능한 task 또는 기존 event를 통해 명시한다.
- 런타임 네트워크 요청과 JSON cache 접근을 제거한다.
- CI가 생성 도구 실행 후 working tree diff가 있으면 실패하도록 한다.
- submodule update는 항상 기록된 commit을 사용하고 기본 빌드에서 `--remote`로 임의 갱신하지 않는다.

공개 API 호환 정책:

- 첫 마이그레이션은 8.x minor 릴리스를 목표로 한다.
- `APIBaseURL`, `GameRegion`, `JSONCacheDirectory`, `PatchVersion`, `UseLocalCache`는 즉시 삭제하지 않고 `[Obsolete]` no-op으로 유지한다.
- 공개 `APIHelper.GetSignatures/GetStructures`는 생성된 리소스를 반환하도록 바꾸고 obsolete 처리한다.
- 외부 소비자가 있는 공개 타입 제거는 다음 major 릴리스에서만 수행한다.
- JSON 직렬화 helper가 공개 API에 남아 있으므로 Newtonsoft.Json 제거는 이번 범위에 포함하지 않는다.

완료 조건:

- handler 초기화 중 HTTP 요청과 JSON 파일 쓰기가 발생하지 않는다.
- 생성된 signature의 pointer path가 FCS 메타데이터와 일치한다.
- FCS pin만 변경하면 생성 diff로 주소/오프셋 변화가 나타난다.
- 런타임 package에 FCS, InteropGenerator, Lumina assembly가 포함되지 않는다.
- package 크기는 Phase 0 baseline 대비 10% 이내 증가를 목표로 한다.

### Phase 4. ring buffer와 초기화 강화

- 고정 `BUFFER_SIZE = 4000`과 고정 capacity `1000` 대신 `OffsetArrayEnd - OffsetArrayStart`에서 capacity를 계산한다.
- `Start <= Pos <= End`, 4바이트 정렬, 합리적인 최대 capacity를 검사한다.
- `LogStart <= LogNext <= LogEnd`와 entry size 상한을 검사한다.
- 잘못된 pointer 상태에서는 빈 결과와 진단 가능한 오류를 반환하고 임의 크기 할당을 하지 않는다.
- 첫 poll은 기존과 같이 cursor만 prime하고 과거 메시지를 방출하지 않는 동작을 유지한다.
- `SharlayanConfiguration.CharacterName`이 설정되면 `PlayerCharacterName`에 사용하고, 없으면 `UNRESOLVED`를 유지한다.

완료 조건:

- capacity가 1,000이 아닌 fixture에서도 정상 동작한다.
- 음수 또는 비정상적으로 큰 entry 크기로 배열을 만들지 않는다.
- 첫 poll, wrap, 프로세스 종료 중 poll의 동작이 테스트로 고정된다.

### Phase 5. CI와 릴리스 정비

- PR과 push CI의 기준 브랜치를 `min-chat`으로 맞춘다.
- CI 순서를 restore, generate/check, build, test, pack으로 고정한다.
- 모든 기존 target framework를 빌드하고 테스트는 우선 `net8.0`에서 실행한다.
- release workflow에서도 publish 전에 같은 테스트를 실행한다.
- package artifact가 없거나 generated resource가 stale하면 publish하지 않는다.
- 현재 사용 중인 NuGet API key 방식은 별도 보안 작업으로 OIDC trusted publishing 전환을 검토한다.
- 자동 dependency update와 FCS pin update를 구분한다. FCS 변경 PR에는 generated diff와 live smoke 결과를 요구한다.

완료 조건:

- `min-chat` 대상 PR에서 CI가 실행된다.
- 테스트 실패 시 NuGet publish 단계에 진입하지 않는다.
- release artifact에 의도하지 않은 FCS/Lumina DLL이 없는지 자동 검사한다.

### Phase 6. 라이브 검증과 릴리스

게임 프로세스가 필요한 검증은 unit test와 분리한 opt-in smoke tool로 수행한다.

- 현재 FFXIV 클라이언트에서 `CHATLOG` 위치가 정확히 하나 해석되는지 확인한다.
- 첫 poll이 cursor를 prime하고 두 번째 poll부터 새 메시지만 반환하는지 확인한다.
- say, tell, party, alliance, novice, system 메시지를 확인한다.
- sender 구분자가 없는 메시지와 payload가 포함된 아이템/퀘스트 링크를 확인한다.
- ring wrap 전후 중복과 누락을 확인한다.
- 단일 consumer 30분 polling과 다중 thread 호출을 확인한다.
- 가능하면 upstream Sharlayan 9.1.2와 동일 프로세스의 cursor 및 raw entry 수를 비교한다.
- 관리자 권한이 없는 경우와 필요한 경우의 attach 결과를 각각 기록한다.

릴리스 순서:

1. 내부 또는 별도 feed에 prerelease를 배포한다.
2. 실제 consumer에서 package 교체, 초기화, 채팅 수집, 종료를 검증한다.
3. 공개 API diff와 package contents를 최종 확인한다.
4. 안정 버전을 배포하고 FCS pin 및 대응 게임 패치를 release note에 기록한다.

중단 조건:

- 현재 게임에서 `CHATLOG` signature가 유일하게 해석되지 않는다.
- wrap 검증에서 메시지 누락 또는 중복이 발생한다.
- 기존 target framework 소비자가 소스 수정 없이 업그레이드할 수 없다.
- package에 의도하지 않은 FCS/Lumina 런타임 의존성이 들어간다.

## 8. target framework와 버전 정책

upstream의 `net10.0` 전환은 전체 FCS/Lumina 재구축에 따른 선택이며 chat-only 런타임에 반드시 필요한 것은 아니다.

첫 마이그레이션에서는 `net462;net48;net6.0;net7.0;net8.0`을 유지한다. 생성 도구만 `net10.0`을 사용한다. 이렇게 해야 리소스 변경과 소비자 호환성 변경을 한 번에 섞지 않을 수 있다.

다음 major 릴리스에서는 별도 조사 후 지원 대상을 줄인다.

- `net6.0`과 `net7.0`은 지원 종료 상태이므로 제거 후보이다.
- `net462`와 `net48`은 실제 consumer 사용 여부를 확인한 후 결정한다.
- Windows 전용 메모리 API를 명확히 표현하기 위해 현대 target에는 Windows TFM 사용을 검토한다.
- target 제거, obsolete API 제거, 잔존 actor 모델 제거는 같은 major 호환성 작업으로 묶을 수 있다.

## 9. 의도적으로 보류할 항목

- `Reader.Actor.cs`와 잔존 actor 타입 제거는 공개 API 영향이 있어 이번 port와 분리한다.
- `CharacterName` 외에 current-player actor reading을 복원하지 않는다.
- upstream `LoggedInStateLatch`를 가져오지 않는다.
- Lumina 기반 이름, zone, status, action lookup을 가져오지 않는다.
- BGM, sound, weather, inventory, party, target, enmity, job resource 변경을 가져오지 않는다.
- upstream open Dependabot PR은 `main`에 병합되기 전까지 기준으로 사용하지 않는다.
- README와 package metadata 전면 정리는 기능 port가 안정된 후 별도 PR로 수행한다.

## 10. 예상 PR 분할

| PR | 범위 | 주요 검증 |
| --- | --- | --- |
| 1 | baseline 및 chat unit test | 기존 동작 characterization |
| 2 | upstream 9.1.2 chat/parser 수정 | parser, malformed payload, concurrency |
| 3 | memory/scanner/native 수정 | allocation, handle, scan failure |
| 4 | FCS generator와 generated resources | deterministic generation, integrity test |
| 5 | JSON 제거와 호환 shim | no-network, public API diff |
| 6 | dynamic ring capacity와 초기화 상태 | wrap, bounds, readiness |
| 7 | CI/release와 문서 | all-target build, test-before-publish |
| 8 | prerelease live validation 결과와 안정 버전 | live smoke, package contents |

각 PR은 독립적으로 빌드되고 되돌릴 수 있어야 한다. 특히 parser hardening과 resource migration을 한 PR에 섞지 않아야 문제 발생 시 원인을 구분할 수 있다.

## 11. 추적 체크리스트

- [x] 로컬 `min-chat`을 `origin/min-chat`에 fast-forward (`9a03add7...`)
- [x] baseline package/API/target framework 기록
- [x] chat parser와 cleaner 테스트 추가
- [x] chat reader ring buffer와 동시성 테스트 추가
- [x] PR #116 chat 변경 수동 이식
- [x] PR #116 memory/scanner/native 변경 수동 이식
- [x] 초기화 task 오류 관찰 적용
- [x] FCS submodule pin 추가
- [x] chat resource generator 구현
- [x] generated resource freshness CI 추가
- [x] JSON runtime dependency 제거
- [x] obsolete compatibility shim 적용
- [x] dynamic ring capacity 및 pointer invariant 적용
- [x] 명시적 초기화 완료 task와 상태 추가
- [x] `CharacterName` fallback 적용
- [x] `min-chat` CI와 수동 release workflow 정비
- [ ] 현재 게임 live smoke 수행
- [ ] prerelease consumer 검증
- [ ] stable release와 FCS/game patch 매핑 기록

## 12. 참고 링크

- upstream 저장소: <https://github.com/FFXIVAPP/sharlayan>
- Sharlayan 9 리소스 재구축: <https://github.com/FFXIVAPP/sharlayan/pull/93>
- 9.1.2 전체 감사: <https://github.com/FFXIVAPP/sharlayan/pull/116>
- 최신 upstream commit: <https://github.com/FFXIVAPP/sharlayan/commit/7bc42ce9dd312a87837bf60dae16a088e260218f>
- FCS 의존성: <https://github.com/aers/FFXIVClientStructs>
- 마지막 JSON 리소스 갱신: <https://github.com/FFXIVAPP/sharlayan-resources/commit/9721dc5b806abecc60e16b6cebc0f66264c19e79>
- upstream FCS dependency 문서: <https://github.com/FFXIVAPP/sharlayan/blob/main/DEPENDENCY.md>
