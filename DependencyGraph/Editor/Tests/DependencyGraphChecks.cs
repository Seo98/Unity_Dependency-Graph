using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PortableTools.DependencyGraph
{
    /// <summary>외부 테스트 패키지 없이 실행하는 소규모 회귀 검사입니다. 원본 씬·에셋은 수정하지 않습니다.</summary>
    public static class DependencyGraphChecks
    {
        private static int assertions;

        /// <summary>메뉴 또는 Unity batchmode의 executeMethod로 분석기·노드 생성 검사를 실행합니다.</summary>
        [MenuItem("Tools/Dependency Graph/Run Checks")]
        public static void Run()
        {
            assertions = 0;
            CheckTypes();
            CheckObjects();
            var inputScripts = AssetDatabase.FindAssets("DependencyGraphModel t:MonoScript")
                .Concat(AssetDatabase.FindAssets("DependencyGraphAnalyzer t:MonoScript"))
                .Select(x => AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(x))).ToArray();
            var scriptGraph = DependencyGraphAnalyzer.Scripts(inputScripts, 1, false);
            Require(scriptGraph.Nodes.ContainsKey(DependencyGraphAnalyzer.TypeId(typeof(DependencyGraphModel))), "Plain C# script input");
            Require(scriptGraph.Nodes.ContainsKey(DependencyGraphAnalyzer.TypeId(typeof(DependencyGraphAnalyzer))), "Multiple scripts with static class");
            var window = ScriptableObject.CreateInstance<DependencyGraphWindow>();
            try
            {
                window.CreateGUI();
                Require(window.rootVisualElement.childCount >= 3, "UI Toolkit window creation");
            }
            finally { Object.DestroyImmediate(window); }
            var view = new DependencyGraphView();
            var graph = DependencyGraphAnalyzer.Types(new[] { typeof(ProbeService) }, 2, true);
            view.Show(graph);
            Require(view.nodes.Count() == graph.Nodes.Count, "GraphView node count");
            Require(view.edges.Any(), "GraphView connections");
            view.Highlight("Probe"); view.AutoLayout();
            view.Show(new DependencyGraphModel());
            Require(!view.nodes.Any() && !view.edges.Any(), "Graph refresh clears previous elements");
            Debug.Log($"DEPENDENCY_GRAPH_CHECKS_PASSED: {assertions} assertions");
        }

        /// <summary>게임 오브젝트가 필요 없는 타입/모델 검사입니다.</summary>
        public static void CheckTypes()
        {
            var graph = DependencyGraphAnalyzer.Types(new[] { typeof(ProbeService), typeof(ProbeService) }, 2, false);
            string service = DependencyGraphAnalyzer.TypeId(typeof(ProbeService));
            string data = DependencyGraphAnalyzer.TypeId(typeof(ProbeData));
            Require(graph.Nodes.ContainsKey(service) && graph.Nodes.ContainsKey(data), "Generic collection element included");
            Require(graph.Nodes.Keys.Count(x => x == service) == 1, "Duplicate seeds deduplicated");
            Require(graph.Links.Any(x => x.Kind == DependencyKind.Field && x.To == data), "Field relation");
            Require(graph.Links.Any(x => x.Kind == DependencyKind.Constructor && x.To == data), "Constructor relation");
            Require(graph.Links.Any(x => x.Kind == DependencyKind.Inheritance), "Interface relation");
            Require(!graph.Links.Any(x => x.Kind == DependencyKind.Signature), "Signatures disabled");
            Require(!graph.Nodes.ContainsKey(DependencyGraphAnalyzer.TypeId(typeof(string))), "System type filtered");
            Require(graph.Links.Any(x => x.From == data && x.To == service), "Cycles represented");
            Require(DependencyGraphAnalyzer.Types(new[] { typeof(ProbeService) }, 0, true).Nodes.Count == 1, "Depth zero");
            var signatures = DependencyGraphAnalyzer.Types(new[] { typeof(ProbeService) }, 1, true);
            Require(signatures.Links.Any(x => x.Kind == DependencyKind.Signature && x.Label.Contains("Read")), "Method signature");
            var bounded = new DependencyGraphModel();
            for (int i = 0; i < 200; i++) bounded.AddNode("n" + i, "Name", "", null, false);
            Require(bounded.Nodes.Count == DependencyGraphModel.MaxNodes && bounded.Warnings.Count > 0, "Node limit reported");
            bounded.AddLink("n0", "absent", "invalid", DependencyKind.Field);
            Require(bounded.Links.Count == 0, "No dangling edges");
            bounded.AddLink("n0", "n1", "same", DependencyKind.Field);
            bounded.AddLink("n0", "n1", "same", DependencyKind.Field);
            Require(bounded.Links.Count == 1, "Duplicate edges deduplicated");
            for (int i = 0; i < 700; i++) bounded.AddLink("n0", "n1", "field" + i, DependencyKind.Field);
            Require(bounded.Links.Count == DependencyGraphModel.MaxLinks, "Edge limit");
            bounded.Nodes["n0"].Title = "Quote\"<|";
            string mermaid = bounded.ToMermaid();
            Require(mermaid.Contains("&quot;") && mermaid.Contains("&lt;") && mermaid.Contains("&#124;"), "Mermaid escaping");
        }

        private static void CheckObjects()
        {
            var first = ScriptableObject.CreateInstance<DependencyGraphProbeAsset>();
            var second = ScriptableObject.CreateInstance<DependencyGraphProbeAsset>();
            first.name = second.name = "Same name";
            first.target = second; second.target = first;
            first.targets = new Object[] { second, second, null };
            first.nested = new DependencyGraphProbeManaged { target = second };
            first.nested.child = first.nested;
            try
            {
                string before = EditorJsonUtility.ToJson(first);
                var graph = DependencyGraphAnalyzer.Objects(new Object[] { first, first }, 3);
                Require(graph.Nodes.Count == 2, "Instance identities; duplicate seeds; cyclic managed refs terminate");
                Require(graph.Links.Any(x => x.Label == "target"), "Serialized object reference");
                Require(graph.Links.Any(x => x.Label.Contains("targets.Array.data[1]")), "Array element reference");
                Require(graph.Links.Any(x => x.Label.Contains("nested.target")), "Managed reference child field");
                Require(!graph.Links.Any(x => x.Label == "m_Script"), "Unity script metadata skipped");
                Require(graph.Warnings.Any(x => x.Contains("빈 참조")), "Empty reference diagnostic");
                Require(EditorJsonUtility.ToJson(first) == before, "Read only scan");
                var shallow = DependencyGraphAnalyzer.Objects(new Object[] { first }, 1);
                Require(!shallow.Links.Any(x => x.From == "object:" + second.GetInstanceID()), "Depth stops expansion");
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Dependency graph check failed: " + message);
            assertions++;
        }

#pragma warning disable CS0649
        private interface IProbe { }
        private sealed class ProbeData { public ProbeService owner; }
        private sealed class ProbeService : IProbe
        {
            private readonly List<ProbeData[]> values;
            public ProbeService(ProbeData data) { values = new List<ProbeData[]> { new[] { data } }; }
            public ProbeData Read(ProbeData input) => input;
        }
#pragma warning restore CS0649
    }

    internal sealed class DependencyGraphProbeAsset : ScriptableObject
    {
        public Object target;
        public Object[] targets;
        [SerializeReference] public DependencyGraphProbeManaged nested;
    }

    [Serializable]
    internal sealed class DependencyGraphProbeManaged
    {
        public Object target;
        [SerializeReference] public DependencyGraphProbeManaged child;
    }
}
