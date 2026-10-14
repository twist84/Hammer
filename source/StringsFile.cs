using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hammer
{
    // strings-file support: `<key> = <value>` parsing (forward), the idempotent merge
    // used by the reverse tool, the NEW file header (retargeted to `hammer`), and the
    // per-map block emission. ported from xml_to_mapinfo.py / mapinfo_to_xml.py.
    static class StringsFile
    {
        // retargeted replacement for the python tools' NEW_STRINGS_FILE_HEADER: it now
        // references the `hammer` tool instead of tools/xml_to_mapinfo.py/mapinfo_to_xml.py.
        const string NewStringsFileHeader =
"# level strings recovered from the blf level/campaign files\n" +
"#\n" +
"# build-time source for the localized `name`/`description` arrays of the `levl`\n" +
"# chunk in maps/info/<map>.mapinfo; parsed by hammer\n" +
"# (`gamefile-scenario-file-make-from-xml`) and written by hammer\n" +
"# (`gamefile-scenario-file-make-from-mapinfo`).\n" +
"#\n" +
"# format: `<key> = <value>`; lines starting with `#` are comments.\n" +
"#   key = <scenario>.<field>[.<lang>]\n" +
"#   <field>     display | description | insertion<N>.display | insertion<N>.description\n" +
"#   <lang>      en ja de fr es es-mx it ko zh-tw zh-cn pt pl (no suffix = english)";

        static string[] NewStringsFileHeaderLines() => NewStringsFileHeader.Split('\n');

        // parse a `<key> = <value>` strings file; `#` starts a comment line. only
        // whitespace around the key and between `=` and the value is insignificant:
        // a trailing space is part of the string.
        public static Dictionary<string, string> ParseStringsFile(string path)
        {
            var strings = new Dictionary<string, string>();
            string text = File.ReadAllText(path, new UTF8Encoding(false));
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string stripped = line.Trim();
                int lineNumber = i + 1;
                if (stripped.Length == 0 || stripped.StartsWith("#") || stripped.IndexOf('=') < 0)
                    continue;
                int eq = line.IndexOf('=');
                string key = line.Substring(0, eq).Trim();
                if (key.Length == 0)
                {
                    Diag.Warn(path + ":" + lineNumber + ": entry with empty key ignored");
                    continue;
                }
                string value = line.Substring(eq + 1).TrimStart().TrimEnd('\r', '\n');
                strings[key] = value;
            }
            return strings;
        }

        public static string StringsKey(string scenarioName, string field, string languageCode)
        {
            if (languageCode == null || languageCode == "en")
                return scenarioName + "." + field;
            return scenarioName + "." + field + "." + languageCode;
        }

        // 'ok', 'missing' (element present, file absent) or 'unspecified'.
        public static string StringsFileStatus(string pathToStringsFile, string dataRoot)
        {
            if (pathToStringsFile.Length == 0)
                return "unspecified";
            string stringsPath = ResolveUnder(dataRoot, pathToStringsFile);
            return File.Exists(stringsPath) ? "ok" : "missing";
        }

        // os.path.join(data_root, path.replace("\\", "/"))
        public static string ResolveUnder(string dataRoot, string relPath)
        {
            return Path.GetFullPath(Path.Combine(dataRoot, relPath.Replace('\\', '/')));
        }

        // <name>.<field>[.<lang>] = <value> lines for one map, non-empty slots only.
        public static List<string> StringsBlockLines(string name, string[] names, string[] descriptions,
            List<(string[] names, string[] descriptions)> insertionPoints)
        {
            var lines = new List<string>();
            Emit(lines, name, Blf.StringsDisplayField, names);
            Emit(lines, name, Blf.StringsDescriptionField, descriptions);
            if (insertionPoints != null)
            {
                for (int index = 0; index < insertionPoints.Count; index++)
                {
                    Emit(lines, name, "insertion" + index + "." + Blf.StringsDisplayField, insertionPoints[index].names);
                    Emit(lines, name, "insertion" + index + "." + Blf.StringsDescriptionField, insertionPoints[index].descriptions);
                }
            }
            return lines;
        }

        static void Emit(List<string> lines, string name, string field, string[] values)
        {
            foreach (var (code, index) in Blf.Languages)
            {
                string value = index < values.Length ? values[index] : "";
                if (value.Length == 0)
                    continue;
                string key = name + "." + field + (code == "en" ? "" : "." + code);
                lines.Add(key + " = " + value);
            }
        }

        static string StringsKeyOf(string line)
        {
            string stripped = line.Trim();
            if (stripped.Length == 0 || stripped.StartsWith("#") || stripped.IndexOf('=') < 0)
                return null;
            string key = stripped.Substring(0, stripped.IndexOf('=')).Trim();
            return key.Length == 0 ? null : key;
        }

        // upsert this map's block into the strings file, preserving everything else.
        // lines whose key starts with `<name>.` (and the directly preceding `# <name>`
        // comment) are replaced in place, and the file's trailing blank lines are kept
        // at the end of the file, so running the tool twice leaves it byte-identical.
        public static void MergeStringsFile(string path, string name, List<string> blockLines)
        {
            var existing = new List<string>();
            int trailingBlankLines = 0;
            if (File.Exists(path))
            {
                byte[] raw = File.ReadAllBytes(path);
                string text = DecodeUtf8Sig(raw).Replace("\r\n", "\n");
                string content = text.TrimEnd('\n');
                existing = content.Length > 0 ? new List<string>(content.Split('\n')) : new List<string>();
                // write_text re-adds one trailing newline; keep any extra blank lines.
                trailingBlankLines = Math.Max(0, text.Length - content.Length - 1);
            }

            var kept = new List<string>();
            int? anchor = null;
            string prefix = name + ".";
            foreach (string line in existing)
            {
                string key = StringsKeyOf(line);
                if (key != null && key.StartsWith(prefix))
                {
                    if (anchor == null)
                    {
                        if (kept.Count > 0 && kept[kept.Count - 1].Trim() == "# " + name)
                            kept.RemoveAt(kept.Count - 1);
                        anchor = kept.Count;
                    }
                    continue;
                }
                kept.Add(line);
            }

            if (anchor == null)
            {
                if (existing.Count == 0)
                {
                    var nb = new List<string>(NewStringsFileHeaderLines());
                    nb.Add("");
                    nb.Add("# " + name);
                    nb.AddRange(blockLines);
                    kept.AddRange(nb);
                }
                else
                {
                    if (kept.Count > 0 && kept[kept.Count - 1].Trim() != "")
                        kept.Add("");
                    kept.Add("# " + name);
                    kept.AddRange(blockLines);
                }
            }
            else
            {
                var block = new List<string>();
                block.Add("# " + name);
                block.AddRange(blockLines);
                kept.InsertRange(anchor.Value, block);
            }

            for (int i = 0; i < trailingBlankLines; i++)
                kept.Add("");

            TextFile.WriteText(path, kept.ToArray(), false, false, true);
        }

        static string DecodeUtf8Sig(byte[] raw)
        {
            int start = 0;
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                start = 3;
            return new UTF8Encoding(false).GetString(raw, start, raw.Length - start);
        }
    }
}
