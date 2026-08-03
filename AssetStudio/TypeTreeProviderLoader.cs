using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace AssetStudio
{
    /// <summary>
    /// TypeTree 数据源插件加载器。
    /// 在指定目录(通常为 exe 旁的 Plugins 子目录)中扫描程序集,反射发现其中的
    /// <see cref="ITypeTreeProvider"/> 实现类(需有无参构造函数)并注册。
    /// 宿主只依赖抽象接口,无需在编译期引用任何具体插件。
    /// </summary>
    public static class TypeTreeProviderLoader
    {
        private static readonly List<Type> _providerTypes = new List<Type>();
        private static readonly List<string> _pluginDirs = new List<string>();
        private static bool _resolveHooked;

        /// <summary>
        /// 已发现的 TypeTree 数据源插件数量。
        /// </summary>
        public static int ProviderCount => _providerTypes.Count;

        /// <summary>
        /// 递归扫描目录下(含各插件子目录)的所有 *.dll,注册其中的 <see cref="ITypeTreeProvider"/> 实现类。
        /// 每个插件通常占一个子目录,与其依赖(含原生库)放在一起。重复调用会先清空已注册的类型。
        /// </summary>
        /// <param name="directory">插件目录(不存在则直接返回 0)。</param>
        /// <returns>本次发现的插件数量。</returns>
        public static int LoadFromDirectory(string directory)
        {
            _providerTypes.Clear();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return 0;

            HookAssemblyResolve();
            if (!_pluginDirs.Contains(directory))
                _pluginDirs.Add(directory);
            // 把每个插件子目录也纳入依赖解析范围(插件的托管依赖通常与它同目录)
            foreach (var subDir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                if (!_pluginDirs.Contains(subDir))
                    _pluginDirs.Add(subDir);
            }

            foreach (var dll in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFrom(dll);
                }
                catch (BadImageFormatException)
                {
                    continue; // 原生 DLL,跳过
                }
                catch (Exception e)
                {
                    Logger.Warning($"Failed to load typetree plugin {Path.GetFileName(dll)}: {e.Message}");
                    continue;
                }
                RegisterProviders(assembly);
            }
            return _providerTypes.Count;
        }

        private static void RegisterProviders(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (!typeof(ITypeTreeProvider).IsAssignableFrom(type))
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Logger.Warning($"Typetree plugin {type.FullName} needs a public parameterless constructor, skipped.");
                    continue;
                }

                _providerTypes.Add(type);
                try
                {
                    if (Activator.CreateInstance(type) is ITypeTreeProvider probe)
                    {
                        (probe as IDisposable)?.Dispose();
                        Logger.Info($"Discovered typetree plugin: {probe.Name} ({assembly.GetName().Name})");
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning($"Failed to probe typetree plugin {type.FullName}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 依次用已发现的插件尝试加载数据源文件。
        /// </summary>
        /// <returns>加载到的 <see cref="ITypeTreeProvider"/>;没有插件能处理时返回 null。</returns>
        public static ITypeTreeProvider Open(string filePath)
        {
            foreach (var type in _providerTypes)
            {
                ITypeTreeProvider provider;
                try
                {
                    provider = Activator.CreateInstance(type) as ITypeTreeProvider;
                }
                catch (Exception e)
                {
                    Logger.Warning($"Failed to instantiate typetree plugin {type.FullName}: {e.Message}");
                    continue;
                }
                if (provider == null)
                    continue;

                try
                {
                    if (provider.Load(filePath))
                        return provider;
                }
                catch
                {
                    (provider as IDisposable)?.Dispose();
                    throw;
                }
                (provider as IDisposable)?.Dispose();
            }
            return null;
        }

        // 插件经 Assembly.LoadFrom 加载后,其依赖(如 AssetStudio.PInvoke)在 .NET Core/.NET 5+
        // 下不会自动从应用根目录解析,这里挂钩从插件目录与应用根目录解析插件依赖。
        private static void HookAssemblyResolve()
        {
            if (_resolveHooked)
                return;
            _resolveHooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolvePluginDependency;
        }

        private static Assembly OnResolvePluginDependency(object sender, ResolveEventArgs args)
        {
            var dllName = new AssemblyName(args.Name).Name + ".dll";
            foreach (var dir in _pluginDirs)
            {
                var candidate = Path.Combine(dir, dllName);
                if (File.Exists(candidate))
                {
                    try { return Assembly.LoadFrom(candidate); }
                    catch { /* ignore and continue */ }
                }
            }
            var baseCandidate = Path.Combine(AppContext.BaseDirectory, dllName);
            if (File.Exists(baseCandidate))
            {
                try { return Assembly.LoadFrom(baseCandidate); }
                catch { /* ignore */ }
            }
            return null;
        }
    }
}
