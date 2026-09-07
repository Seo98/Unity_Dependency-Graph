# 검증 기록

2026-09-08 / Unity 6000.3.12f1

- 설치된 Unity 모듈 DLL로 별도 컴파일: 오류 0, 경고 0.
- 게임 코드·R3·UniTask·VContainer가 없는 임시 Unity 프로젝트에 도구 폴더만 복사.
- Unity batchmode 검사: `DEPENDENCY_GRAPH_CHECKS_PASSED: 29 assertions`, 종료 코드 0.
- 확인: 필드·생성자·상속·선택적 시그니처, 제네릭 내부 타입, 중복 입력·선,
  순환 관계, 깊이 제한, 노드·선 개수 제한, Mermaid 이스케이프.
- 확인: 실제 SerializedObject 참조·배열·SerializeReference 내부 참조·순환 종료,
  같은 이름의 별도 객체, 빈 참조 진단, 스캔 전후 원본 동일성.
- 확인: 여러 일반 C#·static 클래스 스크립트 입력, UI Toolkit 창 생성,
  GraphView 노드·연결 생성과 초기화.

한계: headless 검사입니다. 현재 게임 씬의 시각적 가독성, 실제 마우스 드래그·드롭,
확대/축소·툴팁·다양한 도킹 크기는 창에서 사용자 확인이 필요합니다.
다른 Unity 버전까지 호환 검증한 것은 아닙니다.

다시 검사: `Tools > Dependency Graph > Run Checks`.
기존 게임 스크립트와 씬·SO 설정은 이 도구 작업에서 변경하지 않았습니다.
