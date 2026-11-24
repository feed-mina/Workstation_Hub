# 0.3.0.0 비공개 스마트홈 테스트

기존 0.2 Submission 2 인증을 유지하고, 기존 비공개 테스터 그룹용 패키지 플라이트로 제출합니다. 실제 제출 상태·번호는 완료 후 기록합니다.

## 심사 참고

Private testing update for the existing full-trust x64 desktop app. No administrator rights or background Windows service is required. The four independent windows and chick notification-area menu are retained.

The smart-home panel now supports user-configured Home Assistant REST connections, room/entity bindings, supported vacuum actions, mapped room-cleaning scripts and Music Assistant playlists. Unavailable/unconfigured devices are disabled. Simulated lights and the 3D scene are inside an explicitly labelled demo section.

Optional media archival imports files from a user-selected folder, validates with external FFmpeg/ffprobe (not bundled), retains originals, and edits clips or a 600-second photo sequence. It does not download from the telecom camera app or delete phone files. The user must configure a separate phone transfer tool. Optional snapshots require a compatible camera already connected to Home Assistant.

YouTube connection requires the user's existing Google Cloud desktop OAuth client. The default browser performs consent with loopback redirect and PKCE. The app requests youtube.upload and youtube.readonly; selected videos are uploaded privately only after explicit confirmation or enabling automatic upload. Secrets are DPAPI-encrypted locally. Interrupted uploads resume; expired sessions wait for manual Studio review. Completion requires processing success and private visibility. No developer telemetry or video server is used.

Unconfigured first run should show setup guidance, disabled device controls and an empty archive. Open 연결 설정 in the smart-home panel or 스마트홈 연결 in the tray menu. No reviewer account or real household device is required for first-run and window/menu verification. Real device and upload tests require the reviewer's own configured services. Privacy notice: https://github.com/feed-mina/Workstation_Hub/blob/main/docs/privacy.md
