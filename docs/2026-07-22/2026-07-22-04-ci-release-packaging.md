# CI 및 릴리스 패키징

## 목적

CI와 수동 release가 동일한 generate, build, test, pack, verify 순서를 사용하도록 고정한다. 일반 브랜치 push는 NuGet package를 게시하지 않는다.

## CI 순서

`min-chat` push와 pull request에서 다음 단계를 실행한다.

1. recursive submodule checkout
2. FCS 기반 chat resource 생성 및 freshness 검사
3. solution restore
4. 5개 target framework Release build
5. `Sharlayan.Tests` 실행
6. 명시적 `dotnet pack --no-build`
7. NuGet 8.0.1 baseline package API 호환성 검사
8. package contents와 크기 검사
9. `.nupkg`와 `.snupkg` artifact 업로드

`GeneratePackageOnBuild`는 비활성화한다. 일반 build가 `published` 디렉터리를 변경하거나 package를 생성하지 않는다.

## Package 검사

`dotnet pack`은 NuGet 8.0.1 baseline과 package API 호환성을 검사한다. 이후 `tools/Verify-Package.ps1`은 다음 조건을 검사한다.

- package 디렉터리에 ID와 version이 일치하는 `.nupkg`와 `.snupkg`가 각각 하나 있음
- `net462`, `net48`, `net6.0`, `net7.0`, `net8.0`용 `Sharlayan.dll` 포함
- 5개 target framework용 symbol PDB 포함
- `THIRD-PARTY-NOTICES.md`와 `Logo.png` 포함
- 의도하지 않은 runtime assembly 부재
- FCS, InteropGenerator, Lumina, System.Net.Http runtime dependency 부재
- 기존 8.0.1 package 351,001바이트 대비 main/symbol 합산 10% 상한인 386,101바이트 이하

첫 표준 pack 결과의 main package 크기는 236,061바이트다. `8.1.0-beta.1` 검증 산출물의 main/symbol 합산 크기는 324,646바이트다. PDB와 source symbol은 별도 `.snupkg`로 분리되므로 기존 symbols package rename script는 사용하지 않는다.

로컬 검증 명령은 다음과 같다.

```powershell
dotnet build Sharlayan.sln --configuration Release -p:GeneratePackageOnBuild=false
dotnet test Sharlayan.Tests/Sharlayan.Tests.csproj --configuration Release --no-build
dotnet pack Sharlayan/Sharlayan.csproj --configuration Release --no-build --output artifacts/packages
./tools/Verify-Package.ps1 -PackageDirectory artifacts/packages
```

## 수동 Release

`Release Sharlayan.Lite` workflow는 `min-chat` 브랜치에서 `workflow_dispatch`로만 실행한다.

- `package_version`: 생성할 NuGet version. prerelease 예시는 `8.1.0-beta.1`이다.
- `publish`: 기본값은 `false`다. `false`이면 검증된 artifact만 만들고 NuGet에는 게시하지 않는다.
- `publish: true`: 모든 검증을 통과한 단일 package를 `NUGET_TOKEN`으로 게시한다.

NuGet push에는 `--skip-duplicate`를 사용하지 않는다. version 충돌이나 잘못된 재배포는 workflow 실패로 명확히 드러나야 한다.

현재 게임 live smoke와 실제 consumer 검증이 끝나기 전에는 stable version을 게시하지 않는다.
