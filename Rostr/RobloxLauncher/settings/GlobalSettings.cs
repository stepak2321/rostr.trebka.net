using System.IO;
using System.Xml.Linq;

namespace RobloxLauncher.Settings;

public static class GlobalSettings
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "GlobalBasicSettings_13.xml");

    public static bool TrySetFramerateCap(int fps, out string message)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                message = "Roblox settings file not found. Start Roblox once, then save again.";
                return false;
            }

            // Keep a one-time backup of the original file
            var backup = FilePath + ".bak";
            if (!File.Exists(backup)) File.Copy(FilePath, backup);

            var doc = XDocument.Load(FilePath);
            var el = doc.Descendants("int")
                .FirstOrDefault(e => (string?)e.Attribute("name") == "FramerateCap");

            if (el != null)
            {
                el.Value = fps.ToString();
            }
            else
            {
                var props = doc.Descendants("Item")
                    .FirstOrDefault(i => (string?)i.Attribute("class") == "UserGameSettings")
                    ?.Element("Properties");
                if (props == null)
                {
                    message = "Unexpected settings file format.";
                    return false;
                }
                props.Add(new XElement("int", new XAttribute("name", "FramerateCap"), fps));
            }

            doc.Save(FilePath);
            message = "Applied to Roblox.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Could not write Roblox settings: " + ex.Message;
            return false;
        }
    }
}