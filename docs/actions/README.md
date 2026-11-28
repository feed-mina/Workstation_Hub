# Workstation Hub에서 GitHub Actions로 Codex 실행

1. 전용 **비공개 GitHub 저장소**의 기본 브랜치에 [워크플로 파일](workstation-hub-codex.yml)을 `.github/workflows/workstation-hub-codex.yml`로 저장합니다.
2. 그 저장소의 **Settings → Secrets and variables → Actions → Repository secrets**에 `OPENAI_API_KEY`를 등록합니다. 작업 대상이 다른 비공개 저장소라면, 대상 저장소의 **Contents 읽기** 권한만 있는 토큰을 `HUB_REPOSITORY_READ_TOKEN`으로 추가합니다.
3. Hub에서 GitHub에 로그인합니다. **실행 설정**에서 모델과 추론 강도를 선택합니다. 실행 저장소 칸이 비어 있으면 로그인한 GitHub 계정의 `workstation-hub-actions`를 사용합니다.
4. Hub에서 작업할 프로젝트, 브랜치, **GitHub Actions** 실행 위치를 고르고 작성창의 **GitHub Actions에서 시작**을 누릅니다.

Hub는 선택한 브랜치의 현재 커밋을 조회해 요청과 함께 보냅니다. 요청 시점의 커밋이 실행기에 체크아웃됩니다. 설정을 앱 기본값으로 두면 Actions에서는 GPT-6.2 Sol과 Medium을 사용합니다. 접수 결과에서 실행 페이지를 열 수 있습니다. 완료 후 그 페이지의 **workstation-hub-result**에 응답과 변경 패치가 최대 7일간 보관됩니다.

실행기에는 GitHub 저장소 읽기 권한만 부여됩니다. OpenAI API 키는 비공개 저장소의 Actions Secret에서 공식 Codex 액션으로 전달되며 API 사용량으로 청구됩니다. [공식 Codex GitHub Action](https://learn.chatgpt.com/docs/github-action)
