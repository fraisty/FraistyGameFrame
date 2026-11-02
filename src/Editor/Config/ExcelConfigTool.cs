#if UNITY_EDITOR

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using ExcelDataReader;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

//  | 注释 | 角色ID | 名字 | 稀有度 | 生命 | 攻击 | 头像地址 |
//  |---|---|---|---|---|---|---|
//  | 字段名 | id | name | rarity | hp | atk | avatarAddress |
//  | 类型 | int | string | string | int | int | string |
//  | 数据 | 1001 | 绯樱 | SSR | 1200 | 300 | icon_1001 |
//  | 数据 | 1002 | 白羽 | SR | 900 | 220 | icon_1002 |

// 第 1 行：中文注释
// 第 2 行：字段名，必须和 C# 字段名一致
// 第 3 行：字段类型，可写可不完全依赖，主要用于给策划看
// 第 4 行开始：正式数据

// 支持的字段类型
// int
// long
// float
// double
// bool
// string
// 
// int[]    	1001,1002,1003
// long[]
// float[]
// double[]
// string[] 	fire,dps,girl
// 
// List<int>
// List<long>
// List<float>
// List<double>
// List<string>
// 
// enum
// 
// 复杂对象 JSON    [{"id":1,"count":10}]

/// <summary>
/// 配置输出类型。
/// </summary>
public enum ConfigOutputType
{
    Json,
    ScriptableObject
}

/// <summary>
/// 单张配置表导出定义。
/// 用于一键导出全部配置。
/// </summary>
public class ConfigExportDefine
{
    /// <summary>
    /// Excel 文件路径。
    /// 可以是绝对路径，也可以是相对于项目根目录的路径。
    /// 例如：
    /// ../Configs/Excel/Character.xlsx
    /// D:/Project/Configs/Excel/Character.xlsx
    /// </summary>
    public string excelPath;

    /// <summary>
    /// Sheet 名。
    /// 如果为空，则默认读取第一个可用 Sheet。
    /// </summary>
    public string sheetName;

    /// <summary>
    /// 配置数据类型。
    /// 例如 typeof(CharacterConfig)
    /// </summary>
    public Type configType;

    /// <summary>
    /// ScriptableObject 容器类型。
    /// 如果只导出 JSON，可以为空。
    /// 例如 typeof(CharacterConfigAsset)
    /// </summary>
    public Type assetType;

    /// <summary>
    /// 输出类型。
    /// </summary>
    public ConfigOutputType outputType;

    /// <summary>
    /// 输出目录。
    /// JSON 可以是绝对路径或 Assets 下路径。
    /// SO 必须是 Assets 下路径。
    /// </summary>
    public string outputDir;
}

/// <summary>
/// 项目配置导出注册表。
/// 
/// 实际项目中，建议把所有配置表都注册在这里，
/// 然后使用菜单“一键导出全部配置”。
/// </summary>
public static class ConfigExportRegistry
{
    /// <summary>
    /// 项目根目录。
    /// Application.dataPath 是 xxx/Assets。
    /// 项目根目录就是 xxx。
    /// </summary>
    public static string ProjectRoot
    {
        get
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }

    /// <summary>
    /// 这里注册你的所有配置表。
    /// 示例里注册了 CharacterConfig。
    /// 
    /// 你后面可以继续加：
    /// SkillConfig
    /// ItemConfig
    /// SkinConfig
    /// GachaConfig
    /// </summary>
    public static readonly ConfigExportDefine[] Defines =
    {
        // new ConfigExportDefine
        // {
        //     excelPath = "../Configs/Excel/Character.xlsx",
        //     sheetName = "Character",
        //     configType = typeof(CharacterConfig),
        //     assetType = typeof(CharacterConfigAsset),
        //     outputType = ConfigOutputType.Json,
        //     outputDir = "Assets/Game/Configs/Json"
        // },
        
        new ConfigExportDefine
        {
            excelPath = "GameConfigs/AzurLane/AzurLane.xlsx",
            sheetName = "ShipSkinData",
            configType = typeof(ShipSkinDataConfig),
            outputType = ConfigOutputType.Json,
            outputDir = "Assets/GameRes/AzurLane/Config/GameConfig"
        },
    };
}

/// <summary>
/// 通用 Excel 配置导出器。
/// 
/// 功能：
/// 1. Excel -> List<TConfig>
/// 2. 自动字段映射
/// 3. 自动类型转换
/// 4. 自动 id 重复校验
/// 5. 调用每行配置的 Validate
/// 6. 导出 JSON
/// 7. 导出 ScriptableObject
/// </summary>
public static class ExcelConfigExporter
{
    /// <summary>
    /// 第几行是字段名。
    /// 0 表示 Excel 第一行。
    /// 当前约定：第二行是字段名。
    /// </summary>
    private const int FieldNameRow = 1;

    /// <summary>
    /// 第几行是字段类型。
    /// 当前约定：第三行是字段类型。
    /// </summary>
    private const int FieldTypeRow = 2;

    /// <summary>
    /// 第几行开始是数据。
    /// 当前约定：第四行开始是数据。
    /// </summary>
    private const int DataStartRow = 3;

    /// <summary>
    /// 导出 Excel 为 JSON。
    /// </summary>
    public static List<TConfig> ExportToJson<TConfig>(
        string excelPath,
        string sheetName,
        string outputDir
    )
        where TConfig : IConfigRow, new()
    {
        excelPath = NormalizeExcelPath(excelPath);
        outputDir = NormalizeOutputDir(outputDir);

        List<TConfig> rows = ReadExcelSheet<TConfig>(excelPath, sheetName);

        string tableName = typeof(TConfig).Name;
        ValidateRows(rows, tableName);

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string outputPath = Path.Combine(outputDir, tableName + ".json");
        outputPath = outputPath.Replace("\\", "/");

        string json = JsonConvert.SerializeObject(rows, Formatting.Indented);
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));

        AssetDatabase.Refresh();

        Debug.LogFormat("导出 JSON 成功：{0}，数量：{1}", outputPath, rows.Count);

        return rows;
    }

    /// <summary>
    /// 导出 Excel 为 ScriptableObject。
    /// 
    /// 要求：
    /// TAsset 里必须有一个 public 字段：
    /// public List<TConfig> items;
    /// </summary>
    public static TAsset ExportToScriptableObject<TConfig, TAsset>(
        string excelPath,
        string sheetName,
        string outputDir
    )
        where TConfig : IConfigRow, new()
        where TAsset : ScriptableObject
    {
        excelPath = NormalizeExcelPath(excelPath);
        outputDir = NormalizeOutputDir(outputDir);

        if (!outputDir.StartsWith("Assets"))
        {
            throw new Exception("导出 ScriptableObject 时，输出目录必须在 Assets 下，例如：Assets/Game/Configs/SO");
        }

        List<TConfig> rows = ReadExcelSheet<TConfig>(excelPath, sheetName);

        string tableName = typeof(TConfig).Name;
        ValidateRows(rows, tableName);

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string assetPath = Path.Combine(outputDir, typeof(TAsset).Name + ".asset");
        assetPath = assetPath.Replace("\\", "/");

        TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);

        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<TAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        FieldInfo itemsField = typeof(TAsset).GetField(
            "items",
            BindingFlags.Public | BindingFlags.Instance
        );

        if (itemsField == null)
        {
            throw new Exception(typeof(TAsset).Name + " 中找不到 public 字段 items");
        }

        Type requireType = typeof(List<TConfig>);

        if (itemsField.FieldType != requireType)
        {
            throw new Exception(string.Format(
                "{0}.items 类型错误。当前类型：{1}，需要类型：{2}",
                typeof(TAsset).Name,
                itemsField.FieldType.FullName,
                requireType.FullName
            ));
        }

        itemsField.SetValue(asset, rows);

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.LogFormat("导出 ScriptableObject 成功：{0}，数量：{1}", assetPath, rows.Count);

        return asset;
    }

    /// <summary>
    /// 读取 Excel 指定 Sheet，并转换成 List<TConfig>。
    /// 如果 sheetName 为空，则读取第一个不以 # 开头的 Sheet。
    /// </summary>
    public static List<TConfig> ReadExcelSheet<TConfig>(
        string excelPath,
        string sheetName
    )
        where TConfig : IConfigRow, new()
    {
        excelPath = NormalizeExcelPath(excelPath);

        if (!File.Exists(excelPath))
        {
            throw new FileNotFoundException("Excel 文件不存在：" + excelPath);
        }

        RegisterEncodingProvider();

        using (FileStream stream = File.Open(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using (IExcelDataReader reader = ExcelReaderFactory.CreateReader(stream))
            {
                do
                {
                    string currentSheetName = reader.Name;

                    if (string.IsNullOrEmpty(currentSheetName))
                    {
                        continue;
                    }

                    if (currentSheetName.StartsWith("#"))
                    {
                        continue;
                    }

                    bool match;

                    if (string.IsNullOrEmpty(sheetName))
                    {
                        match = true;
                    }
                    else
                    {
                        match = string.Equals(currentSheetName, sheetName, StringComparison.OrdinalIgnoreCase);
                    }

                    if (match)
                    {
                        return ReadCurrentSheet<TConfig>(reader, excelPath, currentSheetName);
                    }
                }
                while (reader.NextResult());
            }
        }

        throw new Exception(string.Format("Excel 中找不到 Sheet：{0}，Excel：{1}", sheetName, excelPath));
    }

    /// <summary>
    /// 读取当前 Sheet。
    /// </summary>
    private static List<TConfig> ReadCurrentSheet<TConfig>(
        IExcelDataReader reader,
        string excelPath,
        string sheetName
    )
        where TConfig : IConfigRow, new()
    {
        List<List<object>> table = new List<List<object>>();

        while (reader.Read())
        {
            List<object> row = new List<object>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.GetValue(i));
            }

            table.Add(row);
        }

        if (table.Count <= DataStartRow)
        {
            throw new Exception(string.Format("Sheet 数据行不足：{0}/{1}", excelPath, sheetName));
        }

        List<object> fieldNameRow = table[FieldNameRow];
        List<object> fieldTypeRow = table[FieldTypeRow];

        Dictionary<string, FieldInfo> fieldMap = typeof(TConfig)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(f => f.Name, f => f);

        List<TConfig> result = new List<TConfig>();

        for (int rowIndex = DataStartRow; rowIndex < table.Count; rowIndex++)
        {
            List<object> row = table[rowIndex];

            if (IsEmptyRow(row))
            {
                continue;
            }

            string firstCell = GetCellString(row, 0).Trim();

            // 如果第一列以 # 开头，整行跳过，用作注释行。
            if (firstCell.StartsWith("#"))
            {
                continue;
            }

            TConfig item = new TConfig();

            for (int colIndex = 0; colIndex < fieldNameRow.Count; colIndex++)
            {
                string fieldName = GetCellString(fieldNameRow, colIndex).Trim();
                string excelType = GetCellString(fieldTypeRow, colIndex).Trim();

                // 字段名为空，跳过该列。
                if (string.IsNullOrEmpty(fieldName))
                {
                    continue;
                }

                // 字段名以 # 开头，跳过该列。
                if (fieldName.StartsWith("#"))
                {
                    continue;
                }

                FieldInfo fieldInfo;

                if (!fieldMap.TryGetValue(fieldName, out fieldInfo))
                {
                    Debug.LogWarningFormat(
                        "Excel 字段在 C# 类型中不存在，已跳过。Excel：{0}，Sheet：{1}，字段：{2}，类型：{3}",
                        excelPath,
                        sheetName,
                        fieldName,
                        typeof(TConfig).Name
                    );

                    continue;
                }

                object rawValue = colIndex < row.Count ? row[colIndex] : null;

                try
                {
                    object value = ConvertCellValue(rawValue, fieldInfo.FieldType, excelType);
                    fieldInfo.SetValue(item, value);
                }
                catch (Exception e)
                {
                    throw new Exception(
                        string.Format(
                            "解析单元格失败：Excel：{0}，Sheet：{1}，行：{2}，列：{3}，字段：{4}，目标类型：{5}，原始值：{6}",
                            excelPath,
                            sheetName,
                            rowIndex + 1,
                            colIndex + 1,
                            fieldName,
                            fieldInfo.FieldType.Name,
                            rawValue
                        ),
                        e
                    );
                }
            }

            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// 统一校验配置。
    /// </summary>
    public static void ValidateRows<TConfig>(
        List<TConfig> rows,
        string tableName
    )
        where TConfig : IConfigRow
    {
        ConfigValidateContext context = new ConfigValidateContext(tableName);

        HashSet<int> idSet = new HashSet<int>();

        for (int i = 0; i < rows.Count; i++)
        {
            TConfig row = rows[i];

            if (row == null)
            {
                context.Error(string.Format("第 {0} 行为空", i + 1));
                continue;
            }

            int id = row.GetId();

            if (id <= 0)
            {
                context.Error(string.Format("存在非法 id：{0}", id));
            }

            if (!idSet.Add(id))
            {
                context.Error(string.Format("id 重复：{0}", id));
            }

            row.Validate(context);
        }

        if (context.HasError)
        {
            throw new Exception("配置校验失败：" + tableName);
        }

        Debug.LogFormat("配置校验通过：{0}，数量：{1}", tableName, rows.Count);
    }

    /// <summary>
    /// 单元格值转换为目标字段类型。
    /// </summary>
    private static object ConvertCellValue(
        object rawValue,
        Type targetType,
        string excelType
    )
    {
        string value = rawValue == null ? "" : rawValue.ToString().Trim();

        // Nullable<T> 处理。
        Type nullableType = Nullable.GetUnderlyingType(targetType);

        if (nullableType != null)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            targetType = nullableType;
        }

        if (targetType == typeof(string))
        {
            return value;
        }

        if (targetType == typeof(int))
        {
            return string.IsNullOrEmpty(value) ? 0 : Convert.ToInt32(Convert.ToDouble(value));
        }

        if (targetType == typeof(long))
        {
            return string.IsNullOrEmpty(value) ? 0L : Convert.ToInt64(Convert.ToDouble(value));
        }

        if (targetType == typeof(float))
        {
            return string.IsNullOrEmpty(value) ? 0f : Convert.ToSingle(value);
        }

        if (targetType == typeof(double))
        {
            return string.IsNullOrEmpty(value) ? 0d : Convert.ToDouble(value);
        }

        if (targetType == typeof(bool))
        {
            return ParseBool(value);
        }

        if (targetType == typeof(int[]))
        {
            return ParseArray(value, s => Convert.ToInt32(Convert.ToDouble(s)));
        }

        if (targetType == typeof(long[]))
        {
            return ParseArray(value, s => Convert.ToInt64(Convert.ToDouble(s)));
        }

        if (targetType == typeof(float[]))
        {
            return ParseArray(value, s => Convert.ToSingle(s));
        }

        if (targetType == typeof(double[]))
        {
            return ParseArray(value, s => Convert.ToDouble(s));
        }

        if (targetType == typeof(string[]))
        {
            return ParseArray(value, s => s);
        }

        if (targetType == typeof(List<int>))
        {
            return ParseArray(value, s => Convert.ToInt32(Convert.ToDouble(s))).ToList();
        }

        if (targetType == typeof(List<long>))
        {
            return ParseArray(value, s => Convert.ToInt64(Convert.ToDouble(s))).ToList();
        }

        if (targetType == typeof(List<float>))
        {
            return ParseArray(value, s => Convert.ToSingle(s)).ToList();
        }

        if (targetType == typeof(List<double>))
        {
            return ParseArray(value, s => Convert.ToDouble(s)).ToList();
        }

        if (targetType == typeof(List<string>))
        {
            return ParseArray(value, s => s).ToList();
        }

        if (targetType.IsEnum)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Activator.CreateInstance(targetType);
            }

            return Enum.Parse(targetType, value, true);
        }

        // 复杂对象：允许 Excel 单元格里写 JSON。
        // 例如字段类型是 SomeData，单元格里写 {"a":1,"b":"xxx"}
        if (!string.IsNullOrEmpty(value))
        {
            return JsonConvert.DeserializeObject(value, targetType);
        }

        if (targetType.IsValueType)
        {
            return Activator.CreateInstance(targetType);
        }

        return null;
    }

    /// <summary>
    /// 解析数组。
    /// 支持英文逗号、中文逗号、分号、竖线分隔。
    /// 例如：
    /// 1,2,3
    /// 1，2，3
    /// 1;2;3
    /// 1|2|3
    /// </summary>
    private static T[] ParseArray<T>(string value, Func<string, T> parser)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new T[0];
        }

        string[] parts = value.Split(
            new[] { ',', '，', ';', '；', '|', '|' },
            StringSplitOptions.RemoveEmptyEntries
        );

        T[] result = new T[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            result[i] = parser(parts[i].Trim());
        }

        return result;
    }

    /// <summary>
    /// 解析 bool。
    /// 支持：
    /// true / false
    /// 1 / 0
    /// yes / no
    /// y / n
    /// 是 / 否
    /// </summary>
    private static bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().ToLower();

        return value == "true"
               || value == "1"
               || value == "yes"
               || value == "y"
               || value == "是";
    }

    private static string GetCellString(List<object> row, int index)
    {
        if (row == null)
        {
            return "";
        }

        if (index < 0 || index >= row.Count)
        {
            return "";
        }

        return row[index] == null ? "" : row[index].ToString();
    }

    private static bool IsEmptyRow(List<object> row)
    {
        if (row == null || row.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < row.Count; i++)
        {
            object cell = row[i];

            if (cell != null && !string.IsNullOrWhiteSpace(cell.ToString()))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 标准化 Excel 路径。
    /// 支持：
    /// 1. 绝对路径
    /// 2. 相对于项目根目录的路径，例如 ../Configs/Excel/Character.xlsx
    /// </summary>
    private static string NormalizeExcelPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new Exception("Excel 路径为空");
        }

        path = path.Replace("\\", "/");

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path).Replace("\\", "/");
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, path));
        return fullPath.Replace("\\", "/");
    }

    /// <summary>
    /// 标准化输出目录。
    /// 如果是 Assets 开头，转成绝对路径给 File.WriteAllText 用。
    /// 
    /// 注意：
    /// ScriptableObject 导出需要 Assets 相对路径。
    /// 所以 SO 导出前会再次检查。
    /// </summary>
    private static string NormalizeOutputDir(string outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            throw new Exception("输出目录为空");
        }

        outputDir = outputDir.Replace("\\", "/");

        if (outputDir.StartsWith("Assets"))
        {
            return outputDir;
        }

        if (Path.IsPathRooted(outputDir))
        {
            return Path.GetFullPath(outputDir).Replace("\\", "/");
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, outputDir));
        return fullPath.Replace("\\", "/");
    }

    /// <summary>
    /// 注册编码。
    /// 
    /// CodePagesEncodingProvider 属于 System.Text.Encoding.CodePages 程序集，
    /// 有些 Unity 工程默认没有引用，所以这里用反射方式调用，避免编译报错。
    /// 
    /// 如果只读取 .xlsx，一般不需要这个。
    /// 如果需要读取老版 .xls，建议额外导入 System.Text.Encoding.CodePages.dll。
    /// </summary>
    private static void RegisterEncodingProvider()
    {
        try
        {
            Type providerType = Type.GetType(
                "System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages"
                );

            if (providerType == null)
            {
                // 找不到说明当前工程没有 System.Text.Encoding.CodePages。
                // 只读 .xlsx 时通常没关系。
                Debug.LogWarning(
                    "未找到 System.Text.Encoding.CodePages。只读取 .xlsx 通常没问题；如果要读取 .xls，请导入 System.Text.Encoding.CodePages.dll。"
                    );
                return;
            }

            PropertyInfo instanceProperty = providerType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static
                );

            if (instanceProperty == null)
            {
                Debug.LogWarning("CodePagesEncodingProvider.Instance 获取失败。");
                return;
            }

            object providerInstance = instanceProperty.GetValue(null, null);

            MethodInfo registerMethod = typeof(Encoding).GetMethod(
                "RegisterProvider",
                BindingFlags.Public | BindingFlags.Static
                );

            if (registerMethod == null)
            {
                Debug.LogWarning("Encoding.RegisterProvider 方法不存在。");
                return;
            }

            registerMethod.Invoke(null, new object[] { providerInstance });
        }
        catch (Exception e)
        {
            Debug.LogWarning("注册 CodePagesEncodingProvider 失败：" + e.Message);
        }
    }
}

/// <summary>
/// 配置导出菜单。
/// </summary>
public static class ConfigExportMenu
{
    [MenuItem("Tools/Config/打开通用配置导出窗口")]
    public static void OpenWindow()
    {
        ConfigExportWindow.Open();
    }

    [MenuItem("Tools/Config/一键导出全部配置")]
    public static void ExportAll()
    {
        int successCount = 0;
        int failCount = 0;

        foreach (ConfigExportDefine define in ConfigExportRegistry.Defines)
        {
            try
            {
                ExportByDefine(define);
                successCount++;
            }
            catch (Exception e)
            {
                failCount++;
                Debug.LogErrorFormat(
                    "导出配置失败。Excel：{0}，Sheet：{1}，类型：{2}\n{3}",
                    define.excelPath,
                    define.sheetName,
                    define.configType != null ? define.configType.Name : "null",
                    e
                );
            }
        }

        AssetDatabase.Refresh();

        Debug.LogFormat("一键导出全部配置完成。成功：{0}，失败：{1}", successCount, failCount);

        if (failCount > 0)
        {
            throw new Exception("有配置导出失败，请查看 Console");
        }
    }

    /// <summary>
    /// 根据注册表定义导出。
    /// 因为泛型类型运行时才知道，所以这里用反射调用泛型方法。
    /// </summary>
    public static void ExportByDefine(ConfigExportDefine define)
    {
        if (define == null)
        {
            throw new ArgumentNullException("define");
        }

        if (define.configType == null)
        {
            throw new Exception("configType 为空");
        }

        if (!typeof(IConfigRow).IsAssignableFrom(define.configType))
        {
            throw new Exception("配置类型必须实现 IConfigRow：" + define.configType.FullName);
        }

        if (define.outputType == ConfigOutputType.Json)
        {
            MethodInfo method = typeof(ExcelConfigExporter)
                .GetMethod("ExportToJson", BindingFlags.Public | BindingFlags.Static);

            MethodInfo genericMethod = method.MakeGenericMethod(define.configType);

            InvokeAndUnwrap(genericMethod, null, new object[]
            {
                define.excelPath,
                define.sheetName,
                define.outputDir
            });
        }
        else if (define.outputType == ConfigOutputType.ScriptableObject)
        {
            if (define.assetType == null)
            {
                throw new Exception("导出 ScriptableObject 时 assetType 不能为空");
            }

            if (!typeof(ScriptableObject).IsAssignableFrom(define.assetType))
            {
                throw new Exception("assetType 必须继承 ScriptableObject：" + define.assetType.FullName);
            }

            MethodInfo method = typeof(ExcelConfigExporter)
                .GetMethod("ExportToScriptableObject", BindingFlags.Public | BindingFlags.Static);

            MethodInfo genericMethod = method.MakeGenericMethod(define.configType, define.assetType);

            InvokeAndUnwrap(genericMethod, null, new object[]
            {
                define.excelPath,
                define.sheetName,
                define.outputDir
            });
        }
        else
        {
            throw new Exception("不支持的输出类型：" + define.outputType);
        }
    }

    private static object InvokeAndUnwrap(MethodInfo method, object obj, object[] parameters)
    {
        try
        {
            return method.Invoke(obj, parameters);
        }
        catch (TargetInvocationException e)
        {
            if (e.InnerException != null)
            {
                throw e.InnerException;
            }

            throw;
        }
    }
}

/// <summary>
/// 通用配置导出窗口。
/// 
/// 用法：
/// Tools/Config/打开通用配置导出窗口
/// 
/// 输入：
/// 1. Excel 路径
/// 2. Sheet 名
/// 3. 配置类型名，例如 CharacterConfig
/// 4. 输出类型 Json / ScriptableObject
/// 5. 输出目录
/// 6. 如果导出 SO，额外输入 SO 类型名，例如 CharacterConfigAsset
/// </summary>
public class ConfigExportWindow : EditorWindow
{
    private string _excelPath = "";
    private string _sheetName = "";
    private string _configTypeName = "CharacterConfig";
    private string _assetTypeName = "CharacterConfigAsset";
    private ConfigOutputType _outputType = ConfigOutputType.Json;
    private string _outputDir = "Assets/Game/Configs/Json";

    public static void Open()
    {
        ConfigExportWindow window = GetWindow<ConfigExportWindow>("通用配置导出");
        window.minSize = new Vector2(650, 280);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("通用 Excel 配置导出工具", EditorStyles.boldLabel);

        EditorGUILayout.Space();

        DrawExcelPath();

        _sheetName = EditorGUILayout.TextField(
            new GUIContent("Sheet 名", "如果为空，则读取第一个不以 # 开头的 Sheet"),
            _sheetName
        );

        _configTypeName = EditorGUILayout.TextField(
            new GUIContent("配置类型名", "例如 CharacterConfig"),
            _configTypeName
        );

        _outputType = (ConfigOutputType)EditorGUILayout.EnumPopup(
            new GUIContent("输出类型"),
            _outputType
        );

        if (_outputType == ConfigOutputType.ScriptableObject)
        {
            _assetTypeName = EditorGUILayout.TextField(
                new GUIContent("SO 类型名", "例如 CharacterConfigAsset"),
                _assetTypeName
            );
        }

        DrawOutputDir();

        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "Excel 表格约定：\n" +
            "第 1 行：中文注释\n" +
            "第 2 行：字段名，必须和 C# public 字段名一致\n" +
            "第 3 行：字段类型\n" +
            "第 4 行开始：正式数据\n\n" +
            "如果导出 ScriptableObject，SO 类型中必须有 public List<TConfig> items 字段。",
            MessageType.Info
        );

        EditorGUILayout.Space();

        if (GUILayout.Button("导出当前配置", GUILayout.Height(35)))
        {
            ExportCurrent();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("一键导出注册表中的全部配置", GUILayout.Height(28)))
        {
            ConfigExportMenu.ExportAll();
        }
    }

    private void DrawExcelPath()
    {
        EditorGUILayout.BeginHorizontal();

        _excelPath = EditorGUILayout.TextField(
            new GUIContent("Excel 路径"),
            _excelPath
        );

        if (GUILayout.Button("选择", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("选择 Excel 文件", "", "xlsx,xls");

            if (!string.IsNullOrEmpty(path))
            {
                _excelPath = path.Replace("\\", "/");
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawOutputDir()
    {
        EditorGUILayout.BeginHorizontal();

        _outputDir = EditorGUILayout.TextField(
            new GUIContent("输出目录"),
            _outputDir
        );

        if (GUILayout.Button("选择", GUILayout.Width(70)))
        {
            string defaultPath = Application.dataPath;

            string path = EditorUtility.OpenFolderPanel("选择输出目录", defaultPath, "");

            if (!string.IsNullOrEmpty(path))
            {
                path = path.Replace("\\", "/");

                // 如果选择的是 Assets 内目录，自动转换为 Assets/xxx 相对路径。
                if (path.StartsWith(Application.dataPath))
                {
                    _outputDir = "Assets" + path.Substring(Application.dataPath.Length);
                }
                else
                {
                    _outputDir = path;
                }
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void ExportCurrent()
    {
        try
        {
            Type configType = FindType(_configTypeName);

            if (configType == null)
            {
                Debug.LogError("找不到配置类型：" + _configTypeName);
                return;
            }

            if (!typeof(IConfigRow).IsAssignableFrom(configType))
            {
                Debug.LogError("配置类型必须实现 IConfigRow：" + configType.FullName);
                return;
            }

            ConfigExportDefine define = new ConfigExportDefine
            {
                excelPath = _excelPath,
                sheetName = _sheetName,
                configType = configType,
                outputType = _outputType,
                outputDir = _outputDir
            };

            if (_outputType == ConfigOutputType.ScriptableObject)
            {
                Type assetType = FindType(_assetTypeName);

                if (assetType == null)
                {
                    Debug.LogError("找不到 SO 类型：" + _assetTypeName);
                    return;
                }

                if (!typeof(ScriptableObject).IsAssignableFrom(assetType))
                {
                    Debug.LogError("SO 类型必须继承 ScriptableObject：" + assetType.FullName);
                    return;
                }

                define.assetType = assetType;
            }

            ConfigExportMenu.ExportByDefine(define);

            Debug.Log("当前配置导出完成");
        }
        catch (Exception e)
        {
            Debug.LogError("导出失败：\n" + e);
        }
    }

    /// <summary>
    /// 根据类型名查找类型。
    /// 支持：
    /// 1. 完整命名空间类型名
    /// 2. 简短类型名
    /// </summary>
    private static Type FindType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // 先尝试完整类型名。
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(typeName);

            if (type != null)
            {
                return type;
            }
        }

        // 再尝试短类型名。
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;

            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            for (int j = 0; j < types.Length; j++)
            {
                Type type = types[j];

                if (type != null && type.Name == typeName)
                {
                    return type;
                }
            }
        }

        return null;
    }
}

#endif