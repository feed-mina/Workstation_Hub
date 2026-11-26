'use strict';
(() => {
 // Display text only. Names, SKILL.md contents and execution requests stay intact.
 // Check the source description too, so a project skill with the same name does
 // not accidentally inherit a different skill's explanation.
 const entries={
  'agents-sdk':['Build AI agents on Cloudflare Workers','Cloudflare Workers에서 AI 에이전트를 만듭니다. 대화 기록과 상태 저장, 실시간 통신, 예약 작업, 채팅·음성 앱과 브라우저 자동화 등을 구현할 때 사용합니다.'],
  'cloudflare':['Comprehensive Cloudflare platform skill','Cloudflare에서 웹사이트와 서버 기능을 개발하고 배포합니다. Workers·Pages, 데이터베이스·파일 저장소, AI, 도메인·네트워크와 보안 설정을 다룹니다.'],
  'cloudflare-email-service':['Send and receive transactional emails','Cloudflare로 이메일을 보내고 받는 기능을 구현합니다. 이메일 전송·수신 경로, 도메인 인증과 배달 문제를 설정하거나 점검할 때 사용합니다.'],
  'cloudflare-one':['Guides Cloudflare One','Cloudflare One의 사내 접속과 보안 환경을 구성하고 점검합니다. 사용자 접근 권한, WARP·Tunnel, 네트워크 연결과 데이터 보호 설정을 다룹니다.'],
  'cloudflare-one-migrations':['Plans migrations from','기존 VPN이나 Zscaler·Palo Alto 같은 보안 서비스를 Cloudflare One으로 옮기는 계획을 세웁니다. 기능과 정책을 비교하고 전환 순서와 부족한 부분을 정리합니다.'],
  'durable-objects':['Create and review Cloudflare Durable Objects','Cloudflare Durable Objects로 여러 사용자가 함께 쓰는 상태와 데이터를 관리합니다. 채팅방·공동 편집·예약, 실시간 연결, 저장소와 작업 예약을 구현하거나 검토할 때 사용합니다.'],
  'hatch-pet':['Create, repair, validate','캐릭터 그림이나 참고 이미지로 Codex용 움직이는 펫을 만듭니다. 필요한 동작과 방향별 이미지를 구성하고, 기존 펫의 수정·품질 확인과 배포 파일 준비를 돕습니다.'],
  'sandbox-migrate-to-next':['Use when porting a Cloudflare Sandbox app','기존 Cloudflare Sandbox 앱을 새 미리보기 버전으로 이전합니다. 호환되지 않는 코드와 설정을 확인하고 이전에 필요한 수정을 진행합니다.'],
  'sandbox-next':['Use when building or changing Cloudflare Sandbox apps on @cloudflare/sandbox@next','Cloudflare Sandbox의 새 미리보기 버전으로 실행 환경을 만들거나 수정합니다. 코드 실행, 터미널·파일·미리보기 주소, 환경의 시작·종료와 오류를 다룹니다.'],
  'sandbox-sdk':['Build sandboxed applications','코드를 격리된 환경에서 실행하는 앱을 만듭니다. AI 코드 실행기, 코드 해석기, 자동 검사와 개발 환경을 구현하고 파일·명령·미리보기 주소를 관리할 때 사용합니다.'],
  'sandbox-stable':['Use when building or changing Cloudflare Sandbox apps on the current stable','Cloudflare Sandbox의 현재 안정 버전으로 실행 환경을 만들고 관리합니다. 명령 실행, 파일·세션·포트 연결과 문제 해결을 다루며 미리보기 버전으로 이전하지 않습니다.'],
  'turnstile-spin':['Set up Cloudflare Turnstile','Cloudflare Turnstile을 연결해 폼이나 API를 자동화된 공격으로부터 보호합니다. 화면의 인증 위젯과 서버의 검증 코드를 함께 구성하고 정상 동작을 확인합니다.'],
  'web-perf':['Analyzes web performance','웹사이트가 느리게 열리는 원인을 분석합니다. 화면 표시 속도, 클릭 반응, 레이아웃 흔들림과 네트워크 요청을 측정하고 개선할 항목을 정리합니다.'],
  'workers-best-practices':['Reviews and authors Cloudflare Workers code','Cloudflare Workers 코드를 작성하거나 검토할 때 안정적인 운영 기준을 적용합니다. 설정과 비밀값 관리, 스트리밍, 비동기 처리와 로그의 문제를 점검합니다.'],
  'wrangler':['Cloudflare Workers CLI','Wrangler 명령으로 Cloudflare 앱을 개발하고 배포합니다. Workers와 데이터베이스·파일 저장소·작업 큐·비밀값 등의 자원을 설정하고 관리할 때 사용합니다.'],
  'imagegen':['Generate or edit raster images','설명이나 참고 이미지를 바탕으로 이미지를 새로 만들거나 수정합니다. 사진·일러스트·목업·배경 제거 이미지 등이 필요할 때 사용합니다.'],
  'openai-docs':['API first-timers, Codex/Work Q&A','Codex와 ChatGPT Work의 기능, 설정과 문제 해결을 공식 문서에서 확인합니다. OpenAI API를 처음 연결할 때의 안내에도 사용합니다.'],
  'review-agent':['Perform a read-only, defect-first review','다른 에이전트가 맡긴 코드 변경을 읽고 실제로 수정할 문제를 찾습니다. 파일은 직접 바꾸지 않고 발견한 결함과 수정할 위치를 정리합니다.'],
  'skill-creator':['Create or update a Codex skill','새 Codex 스킬을 만들거나 기존 스킬을 수정합니다. 사용 목적과 적용 범위에 맞게 지침을 정리하고 필요한 참고 자료와 보조 파일을 준비합니다.'],
  'skill-installer':['Install Codex skills','추천 목록이나 GitHub 저장소에서 Codex 스킬을 설치합니다. 설치 가능한 스킬을 확인하거나 지정한 공개·비공개 저장소의 스킬을 가져올 때 사용합니다.']
 };
 function describe(name,original){
  const text=typeof original==='string'?original.trim():'';
  if(/[가-힣]/u.test(text))return text;
  const entry=entries[name];
  if(Array.isArray(entry)&&text.toLocaleLowerCase().startsWith(entry[0].toLocaleLowerCase()))return entry[1];
  return '아직 한국어 용도 설명이 등록되지 않은 스킬입니다. 실행하면 설치된 SKILL.md의 지침과 이 버튼에 저장한 요청을 선택한 AI 앱에 전달합니다.';
 }
 window.skillDescriptionsKo=Object.freeze({describe});
})();

