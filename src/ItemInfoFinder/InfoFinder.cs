﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace ItemInfoFinder
{
    public class InfoFinder
    {
        private static readonly Dictionary<string, MainType> _typeMap;

        static InfoFinder()
        {
            var types = new MainType[]
            {
                 new MainType("Ore", 1, "OreType", false, true),
                 new MainType("Ingot", 2, "IngotType", false, true),
                 new MainType("Component", 3, "ComponentType", true, true),
                 new MainType("AmmoMagazine", 4, "AmmoType", true, true),
                 new MainType("PhysicalGunObject", 5, "GunType", true, false),
                 new MainType("OxygenContainerObject", 6, "OxygenType", true, false),
                 new MainType("GasContainerObject", 7, "GasType", true, false),
                 new MainType("ConsumableItem", 8, "Consumable", true, true),
                 new MainType("Datapad", 9, "Datapad", true, false),
                 new MainType("Package", 10, "Package", true, false),
                 new MainType("PhysicalObject", 11, "Physical", true, true),
            };

            _typeMap = types.ToDictionary(q => q.Name);
        }

        public InfoFinder()
        {
            Result = new List<ItemInfo>();
            ModIds = new List<long>();
        }

        public List<long> ModIds { get; set; }
        public WorkshopItemInfo Mods { get; set; }
        public List<ItemInfo> Result { get; set; }

        public void DownloadModData()
        {
            if (ModIds.Count == 0)
                return;

            Mods = WorkshopItemInfo.GetWorkshopItemInfo(ModIds);
        }

        public void FindItemsInFiles(string path, string searchPattern)
        {
            foreach (var dt in Directory.GetFiles(path, searchPattern))
            {
                ProcessFile(File.OpenRead(dt), 0);
            }
        }

        public void FindItemsInZipFiles(string path, string searchPattern, SearchOption searchOption, string innerPath, string dataFileExtension)
        {
            foreach (string dt in Directory.GetFiles(path, searchPattern, searchOption))
            {
                if (!dt.EndsWith(searchPattern.Substring(1)))
                    continue;

                var addedAnything = false;
                try
                {
                    var file = new FileInfo(dt);
                    var dotPos = dt.IndexOfAny(new char[] { Path.DirectorySeparatorChar, '.' }, path.Length + 1);
                    if (dotPos < 1)
                        continue;
                    var name = dt.Substring(path.Length + 1, dotPos - path.Length - 1);
                    long modId;
                    if (Int64.TryParse(name, out modId))
                    {
                        var archive = ZipFile.OpenRead(dt);
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.FullName.StartsWith(innerPath, StringComparison.InvariantCultureIgnoreCase) && entry.FullName.EndsWith(dataFileExtension))
                            {
                                if (ProcessFile(entry.Open(), modId))
                                    addedAnything = true;
                            }
                        }

                        if (addedAnything)
                            ModIds.Add(modId);
                    }
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }

        public string GetOutputText()
        {
            if (Result.Count == 0)
                return "Nothing";

            Result.Sort(Comparision);
            var sb = new StringBuilder();

            var modMap = new Dictionary<long, string>();
            var modUsed = new HashSet<long>();
            if (Mods != null)
            {
                Mods.Data.Sort((a, b) => String.Compare(a.Title, b.Title, true));
                foreach (var dt in Mods.Data)
                    modMap.Add(dt.PublishedFileId, dt.Title);
            }
            modMap.Add(0, "Space Engineers");

            var prev = new ItemInfo(-1, null, null, null, null);
            foreach (var dt in Result)
            {
                if (dt.TypeId != prev.TypeId)
                {
                    sb.AppendLine();
                }
                else if (dt.SubtypeId == prev.SubtypeId)
                {
                    if (dt.Mass == prev.Mass && dt.Volume == prev.Volume)
                        continue;
                    sb.Append("duplicate: ");
                }
                prev = dt;

                string modTitle;
                if (modMap.TryGetValue(dt.ModId, out modTitle) && !String.IsNullOrEmpty(modTitle))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "ret.Add({0}, \"{1}\", {2}M, {3}M, {4}, {5}); // {6}",
                        dt.TypeId.Alias, dt.SubtypeId, dt.Mass, dt.Volume, dt.HasIntegralAmounts ? "true" : "false", dt.IsStackable ? "true" : "false", modTitle);
                    modUsed.Add(dt.ModId);
                }
                else
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "ret.Add({0}, \"{1}\", {2}M, {3}M, {4}, {5});",
                        dt.TypeId.Alias, dt.SubtypeId, dt.Mass, dt.Volume, dt.HasIntegralAmounts ? "true" : "false", dt.IsStackable ? "true" : "false");
                }
                sb.AppendLine();
            }

            if (Mods != null)
            {
                sb.AppendLine();
                sb.AppendLine("/*");
                foreach (var dt in Mods.Data)
                    if (modUsed.Contains(dt.PublishedFileId))
                        sb.AppendFormat(@"- [url=http://steamcommunity.com/sharedfiles/filedetails/{0}]{1}[/url]", dt.PublishedFileId, dt.Title)
                            .AppendLine();
                sb.AppendLine("*/");
            }

            return sb.ToString();
        }

        private int Comparision(ItemInfo x, ItemInfo y)
        {
            var result = x.TypeId.Order - y.TypeId.Order;
            if (result != 0)
                return result;
            result = String.Compare(x.SubtypeId, y.SubtypeId);
            if (result != 0)
                return result;
            return (int)x.ModId - (int)y.ModId;
        }

        private bool ProcessFile(Stream input, long modId)
        {
            using (var reader = XmlReader.Create(input))
            {
                var document = new XmlDocument();
                document.Load(reader);
                if (document.DocumentElement.LocalName != "Definitions")
                    return false;
                return ProcessFileSecondStep(document.DocumentElement, modId);
            }
        }

        private bool ProcessFileParseNodes(XmlNode parentNode, long modId)
        {
            var addedAnything = false;
            foreach (XmlNode node in parentNode.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element)
                    continue;

                XmlElement idNode = node["Id"];
                XmlElement massNode = node["Mass"];
                XmlElement volumeNode = node["Volume"];

                if (idNode == null || massNode == null || volumeNode == null)
                    continue;

                XmlElement typeIdNode = idNode["TypeId"];
                XmlElement subtypeIdNode = idNode["SubtypeId"];

                if (typeIdNode == null || subtypeIdNode == null)
                    continue;

                string typeIdStr = typeIdNode.InnerText.Trim();
                string subtypeId = subtypeIdNode.InnerText.Trim();
                string mass = massNode.InnerText.Trim();
                string volume = volumeNode.InnerText.Trim();

                MainType typeId;
                if (String.IsNullOrEmpty(typeIdStr) || String.IsNullOrEmpty(mass) || String.IsNullOrEmpty(volume) || !_typeMap.TryGetValue(typeIdStr, out typeId))
                    continue;

                if (String.IsNullOrEmpty(subtypeId))
                    subtypeId = String.Empty;

                if (mass.StartsWith("."))
                    mass = "0" + mass;

                if (volume.StartsWith("."))
                    volume = "0" + volume;

                var itemInfo = new ItemInfo(modId, typeId, subtypeId, mass, volume);
                Result.Add(itemInfo);
                addedAnything = true;
            }

            return addedAnything;
        }

        private bool ProcessFileSecondStep(XmlElement xmlElement, long modId)
        {
            var addedAnything = false;
            foreach (XmlNode node in xmlElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element)
                    continue;

                if (ProcessFileParseNodes(node, modId))
                    addedAnything = true;
            }
            return addedAnything;
        }
    }
}
