namespace AssetStudio
{
    /// <summary>
    /// 外部 TypeTree 数据源抽象,同时充当自身插件的入口。
    /// 当 bundle 的 typetree 被剥离时(<see cref="SerializedFile"/> 中 m_EnableTypeTree == false),
    /// 通过该接口从外部数据源恢复 <see cref="TypeTree"/>。
    /// 具体加载/解析由外部插件程序集实现(例如基于 UnionTypeTreeMerger.dll 的实现)。
    /// 宿主经 <see cref="TypeTreeProviderLoader"/> 反射发现实现类(需有无参构造函数),
    /// 选定文件后调用 <see cref="Load"/> 加载,再用 <see cref="FindTypeTree"/> 查询。
    /// </summary>
    public interface ITypeTreeProvider
    {
        /// <summary>
        /// 数据源名称(如 "UnionTypeTree"),用于界面显示。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 数据源是否已加载且可用。
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 数据源中包含的类型数量(用于显示;未知时可返回 0)。
        /// </summary>
        int TypeCount { get; }

        /// <summary>
        /// 加载数据源文件。
        /// </summary>
        /// <param name="filePath">数据源文件路径。</param>
        /// <returns>识别并成功加载返回 true;无法识别该文件则返回 false。</returns>
        /// <exception cref="System.Exception">加载出错(如缺少原生库、文件损坏)时抛出带明确信息的异常。</exception>
        bool Load(string filePath);

        /// <summary>
        /// 根据类型信息查找对应的 <see cref="TypeTree"/>。
        /// </summary>
        /// <param name="serializedType">正在读取的类型(包含 classID、m_ScriptID、m_OldTypeHash 等)。</param>
        /// <returns>找到的 <see cref="TypeTree"/>;未找到时返回 null。</returns>
        TypeTree FindTypeTree(SerializedType serializedType);
    }
}
