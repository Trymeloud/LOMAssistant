
# LOMAssistant 한국어 개선판

**『활협전』(Legend of Mortal) LOMAssistant의 비공식 한국어 현지화 개선 프로젝트입니다.**

원본 모드의 한국어 번역에서 발견되는 중국어 혼용, 미번역 문구, 잘못된 인물명과 용어를 수정하고 있습니다.

이 프로젝트는 [kevinnie2003의 LOMAssistant](https://github.com/kevinnie2003/LOMAssistant)를 기반으로 합니다.

## 주요 개선 사항

- **한국어 번역 보완:** 기존에 누락된 스토리 조건 이름과 플래그 설명 1,053개 항목에 한국어 번역 추가
- **공식 용어 반영:** 활협전 공식 한국어 번역을 참고하여 인물명, 능력치, 장소명 등의 표기 개선
- **스토리 화면 개선:** 현재 상황, 스토리 회고, 수집 현황 등에서 중국어가 출력되는 문제 개선
- **텍스트 처리 수정:** 목록에서 텍스트가 잘릴 때 중국어 원문이 출력되는 현상 수정
- **랜덤 이벤트명 번역:** 이벤트 ID 397개에 대해 원문 이름까지 구분한 한국어 표시 이름 406개를 별도 번역표로 제공
- **번역 검사 자동화:** 번역 누락 및 문자열 처리 오류를 확인하기 위한 검사 도구 추가

## 진행 상태

**현재 배포 버전: [v3.0.9-ko.4 (사전 배포)](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.9-ko.4).**

개발 브랜치 `korean-native-3.2.1`은 **3.2.1-ko.2 통합 검증판**입니다. 원본 3.1.0~3.2.1의 변경과 기존 한국어 개선을 합쳤으며, ko.2에서는 스토리 점프의 「채우지 못하는 것」 설명을 한국어로 직접 생성하도록 수정했습니다. [설치 및 확인 항목](releases/v3.2.1-ko.2.md), [원작 한국어 보완 범위와 통합 보고서](reports/upstream-3.2.1-korean-review.md)를 참고하세요. GitHub 공개 설치판은 위의 3.0.9-ko.4입니다.

**이번 버전은 AI 채팅 전환을 포함한 공개 사전 배포판입니다.** 사용자가 ko.1과 ko.2 적용 후 작동을 확인했으며 ko.3의 실제 플레이·모델 답변은 확인이 필요합니다. 승인된 화면 비율·배치·크기·글꼴·여백 설정을 유지합니다. 모든 동적 자료와 화면을 새 구조로 바꾼 것은 아니며 기존 검수 번역표도 함께 사용합니다. [완료 범위와 남은 작업 순서](reports/korean-migration-remaining.md)에 AI 실사용 확인 → 설정·저장 화면 → F9 전체 설명 → 후속 통합 검수 순으로 정리했습니다. 게임 설치 폴더는 빌드 과정에서 변경하지 않습니다.

### 한국어 개선판 버전 이력

커밋된 소스·로컬 검증 패키지와 GitHub의 공개 릴리스는 구분합니다. 별도 릴리스를 만들지 않은 개발판도 아래 이력에 남깁니다.

| 버전 | 주요 변경 | 배포 상태 | 상세 내역 |
|---|---|---|---|
| 2.9.3-ko.1 | 플래그·조건·인물명과 이벤트명 번역 보완 | [공개 사전 배포](https://github.com/Trymeloud/LOMAssistant/releases/tag/v2.9.3-ko.1) | [변경 사항](releases/v2.9.3-ko.1.md) |
| 3.0.6-ko.1 | 원본 3.0.6 대응 및 새 조건·AI 조회 안내 번역 | 개발판, 별도 공개 릴리스 없음 | [변경 사항](releases/v3.0.6-ko.1.md) |
| 3.0.6-ko.2 | 흐름도 조건을 번역 후 축약하도록 수정 | [공개 사전 배포](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.6-ko.2) | [변경 사항](releases/v3.0.6-ko.2.md) |
| 3.0.6-ko.3 | AI 한국어 응답 규칙·엽운상 말투·표정 태그 보완 | 개발판, 별도 공개 릴리스 없음 | [변경 사항](releases/v3.0.6-ko.3.md) |
| 3.0.6-ko.4 | ID를 재사용하는 이동 이벤트의 이름 오표기 수정 | 개발판, 별도 공개 릴리스 없음 | [변경 사항](releases/v3.0.6-ko.4.md) |
| 3.0.6-ko.5 | 공식 한국어 대사를 참고한 아홉 히로인·당포의 말투와 호칭 추적 | 개발판, 로컬 검증 패키지 | [변경 사항](releases/v3.0.6-ko.5.md) |
| 3.0.9-ko.1 | 원본 3.0.9 대응, 한국어 문구 키·플래그 ID 표·조건/결과 직접 생성, 현재 상황 추적 분리 | 개발판, 로컬 검증 패키지 | [변경 사항](releases/v3.0.9-ko.1.md) |
| 3.0.9-ko.2 | 진행 경로·흐름도·수집 현황 문구 전환, 전체 임무명 조회, 날짜·진행 상태 직접 생성, 배치 유지 검사 | 개발판, 로컬 검증 패키지 | [변경 사항](releases/v3.0.9-ko.2.md) |
| 3.0.9-ko.3 | 원본 3.0.9와 ko.1·ko.2 누적 반영, AI 지시문·인물 설정·도구 설명·조회 안내·요약 전환, 한국어 검색과 원본 자료 기반 경로 합산 | [공개 사전 배포](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.9-ko.3) | [변경 사항](releases/v3.0.9-ko.3.md) |
| 3.0.9-ko.4 | 게임 플래그 변화 팝업의 번역 누락 수정, ID별 한국어 이름과 이전값·새값 표시 | [공개 사전 배포](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.9-ko.4) | [변경 사항](releases/v3.0.9-ko.4.md) |
| 3.2.1-ko.1 | 원작 3.2.1 통합, 새 흐름도·진행 경로·스토리 점프·유학 이벤트·AI 수정과 기존 한국어 개선 유지 | 로컬 통합 검증판, GitHub 미배포 | [변경 사항](releases/v3.2.1-ko.1.md) |
| 3.2.1-ko.2 | 점프의 「채우지 못하는 것」 안내 5종을 한국어로 직접 생성 | 로컬 통합 검증판, GitHub 미배포 | [변경 사항](releases/v3.2.1-ko.2.md) |

현재 공개판은 원본 [v3.0.9](https://github.com/kevinnie2003/LOMAssistant/releases/tag/v3.0.9), 개발 소스는 [v3.2.1](https://github.com/kevinnie2003/LOMAssistant/releases/tag/v3.2.1) 기반입니다. 공식 한국어 게임 `release_1.0.6001.0` 기준이며 기존 한국어 개선을 누적 포함합니다.

- 내장 스토리 조건의 중국어 잔존 검사: 0건
- 공개판 자동 검사: 8,890건 통과. 3.2.1 통합판의 검사 범위와 결과는 [통합 보고서](reports/upstream-3.2.1-korean-review.md)에 기록합니다.
- 게임 임무 메타데이터 511개(그룹 211개·단계 300개)의 제목 후보: 중국어 잔여 0건
- 실제 게임 플래그 표시 조합 5,304건: 실패 0건
- 현재 게임 실행에서 수집된 조건 5,060건 재검사: 중국어 잔여 0건
- F9 플래그 전체 설명에는 한중 혼합 표기 47건이 남아 있습니다. [검사 보고서](reports/flag-localization-audit.md)
- 일부 스토리 제목과 수집 현황에는 중국어가 남아 있을 수 있습니다.
- 3.0.6-ko.2는 빌드·데이터 검사를 완료했으며, 사용자 환경에서 실행과 흐름도 조건의 한국어 표시 수정을 확인했습니다. 모든 게임 상황과 AI 서비스 연결을 검증한 것은 아닙니다.

위 검사 결과는 전체 게임 텍스트의 완전한 한국어화를 의미하지 않습니다.

## 설치 및 사용

배포 파일은 **원본 3.0.9 기반 모드 DLL 교체 패키지**입니다. BepInEx 6·Newtonsoft.Json과 게임 1.0.6001 환경이 필요하며, 로더와 Newtonsoft.Json은 포함하지 않습니다. 기존 3.0.6 한국어판의 해당 환경에도 DLL과 이벤트명 표를 함께 교체해 적용할 수 있습니다. 기존 설치가 정상 작동한다면 원작자의 3.0.9를 다시 받을 필요가 없습니다. [설치 안내](releases/v3.0.9-ko.4.md)의 확인 항목도 참고하세요.

1. [릴리스](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.9-ko.4)에서 `LOMAssistant-3.0.9-ko.4-patch.zip`을 받습니다. GitHub의 `Source code` 압축 파일은 설치용이 아닙니다.
2. 게임을 종료하고 기존 `BepInEx/plugins/LOMAssistant/LOMAssistant.dll`과 `event-names-ko.tsv`가 있으면 게임 폴더 밖에 백업합니다.
3. ZIP의 `BepInEx` 폴더를 게임 설치 폴더에 덮어씁니다. `LOMAssistant.dll`과 `event-names-ko.tsv`가 같은 폴더에 있어야 합니다.
4. 게임과 모드의 표시 언어를 한국어로 설정한 뒤 다시 실행합니다. 번역표 수정도 재실행 후 적용됩니다.

원본과 동일한 플러그인이므로 DLL을 다른 이름으로 추가하거나 두 버전을 동시에 설치하지 마세요. 기존 설정과 저장 파일은 교체 대상이 아닙니다. 되돌리려면 백업한 DLL과 번역표를 복구합니다.

모드의 내장 업데이트 안내와 원본 설치 프로그램은 원본 프로젝트를 기준으로 합니다. 원본으로 업데이트하면 개선판이 교체될 수 있으므로, 한국어 개선판 업데이트는 이 포크의 릴리스에서 확인하세요.

자세한 원본 모드 기능과 설치 방법은 [원본 한국어 설명서](https://github.com/kevinnie2003/LOMAssistant/blob/main/README.ko.md)를 참고해 주세요.

## 소스 빌드 및 패키징

Windows PowerShell과 .NET 9 SDK, 설치된 게임의 `Mortal_Data/Managed`, 원본 배포 파일의 `BepInEx` 폴더가 필요합니다. 저장소 루트에서 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ko-check/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File build-ko.ps1 -Game '게임 설치 경로' -Distribution '원본 배포 파일 경로'
# 생성된 DLL의 스토리 추적·분석용 데이터 유지 여부도 검사
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ko-check/run.ps1 -ModAssembly bin/LOMAssistant.dll
# 커밋한 소스에서 빌드, 번역표 검증, 패치 ZIP 및 SHA256SUMS 생성
powershell -NoProfile -ExecutionPolicy Bypass -File tools/package-ko.ps1 -Game '게임 설치 경로' -Distribution '원본 배포 파일 경로'
```

결과는 `bin/release/<소스 버전>/`에 생성됩니다. 현재 개발 소스의 출력 폴더는 `bin/release/3.2.1-ko.2/`입니다. 빌드·패키징 스크립트는 게임 설치 폴더를 수정하지 않습니다. 패키징에는 커밋된 소스가 필요하며, 빌드에 쓰이지 않는 `reports/`의 미추적 검수 파일은 그대로 둘 수 있습니다.

## 원본 프로젝트 및 감사

- 원본 개발자: [kevinnie2003](https://github.com/kevinnie2003)
- 원본 저장소: [LOMAssistant](https://github.com/kevinnie2003/LOMAssistant)
- 한국어 개선판 유지보수: Trymeloud

이 프로젝트는 원본 개발자의 공식 배포판이 아닌 비공식 한국어 개선 작업입니다. 원본 개발자의 기여와 권리는 그대로 존중합니다.
