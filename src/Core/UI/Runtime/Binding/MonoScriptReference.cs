using System;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 运行时可序列化脚本引用信息。
    /// 编辑器中写入脚本类名与命名空间。
    /// </summary>
    [Serializable]
    public sealed class MonoScriptReference
    {
        public string className;
        public string namespaceName;
        public string assemblyName;

        public string FullTypeName
        {
            get
            {
                if (string.IsNullOrEmpty(namespaceName))
                    return className;
                return namespaceName + "." + className;
            }
        }
    }
}
