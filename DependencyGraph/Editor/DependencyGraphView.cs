using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace PortableTools.DependencyGraph
{
    /// <summary>UI Toolkit GraphView 표현부입니다. 분석기는 GraphView에 의존하지 않습니다.</summary>
    internal sealed class DependencyGraphView : GraphView
    {
        private readonly Dictionary<string, Node> displayed = new Dictionary<string, Node>();
        private DependencyGraphModel model;

        public DependencyGraphView()
        {
            style.flexGrow = 1;
            SetupZoom(0.08f, 1.8f);
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());
        }

        /// <summary>포트 연결 편집은 비활성화합니다. 선은 분석 증거이지 실행 그래프가 아닙니다.</summary>
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter) => new List<Port>();

        public void Show(DependencyGraphModel data)
        {
            ClearSelection();
            foreach (var edge in edges.ToList()) { edge.input?.Disconnect(edge); edge.output?.Disconnect(edge); RemoveElement(edge); }
            foreach (var node in nodes.ToList()) RemoveElement(node);
            displayed.Clear(); model = data;
            var inputs = new Dictionary<string, Port>();
            foreach (var item in data.Nodes.Values)
            {
                var node = new Node { title = item.Title, tooltip = item.Detail };
                node.capabilities = Capabilities.Selectable | Capabilities.Movable;
                node.style.width = 330;
                node.titleContainer.style.backgroundColor = item.IsRoot ? new Color(.12f, .31f, .36f) : new Color(.22f, .24f, .29f);
                var details = new Label(item.Detail);
                details.style.whiteSpace = WhiteSpace.Normal;
                details.style.fontSize = 11;
                details.style.marginLeft = 8; details.style.marginRight = 8;
                node.extensionContainer.Add(details);
                if (item.Target != null)
                {
                    var locate = new Button(() => { if (item.Target == null) return; Selection.activeObject = item.Target; EditorGUIUtility.PingObject(item.Target); }) { text = "Locate source / object" };
                    node.extensionContainer.Add(locate);
                    node.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        if (evt.clickCount == 2 && item.Target is MonoScript script) AssetDatabase.OpenAsset(script);
                    });
                }
                var input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(object));
                input.portName = "Referenced by";
                input.pickingMode = PickingMode.Ignore;
                node.inputContainer.Add(input); inputs[item.Id] = input;
                node.RefreshExpandedState(); node.RefreshPorts();
                displayed.Add(item.Id, node); AddElement(node);
            }
            // Aggregate labels per target and kind: large arrays do not create hundreds of ports on one node.
            foreach (var group in data.Links.GroupBy(x => (x.From, x.To, x.Kind)))
            {
                var first = group.First();
                var from = displayed[first.From];
                var port = from.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(object));
                var labels = group.Select(x => x.Label).Distinct().ToArray();
                string label = labels[0];
                port.portName = first.Kind + ": " + (label.Length > 28 ? label.Substring(0, 28) + "…" : label) +
                    (labels.Length > 1 ? " (+" + (labels.Length - 1) + ")" : "");
                port.tooltip = string.Join("\n", labels);
                port.portColor = ColorFor(first.Kind);
                port.pickingMode = PickingMode.Ignore;
                from.outputContainer.Add(port);
                var edge = port.ConnectTo(inputs[first.To]);
                edge.capabilities = Capabilities.Selectable;
                edge.tooltip = first.Kind + "\n" + string.Join("\n", labels);
                AddElement(edge);
                from.RefreshExpandedState(); from.RefreshPorts();
            }
            AutoLayout();
            schedule.Execute(() => FrameAll()).ExecuteLater(100);
        }

        public void Highlight(string query)
        {
            query = query ?? "";
            foreach (var pair in displayed)
            {
                var item = model.Nodes[pair.Key];
                pair.Value.style.opacity = (item.Title + " " + item.Detail).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ? 1f : .2f;
            }
        }

        /// <summary>진입 노드부터 폭 우선으로 정렬합니다. 순환 관계는 남겨두며 실행 순서를 의미하지 않습니다.</summary>
        public void AutoLayout()
        {
            if (model == null) return;
            var incoming = new HashSet<string>(model.Links.Select(x => x.To));
            var levels = new Dictionary<string, int>();
            var queue = new Queue<string>();
            foreach (var id in model.Nodes.Keys.Where(x => !incoming.Contains(x))) { levels[id] = 0; queue.Enqueue(id); }
            if (queue.Count == 0 && model.Nodes.Count > 0) { var id = model.Nodes.Keys.First(); levels[id] = 0; queue.Enqueue(id); }
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var link in model.Links.Where(x => x.From == id))
                    if (!levels.ContainsKey(link.To)) { levels[link.To] = Math.Min(4, levels[id] + 1); queue.Enqueue(link.To); }
            }
            var nextY = new float[5];
            foreach (var item in model.Nodes.Values.OrderBy(x => x.Title, StringComparer.Ordinal))
            {
                int level = levels.TryGetValue(item.Id, out var value) ? value : 0;
                int portCount = model.Links.Where(x => x.From == item.Id).Select(x => (x.To, x.Kind)).Distinct().Count();
                float height = 160 + 26 * portCount;
                displayed[item.Id].SetPosition(new Rect(60 + level * 470, 60 + nextY[level], 330, height));
                nextY[level] += height + 65;
            }
        }

        private static Color ColorFor(DependencyKind kind)
        {
            switch (kind)
            {
                case DependencyKind.Reference: return new Color(.25f, .85f, .95f);
                case DependencyKind.Constructor: return new Color(1f, .69f, .27f);
                case DependencyKind.Signature: return new Color(.79f, .53f, 1f);
                case DependencyKind.Inheritance: return new Color(.65f, .68f, .73f);
                default: return new Color(.35f, .9f, .63f);
            }
        }
    }
}
