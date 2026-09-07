using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PortableTools.DependencyGraph
{
    /// <summary>다른 프로젝트에 폴더째 복사할 수 있는 읽기 전용 의존성 그래프 에디터입니다.</summary>
    public sealed class DependencyGraphWindow : EditorWindow
    {
        [SerializeField] private List<MonoScript> scripts = new List<MonoScript>();
        [SerializeField] private int depth = 1;
        [SerializeField] private bool signatures;
        private DependencyGraphView graph;
        private DependencyGraphModel model;
        private ScrollView scriptList;
        private Label status, warnings;
        private string search = "";

        /// <summary>Tools 메뉴에서 도구를 엽니다. 씬 변경이나 Play Mode 전환은 하지 않습니다.</summary>
        [MenuItem("Tools/Dependency Graph/Open")]
        public static void Open()
        {
            var window = GetWindow<DependencyGraphWindow>();
            window.titleContent = new GUIContent("Dependency Graph");
            window.minSize = new Vector2(1000, 620);
        }

        /// <summary>Unity UI Toolkit으로 도구 화면을 구성합니다.</summary>
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(() => AnalyzeScene(false)) { text = "Scan Loaded Scenes" });
            toolbar.Add(new ToolbarButton(() => AnalyzeScene(true)) { text = "Scan Selected Objects" });
            toolbar.Add(new ToolbarButton(AnalyzeScripts) { text = "Analyze Scripts" });
            toolbar.Add(new ToolbarButton(() => { graph.AutoLayout(); graph.FrameAll(); }) { text = "Layout / Fit" });
            toolbar.Add(new ToolbarButton(() => { if (model != null) EditorGUIUtility.systemCopyBuffer = model.ToMermaid(); }) { text = "Copy Mermaid" });
            var filter = new ToolbarSearchField { tooltip = "노드 이름·경로 검색. 일치하지 않는 노드는 흐리게 표시합니다." };
            filter.style.flexGrow = 1;
            filter.RegisterValueChangedCallback(evt => { search = evt.newValue; graph.Highlight(search); });
            toolbar.Add(filter); rootVisualElement.Add(toolbar);

            var body = new VisualElement(); body.style.flexDirection = FlexDirection.Row; body.style.flexGrow = 1;
            var sidebar = new ScrollView(ScrollViewMode.Vertical); sidebar.style.width = 260; sidebar.style.flexShrink = 0;
            sidebar.style.paddingLeft = 10; sidebar.style.paddingRight = 10; sidebar.style.paddingTop = 10;
            var intro = new Label("READ-ONLY DEPENDENCIES\n사용하는 쪽 (출력) → 참조되는 쪽 (입력)\n실행 가능한 Visual Scripting 변환은 아닙니다.");
            intro.style.whiteSpace = WhiteSpace.Normal; sidebar.Add(intro);
            var depthField = new IntegerField("Depth (0-3)") { value = depth };
            depthField.tooltip = "0: 입력 노드만, 1: 직접 의존성, 2-3: 연결된 대상까지 확장. 변경 후 다시 분석하세요.";
            depthField.RegisterValueChangedCallback(evt => { depth = Mathf.Clamp(evt.newValue, 0, 3); depthField.SetValueWithoutNotify(depth); });
            sidebar.Add(depthField);
            var signatureField = new Toggle("Method / property types") { value = signatures };
            signatureField.RegisterValueChangedCallback(evt => signatures = evt.newValue); sidebar.Add(signatureField);
            var add = new Button(() => AddScripts(Selection.objects)) { text = "Add Selected Scripts" }; sidebar.Add(add);
            sidebar.Add(new Button(() => { scripts.Clear(); RebuildScriptList(); }) { text = "Clear Script List" });
            var drop = new Label("Project의 C# 파일 여러 개를\n여기로 드래그하세요.");
            drop.style.whiteSpace = WhiteSpace.Normal; drop.style.paddingTop = 14; drop.style.paddingBottom = 14;
            drop.style.unityTextAlign = TextAnchor.MiddleCenter;
            drop.style.backgroundColor = new Color(.16f, .24f, .28f); sidebar.Add(drop);
            drop.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!DragAndDrop.objectReferences.OfType<MonoScript>().Any()) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; evt.StopPropagation();
            });
            drop.RegisterCallback<DragPerformEvent>(evt => { DragAndDrop.AcceptDrag(); AddScripts(DragAndDrop.objectReferences); evt.StopPropagation(); });
            scriptList = new ScrollView(); scriptList.style.height = 170; sidebar.Add(scriptList);
            var legend = new Label("선 종류\n청록 Reference: 실제 Object 참조\n초록 Field: 필드 타입\n주황 Constructor: 생성자 인자\n보라 Signature: 메서드/속성 타입\n회색 Inheritance: 상속/인터페이스\n\n휠: 확대/축소 · 중간 버튼: 이동\n노드 드래그: 위치 변경\n포트에 마우스: 전체 필드명");
            legend.style.whiteSpace = WhiteSpace.Normal; legend.style.fontSize = 11; sidebar.Add(legend);
            var notes = new ScrollView(); notes.style.flexGrow = 1; notes.style.minHeight = 70;
            warnings = new Label("Scan 또는 Analyze를 누르세요.\n자동 재분석하지 않으므로 씬 변경 뒤에는 다시 눌러주세요.");
            warnings.style.whiteSpace = WhiteSpace.Normal; warnings.style.fontSize = 11;
            notes.Add(warnings); sidebar.Add(notes);
            graph = new DependencyGraphView(); body.Add(sidebar); body.Add(graph); rootVisualElement.Add(body);
            status = new Label("Ready | Editor only | No game scripts modified"); rootVisualElement.Add(status);
            RebuildScriptList();
            if (model != null) Present(model, "Snapshot");
        }

        private void AddScripts(IEnumerable<UnityEngine.Object> selection)
        {
            foreach (var script in selection.OfType<MonoScript>())
                if (!scripts.Contains(script)) scripts.Add(script);
            RebuildScriptList();
        }

        private void RebuildScriptList()
        {
            if (scriptList == null) return;
            scriptList.Clear(); scripts.RemoveAll(x => x == null);
            foreach (var script in scripts.ToArray())
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var field = new ObjectField { objectType = typeof(MonoScript), value = script, allowSceneObjects = false };
                field.SetEnabled(false); field.style.flexGrow = 1; row.Add(field);
                row.Add(new Button(() => { scripts.Remove(script); RebuildScriptList(); }) { text = "×" });
                scriptList.Add(row);
            }
        }

        private bool CanAnalyze()
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating) return true;
            status.text = "컴파일·에셋 갱신이 끝난 뒤 다시 분석하세요.";
            return false;
        }

        private void AnalyzeScene(bool selected)
        {
            if (!CanAnalyze()) return;
            var roots = selected ? Selection.gameObjects.AsEnumerable() : DependencyGraphAnalyzer.LoadedSceneRoots();
            // Prefab assets are not scene instances. Prefab Stage is only included by explicit selection.
            roots = roots.Where(x => x != null && !EditorUtility.IsPersistent(x) && x.scene.IsValid());
            Present(DependencyGraphAnalyzer.Scene(roots, depth), "Scene objects");
        }

        private void AnalyzeScripts()
        {
            if (!CanAnalyze()) return;
            Present(DependencyGraphAnalyzer.Scripts(scripts, depth, signatures), "Declared types");
        }

        private void Present(DependencyGraphModel data, string mode)
        {
            model = data; graph.Show(data); graph.Highlight(search);
            status.text = mode + $" | {data.Nodes.Count} nodes / {data.Links.Count} relations | Depth {depth}";
            warnings.text = string.Join("\n\n", data.Warnings);
            if (data.Nodes.Count == 0) warnings.text += "\n\n대상이 없습니다. 씬 오브젝트 또는 컴파일된 C# 스크립트를 선택하세요.";
        }
    }
}
