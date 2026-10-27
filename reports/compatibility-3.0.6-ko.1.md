# 3.0.6-ko.1 이식 검토

검토일: 2026-10-27. 대상 원본: `v3.0.6`, 커밋 `bd7538f8c980e922c146f97ebea7781fbad759f2`.
기존 한국어 개선판: `df09901` (`2.9.3-ko.1`). 게임: 설치된 데이터에서 확인한 `release_1.0.6001.0`.

## 병합 범위

- 원본 3.0.6을 병합하고 한국어 번역표·설정 설명·버전 정보·포크 README의 충돌을 해결했다. 새 원본 번역 키가 빠지지 않았음을 비교했다.
- 기존 한국어 플래그·조건·이름 데이터 파일과 이벤트명 TSV는 2.9.3-ko.1과 동일하다. 새 조건명 4개는 보충 목록에, 호전차 판정은 주석 번역 목록에 추가했다.
- 기존 한국어 수정 대상과 AI 화면의 공략 힌트 제목 표시를 제외한 원본 런타임 소스 **112개가 v3.0.6과 동일**함을 확인했다. AI 통신·도구 실행, 새 회상 기록, 퀵세이브, 분기 저장 등의 원본 변경을 포함한다.
- `conditions.json.gz`와 `contract.tsv.gz`는 v3.0.6 파일과 바이트 단위로 동일하다. 내장 조건 데이터는 Conditions 529개, Switches 3,458개다.
- `StoryReviewPanel.Flow.cs`와 `StoryReviewPanel.Route.cs`는 2.9.3-ko.1의 파일과 동일하다. 취소한 `Readability.cs`와 `FlowChoices.cs`는 빌드 대상에 없다.
- AI 조회 상태 문구를 번역표에 추가했다. `StoryReviewPanel.Ai.cs`에서 달라진 것은 스트리밍 답변의 고정 공략 힌트 제목을 표시 언어로 변환하는 한 줄뿐이며, 모델 답변 본문은 번역표로 재작성하지 않는다.
- 분기 저장의 한국어 설명은 중요 선택지 기준에서 게임의 자동 저장 기준으로 수정했다. 게임 판정·플래그 설정·저장 실행 동작을 한글화 목적으로 추가 변경하지 않았다.

## 검사

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build-ko.ps1
../.venv/Scripts/python.exe tools/ko-check/export-game-flags.py 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal' bin/ko-check/game-flags-3.0.6.json
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ko-check/run.ps1 -GameFlags bin/ko-check/game-flags-3.0.6.json -LiveConditions 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal/BepInEx/plugins/LOMAssistant/StoryReview/conditions.json'
```

- 빌드 성공. 원본에도 있는 미사용 필드 관련 컴파일 경고 2건(CS0649, CS0414)이 있다.
- 번역 검사 **3,103건 통과**. 신규 조건명·AI 조회 문구·글머리표와 동적 이름 조합도 검사했다.
- 3.0.6 내장 조건의 중국어 잔여 **0건**. 이제 잔여 문구가 있으면 검사 스크립트가 실패하도록 했다.
- 새로 추출한 플래그 **1,069개**, 설명이 있는 플래그 884개. 표시 조합 **5,304건, 실패 0건**.
- 기존 실행 캐시의 조건 5,054건에서 중국어 잔여 0건. 새 버전 게임 실행 결과로 해석하면 안 된다.

## 남은 범위

F9 플래그 전체 설명의 한중 혼합 47건은 남아 있다. 소스 문자열 감사에는 로그·식별자·AI 내부 프롬프트도 포함되므로, 후보 건수를 화면 미번역 개수로 해석하지 않는다. AI 생성 답변의 언어·용어 정확성은 이 검사로 보장하지 않는다.

이 작업에서 게임을 실행하거나 설치 폴더를 수정하지 않았다. 게임 안의 표시, 저장·불러오기·회상, 실제 AI 연결은 추가 실플레이 검증이 필요하다. 따라서 배포 시에는 사전 배포판으로 다루는 것이 적절하다.
