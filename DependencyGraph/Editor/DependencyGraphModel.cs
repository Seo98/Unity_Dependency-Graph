using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PortableTools.DependencyGraph
{
    internal enum DependencyKind { Reference, Field, Constructor, Signature, Inheritance }

    /// <summary>분석 결과만 담습니다. 게임 오브젝트나 원본 코드를 수정하지 않습니다.</summary>
    internal sealed class DependencyGraphModel
    {
        internal sealed class Item
        {
            public string Id, Title, Detail;
            public UnityEngine.Object Target;
            public bool IsRoot;
        }

        internal sealed class Link
        {
            public string From, To, Label;
            public DependencyKind Kind;
        }

        public const int MaxNodes = 160;
        public const int MaxLinks = 600;
        public readonly Dictionary<string, Item> Nodes = new Dictionary<string, Item>();
        public readonly List<Link> Links = new List<Link>();
        public readonly List<string> Warnings = new List<string>();
        private readonly HashSet<string> linkKeys = new HashSet<string>();
        private readonly HashSet<string> warningKeys = new HashSet<string>();

        public bool AddNode(string id, string title, string detail, UnityEngine.Object target, bool root)
        {
            if (Nodes.TryGetValue(id, out var existing))
            {
                existing.IsRoot |= root;
                return true;
            }
            if (Nodes.Count >= MaxNodes)
            {
                Warn($"노드 {MaxNodes}개 제한에 도달했습니다. 선택 범위 또는 깊이를 줄이세요.");
                return false;
            }
            Nodes.Add(id, new Item { Id = id, Title = title, Detail = detail, Target = target, IsRoot = root });
            return true;
        }

        public void AddLink(string from, string to, string label, DependencyKind kind)
        {
            if (!Nodes.ContainsKey(from) || !Nodes.ContainsKey(to)) return;
            string key = from + "\n" + to + "\n" + label + "\n" + kind;
            if (linkKeys.Contains(key)) return;
            if (Links.Count >= MaxLinks)
            {
                Warn($"연결 {MaxLinks}개 제한에 도달했습니다. 결과는 일부만 표시됩니다.");
                return;
            }
            linkKeys.Add(key);
            Links.Add(new Link { From = from, To = to, Label = label, Kind = kind });
        }

        public void Warn(string message)
        {
            if (warningKeys.Add(message) && Warnings.Count < 30) Warnings.Add(message);
        }

        /// <summary>문서에 붙여 넣을 Mermaid 텍스트를 만듭니다. 원본 그래프는 변경하지 않습니다.</summary>
        public string ToMermaid()
        {
            var text = new StringBuilder("flowchart LR\n");
            var ids = new Dictionary<string, string>();
            foreach (var pair in Nodes)
            {
                string id = "n" + ids.Count;
                ids.Add(pair.Key, id);
                text.Append("  ").Append(id).Append("[\"").Append(Escape(pair.Value.Title)).Append("\"]\n");
            }
            foreach (var link in Links)
                text.Append("  ").Append(ids[link.From]).Append(" -->|\"")
                    .Append(Escape(link.Kind + ": " + link.Label)).Append("\"| ")
                    .Append(ids[link.To]).Append('\n');
            return text.ToString();
        }

        private static string Escape(string value) => value.Replace("&", "&amp;")
            .Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("|", "&#124;").Replace("\r", " ").Replace("\n", " ");
    }
}
