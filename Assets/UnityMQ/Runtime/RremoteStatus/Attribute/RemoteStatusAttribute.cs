// File: RemoteStatusAttribute.cs
using System;

namespace UnityMQ
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class RemoteStatusAttribute : Attribute
    {
        public string DisplayName { get; }
        public bool ReadOnly { get; }
        public string CallbackMethodName { get; }

        public RemoteStatusAttribute(string displayName, bool readOnly = false, string callbackMethodName = null)
        {
            DisplayName = displayName;
            ReadOnly = readOnly;
            CallbackMethodName = callbackMethodName;
        }
    }
}