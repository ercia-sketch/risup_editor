# RisuAI 미리보기 호환 범위

이 문서는 `RisuAI-reference/src/lib/SideBars/DevTool.svelte`의 **Preview Prompt → Type: Chat**과
`src/ts/process/index.svelte.ts`, `src/ts/parser/parser.svelte.ts`, `src/ts/cbs.ts`를 기준으로 작성했습니다.

## 프리셋만으로 확정되는 처리

- 프롬프트 블록 순서, 역할, jailbreak/COT 활성 여부, ChatML, cache 지점
- RisuAI의 연속 system 메시지 병합과 선택적인 With Join
- description, persona, memory, authornote의 `innerFormat`: 내부 형식의 CBS를 먼저 처리하고 첫 `{{slot}}`에 내용을 삽입
- 커스텀 토글의 `{{getglobalvar::toggle_...}}`, 값이 없는 토글의 `null`, `{{jbtoggled}}`
- 중첩 CBS 인수와 `#if`, `#if_pure`, `#when`, `#each`, `#pure`, `#puredisplay`, `#escape`
- 비교·논리: equal, notequal, greater, less, greaterequal, lessequal, and, or, not
- 문자열·배열·객체: startswith, endswith, contains, replace, split, join, spread, trim, length, 대소문자 변환,
  arrayelement/length/push/pop/shift/splice/assert, dictelement, element, makearray, makedict, objectassert, range, filter
- 수치·집계: calc와 `?` 수식, round, floor, ceil, abs, remaind, pow, tonumber, min, max, sum, average, fixnum
- 정적 유틸리티: 유니코드/16진수 변환, iserror, reverse, xor, crypt, tex, ruby, codeblock, 이스케이프 문자

함수 이름은 RisuAI처럼 대소문자를 구분하지 않고 공백, `_`, `-`를 무시합니다. 중첩 함수의 `::`는 바깥 인수와
섞이지 않도록 깊이를 구분해 해석합니다.

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

