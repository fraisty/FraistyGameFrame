using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// UI配置注册表。
    /// 支持ScriptableObject配置组加载，也支持代码动态注册。
    /// </summary>
    public sealed class UIConfigRegistry
    {
        private readonly Dictionary<string, UIFormConfig> _configsById = new Dictionary<string, UIFormConfig>();
        private readonly Dictionary<string, List<string>> _groupToFormIds = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, string> _groupDefaultPackages = new Dictionary<string, string>();
        private readonly HashSet<string> _loadedGroups = new HashSet<string>();

        public bool IsGroupLoaded(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
                return false;
            return _loadedGroups.Contains(groupName);
        }

        public void Register(UIFormConfig config)
        {
            if (config == null)
            {
                FRLog.Error("UIConfigRegistry.Register failed, config is null.");
                return;
            }

            if (string.IsNullOrEmpty(config.formId))
            {
                FRLog.Error("UIConfigRegistry.Register failed, formId is null or empty.");
                return;
            }

            _configsById[config.formId] = config;

            string groupName = string.IsNullOrEmpty(config.configGroup) ? "Default" : config.configGroup;
            if (!_groupToFormIds.TryGetValue(groupName, out var list))
            {
                list = new List<string>();
                _groupToFormIds.Add(groupName, list);
            }

            if (!list.Contains(config.formId))
            {
                list.Add(config.formId);
            }

            FRLog.Info($"UIConfig loaded : {config.formId}, group : {groupName}");
        }

        public void RegisterGroup(UIConfigGroup group)
        {
            if (group == null)
            {
                FRLog.Error("UIConfigRegistry.RegisterGroup failed, group is null.");
                return;
            }

            string groupName = string.IsNullOrEmpty(group.groupName) ? "Default" : group.groupName;
            _loadedGroups.Add(groupName);
            if (!string.IsNullOrEmpty(group.defaultPackageName))
                _groupDefaultPackages[groupName] = group.defaultPackageName;

            if (group.forms == null)
                return;

            for (int i = 0; i < group.forms.Count; i++)
            {
                UIFormConfig config = group.forms[i];
                if (config == null)
                    continue;

                if (string.IsNullOrEmpty(config.configGroup))
                {
                    config.configGroup = groupName;
                }

                Register(config);
            }

            FRLog.Info($"UIConfig group loaded : {groupName}, count : {group.forms.Count}");
        }

        public bool TryGet(string formId, out UIFormConfig config)
        {
            return _configsById.TryGetValue(formId, out config);
        }

        public UIFormConfig Get(string formId)
        {
            _configsById.TryGetValue(formId, out var config);
            return config;
        }

        public IReadOnlyList<string> GetGroupFormIds(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
                return null;

            if (_groupToFormIds.TryGetValue(groupName, out var list))
                return list;

            return null;
        }

        public string GetDefaultPackageName(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
                return null;

            return _groupDefaultPackages.TryGetValue(groupName, out string packageName) ? packageName : null;
        }

        public void Clear()
        {
            _configsById.Clear();
            _groupToFormIds.Clear();
            _groupDefaultPackages.Clear();
            _loadedGroups.Clear();
        }
    }
}