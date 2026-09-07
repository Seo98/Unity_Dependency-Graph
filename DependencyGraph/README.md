# Dependency Graph - 작은 읽기 전용 노드 도구

Unity UI Toolkit + 기본 제공 GraphView로 **의존성 다이어그램**을 보여주는 에디터 도구입니다.
여러 C# 파일을 한 화면에 넣거나 현재 씬의 MonoBehaviour 연결을 볼 수 있습니다.
실행 가능한 Visual Scripting 그래프, 함수 호출 그래프, 코드 자동 생성기는 아닙니다.

## 열기

1. Unity 컴파일이 끝나면 `Tools > Dependency Graph > Open`.
2. **씬 보기:** `Scan Loaded Scenes`를 누릅니다. 비활성 오브젝트도 포함합니다.
3. 너무 많으면 Hierarchy에서 필요한 오브젝트만 골라 `Scan Selected Objects`.
   선택한 오브젝트의 자식도 포함하며, Project의 Prefab 에셋은 이 모드에서 제외합니다.
4. **스크립트 보기:** Project에서 여러 `.cs`를 선택해 `Add Selected Scripts`,
   또는 왼쪽 드롭 영역에 여러 스크립트를 끌어다 놓고 `Analyze Scripts`.
5. 휠로 확대/축소, 중간 마우스 버튼으로 화면 이동, 노드를 드래그해 배치합니다.
6. `Layout / Fit`은 자동 정렬·전체 보기입니다. 검색은 일치하지 않는 노드를 흐리게 합니다.
7. `Locate source / object`는 원본을 선택합니다. 입력 스크립트 노드 더블 클릭은 코드를 엽니다.
8. `Copy Mermaid`는 현재 다이어그램을 문서용 텍스트로 복사합니다.

현재 프로젝트의 간단한 시작 예시:

- Hierarchy의 `BattleRuntime` 선택 → `Scan Selected Objects`, Depth 1.
- `EnemySpawner.cs`, `EnemyPoolService.cs`, `EnemyPopulationTracker.cs`, `LoopPath.cs`
  4개를 입력 → `Analyze Scripts`, Depth 1.
- Scene 모드와 Type 모드는 **별도 스냅샷**입니다. 서로 같은 의미의 선으로 섞지 않습니다.

## 선이 뜻하는 것

화면의 **출력 포트 쪽 노드가 입력 포트 쪽 노드를 참조**합니다. 실행 순서가 아닙니다.

| 표시 | 의미 |
|---|---|
| Reference / 청록 | SerializedObject에서 읽은 실제 Unity Object 참조 |
| Field / 초록 | 필드에 선언된 타입. 값이 연결됐다는 뜻은 아님 |
| Constructor / 주황 | 생성자가 받는 인자의 타입. 실제 DI 성공 여부는 아님 |
| Signature / 보라 | 메서드 인자·반환값 또는 속성 타입. 호출했다는 뜻은 아님 |
| Inheritance / 회색 | 기반 클래스·구현 인터페이스 |

같은 대상/종류로 가는 여러 필드는 하나의 포트로 묶습니다.
포트와 선의 툴팁에서 전체 필드명(배열 인덱스 포함)을 확인할 수 있습니다.
동일 타입·동일 이름의 서로 다른 컴포넌트 인스턴스는 별도 노드입니다.

## 범위와 제한

- Depth 0: 입력만 / 1: 직접 참조 / 2-3: 연결된 대상까지 확장. 변경 후 다시 분석합니다.
- 자동 갱신하지 않습니다. 씬·코드·Inspector 변경 뒤 명시적으로 재분석하세요.
- C#은 현재 Unity가 **컴파일한 메타데이터**를 읽습니다. 깨진/미컴파일 소스는 분석할 수 없습니다.
- `MonoScript.GetClass()`가 제공하는 대표 타입을 입력으로 사용합니다.
  대표 타입이 없으면 해당 스크립트의 컴파일 어셈블리에서 파일명과 일치하는 유일한 타입을 찾습니다.
  한 파일의 모든 최상위 타입을 가져오지는 않습니다. 이름이 모호하거나 찾을 수 없으면 경고합니다.
  소스 전체를 파싱하여 모든 선언을 추출하지 않습니다.
- 배열·제네릭 내부의 사용자 타입을 찾습니다. Unity/System 타입은 기본적으로 생략합니다.
  외부 라이브러리의 사용자 타입은 나올 수 있고, `Unity...`로 시작하는 어셈블리는 필터링됩니다.
- 기반 클래스 필드를 포함합니다. 프로퍼티 getter·생성자·게임 메서드는 호출하지 않습니다.
- 씬 모드는 사용자 MonoBehaviour에서 시작해 직렬화 참조만 읽습니다.
  참조된 SO는 깊이 범위에서 확장하지만 Transform/GameObject/Material 같은 대상은 말단 노드입니다.
  GameObject 참조를 특정 컴포넌트 의존성이라고 추측하지 않습니다.
- `[SerializeReference]` 내부 Object 참조·배열을 탐색하며, 순환 참조는 한 번만 확장합니다.
- 순수 C# 서비스의 런타임 객체, 코드로 설정한 비직렬화 필드, `new`, `Find`, `GetComponent`,
  DI 컨테이너 바인딩, Addressables GUID 대상, UnityEvent의 실제 호출 순서, `if/for/await` 흐름은 분석하지 않습니다.
- 빈 참조는 선택적 필드일 수도 있으므로 오류로 단정하지 않습니다.
- 160노드 / 600관계 / 객체당 10,000속성 제한을 두며 잘린 결과는 경고합니다.
- 노드 위치는 임시입니다. 다시 분석하거나 창을 다시 열면 초기화될 수 있습니다.
- 씬/에셋 저장, 컴포넌트 추가, 코드 변경, Play Mode 전환은 하지 않습니다.

## 다른 프로젝트로 옮기기

`Assets/Tools/DependencyGraph` 폴더 전체와 `.meta`를 복사하세요.
별도 패키지 설치, R3, UniTask, VContainer, 이 게임의 클래스가 필요하지 않습니다.
Editor 폴더 아래에서만 컴파일되므로 플레이어 빌드에 포함되지 않습니다.
다른 프로젝트의 런타임 asmdef 아래에 넣지 말고 독립된 `Assets/Tools` 아래에 두세요.

Unity 6000.3.12f1 기준으로 제작했습니다. 다른 Unity 버전은 검증 후 사용하세요.
**GraphView는 experimental API**이므로 버전 간 영구 호환을 보장하지 않습니다.
표현부를 `DependencyGraphView.cs`로 분리해 향후 Graph Toolkit 또는 순수 UI Toolkit 캔버스로 바꿀 수 있습니다.

## 파일 역할

- `DependencyGraphWindow`: 메뉴·스크립트 입력·옵션·결과 표시
- `DependencyGraphAnalyzer`: 읽기 전용 씬/타입 분석
- `DependencyGraphModel`: 노드·선·경고·Mermaid 텍스트
- `DependencyGraphView`: UI Toolkit 노드·포트·확대/축소·정렬
- `Tests/DependencyGraphChecks`: 작은 검사 메뉴. 테스트용 임시 SO만 생성·정리합니다.

`Tools > Dependency Graph > Run Checks`로 검사할 수 있습니다.
통과 시 Console에 `DEPENDENCY_GRAPH_CHECKS_PASSED`가 표시됩니다.
실제 씬 가독성·노드 드래그·화면 크기·검색은 창에서 직접 확인해야 합니다.

공식 API:

- [GraphView - experimental](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.GraphView.GraphView.html)
- [MonoScript.GetClass](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoScript.GetClass.html)

확장 순서: 스크립트 파일의 전체 선언 분석 → 함수 단위 노드 → 호출 관계 → 제어 흐름.
그 단계는 현재 반사(reflection) 분석과 별도로 Roslyn 같은 C# 구문·의미 분석기가 필요한 작업입니다.
