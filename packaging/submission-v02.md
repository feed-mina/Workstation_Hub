# Store 0.2 업데이트 제출 메모

## 변경 사항

매크로 패드의 PC 버튼 7개와 매크로 설정 화면을 추가했습니다. 사용자는 저장 폴더, 작업 폴더, VS Code 실행 파일 위치와 OBS 접속 정보를 지정할 수 있습니다. 작업실 시나리오와 스마트홈은 시연 기능입니다.

## 심사 참고

This is an update to the existing private-audience full-trust desktop app. It runs as the signed-in user without administrator access. Seven macro keys now issue Windows desktop commands: lock workstation, media play/pause, mute the default output device, capture all monitors to a local PNG and clipboard, open a terminal in a selected local folder, open VS Code projects, and start/stop OBS recording through obs-websocket 5.x.

The app does not bundle VS Code or OBS. Configure them through 매크로 설정 in the chick tray menu or 설정 in the macro panel. The OBS address defaults to ws://127.0.0.1:4455. The password is encrypted with current-user Windows DPAPI and stored locally. No external telemetry or YouTube upload is implemented. The app can communicate with the OBS address chosen by the user.

When OBS is absent or its WebSocket server is disabled, the recording key should show an unavailable status and a helpful failure notice, and the other panels should remain available. Verify mute state after changing it outside the app. Test the PC lock key last because it locks the interactive Windows session.

The 0.2 application and the existing native smoke-test harness compiled successfully. JavaScript state checks and MakeAppx packaging validation passed. Native execution was blocked by application control on the development PC, so native behavior and packaged-runtime verification remain pending.
