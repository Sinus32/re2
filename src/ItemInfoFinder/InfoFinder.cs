﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace ItemInfoFinder
{
    public class InfoFinder
    {
        public InfoFinder()
        {
            Result = new List<ItemInfo>();
            ModIds = new List<long>();
        }

        public List<ItemInfo> Result { get; set; }

        public List<long> ModIds { get; set; }

        public WorkshopItemInfo Mods { get; set; }

        internal void FindItemsInFiles(string path, string searchPattern)
        {
            foreach (var dt in Directory.GetFiles(path, searchPattern))
            {
                ProcessFile(File.OpenRead(dt));
            }
        }

        internal void FindItemsInZipFiles(string path, string searchPattern, string innerPath, string dataFileExtension)
        {
            foreach (var dt in Directory.GetFiles(path, searchPattern))
            {
                var addedAnything = false;
                var archive = ZipFile.OpenRead(dt);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith(innerPath, StringComparison.InvariantCultureIgnoreCase) && entry.FullName.EndsWith(dataFileExtension))
                    {
                        if (ProcessFile(entry.Open()))
                            addedAnything = true;
                    }
                }

                if (addedAnything)
                {
                    var file = new FileInfo(dt);
                    var name = file.Name.Substring(0, file.Name.Length - file.Extension.Length);
                    long itemId;
                    if (Int64.TryParse(name, out itemId))
                        ModIds.Add(itemId);
                }
            }
        }

        private bool ProcessFile(Stream input)
        {
            using (var reader = XmlReader.Create(input))
            {
                var document = new XmlDocument();
                document.Load(reader);
                if (document.DocumentElement.LocalName != "Definitions")
                    return false;
                return ProcessFileSecondStep(document.DocumentElement);
            }
        }

        private bool ProcessFileSecondStep(XmlElement xmlElement)
        {
            var addedAnything = false;
            foreach (XmlNode node in xmlElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element)
                    continue;

                if (ProcessFileParseNodes(node))
                    addedAnything = true;
            }
            return addedAnything;
        }

        private bool ProcessFileParseNodes(XmlNode parentNode)
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

                string typeId = typeIdNode.InnerText.Trim();
                string subtypeId = subtypeIdNode.InnerText.Trim();
                string mass = massNode.InnerText.Trim();
                string volume = volumeNode.InnerText.Trim();

                if (String.IsNullOrEmpty(typeId) || String.IsNullOrEmpty(mass) || String.IsNullOrEmpty(volume))
                    continue;

                if (String.IsNullOrEmpty(subtypeId))
                    subtypeId = String.Empty;

                if (mass.StartsWith("."))
                    mass = "0" + mass;

                if (volume.StartsWith("."))
                    volume = "0" + volume;

                var itemInfo = new ItemInfo(typeId, subtypeId, mass, volume);
                Result.Add(itemInfo);
                addedAnything = true;
            }

            return addedAnything;
        }

        internal void DownloadModData()
        {
            if (ModIds.Count == 0)
                return;

            Mods = WorkshopItemInfo.GetWorkshopItemInfo(ModIds);
        }

        internal string GetOutputText()
        {
            if (Result.Count == 0)
                return "Nothing";

            var set = new HashSet<ItemInfo>();
            Result.RemoveAll(dt => !set.Add(dt));
            Result.Sort(Comparision);
            var sb = new StringBuilder();

            var lastTypeId = Result[0].TypeId;
            foreach (var dt in Result)
            {
                if (lastTypeId != dt.TypeId)
                {
                    lastTypeId = dt.TypeId;
                    sb.AppendLine();
                }

                sb.AppendFormat(CultureInfo.InvariantCulture, "AddItemInfo({0}Type, \"{1}\", {2}M, {3}M, {4}, {5});", dt.TypeId,
                    dt.SubtypeId, dt.Mass, dt.Volume, dt.IsSingleItem ? "true" : "false", dt.IsStackable ? "true" : "false");
                sb.AppendLine();
            }

            if (Mods != null)
            {
                sb.AppendLine();
                Mods.Data.Sort((a, b) => String.Compare(a.Title, b.Title, true));
                foreach (var dt in Mods.Data)
                    sb.AppendFormat(@"- [url=http://steamcommunity.com/sharedfiles/filedetails/{0}]{1}[/url]", dt.PublishedFileId, dt.Title)
                        .AppendLine();
            }

            return sb.ToString();
        }

        private int Comparision(ItemInfo x, ItemInfo y)
        {
            var result = String.Compare(x.TypeId, y.TypeId);
            if (result != 0)
                return result;
            return String.Compare(x.SubtypeId, y.SubtypeId);
        }
    }
}