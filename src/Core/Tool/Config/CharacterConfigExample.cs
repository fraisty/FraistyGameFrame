using System;
using UnityEngine;

/// <summary>
/// 示例：角色配置表。
/// 
/// Excel 字段名需要和这里的 public 字段名一致。
/// </summary>
[Serializable]
public class CharacterConfig : IConfigRow
{
    public int id;
    public string name;
    public string rarity;
    public int hp;
    public int atk;
    public string avatarAddress;

    public int GetId()
    {
        return id;
    }

    public void Validate(ConfigValidateContext context)
    {
        if (id <= 0)
        {
            context.Error("角色 id 必须大于 0");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            context.Error(string.Format("角色 {0} 的 name 为空", id));
        }

        if (string.IsNullOrWhiteSpace(rarity))
        {
            context.Error(string.Format("角色 {0} 的 rarity 为空", id));
        }

        if (hp <= 0)
        {
            context.Error(string.Format("角色 {0} 的 hp 必须大于 0", id));
        }

        if (atk < 0)
        {
            context.Error(string.Format("角色 {0} 的 atk 不能小于 0", id));
        }

        if (string.IsNullOrWhiteSpace(avatarAddress))
        {
            context.Warning(string.Format("角色 {0} 的 avatarAddress 为空", id));
        }
    }
}
