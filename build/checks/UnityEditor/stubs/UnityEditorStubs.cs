// Compile-only stubs of the UnityEditor members used by Assets/GodSim/Editor.
// Signatures mirror Unity 6 / 2022.3. Bodies are placeholders and never execute. Never copy into Assets/.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CustomEditor : Attribute
    {
        public CustomEditor(Type inspectedType) { }
    }

    public class Editor : ScriptableObject
    {
        public Object target { get; set; }
        public SerializedObject serializedObject => null;
        public virtual void OnInspectorGUI() { }
        public bool DrawDefaultInspector() => false;
        public virtual bool RequiresConstantRepaint() => false;
        public void Repaint() { }
    }

    public class EditorWindow : ScriptableObject
    {
        public static T GetWindow<T>(string title) where T : EditorWindow => null;
        public GUIContent titleContent { get; set; }
        public Vector2 minSize { get; set; }
        public Rect position { get; set; }
        public bool wantsMouseMove { get; set; }
        public void Show() { }
        public void Repaint() { }
    }

    public enum MessageType { None = 0, Info = 1, Warning = 2, Error = 3 }

    public sealed class SerializedObject : IDisposable
    {
        public SerializedObject(Object obj) { }
        public SerializedProperty GetIterator() => null;
        public SerializedProperty FindProperty(string propertyPath) => null;
        public void Update() { }
        public bool ApplyModifiedProperties() => false;
        public void Dispose() { }
    }

    public class SerializedProperty : IDisposable
    {
        public string name => null;
        public string propertyPath => null;
        public bool isArray => false;
        public int arraySize { get; set; }
        public string stringValue { get; set; }
        public SerializedProperty Copy() => null;
        public bool NextVisible(bool enterChildren) => false;
        public SerializedProperty GetArrayElementAtIndex(int index) => null;
        public void Dispose() { }
    }

    public sealed class EditorGUILayout
    {
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string label2, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string label2, GUIStyle style, params GUILayoutOption[] options) { }
        public static long LongField(string label, long value, params GUILayoutOption[] options) => value;
        public static int IntSlider(string label, int value, int leftValue, int rightValue, params GUILayoutOption[] options) => value;
        public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) => selected;
        public static bool Toggle(string label, bool value, params GUILayoutOption[] options) => value;
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) => foldout;
        public static bool PropertyField(SerializedProperty property, bool includeChildren, params GUILayoutOption[] options) => false;
        public static void HelpBox(string message, MessageType type) { }
        public static void Space() { }
        public static Rect GetControlRect(params GUILayoutOption[] options) => default(Rect);
        public static Rect BeginHorizontal(params GUILayoutOption[] options) => default(Rect);
        public static void EndHorizontal() { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
        public static void EndScrollView() { }
    }

    public sealed class EditorGUI
    {
        public static int indentLevel { get; set; }
        public static void DrawRect(Rect rect, Color color) { }

        public struct DisabledScope : IDisposable
        {
            public DisabledScope(bool disabled) { }
            public void Dispose() { }
        }
    }

    public sealed class EditorStyles
    {
        public static GUIStyle boldLabel => null;
        public static GUIStyle miniLabel => null;
        public static GUIStyle miniBoldLabel => null;
        public static GUIStyle wordWrappedLabel => null;
    }

    public sealed class EditorGUIUtility
    {
        public static void PingObject(Object obj) { }
    }

    public sealed class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok) => false;
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => false;
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) => false;
        public static void ClearProgressBar() { }
        public static void SetDirty(Object target) { }
    }

    public sealed class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static bool isPlaying { get; set; }
    }

    public sealed class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => null;
        public static Object[] LoadAllAssetsAtPath(string assetPath) => null;
        public static string[] FindAssets(string filter) => null;
        public static string GUIDToAssetPath(string guid) => null;
        public static string GetAssetPath(Object assetObject) => null;
        public static void CreateAsset(Object asset, string path) { }
        public static void SaveAssets() { }
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parentFolder, string newFolderName) => null;
    }

    public class Undo
    {
        public static void RegisterCreatedObjectUndo(Object objectToUndo, string name) { }
        public static void RecordObject(Object objectToUndo, string name) { }
        public static void IncrementCurrentGroup() { }
        public static void SetCurrentGroupName(string name) { }
        public static int GetCurrentGroup() => 0;
        public static void CollapseUndoOperations(int groupIndex) { }
    }

    public class Selection
    {
        public static Object activeObject { get; set; }
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
        public bool enabled { get; set; }
        public string path { get; set; }
    }

    public class EditorBuildSettings : Object
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }

    public sealed class EditorSceneManager : SceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default(Scene);
        public static bool SaveScene(Scene scene, string dstScenePath) => false;
        public static bool MarkSceneDirty(Scene scene) => false;
        public static bool SaveCurrentModifiedScenesIfUserWantsToSave() => false;
    }
}

namespace UnityEditor.Build
{
    public interface IOrderedCallback { int callbackOrder { get; } }

    public interface IPreprocessBuildWithReport : IOrderedCallback
    {
        void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report);
    }

    public class BuildFailedException : Exception
    {
        public BuildFailedException(string message) : base(message) { }
        public BuildFailedException(Exception innerException) : base(null, innerException) { }
    }
}

namespace UnityEditor.Build.Reporting
{
    public sealed class BuildReport : Object { }
}
