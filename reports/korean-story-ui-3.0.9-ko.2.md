# 스토리 화면 한국어 전환 — 3.0.9-ko.2

작업일: 2026-10-29. 브랜치: `korean-native-3.0.9`.

## 사용자 확인과 제약

사용자는 ko.1의 점검 안내에 대해 「잘 됨」으로 응답했다. 모든 이벤트를 개별 검증했다는 뜻으로 확대하지 않는다. 화면 비율·배치·크기·디자인은 유지하며, 이 단계에서는 표시 문구와 해당 문구의 캐시만 전환한다.

## 구현

- `KoreanStoryMessage`/`KoreanStoryText`는 진행 경로·흐름도·수집 현황의 문구를 키와 데이터로 생성한다. 게임 의존성이 없는 날짜·기간·읽음·인연 단계·발생 경로 함수를 포함한다.
- `StoryUi`는 기존 게임 데이터를 한국어 표시 함수에 전달한다. 기존 `SeriesTitle`, `Desc`, `When`, 날짜 범위·임무 ID·달성 판정·조건과 경로 분석은 유지한다.
- `StoryMission`에 원문 전체 이름과 표시용 시기 메타데이터를 보관한다. 임무 이름은 전체 일치를 먼저 조회하고, 필요한 경우 이름의 ` - ` 구간을 나눠 조회한다. 번역 이전의 원문 축약으로 이름이 누락되는 것을 줄인다.
- `StoryAtlas.Status`는 기존 값을 유지하고, 표시용 진행 상태를 별도로 제공한다. 조건 목록의 논리 연산은 그대로 유지하면서 화면의 「또는」을 한국어로 구성한다.
- `RefreshStoryLanguage`는 언어 변경 시 문구·읽음 표시·경로·흐름도 캐시만 비운다. 스토리 자료, 읽은 기록, 달성 기록, 설정·저장은 지우지 않는다.
- 흐름도 탐색·연결·정렬·상자 생성의 알고리즘은 유지한다. 저장·이벤트 실행·난수 추첨 코드도 수정하지 않았다.

## 확인

- 빌드 성공. 기존 경고 2건 유지.
- 자동 검사 7,702건 통과. 새 문구 키·매개변수·중국어 여부, 연말/연초·월 경계, 매달/기간/시기 미정, 읽음/방문/달성의 구분, 결투 패배 조건과 경로 순서를 검사한다.
- 생성 DLL에서 임무 날짜 표시와 전체 임무명 조회가 새 코드에 연결된 것을 확인한다. ko.1의 읽기 전용 스토리 추적·분석용 원본 유지 검사도 통과한다.
- `LayoutChecks`는 사용자 승인 버전 `2a7fe65cf132b735de3c1703ab4b5439f1ecaae8`의 네 화면 파일과 비교한다. 한국어 텍스트 래퍼를 제외한 `Rect`, `GUIStyle`, `S`/`Z`, 폭/높이·여백 표현식이 동일하다. 이는 소스의 배치·스타일 설정 검사이며 실제 화면 캡처 대조는 아니다. 문자열 길이에 따른 기존 자동 배치는 유지된다.
- `export-game-mission-labels.py`는 게임의 `Mortal.Core.MissionData`/`MissionCheckData` MonoScript와 파일 참조를 확인하고, 이름·설명 필드만 로컬로 읽는다. 임무 그룹 211개·단계 300개, 총 511개에서 제목 후보를 대조했으며 중국어 잔여 0건이다. 비활성·표시되지 않는 자료도 포함하므로 511개 화면을 플레이했다는 의미는 아니다. 게임 대사 전문은 추출하거나 패키지에 넣지 않는다.
- 기존 플래그 표시 조합 5,304건 실패 0건, 조건 캐시 5,060건 중국어 잔여 0건. UI 소스의 중국어 후보에는 비한국어용 문구·로그·내부 키가 포함되므로 실제 미번역 화면 수로 보지 않는다.

재현 명령:

```powershell
../.venv/Scripts/python.exe tools/ko-check/export-game-mission-labels.py '게임 설치 폴더' bin/ko-check/game-mission-labels-3.0.9.json
powershell -NoProfile -ExecutionPolicy Bypass -File build-ko.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ko-check/run.ps1 -GameFlags bin/ko-check/game-flags-3.0.6.json -LiveConditions '게임 설치 폴더/BepInEx/plugins/LOMAssistant/StoryReview/conditions.json' -ModAssembly bin/LOMAssistant.dll
```

## 후속 범위

사용자가 2026-10-29 ko.2 적용 후 「저거 됐음」으로 작동을 확인했다. 모든 이벤트·저장 상황·외부 AI 응답을 개별 검증한 것은 아니다. 설정·저장 화면·AI 내부 안내 전환, F9 전체 설명의 기존 한중 혼합 47건 검수는 남아 있다. 에이전트가 게임 설치와 유료 AI 요청을 수행하지는 않았다. 이후 순서와 확인 기준은 [남은 작업 정리](korean-migration-remaining.md)를 따른다.
