# PC 버튼 1단계 구현 상태

2026-11-24, 공유된 ‘버튼 설계서와 1단계 만드는 방법’의 04~06번에 따라 0.2.0.0 개발 빌드를 만들었습니다. 설치된 Store 버전은 0.1.0.0입니다. 새 버전은 Store 제출 대기 상태입니다.

## 구현

- PC 잠금, 미디어 재생 키, 기본 출력 장치 음소거, 전체 모니터 PNG 저장과 클립보드 복사, 작업 폴더 터미널 실행, 최근 VS Code 프로젝트 선택, OBS WebSocket 5.x 녹화 제어를 연결했습니다.
- 음소거와 녹화 표시등은 실제 상태 응답을 따릅니다. 연결할 수 없는 OBS는 ‘OBS 미연결’로 표시합니다. 미디어 재생 키는 재생 상태 표시등을 사용하지 않습니다.
- 매크로 설정 화면에서 스크린샷 폴더, 작업 폴더, Code.exe와 OBS 접속 정보를 바꿉니다. OBS 암호는 현재 Windows 사용자용 DPAPI로 암호화해 로컬 macros.json에 보관합니다.
- 최근 프로젝트는 허브에서 고른 로컬 항목과 VS Code 기본 프로필의 읽기 전용 기록을 합쳐 5개까지 표시합니다. 폴더와 .code-workspace 파일을 직접 고를 수 있습니다. 다음 터미널은 마지막으로 고른 프로젝트 폴더에서 열립니다.
- Windows Terminal을 찾지 못하면 같은 폴더에서 Windows PowerShell을 엽니다. 스크린샷에는 허브 창도 포함하는 기본 동작을 선택했습니다.

## 연결되는 파일

화면의 ui/app.js → Hub.Receive → MacroCommands.Execute → WindowsCommands / SpeakerVolume / ProjectLauncher / ObsRecorder → 작업 기록과 상태 표시 순서입니다. MacroSettings와 MacroSettingsDialog가 버튼 설정을 담당하며 기존 창 위치 Settings와 별도로 저장합니다.

## 확인 결과

| 확인 항목 | 결과 |
| --- | --- |
| .NET Framework 4.8 x64 앱 컴파일 | 통과 |
| 기존 DesktopSmoke 시험 프로그램 컴파일 | 통과 |
| JavaScript 문법 검사 | 통과 |
| Node 기반 화면 상태 검사 | 통과. 음소거·녹화 상태, OBS 미확인 상태, 중복 누름 차단, 미디어 키 표시, 스마트홈 종료 시 PC 버튼 상태를 검사함 |
| MakeAppx 패키지 생성과 구조 검증 | 통과. 0.2.0.0 x64 |
| 네이티브 창 회귀 시험 실행 | 시작 전 차단. Windows가 DesktopSmoke.exe를 애플리케이션 제어 정책으로 차단함 |
| PC 버튼 7개 실제 동작과 OBS 녹화 파일 확인 | 미검증 |
| Store 0.2 업로드·심사·설치 | 미완료. Partner Center 로그인 안내 후 화면 조작이 Esc 키로 중지됨 |

로컬 MSIX의 SHA-256은 48FCAE06DC388CCF730B818B034E106E90331D7E8834ADB555E01E981CEDF398입니다. 이 파일은 Store 제출용 미서명 패키지입니다. 실제 설치와 실행 검증은 Store 서명본을 받은 뒤 진행해야 합니다.

## 이어서 확인할 동작

1. 기존 비공개 Store 제품의 다음 제출에 0.2.0.0 패키지를 등록합니다. PC 명령과 OBS 접속 기능을 심사 설명에 반영합니다.
2. 서명본을 설치한 뒤 7개 버튼의 실제 결과를 확인합니다. VS Code와 OBS는 해당 PC의 설치·접속 정보를 먼저 확인합니다.
3. OBS 외부에서 녹화 시작·정지, Windows 외부에서 음소거를 바꿔도 표시등이 따라오는지 확인합니다.
4. 네 창의 개별 이동·최소화·작업표시줄 복원과 병아리 메뉴 전환을 다시 확인합니다.

참고한 공식 문서는 [OBS WebSocket 5.x 프로토콜](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md)과 [Windows 출력 장치 음량 API](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume)입니다.
