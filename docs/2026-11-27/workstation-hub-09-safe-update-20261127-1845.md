# Workstation Hub 0.9: 보안을 유지하는 업데이트 조사

2026-11-27 (Asia/Seoul). 이 문서는 읽기 전용 조사와 이후 업데이트 절차를 기록한다. 프로그램 설치·제거, 보안 정책 변경, 인증서 등록, 로그인, 앱 데이터 복원은 수행하지 않았다.

## 결론

검증 장치는 현재 0.8.0.0을 실행 중이며 설치 상태는 Ok, SignatureKind는 Developer다. 0.9 내부 WorkstationHub.exe에 대한 기존 CodeIntegrity 3077과 같은 Correlation ActivityID의 3089를 확인했다. 정책 이름은 VerifiedAndReputableDesktop, VerificationError는 18이다. Smart App Control은 실행 차단 적용 상태다.

보관된 0.8·0.9 개발 패키지는 같은 자체 서명 인증서를 사용하고 Authenticode 검사도 Valid였다. 따라서 “같은 인증서로 유효하게 서명하면 새 빌드도 실행된다”는 가정은 이 장치에서 성립하지 않는다. 오류 18은 UMCI의 허용 서명 기준을 만족하지 못했다는 뜻이며, 그 자체로 변조나 악성코드 판정을 뜻하지 않는다. 0.8이 허용된 정확한 이유는 이번에 확인하지 않았다.

우선 경로는 기존 Microsoft Store 비공개 플라이트에서 같은 제품의 0.9를 인증·게시한 뒤 제공하는 것이다. **이번 조사에서는 Partner Center의 현재 승인·게시·제공 여부를 확인하지 않았다.** 이전 작업 기록에는 제품 개요 로딩 문제로 제출을 완료하지 못했다고 남아 있다. Developer 설치에서 Store 배포본으로 삭제 없이 전환 가능한지도 별도 검증이 필요하다. 조건이 맞지 않으면 0.8을 유지한다.

근거: [PR #3의 0.8 설치·실행 기록](https://github.com/feed-mina/Workstation_Hub/pull/3), [PR #4의 0.9 차단·0.8 복원 기록](https://github.com/feed-mina/Workstation_Hub/pull/4), [Microsoft 이벤트 설명](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/event-id-explanations), [오류 18 및 서명 수준 설명](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/event-tag-explanations).

## 확인한 main의 빌드·배포 구조

조사한 main: `0908d3b8a72542de3d019f84f9757c23e7ebd8e5`. PR #4는 2026-11-27 10:31:53 KST에 병합됐다.

- [build.ps1](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/build.ps1): Windows x64 / .NET Framework 4.8 / Windows Forms + WebView2. 기본 출력은 dist의 EXE·UI·DLL·라이선스다. 이 단계는 서명·설치를 하지 않는다.
- WebView2 SDK 1.0.2903.40과 공식 GitHub CLI 2.105.0을 고정해 다운로드 패키지 SHA-256을 확인한다. 장치에는 WebView2 Runtime이 별도로 필요하다.
- [AppxManifest.xml](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/packaging/AppxManifest.xml): 0.9.0.0, x64, Windows.Desktop 10.0.19041 이상, FullTrustApplication / runFullTrust. 기존 0.8과 같은 Name·Publisher다.
- 저장소에는 MSIX 포장·서명·Store 제출 전체를 자동 수행하는 스크립트가 없다. 제작자는 확인한 커밋에서 빌드하고 Windows SDK 패키징·서명·게시 절차와 출처를 별도로 준비해야 한다.
- [windows.yml](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/.github/workflows/windows.yml)은 화면 상태와 C# 저장·복구·인증 해석 검사 등을 실행하지만 네이티브 창 회귀는 `test-windows.ps1 -CompileOnly`로 컴파일까지만 한다.
- [Windows 실행 #14](https://github.com/feed-mina/Workstation_Hub/actions/runs/45923086535)는 성공했다. 조회 시 이 실행의 산출물과 저장소 Releases는 비어 있었다. CI 성공은 실기기 창·실제 GPU·실계정 로그인 또는 설치 파일 배포의 증거가 아니다.
- 로컬 “signed.msix”는 개발 서명이었고 “Store-submission.msix”는 패키지 서명이 없는 제출용이었다. 후자는 Store가 서명해 내려준 설치본이 아니다. 기존 파일을 현재 main으로 재현 빌드한 것은 아니므로 배포 전 출처를 다시 고정한다.

## 서명 경로 비교

| 경로 | 의미 | 이 장치에서의 적용 |
| --- | --- | --- |
| Store MSIX | 인증 후 Microsoft가 패키지에 서명하고 지정 테스터에게 제공 | 우선 검토. 실제 0.9 게시·제공·동일 앱 전환을 확인해야 함 |
| 자체 서명 개발 MSIX | 개발자가 만든 인증서의 신뢰와 실행 정책 허용이 각각 필요 | 서명 Valid여도 이번 0.9는 SAC가 차단. 인증서 재등록만으로 해결된다고 볼 수 없음 |
| 공인 코드 서명 직접 배포 | 신뢰받는 발급기관 또는 자격 요건을 충족하는 Artifact Signing | 제작자가 자체 EXE·MSIX와 부속 바이너리까지 검토. SmartScreen 평판과 조직 정책은 별도 |
| 서명 없는 빌드·제출용 MSIX | 빌드·심사용 재료 | 보호된 실사용 장치에 바로 설치하는 경로로 쓰지 않음 |

[MSIX 서명 안내](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide), [SAC에 맞는 서명](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control), [비공개 플라이트](https://learn.microsoft.com/en-us/windows/apps/publish/package-flights).

## 읽기 전용 진단 원칙

설치 버전·실행 경로·SignatureKind·Authenticode 결과를 분리해서 읽는다. 기존 차단 시각을 기준으로 CodeIntegrity 3077과 3089를 Correlation ActivityID로 연결한다. 3076은 평가 모드의 기록이므로 실제 차단이라고 해석하지 않는다. 단순히 로그가 없거나 접근이 거부된 것을 허용 증거로 삼지 않는다.

CiTool의 `-lp -json`은 목록 조회다. 이번에는 0x80070005(접근 거부)를 반환했으므로 전체 정책 목록은 미확인이다. AppLocker 유효 규칙과 AppX·Defender 상세 내역은 별도 수집하지 않았다.

- 3077 + VerifiedAndReputableDesktop + 오류 18: 0.8 유지, Store 배포 서명 또는 SAC가 신뢰하는 정식 서명본 준비.
- 0x800B0109 / NotTrusted: 설치 인증서 체인·출처·게시자 확인. 실행 정책 문제와 구분.
- 다른 조직 정책·AppLocker 차단: 관리자가 정확한 파일 지문·서명자·정책을 좁게 검토. Windows 기본 SAC의 개별 예외를 임의로 만들지 않음.
- HashMismatch·폐기 서명·Defender 감지: 실행 중단 후 공식 수정본·오탐 검토. 제외 목록·우회 사용 안 함.
- 0x80073D02: 정상 종료 후 배포 절차에서 재검토. 0x80073D06 또는 같은 버전 재서명 충돌: 더 높은 같은 앱 버전을 제작자에게 요청.
- Developer→Store 전환이나 앱 ID 충돌: 기존 앱 제거·초기화로 해결하지 않고 중단 후 보존 계획부터 검토.

[AppLocker 이벤트](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/using-event-viewer-with-applocker), [CiTool](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/citool-commands), [설치 오류](https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting).

## 데이터 보존과 이후 업데이트 순서

1. 버전·프로젝트·스킬 버튼 수·공통 버튼 이름·연결 별칭·창 배치를 로컬에 기록한다.
2. 앱과 전용 브라우저를 정상 종료한 뒤 실제 앱 데이터 전체와 숨김 파일·백업을 별도 로컬 폴더에 보관한다. 이번에는 종료·복사하지 않았다.
3. 논리적 `%LOCALAPPDATA%\MinaDesk\WorkstationHub`와 패키지의 `LocalCache\Local\MinaDesk\WorkstationHub`를 모두 확인한다. 이번에는 실제 설정이 패키지 LocalCache에서 발견됐고 일반 사용자 경로에도 AccountProfiles가 있었다. LocalState 및 다른 앱 데이터의 존재 여부도 백업 전에 확인한다. 폴더를 임의로 합치지 않는다.
4. skill-workspaces.json / .bak, macros.json, windows.json, developer-console.json, browser-accounts.json / .bak, AccountProfiles, WebView2, github-auth, 스마트홈·작업 기록을 보존한다. 없는 파일을 손실로 단정하지 않는다. 버튼 이름은 WebView2 로컬 저장소이므로 JSON만 복사하면 부족하다.
5. 실제 프로젝트·스킬·외부 미디어·선택적 공유 폴더도 별도로 보존한다. 선택적 프로젝트 공유는 전체 백업이 아니다.
6. 같은 Windows 사용자를 유지한다. Windows 자격 증명 저장소·DPAPI·브라우저 쿠키와 공급자의 공식 AI 로그인은 파일 복사만으로 다른 계정·PC에 이전되는 것이 아니다. 비밀값은 내보내거나 공개하지 않는다.
7. 같은 Name·Publisher·PackageFamilyName·x64의 더 높은 버전인지, 신뢰 경로와 Store 전환이 가능한지 확인한 후 업데이트한다. 설치 취소·초기화·제거·인증서 추가를 선행하지 않는다.
8. 버전과 실제 실행 경로, 기존 데이터, 새 기능을 확인하고 정상 재시작 뒤 유지 여부를 확인한다. 실패하면 새 데이터도 따로 보관하고 추가 저장·계정 해제·공유 충돌 해결을 멈춘다.
9. 낮은 0.8로의 복귀는 Windows가 거부할 수 있다. 제작자의 더 높은 동일 앱 복구 버전 또는 검증된 복구 절차가 필요할 수 있다. 강제 다운그레이드를 기본 절차로 삼지 않는다.

저장 구조: [Program](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/Program.cs), [BrowserAccounts](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/BrowserAccounts.cs), [DeveloperPreferences](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/DeveloperPreferences.cs), [패키지 앱의 Windows 동작](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes).

## 아직 남은 실기기 검증

- 0.9 설치·실행·네이티브 창 이동·최소화·복원·트레이·재시작.
- PC 성능 GPU 값을 작업 관리자와 같은 작업·시각에서 비교. 앱은 가장 바쁜 엔진과 어댑터 메모리 합계이므로 GPU 한 개의 다른 엔진 그래프와 1:1 일치를 요구하지 않는다.
- 온도는 센서·출처·단위 확인. 미제공은 0°C 대신 “센서 미제공”. 이번 장치의 GPU 카운터는 읽혔지만 LHM WMI 네임스페이스는 없었다. 0.9의 Windows 드라이버 GPU 온도 조회는 미실행이다. 센서를 보이게 하려고 드라이버·보호 설정을 바꾸지 않는다.
- AI 계정은 데스크톱 로그인과 CLI 상태를 구분. Hub가 고른 같은 CLI의 상태와 비교하고 연결·재시작 유지를 확인. API 키·타사 공급자·상태 오류를 별도 표시. Hub 연동 해제는 공식 세션 로그아웃이 아니다.
- 모델 두 개 변경·공식 자료 갱신·갱신 실패 시 이전 자료 안내. 네트워크 차단 시험은 가짜 응답 또는 별도 시험 환경에서 수행. 모델 비교 선택은 실제 실행 모델 전환·이용 권한·성능 검증이 아니다.

[GpuTelemetry](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/GpuTelemetry.cs), [TemperatureSensors](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/TemperatureSensors.cs), [AiAccounts](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/AiAccounts.cs), [CodexModelComparison](https://github.com/feed-mina/Workstation_Hub/blob/0908d3b8a72542de3d019f84f9757c23e7ebd8e5/CodexModelComparison.cs).

## 보고서 검수·작업 상태

독립 easy-guide HTML과 동일한 명령을 포함한 상세 문서를 로컬에 작성했다. 내부 링크·고유 ID·스크립트 및 PowerShell 구문·HTML/문서 명령 일치를 정적으로 검사했다. 브라우저가 로컬 파일 URL을 허용하지 않아 실제 클릭·키보드·모바일 화면 검수는 미실행이다.

보고서 작성·조사 완료. 프로그램 설치·정책 변경·백업·복원·실계정 로그인·외부 알림은 미실행. 0.9 실제 사용 검증은 대기 중이다. 공개 문서에는 장치 이름·사용자 식별값·인증정보·설정 원문을 포함하지 않았다.
