// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEditor.Animations;
using UnityEngine;

namespace KINEMATION.Shared.PropertyBindings.Editor
{
    public struct ComponentBinding
    {
        public Behaviour context;
        public string path;

        public ComponentBinding(Behaviour context, string path)
        {
            this.context = context;
            this.path = path;
        }
    }
    
    public struct BindableSearchData
    {
        public MonoBehaviour context;
        public Type propertyType;
        public HashSet<Type> visitedTypes;
        public FieldInfo fieldInfo;
        public List<ComponentBinding> bindings;
    }
    
    public class PropertyBindingsUtility
    {
        // Increased to 10 as requested.
        private const int MAX_SEARCH_DEPTH = 10;

        public static bool CanTraverseType(Type type, List<string> bannedNamespaces, List<Type> allowedTypes)
        {
            if (type == null) return false;

            // 1. Check Allowed Types first (Fast pass)
            foreach (var allowedType in allowedTypes) 
            {
                if (type == allowedType) return true;
            }

            // 2. Check Banned Namespaces
            if (type.Namespace != null)
            {
                foreach (var bannedNamespace in bannedNamespaces)
                {
                    if (type.Namespace.StartsWith(bannedNamespace)) return false;
                }
            }

            return true;
        }
        
        public static bool CanTraverseMember(MemberInfo memberInfo)
        {
            Type typeToCheck = null;

            if (memberInfo is FieldInfo f) typeToCheck = f.FieldType;
            else if (memberInfo is PropertyInfo p) typeToCheck = p.PropertyType;
            else if (memberInfo is MethodInfo m) typeToCheck = m.ReturnType;

            if (typeToCheck == null) return false;

            // PERFORMANCE GUARD:
            // Since we are going 10 layers deep, we MUST filter out Unity Assets.
            // We only want to traverse logic containers, not Meshes/Textures/Materials (data).
            if (typeof(UnityEngine.Object).IsAssignableFrom(typeToCheck))
            {
                bool isLogic = typeof(Component).IsAssignableFrom(typeToCheck) || 
                               typeof(GameObject).IsAssignableFrom(typeToCheck) ||
                               typeof(ScriptableObject).IsAssignableFrom(typeToCheck);
                if (!isLogic) return false;
            }

            List<string> bannedNamespaces = new List<string>()
            {
                "UnityEngine",
                "System",
                "UnityEditor",
                "FishNet.Managing", // Prevents specific deep library internals if needed
            };
            
            List<Type> allowedTypes = new List<Type>()
            {
                typeof(Transform),
                typeof(Vector4),
                typeof(Vector3),
                typeof(Vector2),
                typeof(Quaternion),
                typeof(AnimationClip),
                typeof(float),
                typeof(int),
                typeof(bool),
                typeof(double)
            };

            return CanTraverseType(typeToCheck, bannedNamespaces, allowedTypes);
        }

        public static bool IsPropertyResolved(Type contextType, Type propertyType, string propertyPath)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            string[] paths = propertyPath.Split(".");
            
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                bool foundProperty = false;

                foreach (var prop in contextType.GetProperties(flags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                    var attribute = prop.GetCustomAttribute<FormerlyBoundAsAttribute>();
                    
                    if (!prop.Name.Equals(path) && (attribute == null || !attribute.oldName.Equals(path))
                                                || (i == paths.Length - 1 && prop.PropertyType != propertyType))
                        continue;
                    
                    contextType = prop.PropertyType;
                    foundProperty = true;
                    break;
                }

                if (foundProperty) continue;

                foreach (var field in contextType.GetFields(flags))
                {
                    var attribute = field.GetCustomAttribute<FormerlyBoundAsAttribute>();

                    if (!field.Name.Equals(path) && (attribute == null || !attribute.oldName.Equals(path))
                        || (i == paths.Length - 1 && field.FieldType != propertyType))
                    {
                        continue;
                    }

                    contextType = field.FieldType;
                    foundProperty = true;
                    break;
                }

                if (foundProperty) continue;

                foreach (var method in contextType.GetMethods(flags))
                {
                    if (method.ReturnType == contextType || method.GetParameters().Length > 0
                                                         || method.ReturnType == typeof(void)
                                                         || method.IsSpecialName)
                    {
                        continue;
                    }

                    var attribute = method.GetCustomAttribute<FormerlyBoundAsAttribute>();
                    
                    if (!method.Name.Equals(path) && (attribute == null || !attribute.oldName.Equals(path))
                                                  || (i == paths.Length - 1 && method.ReturnType != propertyType))
                        continue;

                    contextType = method.ReturnType;
                    foundProperty = true;
                    break;
                }

                if (foundProperty) continue;
                return false;
            }

            return true;
        }
        
        // Added 'currentDepth' parameter with default value
        public static void SearchBindableMembers(BindableSearchData data, Type objType, string path, int currentDepth = 0)
        {
            // 1. HARD DEPTH LIMIT
            if (currentDepth > MAX_SEARCH_DEPTH) return;

            // 2. CYCLE DETECTION (Prevents immediate loops)
            if (data.visitedTypes.Contains(objType)) return;

            data.visitedTypes.Add(objType);
            
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            
            foreach (PropertyInfo prop in objType.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                string newPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                
                if (prop.PropertyType == data.propertyType)
                {
                    data.bindings.Add(new ComponentBinding(data.context, newPath));
                    continue;
                }

                if (!CanTraverseMember(prop)) continue;

                // Recurse with Depth + 1
                SearchBindableMembers(data, prop.PropertyType, newPath, currentDepth + 1);
            }
            
            foreach (FieldInfo field in objType.GetFields(flags))
            {
                if (field == data.fieldInfo) continue;
                
                string newPath = string.IsNullOrEmpty(path) ? field.Name : $"{path}.{field.Name}";

                if (field.FieldType == data.propertyType)
                {
                    data.bindings.Add(new ComponentBinding(data.context, newPath));
                    continue;
                }

                if (!CanTraverseMember(field)) continue;

                // Recurse with Depth + 1
                SearchBindableMembers(data, field.FieldType, newPath, currentDepth + 1);
            }
            
            foreach (MethodInfo method in objType.GetMethods(flags))
            {
                if (method.ReturnType == objType || method.GetParameters().Length > 0 
                                                 || method.ReturnType == typeof(void)
                                                 || method.IsSpecialName)
                {
                    continue;
                }
                
                string newPath = string.IsNullOrEmpty(path) ? method.Name : $"{path}.{method.Name}";
                if (method.ReturnType == data.propertyType)
                {
                    data.bindings.Add(new ComponentBinding(data.context, newPath));
                    continue;
                }

                if (!CanTraverseMember(method)) continue;
                
                // Recurse with Depth + 1
                SearchBindableMembers(data, method.ReturnType, newPath, currentDepth + 1);
            }

            // 3. REMOVE FROM VISITED
            // Allows traversing sibling branches (e.g., LeftHand vs RightHand)
            data.visitedTypes.Remove(objType);
        }

        public static void SearchAnimatorParameters(BindableSearchData data, Type propertyType, 
            RuntimeAnimatorController controller = null)
        {
            Animator animator = null;
            AnimatorController animatorController = controller as AnimatorController;
            if (animatorController == null)
            {
                animator = data.context.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning("Animator not found!");
                    return;
                }

                animatorController = animator.runtimeAnimatorController as AnimatorController;
                if (animatorController == null)
                {
                    if (animator.runtimeAnimatorController is not AnimatorOverrideController overrideController)
                    {
                        return;
                    }

                    animatorController = overrideController.runtimeAnimatorController as AnimatorController;
                    if (animatorController == null) return;
                }
            }
            
            foreach (var parameter in animatorController.parameters)
            {
                string paramName = $"Animator.{parameter.name}";

                if (propertyType == typeof(float) && parameter.type == AnimatorControllerParameterType.Float
                    || propertyType == typeof(int) && parameter.type == AnimatorControllerParameterType.Int
                    || propertyType == typeof(bool) && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    data.bindings.Add(new ComponentBinding(animator, paramName));
                }
            }
        }
    }
}