# Parser hardening 기준선

## 목적

이 문서는 chat parser hardening을 시작하기 전 `min-chat`의 빌드, 패키지, 공개 API 기준을 기록한다. 이후 resource migration과 major API 정리에서 호환성 비교 기준으로 사용한다.

## 소스 기준

| 항목 | 값 |
| --- | --- |
| 브랜치 | `min-chat` |
| 기준 커밋 | `9a03add7da26723e1313e75032f7fec2ea89efa6` |
| 패키지 ID | `Sharlayan.Lite` |
| 패키지 버전 | `8.0.1` |
| assembly/file version | `8.0.0.0` |
| target frameworks | `net462;net48;net6.0;net7.0;net8.0` |

## 빌드 기준

다음 명령으로 package 생성을 끄고 전체 target framework를 확인했다.

```powershell
dotnet build Sharlayan.sln --configuration Debug -p:GeneratePackageOnBuild=false
```

기준 결과:

- 오류 0개
- 경고 15개
- 경고는 `MemoryHandler.cs`와 `ChatCleaner.cs`의 미사용 exception 변수에 대한 `CS0168`이다.
- 5개 target framework가 모두 빌드된다.

## 기존 패키지 기준

workspace의 기존 `published/Sharlayan.Lite.8.0.1.nupkg`를 배포 artifact 크기 비교 기준으로 사용한다.

| 항목 | 값 |
| --- | --- |
| 크기 | 351,001 bytes |
| SHA-256 | `3395d312c42d440a65f451c632d7e67837ca7155b910fc432a6d29556a44b8fe` |

이 파일은 git에 추적되지 않는 기존 artifact이다. 이후 package 크기 비교에는 같은 Release build와 pack 절차를 사용해야 한다.

## 공개 API 기준

`net8.0` assembly를 reflection으로 확인한 결과는 다음과 같다.

| 항목 | 값 |
| --- | --- |
| exported type 수 | 44 |
| public declared member 수 | 1,042 |

member 수에는 생성자, property accessor, delegate method, enum field 등 reflection의 `DeclaredOnly` public member가 포함된다. 숫자는 빠른 변경 감지용이며, 실제 호환성 판정은 `Sharlayan.Lite` 8.0.1 assembly를 contract로 사용하는 ApiCompat 결과를 기준으로 해야 한다.

이번 parser hardening의 Release `net8.0` assembly를 기존 8.0.1 package의 `net8.0` assembly와 Microsoft ApiCompat 10.0.302로 비교했으며 호환성 오류가 없음을 확인했다.

채팅 소비자가 직접 의존하는 핵심 공개 API는 다음과 같다.

```text
SharlayanMemoryManager.Instance
SharlayanMemoryManager.AddHandler(SharlayanConfiguration)
SharlayanMemoryManager.GetHandler(int)
SharlayanMemoryManager.GetHandlers()
SharlayanMemoryManager.RemoveHandler(int)

MemoryHandler.Reader
MemoryHandler.Scanner
MemoryHandler.OnException
MemoryHandler.OnMemoryHandlerDisposed
MemoryHandler.OnMemoryLocationsFound

Reader.CanGetChatLog()
Reader.GetChatLog(int previousArrayIndex = 0, int previousOffset = 0)

ChatLogResult.ChatLogItems
ChatLogResult.PreviousArrayIndex
ChatLogResult.PreviousOffset

ChatLogItem.Bytes
ChatLogItem.Code
ChatLogItem.Combined
ChatLogItem.IsInternational
ChatLogItem.Line
ChatLogItem.Message
ChatLogItem.PlayerCharacterName
ChatLogItem.PlayerName
ChatLogItem.Raw
ChatLogItem.TimeStamp

SharlayanConfiguration.APIBaseURL
SharlayanConfiguration.CharacterName
SharlayanConfiguration.GameLanguage
SharlayanConfiguration.GameRegion
SharlayanConfiguration.JSONCacheDirectory
SharlayanConfiguration.PatchVersion
SharlayanConfiguration.ProcessModel
SharlayanConfiguration.ScanAllRegions
SharlayanConfiguration.UseLocalCache
```

## 이번 작업의 호환성 범위

- `ChatEntry`와 `ChatCleaner`의 구현만 변경한다.
- 공개 type, method, property signature를 추가하거나 제거하지 않는다.
- `InternalsVisibleTo`는 `Sharlayan.Tests`에만 부여한다.
- target framework와 NuGet package ID/version은 변경하지 않는다.
- 기존 release workflow와 publish 조건은 변경하지 않는다.
