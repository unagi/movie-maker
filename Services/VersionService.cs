using System.Reflection;

namespace MovieMaker.Services;

public static class VersionService
{
    public static string DisplayVersion
    {
        get
        {
#if DEBUG
            return string.Empty;
#else
            var assembly = Assembly.GetExecutingAssembly();
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                return $"Version {info}";
            }

            var version = assembly.GetName().Version;
            return version == null ? string.Empty : $"Version {version}";
#endif
        }
    }
}
