using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hammer
{
    sealed class InsertionPoint
    {
        public int ZoneSet;
        public string[] Names;
        public string[] Descriptions;
    }

    sealed class ScenarioInfo
    {
        public int MapId;
        public uint Flags;
        public string[] Names;
        public string[] Descriptions;
        public string ImageFileBase = "";
        public string ScenarioPath = "";
        public int PresenceContextId;
        public int SortOrder;
        public int MinimumDesiredPlayers;
        public int MaximumDesiredPlayers;
        public int[] MaximumTeams = new int[Blf.MaximumTeamsCount];
        public List<InsertionPoint> InsertionPoints = new List<InsertionPoint>();
        public List<string> FlagsWarnings = new List<string>();
    }

    sealed class CampaignInfo
    {
        public int CampaignId;
        public uint Flags;
        public string[] Names;
        public string[] Descriptions;
        public List<int> Levels = new List<int>();
    }

    sealed class ReverseOptions
    {
        public string DataRoot;      // resolved absolute, or null
        public string Output;
        public string Name;
        public string StringsFile;   // resolved absolute, or null
        public bool NoStrings;
        public string InfoDir;       // resolved absolute, or null
    }

    // reverse converter: blf -> xml. ported from mapinfo_to_xml.py.
    static class ReverseConverter
    {
        const string XmlComment =
"<!--\n" +
"type can be one of the following: ui, solo, multi.  This influences which type of scenario the game loads the map as.\n" +
"tagsLocation can be one of the following: default, dlc, test, temp.  This is where the .scenario file lives in tags builds.  Ignored for Cache builds.\n" +
"-->";

        // ---------- payload extraction

        public static ScenarioInfo ReadScenario(string path)
        {
            var blf = new BlfFile(path);
            var chunk = blf.RequireChunk(Blf.ScenarioType, Blf.ScenarioSize,
                Blf.ScenarioMajor, Blf.ScenarioMinor, "scenario");

            var info = new ScenarioInfo
            {
                MapId = chunk.I32(Blf.ScenarioMapIdOffset),
                Flags = chunk.U32(Blf.ScenarioFlagsOffset),
                Names = chunk.WStringArray(Blf.ScenarioNameOffset, Blf.NameLength, Blf.NameCount, Blf.NameLength * 2),
                Descriptions = chunk.WStringArray(Blf.ScenarioDescriptionOffset, Blf.DescriptionLength, Blf.NameCount, Blf.DescriptionLength * 2),
                ImageFileBase = chunk.CString(Blf.ScenarioImageFileBaseOffset, Blf.ImageFileBaseLength),
                ScenarioPath = chunk.CString(Blf.ScenarioScenarioFileOffset, Blf.ScenarioFileLength),
                PresenceContextId = chunk.I32(Blf.ScenarioPresenceContextIdOffset),
                SortOrder = chunk.I32(Blf.ScenarioSortOrderOffset),
                MinimumDesiredPlayers = chunk.U8(Blf.ScenarioMinimumDesiredPlayersOffset),
                MaximumDesiredPlayers = chunk.U8(Blf.ScenarioMaximumDesiredPlayersOffset),
            };
            for (int i = 0; i < Blf.MaximumTeamsCount; i++)
                info.MaximumTeams[i] = chunk.U8(Blf.ScenarioMaximumTeamsOffset + i);

            for (int index = 0; index < Blf.InsertionPointCount; index++)
            {
                int b = Blf.ScenarioInsertionPointsOffset + index * Blf.InsertionPointSize;
                int zoneSet = chunk.U16(b + Blf.InsertionPointZoneSetOffset);
                string[] names = chunk.WStringArray(b + Blf.InsertionPointNameOffset, Blf.NameLength, Blf.NameCount, Blf.NameLength * 2);
                string[] descriptions = chunk.WStringArray(b + Blf.InsertionPointDescriptionOffset, Blf.DescriptionLength, Blf.NameCount, Blf.DescriptionLength * 2);
                if (chunk.U8(b) == 0)
                {
                    if (zoneSet != 0 || names.Any(n => n.Length > 0) || descriptions.Any(d => d.Length > 0))
                        Diag.Warn(path + ": insertion point " + index + " is invalid but has data; ignored");
                    continue;
                }
                info.InsertionPoints.Add(new InsertionPoint { ZoneSet = zoneSet, Names = names, Descriptions = descriptions });
            }

            foreach (var (bit, name) in new[] { (Blf.LevelFlagFromDlc, "from_dlc"), (Blf.LevelFlagUnlockable, "unlockable") })
            {
                if ((info.Flags & (1u << bit)) != 0)
                    info.FlagsWarnings.Add("flags bit " + bit + " (" + name + ") is set, which the scenario xml cannot express; dropped");
            }

            return info;
        }

        public static CampaignInfo ReadCampaign(string path)
        {
            var blf = new BlfFile(path);
            var chunk = blf.RequireChunk(Blf.CampaignType, Blf.CampaignSize,
                Blf.CampaignMajor, Blf.CampaignMinor, "campaign");

            var scenarios = new int[Blf.CampaignScenarioCount];
            for (int i = 0; i < Blf.CampaignScenarioCount; i++)
                scenarios[i] = chunk.I32(Blf.CampaignScenariosOffset + 4 * i);
            var usedSlots = new List<int>();
            for (int i = 0; i < scenarios.Length; i++)
                if (scenarios[i] != 0)
                    usedSlots.Add(i);
            var levels = new List<int>();
            foreach (int i in usedSlots)
                levels.Add(scenarios[i]);
            if (usedSlots.Count > 0 && usedSlots.Count != usedSlots[usedSlots.Count - 1] + 1)
                Diag.Warn(path + ": campaign level list has gaps; unused slots dropped");

            return new CampaignInfo
            {
                CampaignId = chunk.I32(Blf.CampaignIdOffset),
                Flags = chunk.U32(Blf.CampaignFlagsOffset),
                Names = chunk.WStringArray(Blf.CampaignNameOffset, Blf.CampaignNameLength, Blf.NameCount, Blf.CampaignNameLength * 2),
                Descriptions = chunk.WStringArray(Blf.CampaignDescriptionOffset, Blf.CampaignDescriptionLength, Blf.NameCount, Blf.CampaignDescriptionLength * 2),
                Levels = levels,
            };
        }

        // ---------- inference

        static string FlagsToType(uint flags, string path)
        {
            var typeBits = Blf.TypeBitToType.Where(t => (flags & (1u << t.bit)) != 0).ToArray();
            if (typeBits.Length == 0)
                throw new ToolError(path + ": no level type flag set (expected ui, solo or multi)");
            if (typeBits.Length > 1)
                Diag.Warn(path + ": multiple level type flags set ("
                    + string.Join(", ", typeBits.Select(t => t.type)) + "); using " + typeBits[0].type);
            return typeBits[0].type;
        }

        static string FlagsToLocation(uint flags)
        {
            foreach (var (bit, name) in Blf.LocationBitToName)
                if ((flags & (1u << bit)) != 0)
                    return name;
            return null;
        }

        static string InferName(ScenarioInfo info, string levelType, ReverseOptions options, string path)
        {
            if (!string.IsNullOrEmpty(options.Name))
                return options.Name;
            if (levelType == "solo")
            {
                string scenarioPath = info.ScenarioPath;
                int underscore = scenarioPath.IndexOf('_');
                string token = underscore < 0 ? scenarioPath : scenarioPath.Substring(0, underscore);
                if (IsAllDigits(token))
                    return token;
                Diag.Warn(path + ": solo scenarioPath " + Text.Repr(scenarioPath) + " has no numeric prefix; using it as @name");
            }
            return info.ScenarioPath;
        }

        static bool IsAllDigits(string s)
        {
            if (s.Length == 0)
                return false;
            foreach (char c in s)
                if (c < '0' || c > '9')
                    return false;
            return true;
        }

        static string LevelDirectory(string levelType, string location)
            => location != null && Array.IndexOf(Blf.DirectoryNames, location) >= 0 ? location : levelType;

        static string StringsFileRelativePath(string levelType, string location)
        {
            string directory = LevelDirectory(levelType, location);
            string stem = directory == "dlc" ? "multi" : directory;
            return "levels\\" + directory + "\\halo3_" + stem + "_names.txt";
        }

        // ---------- xml emission

        static string XmlEscapeAttribute(string value)
            => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        static string XmlEscapeText(string value)
            => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        public static List<string> ScenarioLines(ScenarioInfo info, string name, string levelType, string location, string stringsFile)
        {
            string attributes = string.Format("name=\"{0}\" id=\"{1}\" type=\"{2}\"",
                XmlEscapeAttribute(name), info.MapId, levelType);
            if (location != null)
                attributes += " tagsLocation=\"" + location + "\"";

            var lines = new List<string>
            {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                XmlComment,
                "<scenario " + attributes + ">",
                "\t<pathToStringsFile>" + XmlEscapeText(stringsFile) + "</pathToStringsFile>",
                "\t<imageFileBase>" + XmlEscapeText(info.ImageFileBase) + "</imageFileBase>",
                "\t<scenarioPath>" + XmlEscapeText(info.ScenarioPath) + "</scenarioPath>",
                "\t<presenceContextID>" + info.PresenceContextId + "</presenceContextID>",
                "\t<sortOrder>" + info.SortOrder + "</sortOrder>",
            };
            if (info.MinimumDesiredPlayers != 0)
                lines.Add("\t<multiplayerMinimumDesiredPlayers>" + info.MinimumDesiredPlayers + "</multiplayerMinimumDesiredPlayers>");
            if (info.MaximumDesiredPlayers != 0)
                lines.Add("\t<multiplayerMaximumDesiredPlayers>" + info.MaximumDesiredPlayers + "</multiplayerMaximumDesiredPlayers>");
            if (info.MaximumTeams.Skip(1).Any(v => v != 0))
            {
                lines.Add("\t<maximumTeams>");
                foreach (var (tag, engineIndex) in Blf.GameEngineTags)
                    lines.Add("\t\t<" + tag + ">" + info.MaximumTeams[engineIndex] + "</" + tag + ">");
                lines.Add("\t</maximumTeams>");
            }
            if (info.InsertionPoints.Count > 0)
            {
                lines.Add("\t<insertionPoints>");
                foreach (var ip in info.InsertionPoints)
                    lines.Add("\t\t<insertionPoint zoneSet=\"" + ip.ZoneSet + "\" />");
                lines.Add("\t</insertionPoints>");
            }
            if ((info.Flags & (1u << Blf.LevelFlagVisibleInUi)) == 0)
                lines.Add("\t<visibleInUI>false</visibleInUI>");
            if ((info.Flags & (1u << Blf.LevelFlagAllowsSavedFilms)) == 0)
                lines.Add("\t<allowsSavedFilms>false</allowsSavedFilms>");
            lines.Add("</scenario>");
            return lines;
        }

        public static List<string> CampaignLines(CampaignInfo info, string name, string stringsFile)
        {
            var lines = new List<string>
            {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<campaign name=\"" + XmlEscapeAttribute(name) + "\" id=\"" + info.CampaignId + "\" flags=\"" + info.Flags + "\">",
                "\t<pathToStringsFile>" + XmlEscapeText(stringsFile) + "</pathToStringsFile>",
                "\t<levels>",
            };
            foreach (int mapId in info.Levels)
                lines.Add("\t\t<level>" + mapId + "</level>");
            lines.Add("\t</levels>");
            lines.Add("</campaign>");
            return lines;
        }

        // ---------- conversions

        public static string ConvertScenario(string mapinfoPath, ReverseOptions options)
        {
            var info = ReadScenario(mapinfoPath);
            foreach (string message in info.FlagsWarnings)
                Diag.Warn(mapinfoPath + ": " + message);

            string levelType = FlagsToType(info.Flags, mapinfoPath);
            string location = FlagsToLocation(info.Flags);
            string name = InferName(info, levelType, options, mapinfoPath);

            string dataRoot = options.DataRoot;
            string directory = LevelDirectory(levelType, location);

            string xmlPath;
            if (options.Output != null)
                xmlPath = Path.GetFullPath(options.Output);
            else
                xmlPath = Path.Combine(dataRoot, "levels", directory, info.ScenarioPath, info.ScenarioPath + ".xml");

            string stringsFile;
            if (options.StringsFile != null)
                stringsFile = Path.GetFullPath(options.StringsFile);
            else
                stringsFile = StringsFile.ResolveUnder(dataRoot, StringsFileRelativePath(levelType, location));

            string relStrings = StringsFileRelativePath(levelType, location);
            var lines = ScenarioLines(info, name, levelType, location, relStrings);
            TextFile.WriteText(xmlPath, lines.ToArray(), true, true, false);

            if (!options.NoStrings)
            {
                var insertionPoints = info.InsertionPoints
                    .Select(ip => (ip.Names, ip.Descriptions))
                    .ToList();
                StringsFile.MergeStringsFile(stringsFile, name, StringsFile.StringsBlockLines(name, info.Names, info.Descriptions, insertionPoints));
            }

            Diag.WriteLine(mapinfoPath + " -> " + xmlPath + " (map id " + info.MapId + ", type " + levelType
                + ", " + info.InsertionPoints.Count + " insertion point(s))");
            return xmlPath;
        }

        public static string ConvertCampaign(string campaignPath, ReverseOptions options)
        {
            var info = ReadCampaign(campaignPath);

            string dataRoot = options.DataRoot;
            string name = !string.IsNullOrEmpty(options.Name)
                ? options.Name
                : Path.GetFileNameWithoutExtension(campaignPath);

            string xmlPath;
            if (options.Output != null)
                xmlPath = Path.GetFullPath(options.Output);
            else
                xmlPath = Path.Combine(dataRoot, "levels", name + ".xml");

            string stringsFile;
            if (options.StringsFile != null)
                stringsFile = Path.GetFullPath(options.StringsFile);
            else
                stringsFile = Path.Combine(dataRoot, "levels", "campaigns.txt");

            var lines = CampaignLines(info, name, "levels\\campaigns.txt");
            TextFile.WriteText(xmlPath, lines.ToArray(), true, true, false);

            if (!options.NoStrings)
                StringsFile.MergeStringsFile(stringsFile, name, StringsFile.StringsBlockLines(name, info.Names, info.Descriptions, null));

            Diag.WriteLine(campaignPath + " -> " + xmlPath + " (campaign id " + info.CampaignId
                + ", " + info.Levels.Count + " level(s))");
            return xmlPath;
        }

        public static int MakeAll(ReverseOptions options, string mapsInfoDir, string wantedExtension)
        {
            string infoDir = options.InfoDir != null ? options.InfoDir : mapsInfoDir;
            int converted = 0, failed = 0;
            var files = new List<string>(Directory.EnumerateFiles(infoDir, "*." + wantedExtension));
            files.Sort(StringComparer.Ordinal);
            foreach (string path in files)
            {
                try
                {
                    if (wantedExtension == "mapinfo")
                        ConvertScenario(path, options);
                    else
                        ConvertCampaign(path, options);
                    converted++;
                }
                catch (IOException error) { Diag.Warn(path + ": " + error.Message); failed++; }
                catch (UnauthorizedAccessException error) { Diag.Warn(path + ": " + error.Message); failed++; }
                catch (ToolError error) { Diag.Warn(path + ": " + error.Message); failed++; }
            }
            string summary = wantedExtension + "-make-from-mapinfo-all: " + converted + " converted, " + failed + " failed";
            if (failed > 0)
                Diag.Stderr(summary);
            else
                Diag.WriteLine(summary);
            return failed == 0 ? 0 : 1;
        }
    }
}
