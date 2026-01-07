# Assembly-based Signature Guide for Sharlayan.Lite

이 문서는 Final Fantasy XIV의 메모리 주소를 찾기 위한 Assembly-based Signature 작성 방법을 설명합니다.

## 목차

1. [개요](#개요)
2. [ASM Signature란?](#asm-signature란)
3. [Signature 구조](#signature-구조)
4. [Cheat Engine으로 Signature 찾기](#cheat-engine으로-signature-찾기)
5. [Sharlayan 형식으로 변환](#sharlayan-형식으로-변환)
6. [예제: NPC 대화 Signature](#예제-npc-대화-signature)
7. [문제 해결](#문제-해결)

---

## 개요

게임이 업데이트될 때마다 메모리 주소가 변경됩니다. Assembly-based Signature를 사용하면 고정된 바이트 패턴을 기반으로 동적으로 주소를 찾을 수 있어, 매 패치마다 수동으로 주소를 업데이트할 필요가 없습니다.

## ASM Signature란?

x64 Windows 실행 파일에서는 **RIP-relative addressing**을 사용합니다:

```asm
mov rax, [rip+12345678]  ; 48 8B 05 78 56 34 12
```

이 명령어는 현재 명령어 포인터(RIP) 기준으로 상대 주소를 계산합니다:
- `실제 주소 = 명령어 주소 + displacement + 명령어 길이`

Sharlayan은 이 패턴을 찾아 자동으로 실제 메모리 주소를 계산합니다.

## Signature 구조

### Signature 모델 (`Sharlayan/Models/Signature.cs`)

```csharp
public class Signature {
    public bool ASMSignature { get; set; }      // ASM 시그니처 여부
    public string Key { get; set; }              // 고유 식별자
    public string Value { get; set; }            // 바이트 패턴 (hex)
    public List<long> PointerPath { get; set; } // 오프셋 체인
}
```

### Value 형식

- 16진수 문자열 (공백 없음)
- `??` 또는 `**`: 와일드카드 (어떤 바이트든 매칭)

```
488B05????????488D5424??C74424
       ^^^^^^^^        ^^
       와일드카드 (4바이트, 1바이트)
```

### PointerPath 작동 방식

`MemoryHandler.ResolvePointerPath()` 메소드의 동작:

```csharp
foreach (long offset in path) {
    baseAddress = new IntPtr(nextAddress.ToInt64() + offset);

    if (IsASMSignature) {
        // RIP-relative 계산: base + int32값 + 4
        nextAddress = baseAddress + GetInt32(baseAddress) + 4;
        IsASMSignature = false;
    } else {
        // 일반 포인터 역참조
        nextAddress = ReadPointer(baseAddress);
    }
}
return baseAddress;
```

**핵심 포인트:**
- 첫 번째 오프셋: RIP-relative 계산 수행
- 이후 오프셋: 일반 포인터 역참조
- 마지막 오프셋 후에는 역참조 없이 주소 반환

---

## Cheat Engine으로 Signature 찾기

### Step 1: 대상 데이터 찾기

1. Cheat Engine을 ffxiv_dx11.exe에 연결
2. **Scan Type**: String (UTF-8)
3. 찾고자 하는 텍스트 검색 (예: NPC 대사)

### Step 2: 접근하는 코드 찾기

1. 찾은 주소 우클릭 → **"Find out what accesses this address"**
2. 게임에서 해당 기능 실행 (예: NPC와 대화)
3. 접근하는 명령어 목록 확인

### Step 3: Pointer Scan 실행

1. **Tools** → **Pointer Scanner** → **Scan for Address**
2. 설정:
   ```
   Address to find: [찾은 문자열 주소]
   Max Level: 5
   Max Offset: 8000 (hex)
   ☑ Only find paths with a static base
   ```
3. `ffxiv_dx11.exe+XXXXXXX` 형태의 결과 찾기

### Step 4: 베이스 주소의 코드 찾기

1. Pointer Scan 결과에서 베이스 주소 확인
2. 해당 주소를 CE 메인 화면에 추가
3. 우클릭 → **"Find out what accesses this address"**
4. `mov r??, [rip+????]` 형태의 명령어 찾기

### Step 5: 바이트 패턴 추출

1. **"Show disassembler"** 클릭
2. 해당 명령어 주변 선택
3. 우클릭 → **"Copy to clipboard"** → **"Bytes only"**

---

## Sharlayan 형식으로 변환

### 바이트 패턴 변환

**원본 바이트:**
```
8B C5 F0 0F C1 05 D9 6E 42 02 85 C0 74 38 48 8B 15 36 6E 42 02
```

**변환 과정:**
1. 공백 제거
2. 변하는 displacement를 `??`로 교체
3. 변할 수 있는 jump offset도 `??`로 교체

**결과:**
```
8BC5F00FC105????????85C074??488B15
```

### PointerPath 변환

**Cheat Engine Pointer Scan 결과:**
```
Base Address: ffxiv_dx11.exe+027A9618
Offset 0: A8
Offset 1: 20
Offset 2: 20
Offset 3: 120
Offset 4: 0
```

**Sharlayan PointerPath 변환:**

| CE 형식 | Sharlayan | 설명 |
|---------|-----------|------|
| Base | `0` | RIP-relative 계산 |
| (암시적) | `0` | 베이스 주소 역참조 |
| A8 | `168` | 0xA8 = 168 |
| 20 | `32` | 0x20 = 32 |
| 20 | `32` | 0x20 = 32 |
| 120 | `288` | 0x120 = 288 |
| 0 | `0` | 최종 오프셋 |

**결과:** `[0, 0, 168, 32, 32, 288, 0]`

### 음수 오프셋 사용

패턴이 displacement 이후까지 계속될 경우, 음수 오프셋으로 조정:

```json
{
  "PointerPath": [-19, 0, 168, 32, 32, 288, 0],
  "Value": "8BC5F00FC105????????85C074??488B15????????488B0D"
}
```

계산: `SigScanAddress`가 패턴 끝에 있으므로, displacement 위치로 돌아가기 위해 음수 사용.

---

## 예제: NPC 대화 Signature

### 최종 Signature 정의

```json
{
  "ASMSignature": true,
  "Key": "NPCTALK",
  "PointerPath": [0, 0, 168, 32, 32, 288, 0],
  "Value": "8BC5F00FC105????????85C074??488B15"
}
```

### 코드에서 사용

```csharp
// Signature 정의
var npcTalkSignature = new Signature {
    ASMSignature = true,
    Key = "NPCTALK",
    PointerPath = new List<long> { 0, 0, 168, 32, 32, 288, 0 },
    Value = "8BC5F00FC105????????85C074??488B15"
};

// Scanner에 추가 후 주소 획득
IntPtr npcTalkAddress = memoryHandler.Scanner.Locations["NPCTALK"];

// 문자열 읽기
string npcDialogue = memoryHandler.GetString(npcTalkAddress);
```

---

## 문제 해결

### Signature가 찾아지지 않는 경우

1. **패턴 확인**: 게임 업데이트로 코드가 변경되었을 수 있음
2. **와일드카드 추가**: 변할 수 있는 바이트에 `??` 추가
3. **ScanAllRegions 활성화**: 메인 모듈 외부에 있을 수 있음

### 주소는 찾았지만 데이터가 올바르지 않은 경우

1. **오프셋 확인**: 게임 업데이트로 구조체 레이아웃이 변경되었을 수 있음
2. **Pointer Scan 재실행**: 새로운 오프셋 체인 확인
3. **디버그 출력**: 각 단계의 주소 값 확인

```csharp
// 디버그용 코드
var baseAddr = memoryHandler.Scanner.Locations["NPCTALK"].GetAddress();
Console.WriteLine($"Base: {baseAddr:X}");

var ptr1 = memoryHandler.ReadPointer(baseAddr);
Console.WriteLine($"After deref: {ptr1:X}");
// ...
```

### 흔한 실수

| 실수 | 해결 방법 |
|------|----------|
| PointerPath에 첫 번째 `0` 누락 | ASM signature는 항상 첫 번째 오프셋에서 RIP 계산 수행 |
| 16진수/10진수 혼동 | PointerPath는 10진수, Value는 16진수 |
| 와일드카드 개수 오류 | 각 바이트 = 2개 문자 (`??` = 1바이트) |
| 마지막 역참조 누락 | 필요시 마지막에 `0` 추가하여 최종 역참조 수행 |

---

## 참고 자료

- [Sharlayan GitHub](https://github.com/FFXIVAPP/sharlayan)
- [Cheat Engine Tutorial](https://wiki.cheatengine.org/)
- [x64 RIP-relative Addressing](https://www.tortall.net/projects/yasm/manual/html/nasm-effaddr.html)

---

## 기여

새로운 Signature를 찾으셨다면:
1. 이 가이드에 따라 테스트
2. 여러 게임 패치에서 동작 확인
3. Pull Request 제출
