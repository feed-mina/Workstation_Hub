# 0.3.0.0 비공개 스마트홈 테스트

2026-11-24, 0.3은 기존 비공개 테스터 그룹용 `Smart Home 0.3 Private` 패키지 플라이트로 **제출 완료·인증 진행 중**입니다. 제출 ID는 `1152921505702492900`입니다. `Mini Workstation Hub - Private Test` 그룹만 대상으로 하며 인증 통과 후 자동 게시합니다. 기존 0.2 심사는 취소·교체하지 않았고, 확인 중 Submission 2가 게시 완료됐습니다. 이 PC의 앱은 삭제·교체하지 않았습니다.

로컬 MSIX 포장 검사와 Windows CI가 통과했습니다. 실제 기기·사용자 Google 계정·Store 서명본 실행·네 창과 트레이의 0.3 회귀 시험은 남아 있습니다. 자동 시험 근거: https://github.com/feed-mina/Workstation_Hub/actions/runs/45383053432

## 심사 참고

Private testing update for the existing full-trust x64 desktop app. No administrator rights or background Windows service is required. The four independent windows and chick notification-area menu are retained.

The smart-home panel now supports user-configured Home Assistant REST connections, room/entity bindings, supported vacuum actions, mapped room-cleaning scripts and Music Assistant playlists. Unavailable/unconfigured devices are disabled. Simulated lights and the 3D scene are inside an explicitly labelled demo section.

Optional media archival imports files from a user-selected folder, validates with external FFmpeg/ffprobe (not bundled), retains originals, and edits clips or a 600-second photo sequence. It does not download from the telecom camera app or delete phone files. The user must configure a separate phone transfer tool. Optional snapshots require a compatible camera already connected to Home Assistant.

YouTube connection requires the user's existing Google Cloud desktop OAuth client. The default browser performs consent with loopback redirect and PKCE. The app requests youtube.upload and youtube.readonly; selected videos are uploaded privately only after explicit confirmation or enabling automatic upload. Secrets are DPAPI-encrypted locally. Interrupted uploads resume; expired sessions wait for manual Studio review. Completion requires processing success and private visibility. No developer telemetry or video server is used.

Unconfigured first run should show setup guidance, disabled device controls and an empty archive. Open 연결 설정 in the smart-home panel or 스마트홈 연결 in the tray menu. No reviewer account or real household device is required for first-run and window/menu verification. Real device and upload tests require the reviewer's own configured services. Privacy notice: https://github.com/feed-mina/Workstation_Hub/blob/main/docs/privacy.md
