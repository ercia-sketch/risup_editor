# RisuAI 미리보기 호환 범위

이 문서는 `RisuAI-reference/src/lib/SideBars/DevTool.svelte`의 **Preview Prompt → Type: Chat**과
`src/ts/process/index.svelte.ts`, `src/ts/parser/parser.svelte.ts`, `src/ts/cbs.ts`를 기준으로 작성했습니다.
대조한 로컬 RisuAI 기준은 `2026.8.250` (`25001174e0452e3b9d16aee459ce9d2444c197d7`)입니다.

## 프리셋만으로 확정되는 처리

- 프롬프트 블록 순서, 역할, jailbreak/COT 활성 여부, ChatML, cache 지점
- RisuAI의 연속 system 메시지 병합과 선택적인 With Join
- description, persona, memory, authornote의 `innerFormat`: 내부 형식의 CBS를 먼저 처리하고 첫 `{{slot}}`에 내용을 삽입
- 커스텀 토글의 `{{getglobalvar::toggle_...}}`, 값이 없는 토글의 `null`, `{{jbtoggled}}`
- 중첩 CBS 인수와 `#if`, `#if_pure`, `#when`, `#each`, `#pure`, `#puredisplay`, `#escape`, `#code`, `#func`/`call`
- 비교·논리: equal, notequal, greater, less, greaterequal, lessequal, and, or, not
- 문자열·배열·객체: startswith, endswith, contains, replace, split, join, spread, trim, length, 대소문자 변환,
  arrayelement/length/push/pop/shift/splice/assert, dictelement, element, makearray, makedict, objectassert, range, filter
- 수치·집계: calc와 `?` 수식, round, floor, ceil, abs, remaind, pow, tonumber, min, max, sum, average, fixnum
- 정적 유틸리티: 유니코드/16진수 변환, iserror, reverse, xor, crypt, tex, ruby, codeblock, 이스케이프 문자

함수 이름은 RisuAI처럼 대소문자를 구분하지 않고 공백, `_`, `-`를 무시합니다. 중첩 함수의 `::`는 바깥 인수와
섞이지 않도록 깊이를 구분해 해석합니다.

## CBS 블록 규칙

- `{{/}}`, `{{/if}}`, `{{/when}}`처럼 `//`가 아닌 단일 `/`로 시작하는 종료 토큰은 이름과 관계없이 가장 가까운 블록을 닫습니다.
- `#if`는 참일 때 블록 앞뒤와 각 줄의 앞 공백을 정리하고, 거짓일 때 내부 CBS를 실행하지 않습니다. `:else`는 `#if`의 분기 문법이 아닙니다.
- `#if_pure`는 `#if`와 같은 조건 판정을 사용하되 참인 본문의 공백과 줄바꿈을 보존합니다. 본문 안의 중첩 CBS는 정상적으로 처리합니다.
- `:else`는 `#when`에서만 분기로 처리합니다. `keep`은 공백을 보존하고 `legacy`는 `#if` 방식의 공백 처리와 거짓 블록 무시를 사용합니다.
- `#pure`, `#puredisplay`, `#escape`, `#each`, `#func`의 본문은 RisuAI의 pure-mode 중첩 규칙으로 경계를 찾습니다.
- `#each`는 JSON 배열이 아닌 값도 RisuAI처럼 하나의 항목으로 취급하며, `§` 구분 입력과 `as` 생략형을 지원합니다.
- RisuAI가 블록으로 인식하지 않는 `#...` 표현과 단독 종료 표현은 삭제하지 않고 원문으로 보존합니다.

`parser.svelte.ts`의 `blockStartMatcher`/`blockEndMatcher` 반환 유형은 아래처럼 대응합니다.

| RisuAI 유형 | 해당 문법 | 미리보기 처리 |
|---|---|---|
| `parse` / `ignore` | `#if`, `#when::legacy` | 참이면 legacy 공백 정리, 거짓이면 내부를 실행하지 않고 제거 |
| `ifpure` | `#if_pure` | 참인 본문과 줄바꿈을 보존하면서 중첩 CBS 처리 |
| `newif` / `newif-falsy` | `#when` | `:else`, `keep`, 비교·논리·토글 연산 처리 |
| `pure` | `#pure` | 내부 CBS를 실행하지 않고 앞뒤 공백만 정리 |
| `pure-display` | `#puredisplay`, `#pure_display` | 내부 CBS를 실행하지 않고 재해석 방지용 괄호 이스케이프 적용 |
| `normalize` | `#code` | 개행·탭 제거와 RisuAI 이스케이프 해석 적용 |
| `escape` | `#escape` | 내부를 실행하지 않으며 `keep` 공백 규칙 적용 |
| `each` | `#each` | RisuAI 배열·`§` fallback, 슬롯 치환, 결과 재해석 적용 |
| `function` | `#func`, `call` | 호출별 함수 범위, 인수 치환, 20단계 호출 제한 적용 |
| `nothing` | 알 수 없는 블록 시작 | 원문 보존 |

## 현재 프리셋만으로 확정할 수 없는 처리

아래 항목은 편집기가 값을 지어내지 않고 색이 있는 원문 또는 자리 표시로 남깁니다.

- 현재 캐릭터·사용자·페르소나·작가의 노트·메모리·로어북·실제 채팅 기록
- 채팅 변수와 일반 전역 변수, 모듈·에셋·화면·모델 메타데이터
- 현재 시각, 난수, 주사위, 채팅 ID를 사용하는 pick/rollp/hash
- 캐릭터의 Lua `editRequest`, 트리거, 삽입 로어북의 position 처리
- 실제 채팅 메시지에 적용되는 `editprocess` 정규식 결과

이 값들은 프리셋 파일 안에만 있지 않고 실행 중인 RisuAI의 캐릭터·채팅·환경 상태에 의존합니다. 따라서 이 편집기의
미리보기는 그 위치와 원문을 명확히 보여 주며, 프리셋을 내보낼 때는 어떤 표현도 삭제하거나 바꾸지 않습니다.

## 색상

- 빨강: `{{user}}`
- 파랑: `{{char}}`
- 노랑: `{{maxcontext}}`
- 초록: 난수·주사위 계열
- 주황: 현재 프리셋만으로 확정할 수 없는 다른 실행 시점 표현
- 청록 배경: description/persona/memory/authornote/chat/lorebook 등의 실제 데이터가 삽입될 자리
