using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PortableTools.DependencyGraph
{
    /// <summary>씬의 실제 직렬화 참조와 컴파일된 타입의 선언 의존성을 구분하여 읽습니다.</summary>
    internal static class DependencyGraphAnalyzer
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static IEnumerable<GameObject> LoadedSceneRoots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.IsValid()) continue;
                foreach (var root in scene.GetRootGameObjects()) yield return root;
            }
        }

        /// <summary>루트 아래 비활성 MonoBehaviour까지 읽습니다. 같은 타입의 인스턴스도 별도 노드입니다.</summary>
        public static DependencyGraphModel Scene(IEnumerable<GameObject> roots, int depth)
        {
            var seeds = new List<Object>();
            bool missing = false;
            foreach (var root in roots.Where(x => x != null).Distinct())
            {
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) { missing = true; continue; }
                    if (!IsUserType(component.GetType())) continue;
                    seeds.Add(component);
                }
            }
            var result = Objects(seeds, depth);
            if (missing) result.Warn("Missing Script가 있는 오브젝트가 있습니다.");
            return result;
        }

        internal static DependencyGraphModel Objects(IEnumerable<Object> seeds, int depth)
        {
            var result = new DependencyGraphModel();
            var queue = new Queue<(Object target, int depth)>();
            var scanned = new HashSet<int>();
            foreach (var seed in seeds.Where(x => x != null).Distinct())
                if (AddObject(result, seed, true)) queue.Enqueue((seed, 0));
            depth = Mathf.Clamp(depth, 0, 3);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.target == null || !scanned.Add(current.target.GetInstanceID())) continue;
                if (current.depth >= depth) continue;
                if (!(current.target is MonoBehaviour) && !(current.target is ScriptableObject)) continue;
                try
                {
                    using (var serialized = new SerializedObject(current.target))
                    {
                        var iterator = serialized.GetIterator();
                        var managedIds = new HashSet<long>();
                        bool enter = true;
                        int inspected = 0, empty = 0;
                        while (iterator.Next(enter))
                        {
                            enter = true;
                            if (++inspected > 10000)
                            {
                                result.Warn(current.target.name + ": 속성 10,000개 제한으로 일부 참조만 표시합니다.");
                                break;
                            }
                            if (iterator.propertyType == SerializedPropertyType.ManagedReference)
                                enter = managedIds.Add(iterator.managedReferenceId);
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference || IsUnityInternal(iterator.propertyPath)) continue;
                            Object referenced = iterator.objectReferenceValue;
                            if (referenced == null) { empty++; continue; }
                            if (!AddObject(result, referenced, false)) continue;
                            result.AddLink(ObjectId(current.target), ObjectId(referenced), iterator.propertyPath, DependencyKind.Reference);
                            queue.Enqueue((referenced, current.depth + 1));
                        }
                        if (empty > 0)
                            result.Warn(current.target.name + " / " + current.target.GetType().Name + $": 빈 참조 {empty}개 (선택적 필드일 수도 있음).");
                    }
                }
                catch (Exception exception)
                {
                    result.Warn(current.target.name + ": 참조 읽기 실패 - " + exception.GetType().Name);
                }
            }
            result.Warn("씬 모드: 저장된 Object 참조의 스냅샷입니다. C# 서비스·런타임 DI·Addressables GUID 해석은 포함하지 않습니다.");
            return result;
        }

        private static bool IsUnityInternal(string path) => path == "m_Script" || path == "m_GameObject" ||
            path == "m_CorrespondingSourceObject" || path == "m_PrefabInstance" || path == "m_PrefabAsset";

        private static string ObjectId(Object target) => "object:" + target.GetInstanceID();
        internal static string TypeId(Type type) => "type:" + type.AssemblyQualifiedName;

        private static bool AddObject(DependencyGraphModel graph, Object target, bool root)
        {
            string path = AssetDatabase.GetAssetPath(target);
            var component = target as Component;
            if (component != null) path = ScenePath(component.transform);
            else if (target is GameObject go) path = ScenePath(go.transform);
            return graph.AddNode(ObjectId(target), target.name + " : " + target.GetType().Name,
                (string.IsNullOrEmpty(path) ? "In-memory object" : path) + "\nInstance " + target.GetInstanceID(), target, root);
        }

        private static string ScenePath(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return transform.gameObject.scene.name + "/" + path;
        }

        /// <summary>여러 MonoScript의 대표 타입을 입력받습니다. 소스 AST나 실행 순서는 분석하지 않습니다.</summary>
        public static DependencyGraphModel Scripts(IEnumerable<MonoScript> scripts, int depth, bool signatures)
        {
            var mapping = new Dictionary<Type, MonoScript>();
            var unresolved = new List<string>();
            foreach (var script in scripts.Where(x => x != null).Distinct())
            {
                var type = ResolveScriptType(script);
                if (type == null) unresolved.Add(script.name + ": 대표 타입을 얻지 못했습니다. 컴파일·파일명/타입명·제네릭 선언을 확인하세요.");
                else mapping[type] = script;
            }
            var graph = Types(mapping.Keys, depth, signatures);
            foreach (var pair in mapping)
                if (graph.Nodes.TryGetValue(TypeId(pair.Key), out var node)) node.Target = pair.Value;
            foreach (var warning in unresolved) graph.Warn(warning);
            return graph;
        }

        // Some plain C# scripts have no Unity MonoScript representative type. Only accept an
        // unambiguous file-name match in that script's own compiled assembly; never guess by text.
        private static Type ResolveScriptType(MonoScript script)
        {
            var primary = script.GetClass();
            if (primary != null) return primary;
            string assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(AssetDatabase.GetAssetPath(script));
            if (string.IsNullOrEmpty(assemblyName)) return null;
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == assemblyName);
            if (assembly == null) return null;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types; }
            var matches = types.Where(x => x != null && !x.IsNested && x.Name.Split('`')[0] == script.name).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        /// <summary>필드·생성자·상속 및 선택적으로 메서드/속성 시그니처의 타입 의존성을 분석합니다.</summary>
        internal static DependencyGraphModel Types(IEnumerable<Type> seeds, int depth, bool signatures)
        {
            var graph = new DependencyGraphModel();
            var queue = new Queue<(Type type, int depth)>();
            var scanned = new HashSet<Type>();
            foreach (var type in seeds.Where(x => x != null).Distinct())
                if (AddType(graph, type, true)) queue.Enqueue((type, 0));
            depth = Mathf.Clamp(depth, 0, 3);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!scanned.Add(current.type) || current.depth >= depth) continue;
                try
                {
                    foreach (var relation in Relations(current.type, signatures))
                    {
                        foreach (var dependency in Unwrap(relation.type).Where(IsUserType).Distinct())
                        {
                            if (!AddType(graph, dependency, false)) continue;
                            graph.AddLink(TypeId(current.type), TypeId(dependency), relation.label, relation.kind);
                            queue.Enqueue((dependency, current.depth + 1));
                        }
                    }
                }
                catch (Exception exception) { graph.Warn(current.type.Name + ": 타입 읽기 실패 - " + exception.GetType().Name); }
            }
            graph.Warn("타입 모드: 선언 관계입니다. 실제 주입 여부·메서드 호출·new/Find/GetComponent/if 분기는 판정하지 않습니다.");
            return graph;
        }

        private static bool AddType(DependencyGraphModel graph, Type type, bool root) =>
            graph.AddNode(TypeId(type), NiceName(type), (type.Namespace ?? "(global)") + "\n" +
                type.Assembly.GetName().Name + (type.IsInterface ? " / interface" : " / type"), null, root);

        private static IEnumerable<(Type type, string label, DependencyKind kind)> Relations(Type type, bool signatures)
        {
            if (type.BaseType != null) yield return (type.BaseType, "base", DependencyKind.Inheritance);
            foreach (var contract in type.GetInterfaces()) yield return (contract, "implements", DependencyKind.Inheritance);
            for (var owner = type; owner != null && IsUserType(owner); owner = owner.BaseType)
                foreach (var field in owner.GetFields(Members))
                {
                    if (field.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
                    yield return (field.FieldType, (field.IsStatic ? "static " : "") + field.Name, DependencyKind.Field);
                }
            foreach (var constructor in type.GetConstructors(Members))
                foreach (var parameter in constructor.GetParameters())
                    yield return (parameter.ParameterType, "ctor: " + parameter.Name, DependencyKind.Constructor);
            if (!signatures) yield break;
            foreach (var property in type.GetProperties(Members))
                yield return (property.PropertyType, property.Name + " { get/set }", DependencyKind.Signature);
            foreach (var method in type.GetMethods(Members))
            {
                if (method.IsSpecialName || method.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
                yield return (method.ReturnType, method.Name + " return", DependencyKind.Signature);
                foreach (var parameter in method.GetParameters())
                    yield return (parameter.ParameterType, method.Name + "(" + parameter.Name + ")", DependencyKind.Signature);
            }
        }

        private static IEnumerable<Type> Unwrap(Type type)
        {
            if (type.HasElementType)
            {
                foreach (var element in Unwrap(type.GetElementType())) yield return element;
                yield break;
            }
            yield return type;
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments())
                    foreach (var element in Unwrap(argument)) yield return element;
        }

        private static bool IsUserType(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type.IsGenericParameter || type == typeof(string)) return false;
            string assembly = type.Assembly.GetName().Name;
            return assembly != "mscorlib" && assembly != "netstandard" && assembly != "System" &&
                !assembly.StartsWith("System.", StringComparison.Ordinal) &&
                !assembly.StartsWith("Unity", StringComparison.Ordinal) &&
                !assembly.StartsWith("Microsoft.", StringComparison.Ordinal);
        }

        internal static string NiceName(Type type)
        {
            if (!type.IsGenericType) return type.Name;
            return type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(NiceName)) + ">";
        }
    }
}
