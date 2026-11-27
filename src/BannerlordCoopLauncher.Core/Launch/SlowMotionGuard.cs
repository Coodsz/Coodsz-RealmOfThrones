using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Launch;

public readonly struct SlowMotionGuardResult
{
    public bool Dismemberment { get; init; }
    public bool RtsCamera { get; init; }
}

public static class SlowMotionGuard
{
    public static string BannerlordDocuments() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");

    public static SlowMotionGuardResult Apply(IEnumerable<string> moduleRoots, string documentsRoot)
    {
        var settings = FindDismembermentSettings(moduleRoots).ToList();
        var dismemberment = false;
        foreach (var path in settings)
            dismemberment |= DisableDismemberment(path);

        if (!string.IsNullOrWhiteSpace(documentsRoot))
        {
            var saved = Path.Combine(documentsRoot, "Configs", "ModSettings", "Global", "DismembermentPlus", "DismembermentPlus.json");
            if (settings.Count > 0 || File.Exists(saved))
                dismemberment |= DisableDismemberment(saved);
            var camera = Path.Combine(documentsRoot, "Configs", "RTSCamera", "RTSCameraConfig.xml");
            var rts = DisableRtsCamera(camera);
            return new SlowMotionGuardResult { Dismemberment = dismemberment, RtsCamera = rts };
        }

        return new SlowMotionGuardResult { Dismemberment = dismemberment, RtsCamera = false };
    }

    private static IEnumerable<string> FindDismembermentSettings(IEnumerable<string> moduleRoots)
    {
        foreach (var root in moduleRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = ClientFiles.Under(root, "DismembermentPlus.json");
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }
        }
    }

    private static bool DisableDismemberment(string path)
    {
        if (!File.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent))
                return false;
            Directory.CreateDirectory(parent);
            File.WriteAllText(path, "{\r\n  \"SlowMotion\": false\r\n}\r\n");
            return true;
        }

        var text = File.ReadAllText(path);
        var updated = Regex.Replace(text, "(\"SlowMotion\"\\s*:\\s*)true\\b", "$1false");
        if (updated == text)
            return false;
        File.WriteAllText(path, updated);
        return true;
    }

    private static bool DisableRtsCamera(string path)
    {
        if (!File.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent))
                return false;
            Directory.CreateDirectory(parent);
            File.WriteAllText(path, """
                <?xml version="1.0" encoding="utf-8"?>
                <RTSCameraConfig xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <ConfigVersion>1.9</ConfigVersion>
                  <SlowMotionMode>false</SlowMotionMode>
                  <SlowMotionOnRtsView>false</SlowMotionOnRtsView>
                </RTSCameraConfig>

                """);
            return true;
        }

        var text = File.ReadAllText(path);
        var updated = SetXml(text, "SlowMotionMode");
        updated = SetXml(updated, "SlowMotionOnRtsView");
        if (updated == text)
            return false;
        File.WriteAllText(path, updated);
        return true;
    }

    private static string SetXml(string text, string element)
    {
        var pattern = "(<" + element + ">)[^<]*(</" + element + ">)";
        if (Regex.IsMatch(text, pattern))
            return Regex.Replace(text, pattern, "$1false$2");

        const string close = "</RTSCameraConfig>";
        var index = text.LastIndexOf(close, StringComparison.Ordinal);
        if (index < 0)
            return text;
        return text.Insert(index, "  <" + element + ">false</" + element + ">\r\n");
    }
}
