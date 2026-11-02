using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 示例：角色配置 ScriptableObject 容器。
/// 
/// 通用导出器默认会找名为 items 的 public 字段。
/// 类型必须是 List<你的配置类型>。
/// </summary>
public class CharacterConfigAsset : ScriptableObject
{
    public List<CharacterConfig> items = new List<CharacterConfig>();
}
