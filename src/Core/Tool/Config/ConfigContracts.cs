using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 所有配置行建议实现这个接口。
/// 
/// 好处：
/// 1. 可以统一获取 id
/// 2. 可以统一做基础校验
/// 3. 每张表可以写自己的 Validate 逻辑
/// </summary>
public interface IConfigRow
{
    /// <summary>
    /// 返回配置 id。
    /// 一般每张配置表都应该有唯一 id。
    /// </summary>
    int GetId();

    /// <summary>
    /// 自定义校验逻辑。
    /// 例如：名字不能为空、数值不能小于 0、资源地址不能为空等。
    /// </summary>
    void Validate(ConfigValidateContext context);
}

/// <summary>
/// 配置校验上下文。
/// 用于收集错误和警告。
/// </summary>
public class ConfigValidateContext
{
    public string TableName { get; private set; }

    private readonly List<string> _errors = new List<string>();
    private readonly List<string> _warnings = new List<string>();

    public bool HasError
    {
        get { return _errors.Count > 0; }
    }

    public IReadOnlyList<string> Errors
    {
        get { return _errors; }
    }

    public IReadOnlyList<string> Warnings
    {
        get { return _warnings; }
    }

    public ConfigValidateContext(string tableName)
    {
        TableName = tableName;
    }

    public void Error(string message)
    {
        string fullMessage = string.Format("[{0}] {1}", TableName, message);
        _errors.Add(fullMessage);
        Debug.LogError(fullMessage);
    }

    public void Warning(string message)
    {
        string fullMessage = string.Format("[{0}] {1}", TableName, message);
        _warnings.Add(fullMessage);
        Debug.LogWarning(fullMessage);
    }
}
