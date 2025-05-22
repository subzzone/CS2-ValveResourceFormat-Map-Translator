using Newtonsoft.Json;
using SteamDatabase.ValvePak;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using static ValveResourceFormat.ResourceTypes.EntityLump;
using GUI.Utils;
using System.Xml;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace ValveResourceFormat.Utils;
public static class MapTranslator
{
    public static void ExportTextToJson(string directoryPath, string lang)
    {
        if (!Directory.Exists(directoryPath))
        {
            Log.Debug("EXPORT_TEXT_TO_JSON", $"Folder not found: {directoryPath}");
            return;
        }

        Log.Debug("EXPORT_TEXT_TO_JSON", "-------------------- [ Starting map text export ] --------------------");

        foreach (string file in Directory.GetFiles(directoryPath, "*.vpk"))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) || !name.Contains('_'))
            {
                ExtractMapTexts(file, directoryPath, lang);
            }
        }
    }
    private static void ExtractMapTexts(string vpkFilePath, string rootDir, string lang)
    {
#pragma warning disable CA2000
        using var package = new Package();
#pragma warning restore CA2000

        package.Read(vpkFilePath);

        var targetNames = new List<string>();
        var extractedTexts = new List<Dictionary<string, object>>();

        TraversePackage(package, (entityLump) =>
        {
            var names = GetPointServerCommandTargetNames(entityLump);
            targetNames.AddRange(names);
        });

        if (targetNames.Count == 0)
        {
            Log.Debug("EXPORT_TEXT_TO_JSON", $"No entities found in {Path.GetFileName(vpkFilePath)}");
            return;
        }

        TraversePackage(package, (entityLump) =>
        {
            foreach (var name in targetNames)
            {
                var foundTexts = GetSayCommandTexts(entityLump, name, lang);
                extractedTexts.AddRange(foundTexts);
            }
        });

        if (targetNames.Count == 0)
        {
            string jsonDirectory = Path.Combine(rootDir, "json");
            Directory.CreateDirectory(jsonDirectory);

            string jsonFilePath = Path.Combine(jsonDirectory, $"{Path.GetFileNameWithoutExtension(vpkFilePath)}_text.json");
            File.WriteAllText(jsonFilePath, JsonConvert.SerializeObject(new { modify = extractedTexts }, Newtonsoft.Json.Formatting.Indented));

            Log.Debug("EXPORT_TEXT_TO_JSON", $"Saved: {jsonFilePath}");
        }
        else
        {
            Log.Debug("EXPORT_TEXT_TO_JSON", $"No text: {Path.GetFileName(vpkFilePath)}");
        }
    }
    private static void TraversePackage(Package package, Action<EntityLump> entityCallback)
    {
        foreach (var entryGroup in package.Entries)
        {
            if (entryGroup.Key is not ("vpk" or "vents_c")) continue;

            foreach (var entry in entryGroup.Value)
            {
                if (entry.FileName.Contains("skybox", StringComparison.OrdinalIgnoreCase)) continue;

                if (entry.Length == 0)
                {
                    Log.Debug("EXPORT_TEXT_TO_JSON", $"Skipping empty: {entry.FileName}");
                    continue;
                }

                package.ReadEntry(entry, out var output);

                using var stream = new MemoryStream(output);
#pragma warning disable CA2000
                using var nested = new Package();
#pragma warning restore CA2000

                if (entryGroup.Key == "vpk")
                {
                    nested.SetFileName(entry.FileName);
                    nested.Read(stream);

                    TraversePackage(nested, entityCallback);
                }
                else
                {
                    using var resource = new Resource();
                    resource.Read(stream);

                    if (resource.ResourceType == ResourceType.EntityLump)
                    {

                        if (resource.DataBlock is EntityLump lump)
                        {
                            entityCallback(lump);
                        }
                    }
                }
            }
        }
    }
    private static List<string> GetPointServerCommandTargetNames(EntityLump lump)
    {
        return lump.GetEntities()
                   .Where(e => e.GetProperty<string>("classname") == "point_servercommand")
                   .Select(e => e.GetProperty<string>("targetname"))
                   .Where(name => name != null)
                   .ToList();
    }
    private static List<Dictionary<string, object>> GetSayCommandTexts(EntityLump lump, string targetName, string lang)
    {
        var result = new List<Dictionary<string, object>>();

        foreach (var entity in lump.GetEntities())
        {
            if (entity.Connections == null) continue;

            foreach (var conn in entity.Connections)
            {
                if (conn == null) continue;

                var target = conn.GetProperty<string>("m_targetName") ?? "";
                var input = conn.GetProperty<string>("m_inputName") ?? "";
                var param = conn.GetProperty<string>("m_overrideParam") ?? "";


                if (target == targetName && input == "Command" && param?.StartsWith("say") == true)
                {
                    result.Add(new Dictionary<string, object>
                    {
                        ["match"] = new
                        {
                            io = new[] { new { overrideparam = param } }
                        },
                        ["replace"] = new
                        {
                            io = new { overrideparam = "" }
                        }
                    });
                }
            }
        }

        return result;
    }
    public static void ExportSingleMap(string vpkFilePath, string rootDir, string lang, Action<string> progressCallback)
    {
        using var package = new Package();
        package.Read(vpkFilePath);

        var targetNames = new List<string>();
        var extractedTexts = new List<Dictionary<string, object>>();

        progressCallback?.Invoke($"Reading entities in {Path.GetFileName(vpkFilePath)}...");

        TraversePackage(package, (entityLump) =>
        {
            var names = GetPointServerCommandTargetNames(entityLump);
            targetNames.AddRange(names);
        });

        if (targetNames.Count == 0)
        {
            progressCallback?.Invoke($"No entities found in {Path.GetFileName(vpkFilePath)}");
            return;
        }

        TraversePackage(package, (entityLump) =>
        {
            foreach (var name in targetNames)
            {
                var foundTexts = GetSayCommandTexts(entityLump, name, lang);
                extractedTexts.AddRange(foundTexts);
            }
        });

        if (extractedTexts.Count != 0)
        {
            var result = new { modify = extractedTexts };
            string mapName = Path.GetFileNameWithoutExtension(vpkFilePath);
            string jsonDirectory = Path.Combine(rootDir, "json");
            Directory.CreateDirectory(jsonDirectory);

            string jsonFilePath = Path.Combine(jsonDirectory, $"{mapName}_text.json");
            File.WriteAllText(jsonFilePath, JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));

            progressCallback?.Invoke($"✔ Saved: {jsonFilePath}");
        }
        else
        {
            progressCallback?.Invoke($"No text found to export in: {Path.GetFileName(vpkFilePath)}");
        }
    }
}
