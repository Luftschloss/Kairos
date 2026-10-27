using System;
using UnityEditor;
using UnityEngine;

namespace Kairos.Editor
{
    public sealed class UiNavigator : EditorWindow
    {
        string query = "";
        Vector2 scroll;
        [MenuItem("KAIROS/UI/Navigator")]
        public static void Open() => GetWindow<UiNavigator>("KAIROS UI");
        void OnGUI()
        {
            EditorGUILayout.LabelField("UI Prefab定位", EditorStyles.boldLabel);
            query = EditorGUILayout.TextField("搜索页面 / 面板", query);
            var live = UnityEngine.Object.FindObjectOfType<Kairos.Client.UiFramework>();
            if (live != null) EditorGUILayout.LabelField("当前页面", live.CurrentPage + (live.HasModal ? " · 弹窗打开" : ""));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Kairos/UI/Resources/KairosUI" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (query.Length > 0 && path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(path.Substring("Assets/Kairos/UI/Resources/KairosUI/".Length));
                if (GUILayout.Button("定位", GUILayout.Width(48))) { var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path); Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
                if (GUILayout.Button("打开", GUILayout.Width(48))) AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
