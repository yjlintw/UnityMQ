// File: CommandHandlerGenerator.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Plastic.Newtonsoft.Json;
using UnityEngine;
using UnityMQ.Constants;

namespace UnityMQ
{
    public class CommandHandlerGenerator
    {
        private readonly Action<string, Action<Dictionary<string, object>>> _registerHandler;

        public CommandHandlerGenerator(Action<string, Action<Dictionary<string, object>>> registerHandler)
        {
            this._registerHandler = registerHandler;
        }

        // Method to generate handlers for fields in the current GameObject and its children
        public void GenerateHandlersForHierarchy(GameObject rootObject, string guid)
        {
            // Get all MonoBehaviour components in the root object and its children
            MonoBehaviour[] monoBehaviours = rootObject.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var monoBehaviour in monoBehaviours)
            {
                GenerateHandlersForFields(monoBehaviour, rootObject, guid);
            }
        }

        private void GenerateHandlersForFields(object target, GameObject rootObject, string guid)
        {
            var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<RemoteStatusAttribute>();
                if (attr is { ReadOnly: false })
                {
                    string topic = $"{TopicConfig.CommandBase}/{guid}/{rootObject.GetInstanceID()}/{attr.DisplayName}";
                    Action<Dictionary<string, object>> handler = CreateHandler(field, target, attr);
                    _registerHandler(topic, handler);
                }
            }
        }

        private Action<Dictionary<string, object>> CreateHandler(FieldInfo field, object target, RemoteStatusAttribute attr)
        {
            Debug.Log("update value");
            return (values) =>
            {
                if (values.TryGetValue(attr.DisplayName, out object value))
                {
                    try
                    {
                        // Update field value
                        UpdateFieldValue(field, target, value);
                        
                        // Invoke the callback if specified
                        if (!string.IsNullOrEmpty(attr.CallbackMethodName))
                        {
                            MethodInfo callbackMethod = target.GetType().GetMethod(attr.CallbackMethodName,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (callbackMethod != null)
                            {
                                callbackMethod.Invoke(target, new object[] { values });
                            }
                            else
                            {
                                Debug.LogWarning("Method not found: " + attr.CallbackMethodName);
                            }
                        }
                        Debug.Log($"{attr.DisplayName} updated to: {field.GetValue(target)}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error updating field {attr.DisplayName}: {ex.Message}");
                    }
                }
            };
        }

        private void UpdateFieldValue(FieldInfo field, object target, object value)
        {
            Debug.Log($"Incoming  Field: {field.FieldType.Name}");
            if (field.FieldType == typeof(int) && int.TryParse(value.ToString(), out int intValue))
            {
                field.SetValue(target, intValue);
            }
            else if (field.FieldType == typeof(float) && float.TryParse(value.ToString(), out float floatValue))
            {
                field.SetValue(target, floatValue);
            }
            else if (field.FieldType == typeof(bool) && bool.TryParse(value.ToString(), out bool boolValue))
            {
                field.SetValue(target, boolValue);
            }
            else if (field.FieldType == typeof(string))
            {
                field.SetValue(target, value.ToString());
            }
            else if (field.FieldType == typeof(Vector3))
            {
                Vector3 vectorValue = JsonConvert.DeserializeObject<Vector3>(
                    JsonConvert.SerializeObject(value), new Vector3Converter());
                field.SetValue(target, vectorValue);
            }
            else if (field.FieldType == typeof(Color))
            {
                Color colorValue = JsonConvert.DeserializeObject<Color>(
                    JsonConvert.SerializeObject(value), new ColorConverter());
                field.SetValue(target, colorValue);
            }
            else if (field.FieldType == typeof(Quaternion))
            {
                Quaternion quaternionValue = JsonConvert.DeserializeObject<Quaternion>(
                    JsonConvert.SerializeObject(value), new QuaternionConverter());
                field.SetValue(target, quaternionValue);
            }
            else if (field.FieldType == typeof(Vector2))
            {
                Vector2 vector2Value = JsonConvert.DeserializeObject<Vector2>(
                    JsonConvert.SerializeObject(value), new Vector2Converter());
                field.SetValue(target, vector2Value);
            }
            else
            {
                Debug.LogWarning($"Unsupported type or parsing failed for {field.Name}.");
            }

        }
    }
}