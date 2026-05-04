using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class GamaSceneUtility
{
#if UNITY_EDITOR
    private static readonly HashSet<string> MissingTagsLogged = new HashSet<string>();
#endif

    public static GameObject FindGameObjectWithTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        try
        {
            return GameObject.FindGameObjectWithTag(tag);
        }
        catch (UnityException)
        {
            return null;
        }
    }

    public static GameObject[] FindGameObjectsWithTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return new GameObject[0];
        }

        try
        {
            return GameObject.FindGameObjectsWithTag(tag);
        }
        catch (UnityException)
        {
            return new GameObject[0];
        }
    }

    public static bool TrySetTag(GameObject gameObject, string tag)
    {
        if (gameObject == null || string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

#if UNITY_EDITOR
        if (!IsDefinedTag(tag))
        {
            if (!EnsureTag(tag))
            {
                if (MissingTagsLogged.Add(tag))
                {
                    Debug.LogWarning("[GAMA] Tag '" + tag + "' is not defined in TagManager and could not be created automatically.");
                }

                return false;
            }
        }
#endif

        try
        {
            // Even if IsDefinedTag returns true (or we're not in editor), 
            // the tag might still be missing at runtime.
            gameObject.tag = tag;
            return true;
        }
        catch (Exception)
        {
            // Unity logs an error internally when set_tag fails, 
            // so we try to avoid this by using IsDefinedTag above.
            return false;
        }
    }

    public static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    public static Component GetOrAddComponent(GameObject gameObject, Type componentType)
    {
        if (gameObject == null || componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            return null;
        }

        Component component = gameObject.GetComponent(componentType);
        if (component != null)
        {
            return component;
        }

        try
        {
            return gameObject.AddComponent(componentType);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GAMA] Could not add component " + componentType.FullName + " to " + gameObject.name + ": " + ex.Message);
            return null;
        }
    }

    public static Component AddOptionalComponent(GameObject gameObject, params string[] typeNames)
    {
        foreach (string typeName in typeNames)
        {
            Type type = FindType(typeName);
            Component component = GetOrAddComponent(gameObject, type);
            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    public static GameObject GetOrCreateChild(GameObject parent, string childName)
    {
        Transform existing = parent.transform.Find(childName);
        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject child = new GameObject(childName);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static Type FindType(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        Type type = Type.GetType(fullName);
        if (type != null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    private static bool EnsureTag(string tag)
    {
        if (IsDefinedTag(tag))
        {
            return true;
        }

        try
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                return false;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");
            if (tags == null || !tags.isArray)
            {
                return false;
            }

            for (int i = 0; i < tags.arraySize; i++)
            {
                SerializedProperty tagProperty = tags.GetArrayElementAtIndex(i);
                if (tagProperty.stringValue == tag)
                {
                    return true;
                }
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();

            if (MissingTagsLogged.Add(tag))
            {
                Debug.Log("[GAMA] Created missing Unity tag '" + tag + "' from GAMA data.");
            }

            return true;
        }
        catch (Exception ex)
        {
            if (MissingTagsLogged.Add(tag))
            {
                Debug.LogWarning("[GAMA] Could not create Unity tag '" + tag + "': " + ex.Message);
            }

            return false;
        }
    }

    private static bool IsDefinedTag(string tag)
    {
        // Built-in tags that are always present
        if (tag == "Untagged" || tag == "Respawn" || tag == "Finish" || tag == "EditorOnly" || 
            tag == "MainCamera" || tag == "Player" || tag == "GameController")
        {
            return true;
        }

        try
        {
            Type internalEditorUtilityType = Type.GetType("UnityEditorInternal.InternalEditorUtility, UnityEditor");
            if (internalEditorUtilityType == null)
            {
                // Fallback to searching all assemblies if direct Type.GetType fails
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "UnityEditor")
                    {
                        internalEditorUtilityType = assembly.GetType("UnityEditorInternal.InternalEditorUtility");
                        if (internalEditorUtilityType != null) break;
                    }
                }
            }

            if (internalEditorUtilityType == null)
            {
                // If we're in the editor but can't find InternalEditorUtility, 
                // we'll assume it's NOT defined to be safe.
                return false;
            }

            var tagsProperty = internalEditorUtilityType.GetProperty("tags", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (tagsProperty == null) return false;

            string[] tags = tagsProperty.GetValue(null, null) as string[];
            if (tags == null) return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == tag)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
#endif
}
