
# LOMAssistant 한국어 개선판

**『활협전』(Legend of Mortal) LOMAssistant의 비공식 한국어 현지화 개선 프로젝트입니다.**

원본 모드의 한국어 번역에서 발견되는 중국어 혼용, 미번역 문구, 잘못된 인물명과 용어를 수정하고 있습니다.

이 프로젝트는 [kevinnie2003의 LOMAssistant](https://github.com/kevinnie2003/LOMAssistant)를 기반으로 합니다.

## 주요 개선 사항

- **한국어 번역 보완:** 기존에 누락된 스토리 조건 이름과 플래그 설명 1,053개 항목에 한국어 번역 추가
- **공식 용어 반영:** 활협전 공식 한국어 번역을 참고하여 인물명, 능력치, 장소명 등의 표기 개선
- **스토리 화면 개선:** 현재 상황, 스토리 회고, 수집 현황 등에서 중국어가 출력되는 문제 개선
- **텍스트 처리 수정:** 목록에서 텍스트가 잘릴 때 중국어 원문이 출력되는 현상 수정
- **랜덤 이벤트명 번역:** 이벤트 ID에 대응하는 한국어 표시 이름 397개를 별도 번역표로 제공
- **번역 검사 자동화:** 번역 누락 및 문자열 처리 오류를 확인하기 위한 검사 도구 추가

## 진행 상태

**현재 배포 버전: [v3.0.6-ko.2 (사전 배포)](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.6-ko.2).**

원본 [v3.0.6](https://github.com/kevinnie2003/LOMAssistant/releases/tag/v3.0.6)과 게임 `release_1.0.6001.0`을 기준으로 한국어 패치를 갱신했습니다. 흐름도의 「그 외」 조건은 한국어 번역 후 축약하도록 수정했습니다. [설치 안내와 변경 사항](releases/v3.0.6-ko.2.md), [3.0.6 이식 검토](reports/compatibility-3.0.6-ko.1.md)를 참고하세요.

- 내장 스토리 조건의 중국어 잔존 검사: 0건
- 자동 번역 검사: 3,108건 통과(흐름도 조건 축약 검사 포함)
- 실제 게임 플래그 표시 조합 5,304건: 실패 0건
- 현재 게임 실행에서 수집된 조건 5,060건 재검사: 중국어 잔여 0건
- F9 플래그 전체 설명에는 한중 혼합 표기 47건이 남아 있습니다. [검사 보고서](reports/flag-localization-audit.md)
- 일부 스토리 제목과 수집 현황에는 중국어가 남아 있을 수 있습니다.
- 3.0.6-ko.2는 빌드·데이터 검사를 완료했으며, 사용자 환경에서 실행과 흐름도 조건의 한국어 표시 수정을 확인했습니다. 모든 게임 상황과 AI 서비스 연결을 검증한 것은 아닙니다.

위 검사 결과는 전체 게임 텍스트의 완전한 한국어화를 의미하지 않습니다.

## 설치 및 사용

현재 소스로 만드는 배포 파일은 **원본 LOMAssistant 3.0.6이 설치된 BepInEx 6 환경에 적용하는 한국어 개선 패치**입니다. 게임 1.0.6001의 공식 한국어를 기준으로 작업했습니다. BepInEx와 Newtonsoft.Json은 패치에 포함하지 않습니다. 2.9.3-ko.1 사용자는 원본 3.0.6으로 업데이트한 뒤 이 패치를 적용하세요.

1. [릴리스](https://github.com/Trymeloud/LOMAssistant/releases/tag/v3.0.6-ko.2)에서 `LOMAssistant-3.0.6-ko.2-patch.zip`을 받습니다. GitHub의 `Source code` 압축 파일은 설치용이 아닙니다.
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
# 커밋한 소스에서 빌드, 번역표 검증, 패치 ZIP 및 SHA256SUMS 생성
powershell -NoProfile -ExecutionPolicy Bypass -File tools/package-ko.ps1 -Game '게임 설치 경로' -Distribution '원본 배포 파일 경로'
```

결과는 `bin/release/3.0.6-ko.2/`에 생성됩니다. 빌드·패키징 스크립트는 게임 설치 폴더를 수정하지 않습니다. 패키징에는 커밋된 소스가 필요하며, 빌드에 쓰이지 않는 `reports/`의 미추적 검수 파일은 그대로 둘 수 있습니다.

## 원본 프로젝트 및 감사

- 원본 개발자: [kevinnie2003](https://github.com/kevinnie2003)
- 원본 저장소: [LOMAssistant](https://github.com/kevinnie2003/LOMAssistant)
- 한국어 개선판 유지보수: Trymeloud

이 프로젝트는 원본 개발자의 공식 배포판이 아닌 비공식 한국어 개선 작업입니다. 원본 개발자의 기여와 권리는 그대로 존중합니다.
