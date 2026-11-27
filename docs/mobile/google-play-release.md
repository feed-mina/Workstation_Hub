# Android Google Play 출시 준비

대상 브랜치: mobile. 앱 ID `com.minadesk.workstationhub`, 버전 `0.9.1`, versionCode `901`, 최소 Android 8(API 26), compile/target API 36. 2026-11-27 기준 신규 Play 제출에 필요한 Android 16 대상으로 변경했다.

## 코드와 배포 파일

- AGP 8.10.1 + Gradle 8.11.1 + JDK 17. 기존 Gradle wrapper는 유지한다.
- Android 13 이상은 플랫폼 뒤로가기 콜백을 사용해 앱 안 대화상자를 먼저 닫는다. 이전 Android의 뒤로가기 경로도 유지한다.
- 모든 탭 위의 **앱 정보**에서 버전·공개 개인정보 안내·지원 문의를 연다. 같은 안내를 네트워크 없이 앱 안에서도 읽는다.
- 개인정보 원본은 `android/app/src/main/assets/privacy-android.json`이다. `node tools/android-policy.cjs`로 공개 문서를 생성하고 `--check`로 같은 내용인지 확인한다.
- 공개 개인정보 안내: https://github.com/feed-mina/Workstation_Hub/blob/mobile/docs/privacy-android.md . 공개 저장소의 로그인 없는 웹 문서이며, 스토어의 개발자명·연락처와 최종 일치 여부를 제출 전에 확인한다.
- CI는 기존 Node 5개, JVM 검사, debug APK, release AAB, debug/release lint를 검사한다. Android 11(API 30)과 Android 16(API 36) 가상 기기에서 탭·뒤로가기·개인정보 안내·설정 여백을 시험한다. 실제 휴대폰 및 실제 HA·음악 제어의 통과를 대신하지 않는다.
- 서명 없는 CI AAB는 **제출 전 후보**다. 실제 업로드에는 사용자 업로드 키로 서명한 별도 AAB가 필요하다. 키 파일과 비밀번호는 GitHub나 CI 산출물에 넣지 않는다.

## 서명 설정

환경변수 네 개를 모두 제공하면 release에 업로드 키를 사용한다. 일부만 제공하면 빌드를 실패시킨다. `-PhubRequireSigning=true`를 사용하면 키가 없는 배포 빌드도 실패시킨다.

| 변수 | 의미 |
| --- | --- |
| HUB_UPLOAD_STORE_FILE | 작업 폴더 밖에 보관한 keystore의 절대 경로 |
| HUB_UPLOAD_STORE_PASSWORD | keystore 비밀번호 |
| HUB_UPLOAD_KEY_ALIAS | 업로드 키 별칭 |
| HUB_UPLOAD_KEY_PASSWORD | 업로드 키 비밀번호 |

서명된 빌드: `./gradlew bundleRelease -PhubRequireSigning=true`. CI가 만든 unsigned AAB도 Windows의 `tools/sign-android-bundle.ps1`로 원본을 유지한 채 서명할 수 있다. 이 도구는 현재 Windows 계정으로 보호한 비밀번호 파일을 읽고 Java 서명 도구에 환경변수로 전달한다. 비밀번호를 인자나 로그로 출력하지 않는다.

Play App Signing의 **앱 서명 키**와 여기서 준비한 **업로드 키**는 구분한다. Google이 앱 서명을 관리하고 개발자는 업로드 키로 제출한다. 키와 복구 자료는 별도 안전한 보관소에 백업한다. Windows 계정으로 보호한 비밀번호 파일은 다른 계정·새 PC에서 그대로 복호화할 수 없으므로 키 이전 전에 복구 경로를 준비한다.

## 콘솔 등록 직전에 결정할 사항

- 개인/조직 계정, 공개 지원 이메일, 개발자명, 국가·가격·대상 연령은 소유자가 정한다. 이 문서가 임의로 계정이나 법적 신원을 정하지 않는다.
- 이름 초안: **Mini Workstation Hub**. 짧은 설명 초안: **업무 바로가기, 스마트홈과 작업 기록을 휴대폰에서 관리하세요.**
- 소개: 업무 시작 앱 선택, 미디어 키, 사용자 Home Assistant 연결, 휴대폰 작업 기록, 요청문 구성과 날씨를 제공한다. PC의 잠금·녹화·파일 편집 등은 지원하지 않는다. HA 기능은 사용자가 운영하는 서버와 지원 기기가 필요하다.
- 실제 기기 스크린샷 최소 두 장, 아이콘과 소개 이미지를 준비한다. 가이드의 재구성 그림이나 테스트용 예시 데이터를 실제 기기 기능 검증으로 표시하지 않는다.
- 광고 SDK·결제·앱 자체 계정 가입 기능은 코드에 없다. 실제 출시의 광고·결제·대상 연령·연령 등급 답변은 출시 동작과 일치하게 확인한다.
- 심사자의 제한 기능 접근에는 운영 집 토큰 대신 별도의 검토용 HA 환경과 유효한 접근 방법을 제공한다. 데모 경로만 설명하고 심사에 필요한 실제 접근을 생략하지 않는다.
- 2023-11-13 이후 새 개인 계정이라면 최소 12명이 연속 14일 참여한 비공개 시험과 프로덕션 접근 신청이 필요하다. 내부 시험과 자동 검사는 이 요건을 대체하지 않는다.

## Data safety 초안의 근거

최종 답변은 배포 파일·실제 서버 처리·정책 정의를 대조해 소유자가 확인한다. 분석 SDK가 없다는 이유만으로 모든 항목을 ‘없음’으로 표시하지 않는다.

| 경로 | 처리와 수신자 | 작성할 때 주의 |
| --- | --- | --- |
| 설정·요청문·기록·성능 수치 | 기기 로컬. 개발자 서버로 자동 전송하지 않음 | 로컬 전용 처리와 외부 전송을 구분 |
| Home Assistant | 사용자가 입력한 서버에 토큰·기기 조회·제어 요청 전송 | 사용자 지정 서버의 처리·저장 방식과 Google의 예외 정의 확인 |
| 날씨 | 선택한 도시의 고정 좌표·조회 조건을 Open-Meteo에 전송. 접속 IP가 수신될 수 있음 | 현재 기기 위치/GPS 권한은 없음. 위치·IP·임시 처리 관련 정의와 제공자 정책 확인 |
| 요청문 복사·외부 AI 앱 | 시스템 클립보드에 복사하고 앱을 열기. 사용자가 붙여넣기·전송 | 외부 서비스 정책·사용자 주도 전송 예외를 확인 |
| 웹 바로가기 | 사용자가 고른 브라우저/앱에 URL 전달 | URL·접속 정보의 외부 처리 설명 |
| 전송 보안 | 사용자 HA의 HTTP 주소를 허용함 | **모든 정보가 전송 중 암호화됨**이라고 답할 근거가 없음. HTTPS 설정·실제 배포 동작 확인 |

Open-Meteo 자료 사용 조건과 표시 의무, 상업적 배포 해당 여부도 출시 형태에 맞춰 확인한다.

## 완료 증거

1. 해당 소스 커밋의 자동 검사·가상 기기 결과.
2. 서명된 AAB의 해시·인증서 지문·API 36·버전 901·정상 구조.
3. 실제 휴대폰의 설치·기록 보존·연결·미디어·키보드·복귀 시험.
4. Play Console 계정 확인·정책 답변·필요한 비공개 시험·심사·게시 상태.
5. 실제 Play 페이지에서 대상 계정이 설치하고 버전을 확인한 기록.

1~2의 성공은 3~5의 성공을 뜻하지 않는다. 이 작업은 계정 생성·결제·스토어 제출을 수행하지 않는다.

공식 근거: [대상 API](https://developer.android.com/google/play/requirements/target-sdk), [AGP 호환성](https://developer.android.com/build/releases/agp-8-10-0-release-notes), [Android 16 동작 변경](https://developer.android.com/about/versions/16/behavior-changes-16), [앱 서명](https://developer.android.com/studio/publish/app-signing), [Data safety](https://support.google.com/googleplay/android-developer/answer/10787469?hl=en), [개인 계정 시험](https://support.google.com/googleplay/android-developer/answer/14151465?hl=en), [Open-Meteo 약관](https://open-meteo.com/en/terms).

## 스토어 소개와 이미지 초안

`play-listing.ko-KR.json`에 제목·짧은 소개·전체 설명·정책 URL·심사용 접근 준비 항목을 정리했습니다. `play-assets`에는 규격에 맞춘 아이콘과 소개 이미지 PNG 및 SVG 원본이 있습니다. 실제 화면은 가상 기기 검사 산출물에서 확인합니다. 공개 지원 이메일·계정 유형·가격·국가·연령 대상·Data safety와 심사용 접근의 최종 확인은 남아 있습니다. 무료 Open-Meteo API의 비상업적 이용 조건도 선택할 판매 방식과 대조해야 합니다.
