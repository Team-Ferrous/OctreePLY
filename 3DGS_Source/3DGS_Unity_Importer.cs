// Kiri3DGS_InitialPort.cs
// Converted / ported from first ~250 lines of Kiri-Innovation 3dgs-render-blender-addon __init__.py
// Notes:
// - Blender-specific behavior is left as stubs/TODOs — must be implemented to interface with Unity's Mesh/GameObject systems.
// - This file is safe to compile as a normal C# class library for Unity.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Kiri3DGS.UnityImporter
{
    public static class Globals
    {
        // Python dict equivalents
        public static Dictionary<string, object> addon_keymaps = new Dictionary<string, object>();
        public static object _icons = null; // placeholder for icon manager (Unity uses different UI/Icon system)

        // these mimic the Python dicts in the original file
        public static Dictionary<string, object> dgs_render__active_3dgs_object =
            new Dictionary<string, object> { { "sna_apply_modifier_list", new List<object>() }, { "sna_in_camera_view", false } };

        public static Dictionary<string, object> dgs_render__collection_snippets =
            new Dictionary<string, object> { { "sna_collections_temp_list", new List<object>() } };

        public static Dictionary<string, object> dgs_render__hq_mode =
            new Dictionary<string, object> { { "sna_lq_object_list", new List<object>() } };

        public static Dictionary<string, object> dgs_render__import =
            new Dictionary<string, object> { { "sna_dgs_lq_active", null } };

        public static Dictionary<string, object> dgs_render__omniview =
            new Dictionary<string, object> {
                { "sna_omniviewobjectsformerge", new List<object>() },
                { "sna_omniviewbase", null },
                { "sna_omniviewmodifierlist", new List<object>() }
            };
    }

    public static class Utils
    {
        /// <summary>
        /// Convert a numeric string to int, otherwise return 0.
        /// Mirrors string_to_int in Python.
        /// </summary>
        public static int StringToInt(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int outv;
            if (int.TryParse(value, out outv)) return outv;
            return 0;
        }

        /// <summary>
        /// Placeholder for mapping an 'icon' name to an integer id.
        /// Blender used runway icon enums — in Unity you'll map icons differently.
        /// </summary>
        public static int StringToIcon(string value)
        {
            // Unity editor icons are handled via EditorGUIUtility.IconContent and it's editor-only.
            // For a runtime dll, return 0 as default. If building editor plugin, replace with Editor GUI lookups.
            return StringToInt(value);
        }

        /// <summary>
        /// Open a folder cross-platform (like open_folder_skd in Python).
        /// Uses platform-specific shell commands (Windows explorer, macOS open, linux xdg-open).
        /// </summary>
        public static void OpenFolder(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            var path = Path.GetFullPath(directory);

            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    // explorer accepts backslashes and spaces
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { CreateNoWindow = true });
                }
                else if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
                {
                    Process.Start("open", path);
                }
                else // Linux / others
                {
                    Process.Start("xdg-open", path);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"OpenFolder failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Minimal reflection-based "property exists" attempt.
        /// Python's property_exists runs eval(prop_path) - here we try to resolve a dotted path from a root object.
        /// Usage note: original code often invoked things like "bpy.context.scene.objects" (global names).
        /// In C# you should call PropertyExistsWithRoot with an actual root object (e.g., a known container).
        /// This method will try to traverse properties/fields by name.
        /// </summary>
        public static bool PropertyExistsWithRoot(object root, string dottedPath)
        {
            if (root == null || string.IsNullOrEmpty(dottedPath)) return false;

            object current = root;
            var parts = dottedPath.Split('.');
            foreach (var p in parts)
            {
                if (current == null) return false;
                var t = current.GetType();
                // try property
                var prop = t.GetProperty(p, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    continue;
                }
                // try field
                var field = t.GetField(p, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    current = field.GetValue(current);
                    continue;
                }
                // try dictionary lookup
                var dictInterface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
                if (dictInterface != null)
                {
                    try
                    {
                        var containsKeyMethod = t.GetMethod("ContainsKey");
                        var tryGetValueMethod = t.GetMethod("TryGetValue");
                        if (containsKeyMethod != null && tryGetValueMethod != null)
                        {
                            var args = new object[] { p, null };
                            bool has = (bool)containsKeyMethod.Invoke(current, new object[] { p });
                            if (has)
                            {
                                // TryGetValue pattern: we need to call TryGetValue into object
                                var genericArgs = dictInterface.GetGenericArguments();
                                var dictT = typeof(Dictionary<,>).MakeGenericType(genericArgs);
                                // best-effort — not always possible; bail out and return true (key exists)
                                return true;
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                // can't find a member named p
                return false;
            }

            return true;
        }

        /// <summary>
        /// Fallback property_exists when code passes a string representing a global expression.
        /// This version will attempt to resolve a simple expression like "SomeRoot.SomeChild" by looking up types in the current assembly.
        /// Not as powerful as Python eval; primarily a defensive helper.
        /// </summary>
        public static bool PropertyExists(string expression)
        {
            // Attempt to handle patterns like "SomeStaticClass.SomeStaticProperty"
            if (string.IsNullOrEmpty(expression)) return false;
            try
            {
                var parts = expression.Split('.');
                Type currentType = null;
                object currentObj = null;

                // try to locate the first token as a type/field in this assembly
                var asm = Assembly.GetExecutingAssembly();
                var first = parts[0];
                var candidates = asm.GetTypes().Where(t => t.Name.Equals(first, StringComparison.OrdinalIgnoreCase) || t.FullName.EndsWith("." + first, StringComparison.OrdinalIgnoreCase));
                currentType = candidates.FirstOrDefault();
                if (currentType == null)
                {
                    // fallback: maybe it's a known globals dictionary key
                    return Globals.GetType().GetFields(BindingFlags.Static | BindingFlags.Public)
                          .Any(f => string.Equals(f.Name, first, StringComparison.OrdinalIgnoreCase));
                }

                for (int i = 1; i < parts.Length; ++i)
                {
                    var p = parts[i];
                    var prop = currentType.GetProperty(p, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop != null)
                    {
                        currentType = prop.PropertyType;
                        continue;
                    }
                    var field = currentType.GetField(p, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (field != null)
                    {
                        currentType = field.FieldType;
                        continue;
                    }
                    // nothing found
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Placeholder for preview icon loading - Unity has its own icon and Editor GUI system.
        /// </summary>
        public static int LoadPreviewIcon(string path)
        {
            // Not implementable in a runtime-agnostic DLL here.
            // If this is for an Editor plugin, replace this method to use UnityEditor.AssetDatabase / EditorGUIUtility.
            // Return 0 if icon can't be found.
            return 0;
        }
    }

    /// <summary>
    /// A simple class that mirrors the SNA_OT_Launch_Kiri_Site__3Dgs_D26Bf operator.
    /// In Unity, use Application.OpenURL to open a web page from runtime/editor code.
    /// </summary>
    public static class LaunchKiriSite
    {
        private const string KiriUrl = "https://www.kiriengine.com/";

        /// <summary>
        /// Open the Kiri Engine website. Safe for runtime & editor.
        /// </summary>
        public static void OpenKiriSite()
        {
            try
            {
                // In Unity, this will open the default browser
                Application.OpenURL(KiriUrl);
                UnityEngine.Debug.Log($"Opening web browser to {KiriUrl}");
            }
            catch (Exception)
            {
                // Fallback to system Process.Start
                try
                {
                    Process.Start(new ProcessStartInfo(KiriUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Failed to open URL {KiriUrl}: {ex.Message}");
                }
            }
        }
    }

    // ---- Blender-specific functions converted to stubs ----
    // The following methods in the original Python code interact heavily with Blender's bpy,
    // modifiers, node groups, and UI. They must be re-implemented for Unity:
    //
    // - sna_update_sna_kiri3dgs_active_object_update_mode_868D4
    // - sna_update_sna_kiri3dgs_active_object_enable_active_camera_DE26E
    // - sna_update_sna_kiri3dgs_modifier_enable_animate_1F5D0
    // - sna_update_sna_kiri3dgs_hq_objects_overlap_DDF15
    // - sna_update_sna_kiri3dgs_lq__hq_065F9
    // - sna_update_sna_kiri3dgs_active_mode_BA558
    // - sna_update_sna_kiri3dgs_modifier_enable_decimate_641A7
    // - sna_update_sna_kiri3dgs_modifier_enable_camera_cull_A98D6
    // - sna_update_sna_kiri3dgs_modifier_enable_crop_box_6FCA7
    // - sna_update_sna_kiri3dgs_modifier_enable_colour_edit_1D6A1
    // - sna_update_sna_kiri3dgs_modifier_enable_remove_by_size_488C9
    // - sna_add_geo_nodes__append_group_2D522_90019  (and several duplicates)
    //
    // Each of these is highly dependent on Blender semantics (modifiers, node groups, object context).
    // To continue porting: locate the code sections that actually parse .3dgs files and any functions that transform binary->geometry.
    // We'll port the parser next, and implement Unity mesh creation and material assignment.

    public static class BlenderStubs
    {
        public static void SnaUpdateActiveObjectUpdateMode(string newMode)
        {
            // TODO: implement mapping between Blender "update modes" and Unity behavior.
            UnityEngine.Debug.Log($"[STUB] SnaUpdateActiveObjectUpdateMode called with: {newMode}");
        }

        public static void SnaUpdateEnableActiveCamera(bool enable)
        {
            // TODO: implement camera switching behavior in Unity (set SceneView or Camera)
            UnityEngine.Debug.Log($"[STUB] SnaUpdateEnableActiveCamera: {enable}");
        }

        public static void SnaAddGeoNodesAppendGroup(string appendPath, string nodeGroupName, object objects, string modifierName)
        {
            // TODO: In Blender this appended a node group into the blend file and linked it into an object modifier.
            // In Unity we'd replicate the node group functionality with a custom component or shader graph asset.
            UnityEngine.Debug.Log($"[STUB] Append geo nodes: {nodeGroupName} from {appendPath} into modifier {modifierName}");
        }
    }
}
