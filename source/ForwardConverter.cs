using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Hammer
{
    sealed class Level
    {
        public string Name;
        public long Id;
        public string Type;
        public string TagsLocation = "default";
        public string PathToStringsFile = "";
        public string ImageFileBase = "";
        public string ScenarioPath = "";
        public long PresenceContextId;
        public long SortOrder;
        public long MinimumDesiredPlayers;
        public long MaximumDesiredPlayers;
        public bool VisibleInUi = true;
        public bool AllowsSavedFilms = true;
        public long Flags;
        public int[] MaximumTeams = new int[Blf.MaximumTeamsCount];
        public List<long> InsertionPoints = new List<long>();
    }

    sealed class Campaign
    {
        public string Name;
        public long Id;
        public long Flags;
        public string PathToStringsFile = "";
        public List<long> Scenarios = new List<long>();
    }

    sealed class ForwardOptions
    {
        public string SignatureProvider = "secrets";
        public string CopySignatureFrom;
    }

    // forward converter: xml descriptors -> blf. ported from xml_to_mapinfo.py.
    static class ForwardConverter
    {
        public static int BulkMakeAll(string dataRoot, string mapsInfoDir, string wantedRootTag, ForwardOptions opt, bool includeMissingStrings)
        {
            int converted = 0, skipped = 0, failed = 0;
            foreach (string xmlPath in DiscoverLevelXmls(dataRoot))
            {
                string rootTag;
                try
                {
                    rootTag = XmlRootTag(xmlPath);
                }
                catch (ToolError error)
                {
                    Diag.Warn(error.Message);
                    failed++;
                    continue;
                }
                if (rootTag != wantedRootTag)
                    continue;

                try
                {
                    if (wantedRootTag == "scenario")
                    {
                        var level = ParseLevelXml(xmlPath, out _);
                        string outputPath = DefaultScenarioOutput(mapsInfoDir, level);
                        if (!includeMissingStrings
                            && StringsFile.StringsFileStatus(level.PathToStringsFile, dataRoot) == "missing")
                        {
                            Diag.Warn(xmlPath + ": skipped, strings file " + level.PathToStringsFile
                                + " not found (pass --include-missing-strings to convert anyway)");
                            skipped++;
                            continue;
                        }
                        ConvertScenario(xmlPath, outputPath, dataRoot, opt);
                        Diag.WriteLine(xmlPath + " -> " + outputPath + " (map id " + level.Id
                            + ", flags 0x" + ((uint)level.Flags).ToString("X8") + ")");
                    }
                    else
                    {
                        var campaign = ParseCampaignXml(xmlPath, out _);
                        string outputPath = DefaultCampaignOutput(mapsInfoDir, xmlPath);
                        ConvertCampaign(xmlPath, outputPath, dataRoot, opt);
                        Diag.WriteLine(xmlPath + " -> " + outputPath + " (campaign id " + campaign.Id
                            + ", " + campaign.Scenarios.Count + " levels)");
                    }
                    converted++;
                }
                catch (IOException error) { Diag.Warn(xmlPath + ": " + error.Message); failed++; }
                catch (UnauthorizedAccessException error) { Diag.Warn(xmlPath + ": " + error.Message); failed++; }
                catch (ToolError error) { Diag.Warn(xmlPath + ": " + error.Message); failed++; }
            }

            string summary = wantedRootTag + "-make-all: " + converted + " converted, " + skipped
                + " skipped, " + failed + " failed";
            if (failed > 0 || skipped > 0)
                Diag.Stderr(summary);
            else
                Diag.WriteLine(summary);
            return failed == 0 ? 0 : 1;
        }

        // ---------- conversions

        public static Level ConvertScenario(string xmlPath, string outputPath, string dataRoot, ForwardOptions opt)
        {
            var level = ParseLevelXml(xmlPath, out var warnings);
            foreach (string message in warnings)
                Diag.Warn(xmlPath + ": " + message);

            var strings = LoadStrings(level.PathToStringsFile, dataRoot, xmlPath);
            var contents = BlfWriter.Assemble(MakeScenarioChunk(level, strings, xmlPath),
                BlfWriter.ResolveSignature(opt.SignatureProvider, opt.CopySignatureFrom, outputPath));
            if (contents.Length != Blf.StartOfFileSize + Blf.ScenarioSize + Blf.EndOfFileWithRsaSize)
                throw new ToolError("assembled mapinfo has unexpected size " + contents.Length);
            BlfWriter.WriteOutput(outputPath, contents);
            return level;
        }

        public static Campaign ConvertCampaign(string xmlPath, string outputPath, string dataRoot, ForwardOptions opt)
        {
            var campaign = ParseCampaignXml(xmlPath, out var warnings);
            foreach (string message in warnings)
                Diag.Warn(xmlPath + ": " + message);

            var strings = LoadStrings(campaign.PathToStringsFile, dataRoot, xmlPath);
            var contents = BlfWriter.Assemble(MakeCampaignChunk(campaign, strings, xmlPath),
                BlfWriter.ResolveSignature(opt.SignatureProvider, opt.CopySignatureFrom, outputPath));
            if (contents.Length != Blf.StartOfFileSize + Blf.CampaignSize + Blf.EndOfFileWithRsaSize)
                throw new ToolError("assembled campaign file has unexpected size " + contents.Length);
            BlfWriter.WriteOutput(outputPath, contents);
            return campaign;
        }

        static Dictionary<string, string> LoadStrings(string pathToStringsFile, string dataRoot, string xmlPath)
        {
            if (pathToStringsFile.Length == 0)
            {
                Diag.Warn(xmlPath + ": no <pathToStringsFile>; names fall back to the scenario name "
                    + "and descriptions stay empty");
                return new Dictionary<string, string>();
            }
            string stringsPath = StringsFile.ResolveUnder(dataRoot, pathToStringsFile);
            if (!File.Exists(stringsPath))
            {
                Diag.Warn(xmlPath + ": strings file " + pathToStringsFile + " (resolved to " + stringsPath
                    + ") not found; names fall back to the scenario name and descriptions stay empty");
                return new Dictionary<string, string>();
            }
            return StringsFile.ParseStringsFile(stringsPath);
        }

        // ---------- chunk builders

        public static byte[] MakeScenarioChunk(Level level, Dictionary<string, string> strings, string xmlPath)
        {
            var chunk = new byte[Blf.ScenarioSize];
            Array.Copy(BlfWriter.PackHeader(Blf.ScenarioType, Blf.ScenarioSize,
                Blf.ScenarioMajor, Blf.ScenarioMinor), chunk, Blf.HeaderSize);

            WriteU32BE(chunk, Blf.ScenarioMapIdOffset, (uint)(level.Id & 0xFFFFFFFF));
            WriteU32BE(chunk, Blf.ScenarioFlagsOffset, (uint)(level.Flags & 0xFFFFFFFF));

            string[] names = LocalizedStrings(strings, level.Name, Blf.StringsDisplayField, Text.Title(level.Name));
            byte[] nameBytes = BlfWriter.EncodeWcharArray(names, Blf.NameLength, Blf.NameCount, "name");
            Array.Copy(nameBytes, 0, chunk, Blf.ScenarioNameOffset, nameBytes.Length);

            string[] descriptions = LocalizedStrings(strings, level.Name, Blf.StringsDescriptionField, null);
            byte[] descBytes = BlfWriter.EncodeWcharArray(descriptions, Blf.DescriptionLength, Blf.DescriptionCount, "description");
            Array.Copy(descBytes, 0, chunk, Blf.ScenarioDescriptionOffset, descBytes.Length);

            byte[] image = BlfWriter.EncodeAsciiField(level.ImageFileBase, Blf.ImageFileBaseLength, "imageFileBase", xmlPath);
            Array.Copy(image, 0, chunk, Blf.ScenarioImageFileBaseOffset, image.Length);

            byte[] scenario = BlfWriter.EncodeAsciiField(level.ScenarioPath, Blf.ScenarioFileLength, "scenarioPath", xmlPath);
            Array.Copy(scenario, 0, chunk, Blf.ScenarioScenarioFileOffset, scenario.Length);

            WriteI32BE(chunk, Blf.ScenarioPresenceContextIdOffset, (int)level.PresenceContextId);
            WriteI32BE(chunk, Blf.ScenarioSortOrderOffset, (int)level.SortOrder);
            chunk[Blf.ScenarioMinimumDesiredPlayersOffset] = (byte)(level.MinimumDesiredPlayers & 0xFF);
            chunk[Blf.ScenarioMaximumDesiredPlayersOffset] = (byte)(level.MaximumDesiredPlayers & 0xFF);
            for (int i = 0; i < Blf.MaximumTeamsCount; i++)
                chunk[Blf.ScenarioMaximumTeamsOffset + i] = (byte)(level.MaximumTeams[i] & 0xFF);

            for (int index = 0; index < level.InsertionPoints.Count; index++)
            {
                long zoneSet = level.InsertionPoints[index];
                int entry = Blf.ScenarioInsertionPointsOffset + index * Blf.InsertionPointSize;
                chunk[entry] = 1; // valid
                chunk[entry + Blf.InsertionPointZoneSetOffset] = (byte)((zoneSet >> 8) & 0xFF);
                chunk[entry + Blf.InsertionPointZoneSetOffset + 1] = (byte)(zoneSet & 0xFF);

                string[] ipNames = LocalizedStrings(strings, level.Name, "insertion" + index + "." + Blf.StringsDisplayField, null);
                byte[] ipNameBytes = BlfWriter.EncodeWcharArray(ipNames, Blf.NameLength, Blf.NameCount, "insertion point name");
                Array.Copy(ipNameBytes, 0, chunk, entry + Blf.InsertionPointNameOffset, ipNameBytes.Length);

                string[] ipDesc = LocalizedStrings(strings, level.Name, "insertion" + index + "." + Blf.StringsDescriptionField, null);
                byte[] ipDescBytes = BlfWriter.EncodeWcharArray(ipDesc, Blf.DescriptionLength, Blf.DescriptionCount, "insertion point description");
                Array.Copy(ipDescBytes, 0, chunk, entry + Blf.InsertionPointDescriptionOffset, ipDescBytes.Length);
            }

            return chunk;
        }

        public static byte[] MakeCampaignChunk(Campaign campaign, Dictionary<string, string> strings, string xmlPath)
        {
            var chunk = new byte[Blf.CampaignSize];
            Array.Copy(BlfWriter.PackHeader(Blf.CampaignType, Blf.CampaignSize,
                Blf.CampaignMajor, Blf.CampaignMinor), chunk, Blf.HeaderSize);

            WriteU32BE(chunk, Blf.CampaignIdOffset, (uint)(campaign.Id & 0xFFFFFFFF));
            WriteU32BE(chunk, Blf.CampaignFlagsOffset, (uint)(campaign.Flags & 0xFFFFFFFF));

            string[] names = LocalizedStrings(strings, campaign.Name, Blf.StringsDisplayField, Text.Title(campaign.Name));
            byte[] nameBytes = BlfWriter.EncodeWcharArray(names, Blf.CampaignNameLength, Blf.NameCount, "campaign name");
            Array.Copy(nameBytes, 0, chunk, Blf.CampaignNameOffset, nameBytes.Length);

            string[] descriptions = LocalizedStrings(strings, campaign.Name, Blf.StringsDescriptionField, null);
            byte[] descBytes = BlfWriter.EncodeWcharArray(descriptions, Blf.CampaignDescriptionLength, Blf.NameCount, "campaign description");
            Array.Copy(descBytes, 0, chunk, Blf.CampaignDescriptionOffset, descBytes.Length);

            for (int mapIndex = 0; mapIndex < Blf.CampaignScenarioCount; mapIndex++)
            {
                long mapId = mapIndex < campaign.Scenarios.Count ? campaign.Scenarios[mapIndex] : 0;
                WriteI32BE(chunk, Blf.CampaignScenariosOffset + 4 * mapIndex, (int)mapId);
            }

            // the trailing pad word stays zero
            return chunk;
        }

        static string[] LocalizedStrings(Dictionary<string, string> strings, string name, string field, string fallback)
        {
            var values = new string[Blf.LanguageCount];
            for (int i = 0; i < values.Length; i++)
                values[i] = "";
            foreach (var (code, index) in Blf.Languages)
            {
                string key = StringsFile.StringsKey(name, field, code);
                if (strings.TryGetValue(key, out var value))
                    values[index] = value;
            }
            if ((values[0] == null || values[0].Length == 0) && fallback != null)
                values[0] = fallback;
            return values;
        }

        static long LevelFlags(Level level)
        {
            long flags = 0;
            if (level.VisibleInUi)
                flags |= 1L << Blf.LevelFlagVisibleInUi;
            if (level.AllowsSavedFilms)
                flags |= 1L << Blf.LevelFlagAllowsSavedFilms;
            if (!Blf.LevelTypeFlagBits.TryGetValue(level.Type, out int typeBit))
                throw new ToolError("<scenario> @type " + Text.Repr(level.Type) + " is not one of mainmenu/multi/solo/ui");
            flags |= 1L << typeBit;
            if (Blf.TagsLocationFlagBits.TryGetValue(level.TagsLocation, out int? locationBit) && locationBit != null)
                flags |= 1L << locationBit.Value;
            return flags;
        }

        // ---------- xml parsing

        public static string DefaultScenarioOutput(string mapsInfoDir, Level level)
            => Path.Combine(mapsInfoDir, level.ScenarioPath + ".mapinfo");

        public static string DefaultCampaignOutput(string mapsInfoDir, string xmlPath)
        {
            string stem = Path.GetFileNameWithoutExtension(xmlPath);
            return Path.Combine(mapsInfoDir, stem + ".campaign");
        }

        static XElement ParseXmlRoot(string path)
        {
            try
            {
                return XDocument.Load(path).Root;
            }
            catch (XmlException error)
            {
                throw new ToolError(path + ": malformed xml (" + error.Message + ")");
            }
        }

        static string XmlRootTag(string path) => ParseXmlRoot(path).Name.LocalName;

        static XElement Find(XElement parent, string tag) => parent.Elements(tag).FirstOrDefault();
        static string FindText(XElement parent, string tag)
        {
            var e = Find(parent, tag);
            return e == null ? null : e.Value;
        }
        static string Attr(XElement e, string name)
        {
            var a = e.Attribute(name);
            return a == null ? null : a.Value;
        }

        static long? ParseInteger(string text)
        {
            text = text.Trim();
            if (TryParseDecimal(text, out long v10))
                return v10;
            if (TryParseBaseZero(text, out long v0))
                return v0;
            return null;
        }

        static bool TryParseDecimal(string text, out long value)
        {
            value = 0;
            if (text.Length == 0)
                return false;
            int i = 0;
            bool neg = false;
            if (text[0] == '+') { i = 1; }
            else if (text[0] == '-') { neg = true; i = 1; }
            if (i >= text.Length)
                return false;
            long acc = 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                    return false;
                acc = unchecked(acc * 10 + (c - '0'));
            }
            value = neg ? -acc : acc;
            return true;
        }

        static bool TryParseBaseZero(string text, out long value)
        {
            value = 0;
            if (text.Length == 0)
                return false;
            int i = 0;
            bool neg = false;
            if (text[0] == '+') { i = 1; }
            else if (text[0] == '-') { neg = true; i = 1; }
            string rest = text.Substring(i);
            long mag = 0;
            if (rest.Length >= 2 && rest[0] == '0' && (rest[1] == 'x' || rest[1] == 'X'))
            {
                string hex = rest.Substring(2);
                if (hex.Length == 0)
                    return false;
                foreach (char c in hex)
                {
                    int d = HexVal(c);
                    if (d < 0)
                        return false;
                    mag = unchecked(mag * 16 + d);
                }
            }
            else if (rest.Length >= 2 && rest[0] == '0' && (rest[1] == 'o' || rest[1] == 'O'))
            {
                string oct = rest.Substring(2);
                if (oct.Length == 0)
                    return false;
                foreach (char c in oct)
                {
                    if (c < '0' || c > '7')
                        return false;
                    mag = unchecked(mag * 8 + (c - '0'));
                }
            }
            else if (rest.Length >= 2 && rest[0] == '0' && (rest[1] == 'b' || rest[1] == 'B'))
            {
                string bin = rest.Substring(2);
                if (bin.Length == 0)
                    return false;
                foreach (char c in bin)
                {
                    if (c != '0' && c != '1')
                        return false;
                    mag = unchecked(mag * 2 + (c - '0'));
                }
            }
            else if (rest == "0")
            {
                mag = 0;
            }
            else
            {
                return false;
            }
            value = neg ? -mag : mag;
            return true;
        }

        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        static long ParseInt(XElement element, string tag, long def, List<string> warnings)
        {
            var child = Find(element, tag);
            if (child == null || child.FirstNode == null || child.Value.Trim().Length == 0)
                return def;
            var value = ParseInteger(child.Value);
            if (value == null)
            {
                warnings.Add("<" + tag + "> has non-integer value " + Text.Repr(child.Value) + "; using " + def);
                return def;
            }
            return value.Value;
        }

        static bool ParseBool(XElement element, string tag, bool def, List<string> warnings)
        {
            var child = Find(element, tag);
            if (child == null || child.FirstNode == null)
                return def;
            string text = child.Value.Trim().ToLowerInvariant();
            if (text == "true" || text == "1")
                return true;
            if (text == "false" || text == "0")
                return false;
            warnings.Add("<" + tag + "> has unexpected value " + Text.Repr(child.Value)
                + " (expected true/false); using " + (def ? "true" : "false"));
            return def;
        }

        public static Level ParseLevelXml(string path, out List<string> warnings)
        {
            warnings = new List<string>();
            XElement root = ParseXmlRoot(path);
            if (root.Name.LocalName != "scenario")
                throw new ToolError(path + ": root element is <" + root.Name.LocalName + ">, expected <scenario> "
                    + "(use gamefile-campaign-file-make-from-xml for campaign files)");

            foreach (string attribute in new[] { "name", "id", "type" })
                if (Attr(root, attribute) == null)
                    throw new ToolError(path + ": <scenario> is missing the @" + attribute + " attribute");

            string levelType = Attr(root, "type");
            if (!Blf.LevelTypeFlagBits.ContainsKey(levelType))
                throw new ToolError(path + ": <scenario> @type " + Text.Repr(levelType) + " is not one of "
                    + string.Join("/", Blf.LevelTypeFlagBits.Keys.OrderBy(k => k)));

            var idValue = ParseInteger(Attr(root, "id"));
            if (idValue == null)
                throw new ToolError(path + ": <scenario> @id " + Text.Repr(Attr(root, "id")) + " is not an integer");

            var level = new Level
            {
                Name = Attr(root, "name"),
                Id = idValue.Value,
                Type = levelType,
                TagsLocation = Attr(root, "tagsLocation") != null ? Attr(root, "tagsLocation") : "default",
                PathToStringsFile = (FindText(root, "pathToStringsFile") ?? "").Trim(),
                ImageFileBase = (FindText(root, "imageFileBase") ?? "").Trim(),
                ScenarioPath = (FindText(root, "scenarioPath") ?? "").Trim(),
                PresenceContextId = ParseInt(root, "presenceContextID", 0, warnings),
                SortOrder = ParseInt(root, "sortOrder", 0, warnings),
                MinimumDesiredPlayers = ParseInt(root, "multiplayerMinimumDesiredPlayers", 0, warnings),
                MaximumDesiredPlayers = ParseInt(root, "multiplayerMaximumDesiredPlayers", 0, warnings),
                VisibleInUi = ParseBool(root, "visibleInUI", true, warnings),
                AllowsSavedFilms = ParseBool(root, "allowsSavedFilms", true, warnings),
            };

            if (!Blf.TagsLocationFlagBits.ContainsKey(level.TagsLocation))
                warnings.Add("@tagsLocation " + Text.Repr(level.TagsLocation) + " is not one of "
                    + string.Join("/", Blf.TagsLocationFlagBits.Keys.OrderBy(k => k)) + "; treating as default");

            if (level.ScenarioPath.Length == 0)
                level.ScenarioPath = level.Name;

            level.Flags = LevelFlags(level);

            var teamsElement = Find(root, "maximumTeams");
            if (teamsElement != null)
            {
                foreach (var child in teamsElement.Elements())
                {
                    string childTag = child.Name.LocalName;
                    if (!Blf.GameEngineTagByName.TryGetValue(childTag, out int engineIndex))
                    {
                        warnings.Add("<" + childTag + "> is not a known game engine tag; ignored");
                        continue;
                    }
                    var value = ParseInteger(child.Value ?? "");
                    if (value == null)
                    {
                        warnings.Add("<" + childTag + "> has non-integer value " + Text.Repr(child.Value ?? "") + "; using 0");
                        value = 0;
                    }
                    level.MaximumTeams[engineIndex] = (int)(value.Value & 0xFFFFFFFF);
                }
            }

            var insertionPointsElement = Find(root, "insertionPoints");
            if (insertionPointsElement != null)
            {
                foreach (var child in insertionPointsElement.Elements("insertionPoint"))
                {
                    var zoneSetText = Attr(child, "zoneSet");
                    var zoneSet = ParseInteger(zoneSetText != null ? zoneSetText : "0");
                    if (zoneSet == null)
                    {
                        warnings.Add("<insertionPoint> @zoneSet " + Text.Repr(zoneSetText) + " is not an integer; using 0");
                        zoneSet = 0;
                    }
                    if (zoneSet < 0 || zoneSet > 0xFFFF)
                    {
                        warnings.Add("<insertionPoint> @zoneSet " + zoneSet + " out of range; clamped");
                        zoneSet = Math.Max(0, Math.Min(zoneSet.Value, 0xFFFF));
                    }
                    level.InsertionPoints.Add(zoneSet.Value);
                }
                if (level.InsertionPoints.Count > Blf.InsertionPointCount)
                {
                    warnings.Add(level.InsertionPoints.Count + " insertion points listed, only the first "
                        + Blf.InsertionPointCount + " are written");
                    level.InsertionPoints = level.InsertionPoints.GetRange(0, Blf.InsertionPointCount);
                }
            }

            return level;
        }

        public static Campaign ParseCampaignXml(string path, out List<string> warnings)
        {
            warnings = new List<string>();
            XElement root = ParseXmlRoot(path);
            if (root.Name.LocalName != "campaign")
                throw new ToolError(path + ": root element is <" + root.Name.LocalName + ">, expected <campaign> "
                    + "(use gamefile-scenario-file-make-from-xml for scenario files)");

            foreach (string attribute in new[] { "name", "id", "flags" })
                if (Attr(root, attribute) == null)
                    throw new ToolError(path + ": <campaign> is missing the @" + attribute + " attribute");

            var idValue = ParseInteger(Attr(root, "id"));
            if (idValue == null)
                throw new ToolError(path + ": <campaign> @id " + Text.Repr(Attr(root, "id")) + " is not an integer");
            var flagsValue = ParseInteger(Attr(root, "flags"));
            if (flagsValue == null)
                throw new ToolError(path + ": <campaign> @flags " + Text.Repr(Attr(root, "flags")) + " is not an integer");

            var campaign = new Campaign
            {
                Name = Attr(root, "name"),
                Id = idValue.Value,
                Flags = flagsValue.Value,
                PathToStringsFile = (FindText(root, "pathToStringsFile") ?? "").Trim(),
            };

            var levelsElement = Find(root, "levels");
            if (levelsElement != null)
            {
                foreach (var child in levelsElement.Elements("level"))
                {
                    var value = ParseInteger(child.Value ?? "");
                    if (value == null)
                    {
                        warnings.Add("<level> has non-integer value " + Text.Repr(child.Value ?? "") + "; ignored");
                        continue;
                    }
                    campaign.Scenarios.Add(value.Value);
                }
            }
            if (campaign.Scenarios.Count > Blf.CampaignScenarioCount)
            {
                warnings.Add(campaign.Scenarios.Count + " levels listed, only the first "
                    + Blf.CampaignScenarioCount + " are written");
                campaign.Scenarios = campaign.Scenarios.GetRange(0, Blf.CampaignScenarioCount);
            }

            return campaign;
        }

        static IEnumerable<string> DiscoverLevelXmls(string dataRoot)
        {
            var files = new List<string>();
            foreach (string file in Directory.EnumerateFiles(dataRoot, "*.xml", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.ToLowerInvariant().EndsWith(".xml"))
                    files.Add(file);
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        static void WriteU32BE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        static void WriteI32BE(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
