# 플래그 한국어 표시 검사

검사일: 2026-10-26. 워크스페이스 소스와 설치된 활협전의 데이터를 기준으로 검사했습니다.

**플래그 이름과 조건 표시는 검사 범위에서 정상입니다. F9 전체 설명에는 47건의 한중 혼합 표기가 남아 있습니다.**

| 검사 대상 | 결과 |
| --- | --- |
| 실제 게임 FlagData | 1,069개 |
| 설명이 있는 플래그 | 884개, 고유 이름 841개 |
| 원래 설명이 없는 플래그 | 185개. ID만 표시하는 항목이며 번역 누락으로 세지 않음 |
| 첫 줄 이름의 중국어 잔여 | 0건 |
| 조건·색상 태그·변경 알림·설정 결과·증감 결과·역추적 문장 | 5,304개 조합 중 실패 0건 |
| 게임이 실제 수집한 조건 기록 | 5,054건 중 중국어 잔여 0건 |
| F9에 표시되는 전체 설명 | 884건 중 중국어 잔여 47건 |
| 기존 번역 검사 | 2,961개 통과 |

## 확인된 문제

1. **기존 전체 설명 번역이 F9에 적용되지 않는 항목: 44건.**
   `UiTextKorean.Notes`에 번역이 있지만 `NoteWords()`는 `〔…〕` 또는 `〈…〉`로 감싼 문장만 처리합니다. F9는 이 괄호 없이 `DevNote` 전체를 표시합니다. 여기에 `.Replace("\n", " ")`만 적용해 `\r`이 남고, 연속 공백도 번역표의 키와 달라집니다. 첫 줄 플래그 이름만 치환되고 뒤의 값별 설명은 중국어로 남습니다.
2. **전체 설명 번역 자체가 없는 항목: 3건.**
   `M30052`(엽운상이 신도룡에게 준 비급의 종류), `M80006`(삼사형 호위 중 첫 번째 자객), `M80011`(당문에서 동서 무림맹이 내린 결정)의 값별 설명이 빠져 있습니다. 첫 줄 제목 번역은 존재합니다.

재현 사례:

| 플래그 ID | 현재 번역 결과 |
| --- | --- |
| `M20008` | `유학 1: 青城 2: 崆峒` |
| `M20001` | `소사매가 남궁세가 연회에 참전 1: 参战` |
| `M30007` | `개방 포로 수\r 1:两成\r 2:五成` |
| `M80011` | `당문에서 동서 무림맹이 내린 결정\r 1. 护卫삼사형\r 2. 唐门出击` |

전체 목록과 실제 치환 결과는 [flag-description-missing.tsv](flag-description-missing.tsv), 모든 플래그 결과는 [flag-translations.tsv](flag-translations.tsv)에 있습니다. TSV의 `\r`, `\n`, `\t`는 실제 제어문자를 읽기 쉽게 표기한 것입니다.

관련 구현:

- `LOMAssistant.Features.Story/StoryReviewPanel.Stats.cs:426`: F9 전체 설명 생성.
- `LOMAssistant/DataHelper.cs:513`: 조건과 선택지 등에 사용할 첫 줄 이름 생성.
- `LOMAssistant/UiTextKorean.cs:18`: 괄호 안 설명에 한정된 번역 매칭.

수정 방향은 공백·줄바꿈을 정규화한 **전체 설명 조회를 단어 치환보다 먼저 수행**하고, 누락된 3개 전체 설명을 추가하는 것입니다. 게임 판정용 ID와 값은 그대로 유지해야 합니다.

## 검사 방법과 한계

- 게임을 실행하거나 저장을 바꾸지 않고 Unity 자산을 읽었습니다. `Mortal.Core.FlagData`의 MonoScript 신원을 확인하고 직렬화 구조를 검증하여 추출했습니다.
- 현재 소스의 실제 `UiText.Translate`를 실행했습니다. 테스트에 필요한 외부 번역 파일 로더와 일본어 패치 안내만 대체했습니다.
- 실제 조건 캐시는 `StoryReview/conditions.json`, 게임 버전은 `release_1.0.6000.5`입니다. 실제 조건 검사에서는 괄호 안 문장을 무조건 제외하지 않고 중국어를 엄격하게 검사했습니다.
- 번역표의 이름이 결과에 온전히 포함되는지, 플래그 ID·비교식·색상 태그가 유지되는지도 확인했습니다.
- 이는 번역 적용·누락 검사입니다. 전체 번역의 의미와 말투가 정확하다는 보증이나 게임 화면에서의 글꼴·줄바꿈 검수는 아닙니다. 현재 게임을 실행해 화면으로 확인하지는 않았습니다.
- 이번 작업에서는 검사 도구와 결과 보고서만 추가했습니다. 번역표·게임 동작 코드·설치된 DLL은 수정하지 않았습니다.

## 다시 검사하기

워크스페이스 루트에서 실행합니다. 추출에는 기존 `.venv`의 UnityPy가 필요합니다.

```powershell
& .venv/Scripts/python.exe source/tools/ko-check/export-game-flags.py 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal' source/bin/ko-check/game-flags.json
powershell -NoProfile -ExecutionPolicy Bypass -File source/tools/ko-check/run.ps1 -GameFlags source/bin/ko-check/game-flags.json -LiveConditions 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/BepInEx/plugins/LOMAssistant/StoryReview/conditions.json'
```

새 검사 결과는 `source/bin/ko-check/flag-translations.tsv`, `flag-context-failures.tsv`, `live-condition-failures.tsv`, `flag-audit-summary.txt`에 생성됩니다. 이 보고서와 `reports`의 TSV는 검사 당시의 스냅샷입니다. 기존 검사 통과 메시지와 별개로 `F9 full descriptions with Chinese remaining` 수치를 확인해야 합니다.
