# Chat resource 생성 절차

## 개요

Sharlayan.Lite는 더 이상 `sharlayan-resources`의 JSON을 런타임에 다운로드하지 않는다. 채팅에 필요한 signature와 구조 offset은 고정된 FFXIVClientStructs(FCS) commit에서 빌드 시 생성해 source로 포함한다.

| 항목 | 값 |
| --- | --- |
| FCS 경로 | `external/FFXIVClientStructs` |
| FCS commit | `15ae1806b0c175d1e2dd2ae845e4c853f332fd07` |
| generator | `tools/Sharlayan.ChatResources.Generator` |
| 생성 결과 | `Sharlayan/Resources/GeneratedChatResources.g.cs` |
| generator target | `net10.0` |
| runtime FCS 참조 | 없음 |

## 생성 데이터

generator는 FCS의 다음 메타데이터만 읽는다.

- `Framework.Instance`의 `StaticAddressAttribute`
- `Framework.UIModule`의 `FieldOffsetAttribute`
- `UIModule.RaptureLogModule`의 `FieldOffsetAttribute`
- `LogModule.LogMessageIndex`의 `FieldOffsetAttribute`
- `LogModule.LogMessageData`의 `FieldOffsetAttribute`

현재 생성 결과는 다음과 같다.

```text
Signature: 488B1D????????8B7C24
PointerPath: [-7, 0, 0x2B68, 0x1AC0]
OffsetArray: 0x48 / 0x50 / 0x58
LogData: 0x60 / 0x68 / 0x70
```

## 로컬 생성

submodule을 초기화한 뒤 repository root에서 실행한다.

```powershell
git submodule update --init --recursive
dotnet run --project tools/Sharlayan.ChatResources.Generator/Sharlayan.ChatResources.Generator.csproj --configuration Release --no-launch-profile
```

입력 commit과 메타데이터가 같으면 생성 파일은 변경되지 않는다. CI는 generator 실행 후 다음 명령으로 stale output을 차단한다.

```powershell
git diff --exit-code -- Sharlayan/Resources/GeneratedChatResources.g.cs
```

## FCS 갱신 절차

1. `external/FFXIVClientStructs`를 검증할 commit으로 checkout한다.
2. generator를 실행한다.
3. 생성 파일의 FCS commit, signature, pointer path, vector offset diff를 검토한다.
4. 전체 target framework를 빌드하고 unit test를 실행한다.
5. 현재 게임 client에서 `CHATLOG` signature와 실제 채팅 poll을 smoke test한다.
6. submodule gitlink와 생성 파일을 같은 commit에 포함한다.

FCS submodule을 자동으로 `--remote` 갱신하지 않는다. repository에 기록된 gitlink가 유일한 생성 기준이다.

## 런타임 동작

- `MemoryHandler`는 생성된 structure를 동기적으로 설정한 뒤 scanner를 시작한다.
- `Signatures.Resolve`는 생성된 `CHATLOG` signature 하나를 반환한다.
- `APIHelper.GetSignatures/GetStructures`는 호환성을 위해 남아 있으며 생성 데이터를 반환한다.
- 기존 API/cache 관련 configuration property는 obsolete no-op으로 유지한다.
- handler 초기화 중 HTTP 요청과 JSON cache 읽기/쓰기는 발생하지 않는다.
- FCS, InteropGenerator.Runtime, Lumina assembly는 Sharlayan.Lite runtime package에 포함되지 않는다.

## 라이선스

FCS 출처와 MIT license는 `THIRD-PARTY-NOTICES.md`에 기록한다.
