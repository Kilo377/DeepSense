using UnityEngine;
using System.Collections.Generic; // 用于列表 (List)
using System.IO; // 用于文件读写

// -------------------------------------------
// 1. 定义与 JSON 结构匹配的 C# 类
// -------------------------------------------

// 定义 Vector2，因为 JsonUtility 不直接支持
[System.Serializable]
public class SimpleVector2
{
    public float x;
    public float y;
}

// 定义单个物体的数据结构
[System.Serializable]
public class MapObjectData
{
    public string type;
    public SimpleVector2 position;
    public SimpleVector2 scale;
}

// 定义整个 JSON 文件的根数据结构
[System.Serializable]
public class MapLayoutData
{
    public SimpleVector2 roomSize;
    public List<MapObjectData> objects;
}

// -------------------------------------------
// 2. 核心：地图生成器脚本
// -------------------------------------------
public class MapGenerator : MonoBehaviour
{
    [Header("1. JSON 文件名")]
    public string jsonFileName = "layout.json";

    [Header("2. 物体“预制件”列表")]
    // 这是关键：将 "Desk" 这样的字符串
    // 链接到您在 Unity 中制作的 2D 预制件
    public List<PrefabMapping> prefabMappings;
    
    [Header("3. 地板预制件 (Floor)")]
    public GameObject floorPrefab; // 用于地板的预制件
    public int floorSortingOrder = -10; // 确保地板在最下面

    // 用于在代码中快速查找预制件
    private Dictionary<string, GameObject> prefabDict;

    void Awake()
    {
        // 初始化字典，用于快速查找
        prefabDict = new Dictionary<string, GameObject>();
        foreach (var mapping in prefabMappings)
        {
            if (!prefabDict.ContainsKey(mapping.typeName))
            {
                prefabDict.Add(mapping.typeName, mapping.prefab);
            }
        }
    }

    void Start()
    {
        GenerateMap();
    }

    public void GenerateMap()
    {
        // 1. 构造 JSON 文件的完整路径
        // StreamingAssets 是一个特殊文件夹，用于存放原始数据文件
        string filePath = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        
        if (File.Exists(filePath))
        {
            // 2. 读取 JSON 文件的所有文本
            string jsonContent = File.ReadAllText(filePath);

            // 3. 将 JSON 文本解析为 C# 对象
            MapLayoutData layoutData = JsonUtility.FromJson<MapLayoutData>(jsonContent);

            // 4. (新!) 生成地板
            GenerateFloor(layoutData.roomSize);

            // 5. 开始循环，生成家具！
            if (layoutData.objects != null)
            {
                foreach (var objData in layoutData.objects)
                {
                    GenerateObject(objData);
                }
            }
        }
        else
        {
            Debug.LogError($"找不到地图文件: {filePath}");
        }
    }

    // (新!) 生成地板的函数 - 已修复
    private void GenerateFloor(SimpleVector2 roomSize)
    {
        if (floorPrefab == null)
        {
            Debug.LogWarning("未指定 Floor Prefab，将不生成地板。");
            return;
        }

        if (roomSize == null || (roomSize.x == 0 && roomSize.y == 0))
        {
            Debug.LogError("JSON 中的 roomSize 数据无效或为 (0,0)。请检查 layout.json 中 'roomSize' 是否使用了 'x' 和 'y'！");
            // 即使数据无效，我们还是在 (0,0) 生成一个 1x1 的地板，以便调试
            Instantiate(floorPrefab, Vector3.zero, Quaternion.identity);
            return;
        }

        // --- 仅当 prefab 和 roomSize 都有效时才运行 ---
        
        // 因为 (0,0) 是中心，所以地板的位置就是 (0,0,0)
        Vector3 floorPosition = Vector3.zero;
        GameObject floor = Instantiate(floorPrefab, floorPosition, Quaternion.identity);
        floor.name = "Generated_Floor";

        // (重要!) Tiled 模式的 Sprite Renderer 使用 'size' 属性来控制大小,
        // 而不是使用 'transform.localScale'。
        SpriteRenderer sr = floor.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (sr.drawMode == SpriteDrawMode.Tiled)
            {
                sr.size = new Vector2(roomSize.x, roomSize.y);
            }
            else
            {
                // 如果不是Tiled模式, 警告用户并回退到Scale (会导致拉伸)
                Debug.LogWarning("Floor Prefab 不是 Tiled 模式, 可能会导致纹理拉伸。请在 Floor_Prefab 的 Sprite Renderer 中设置 Draw Mode = Tiled。");
                floor.transform.localScale = new Vector3(roomSize.x, roomSize.y, 1);
            }

            // --- (这就是修复!) ---
            // 应用您在 Inspector 中设置的排序
            sr.sortingOrder = floorSortingOrder;
            // --- (修复结束) ---
        }
        else
        {
            Debug.LogError("Floor Prefab 上没有找到 SpriteRenderer 组件!");
        }
    }

    private void GenerateObject(MapObjectData objData)
    {
        // 6. 根据 "type" 字符串查找对应的预制件
        if (prefabDict.TryGetValue(objData.type, out GameObject prefabToSpawn))
        {
            // 7. 在场景中创建 (Instantiate) 物体
            // (0,0) 现在是中心，所以JSON中的坐标可以直接使用
            Vector3 position = new Vector3(objData.position.x, objData.position.y, 0);
            
            // "Instantiate" 就是“生成”
            GameObject spawnedObject = Instantiate(prefabToSpawn, position, Quaternion.identity);

            // 8. 应用 JSON 中定义的缩放 (大小)
            spawnedObject.transform.localScale = new Vector3(objData.scale.x, objData.scale.y, 1);

            // -------------------------------------------
            // (新!) 自动 Y 轴排序
            // -------------------------------------------
            // 我们根据 Y 坐标来自动设置 2D 排序
            // Y 坐标越大 (越靠上)，sortingOrder 越小 (越靠后)
            // 乘以 10 是为了给 Y 坐标相近的物体留出排序空间
            SpriteRenderer sr = spawnedObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                // position.y 范围是 -5 到 +5
                // 结果 sortingOrder 范围是 +50 到 -50
                sr.sortingOrder = -(int)(position.y * 10);
            }
            // -------------------------------------------
        }
        else
        {
            Debug.LogWarning($"未知的物体类型: {objData.type}");
        }
    }
}

// -------------------------------------------
// 3. 辅助类：用于在 Inspector 中设置预制件
// -------------------------------------------
[System.Serializable]
public class PrefabMapping
{
    public string typeName; // 对应 JSON 中的 "type"
    public GameObject prefab; // 对应您在 Unity 中创建的 2D 预制件
}