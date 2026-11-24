# 0.3 비공개 Store 테스트 제출 기록

2026-11-24 Asia/Seoul에 Partner Center에서 직접 확인한 최초 제출 기록입니다. **이후 0.3 심사 통과와 Store 설치·UI Automation 검증을 완료했습니다. [최신 결과](store03-verified-20261124.md)**

아래 표와 순서는 제출 당시 상태를 보존합니다.

| 항목 | 확인 결과 |
| --- | --- |
| 제품 | Mini Workstation Hub · 9P7W7FZV2141 |
| 패키지 | MiniWorkstationHub_0.3.0.0_x64.msix · 0.3.0.0 · X64 |
| SHA-256 | 85F11D8EA6239E71E45FAF0887F3AD230F6D91C5400F1B3BF6EBD38B65553D45 |
| 플라이트 | Smart Home 0.3 Private |
| 제출 ID | 1152921505702492900 |
| 대상 | 기존 Mini Workstation Hub - Private Test 그룹만 선택 |
| 게시 | 인증 통과 후 게시, 단계적 배포·강제 업데이트는 선택하지 않음 |
| Store 검증 | 업로드와 버전 검사 통과. runFullTrust 승인 경고는 심사 대상으로 안내됨 |
| 제출 결과 | 인증 상태 페이지에서 사전 처리 진행 중, 인증 취소 링크 표시 확인 |
| 기존 0.2 | 심사를 취소·교체하지 않음. 이후 개요에 Store 현재 상태 Submission 2 및 Microsoft Store에 있음으로 표시 |
| PC 설치 | 0.3 승인 전, 앱 삭제·교체 및 0.3 실행 시험 미수행 |

심사 참고 사항에는 0.1 시연, 0.2·0.3 실제 PC 명령, 0.3 스마트홈·영상 연결 조건을 버전별로 구분했습니다. 저장 후 새로고침해 유지됨을 확인했습니다. 계정 자격 증명은 업로드하지 않았습니다.

## 승인 후 확인 순서

1. 테스터 계정의 Store에서 실제 제공 버전이 0.3.0.0인지 확인하고 업데이트합니다. 승인 화면만 보고 설치됐다고 판단하지 않습니다.
2. 네 창을 하나씩 이동·최소화하고 작업표시줄 미리보기에서 해당 창만 복원합니다. 나머지 창의 상태가 유지되는지 확인합니다.
3. 숨겨진 아이콘의 병아리 메뉴에서 네 항목을 하나씩 껐다 켜며 체크·창·위치 복원을 확인합니다.
4. 스마트홈 연결 설정에서 사용자가 HA 토큰을 직접 입력하고 기기 목록을 읽습니다. 필요한 Google 연결도 사용자가 기본 브라우저에서 승인합니다.
5. 실제 기기 한 개부터 시험합니다. 음악은 Music Assistant와 재생목록 연결, 청소는 해당 기기와 방 청소 동작의 지원 확인이 먼저 필요합니다. 맘카는 갤러리에 내려받은 한 편을 PC로 전송하는 경로부터 확인합니다.

[Partner Center 제출 상태](https://partner.microsoft.com/ko-kr/dashboard/products/9P7W7FZV2141/submissions/1152921505702492900/certificationstatus) · [비공개 Store 링크](https://apps.microsoft.com/detail/restricted/9P7W7FZV2141)
