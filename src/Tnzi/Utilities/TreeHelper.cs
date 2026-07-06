
namespace Tnzi.Utilities;

/// <summary>
/// 树形数据辅助类
/// </summary>
public static class TreeHelper
{
    /// <summary>
    /// 平面数据转树形数据
    /// </summary>
    /// <typeparam name="T">树节点类型</typeparam>
    /// <typeparam name="TKey">节点ID类型</typeparam>
    /// <param name="flatData">平面数据列表</param>
    /// <param name="getId">获取节点ID的委托</param>
    /// <param name="getParentId">获取父节点ID的委托</param>
    /// <param name="getChildren">获取子节点集合的委托</param>
    /// <param name="setChildren">设置子节点集合的委托</param>
    /// <param name="rootId">根节点ID，默认为default(TKey)</param>
    /// <returns>树形数据列表</returns>
    public static IList<T> ToTree<T, TKey>(
        IEnumerable<T> flatData,
        Func<T, TKey> getId,
        Func<T, TKey> getParentId,
        Func<T, IList<T>> getChildren,
        Action<T, IList<T>> setChildren,
        TKey? rootId = default)
        where TKey : struct, IEquatable<TKey>
    {
        if (flatData == null || getId == null || getParentId == null || getChildren == null || setChildren == null)
            return new List<T>();

        var dataList = flatData.ToList();
        if (dataList.Count == 0)
            return new List<T>();

        // 初始化所有节点的Children集合
        foreach (var item in dataList)
        {
            var children = getChildren(item);
            if (children == null)
            {
                setChildren(item, new List<T>());
            }
        }

        // 创建节点字典，便于快速查找
        var nodeDict = dataList.ToDictionary(x => getId(x), x => x);

        // 构建树形结构
        var rootNodes = new List<T>();
        var defaultKey = default(TKey);
        
        foreach (var item in dataList)
        {
            var parentId = getParentId(item);
            bool isRoot = rootId.HasValue 
                ? parentId.Equals(rootId.Value) 
                : parentId.Equals(defaultKey);
                
            if (isRoot)
            {
                // 根节点
                rootNodes.Add(item);
            }
            else if (nodeDict.TryGetValue(parentId, out var parent))
            {
                // 子节点，添加到父节点的Children集合中
                var parentChildren = getChildren(parent);
                parentChildren.Add(item);
            }
        }

        return rootNodes;
    }

    /// <summary>
    /// 平面数据转树形数据（可空父键版，适配 <c>TKey? ParentId</c> + 已初始化 Children 集合的常见 DTO 形态）。
    /// ParentId 为 null 或指向数据集中不存在的节点（悬空引用）时一律视为根节点，保证节点不丢失。
    /// 输出保持输入顺序（根列表与各父节点的子列表均按 flatData 的枚举顺序）。
    /// </summary>
    /// <typeparam name="T">节点类型</typeparam>
    /// <typeparam name="TKey">键类型（值类型）</typeparam>
    /// <param name="flatData">平面数据</param>
    /// <param name="getId">获取节点ID的委托</param>
    /// <param name="getParentId">获取可空父节点ID的委托</param>
    /// <param name="addChild">把子节点挂到父节点的委托（如 <c>(p, c) =&gt; p.Children.Add(c)</c>）</param>
    /// <returns>树形数据列表</returns>
    public static IList<T> ToTree<T, TKey>(
        IEnumerable<T> flatData,
        Func<T, TKey> getId,
        Func<T, TKey?> getParentId,
        Action<T, T> addChild)
        where TKey : struct, IEquatable<TKey>
    {
        if (flatData == null || getId == null || getParentId == null || addChild == null)
            return new List<T>();

        var dataList = flatData.ToList();
        if (dataList.Count == 0)
            return new List<T>();

        var nodeDict = dataList.ToDictionary(x => getId(x), x => x);

        var rootNodes = new List<T>();
        foreach (var item in dataList)
        {
            var parentId = getParentId(item);
            if (parentId.HasValue && nodeDict.TryGetValue(parentId.Value, out var parent))
            {
                addChild(parent, item);
            }
            else
            {
                rootNodes.Add(item);
            }
        }

        return rootNodes;
    }

    /// <summary>
    /// 平面数据转树形数据（支持引用类型Key）
    /// </summary>
    public static IList<T> ToTree<T, TKey>(
        IEnumerable<T> flatData,
        Func<T, TKey> getId,
        Func<T, TKey> getParentId,
        Func<T, IList<T>> getChildren,
        Action<T, IList<T>> setChildren,
        TKey? rootId = null)
        where TKey : class, IEquatable<TKey>
    {
        if (flatData == null || getId == null || getParentId == null || getChildren == null || setChildren == null)
            return new List<T>();

        var dataList = flatData.ToList();
        if (dataList.Count == 0)
            return new List<T>();

        // 初始化所有节点的Children集合
        foreach (var item in dataList)
        {
            var children = getChildren(item);
            if (children == null)
            {
                setChildren(item, new List<T>());
            }
        }

        // 创建节点字典，便于快速查找
        var nodeDict = dataList.ToDictionary(x => getId(x), x => x);

        // 构建树形结构
        var rootNodes = new List<T>();
        foreach (var item in dataList)
        {
            var parentId = getParentId(item);
            if ((rootId == null && parentId == null) || (rootId != null && parentId != null && parentId.Equals(rootId)))
            {
                // 根节点
                rootNodes.Add(item);
            }
            else if (parentId != null && nodeDict.TryGetValue(parentId, out var parent))
            {
                // 子节点，添加到父节点的Children集合中
                var parentChildren = getChildren(parent);
                parentChildren.Add(item);
            }
        }

        return rootNodes;
    }

    /// <summary>
    /// 树形数据转平面数据（深度优先遍历）
    /// </summary>
    /// <typeparam name="T">树节点类型</typeparam>
    /// <param name="treeData">树形数据列表</param>
    /// <param name="getChildren">获取子节点集合的委托</param>
    /// <param name="createFlatNode">创建平面节点的委托</param>
    /// <returns>平面数据列表</returns>
    public static IList<T> ToFlat<T>(
        IEnumerable<T> treeData,
        Func<T, IEnumerable<T>> getChildren,
        Func<T, T> createFlatNode)
    {
        if (treeData == null || getChildren == null || createFlatNode == null)
            return new List<T>();

        var result = new List<T>();
        var stack = new Stack<T>();

        // 将所有根节点压入堆栈
        foreach (var root in treeData.Reverse())
        {
            stack.Push(root);
        }

        // 深度优先遍历
        while (stack.Count > 0)
        {
            var currentNode = stack.Pop();

            // 创建当前节点的副本（避免修改原树结构）
            var flatNode = createFlatNode(currentNode);
            result.Add(flatNode);

            // 将子节点压入堆栈（逆序压入保证遍历顺序）
            var children = getChildren(currentNode);
            if (children != null)
            {
                foreach (var child in children.Reverse())
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 用堆栈实现树的前序遍历（非递归）
    /// </summary>
    /// <typeparam name="T">树节点类型</typeparam>
    /// <param name="root">根节点</param>
    /// <param name="getChildNodes">获取节点子节点的委托（适配不同树结构）</param>
    /// <param name="processNode">处理节点的委托（遍历到节点时执行的操作）</param>
    public static void TraverseWithStack<T>(
        T root,
        Func<T, IEnumerable<T>> getChildNodes,
        Action<T> processNode)
    {
        // 边界检查：根节点为空或无处理逻辑时直接返回
        if (root == null || processNode == null || getChildNodes == null)
            return;

        // 初始化堆栈并压入根节点
        var stack = new Stack<T>();
        stack.Push(root);

        // 循环处理堆栈中的节点
        while (stack.Count > 0)
        {
            // 1. 弹出栈顶节点并处理（前序遍历：先处理根节点）
            var currentNode = stack.Pop();
            processNode(currentNode);

            // 2. 获取子节点并逆序压入堆栈（保证子节点按原顺序遍历）
            // 注意：堆栈是后进先出，所以逆序入栈才能让子节点按正序出栈
            var childNodes = getChildNodes(currentNode);
            if (childNodes == null)
            {
                continue;
            }

            foreach (var child in childNodes.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>
    /// 在平面数据中查找指定节点
    /// </summary>
    /// <typeparam name="T">节点类型</typeparam>
    /// <typeparam name="TKey">ID 类型</typeparam>
    /// <param name="flatData">平面数据列表</param>
    /// <param name="getId">获取节点 ID 的委托</param>
    /// <param name="targetId">要查找的节点 ID</param>
    /// <returns>找到的节点，不存在返回 default</returns>
    public static T? FindNode<T, TKey>(IEnumerable<T> flatData, Func<T, TKey> getId, TKey targetId)
        where TKey : notnull
    {
        Check.NotNull(flatData);
        Check.NotNull(getId);

        return flatData.FirstOrDefault(item => EqualityComparer<TKey>.Default.Equals(getId(item), targetId));
    }

    /// <summary>
    /// 获取指定节点的所有祖先节点（从直接父节点到根节点）
    /// </summary>
    /// <typeparam name="T">节点类型</typeparam>
    /// <typeparam name="TKey">ID 类型</typeparam>
    /// <param name="flatData">平面数据列表</param>
    /// <param name="getId">获取节点 ID 的委托</param>
    /// <param name="getParentId">获取父节点 ID 的委托</param>
    /// <param name="targetId">起始节点 ID</param>
    /// <returns>祖先节点列表（从直接父节点到根节点排列）</returns>
    public static IList<T> FindAncestors<T, TKey>(
        IEnumerable<T> flatData,
        Func<T, TKey> getId,
        Func<T, TKey?> getParentId,
        TKey targetId)
        where TKey : notnull
    {
        Check.NotNull(flatData);
        Check.NotNull(getId);
        Check.NotNull(getParentId);

        var dataList = flatData as IList<T> ?? flatData.ToList();
        var nodeDict = dataList.ToDictionary(x => getId(x), x => x);
        var ancestors = new List<T>();
        var visited = new HashSet<TKey>();

        var current = nodeDict.GetValueOrDefault(targetId);
        if (current == null) return ancestors;

        var parentId = getParentId(current);
        while (parentId != null && nodeDict.TryGetValue(parentId, out var parent))
        {
            if (!visited.Add(getId(parent)))
                break; // 防止循环引用

            ancestors.Add(parent);
            parentId = getParentId(parent);
        }

        return ancestors;
    }

    /// <summary>
    /// 获取指定节点的所有后代节点（深度优先）
    /// </summary>
    /// <typeparam name="T">节点类型</typeparam>
    /// <typeparam name="TKey">ID 类型</typeparam>
    /// <param name="flatData">平面数据列表</param>
    /// <param name="getId">获取节点 ID 的委托</param>
    /// <param name="getParentId">获取父节点 ID 的委托</param>
    /// <param name="targetId">起始节点 ID</param>
    /// <returns>所有后代节点列表</returns>
    public static IList<T> FindDescendants<T, TKey>(
        IEnumerable<T> flatData,
        Func<T, TKey> getId,
        Func<T, TKey?> getParentId,
        TKey targetId)
        where TKey : notnull
    {
        Check.NotNull(flatData);
        Check.NotNull(getId);
        Check.NotNull(getParentId);

        var dataList = flatData as IList<T> ?? flatData.ToList();
        var childrenMap = new Dictionary<TKey, List<T>>();

        foreach (var item in dataList)
        {
            var pid = getParentId(item);
            if (pid != null)
            {
                if (!childrenMap.TryGetValue(pid, out var children))
                {
                    children = [];
                    childrenMap[pid] = children;
                }
                children.Add(item);
            }
        }

        var descendants = new List<T>();
        var stack = new Stack<TKey>();
        stack.Push(targetId);

        while (stack.Count > 0)
        {
            var currentId = stack.Pop();
            if (!childrenMap.TryGetValue(currentId, out var childList))
                continue;

            foreach (var child in childList)
            {
                descendants.Add(child);
                stack.Push(getId(child));
            }
        }

        return descendants;
    }

    /// <summary>
    /// 获取从根节点到指定节点的完整路径
    /// </summary>
    /// <typeparam name="T">节点类型</typeparam>
    /// <typeparam name="TKey">ID 类型</typeparam>
    /// <param name="flatData">平面数据列表</param>
    /// <param name="getId">获取节点 ID 的委托</param>
    /// <param name="getParentId">获取父节点 ID 的委托</param>
    /// <param name="targetId">目标节点 ID</param>
    /// <returns>从根节点到目标节点的路径列表</returns>
    public static IList<T> FindPath<T, TKey>(
        IEnumerable<T> flatData,
        Func<T, TKey> getId,
        Func<T, TKey?> getParentId,
        TKey targetId)
        where TKey : notnull
    {
        var ancestors = FindAncestors(flatData, getId, getParentId, targetId);
        var target = FindNode(flatData, getId, targetId);

        // 反转祖先列表（从根到叶），再加上目标节点本身
        var path = new List<T>(ancestors.Count + 1);
        for (int i = ancestors.Count - 1; i >= 0; i--)
        {
            path.Add(ancestors[i]);
        }
        if (target != null)
        {
            path.Add(target);
        }

        return path;
    }
}