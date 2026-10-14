using System;
using System.Collections.Generic;
using System.IO;

namespace Hammer
{
    // dispatch + argparse-like per-subcommand parsing for the 8 `gamefile-*` commands.
    // (the python argparse prog name becomes `Hammer`.)
    static class Program
    {
        const string TopUsage =
"usage: Hammer [-h] command ...\n"
+ "\n"
+ "recreate the hammer gamefile level/campaign import/export commands.\n"
+ "\n"
+ "forward (xml -> blf), from tools/xml_to_mapinfo.py:\n"
+ "  gamefile-scenario-file-make-from-xml           make one .mapinfo from a scenario xml\n"
+ "  gamefile-campaign-file-make-from-xml           make one .campaign from a campaign xml\n"
+ "  gamefile-scenario-make-all                     make .mapinfo files for every scenario xml\n"
+ "  gamefile-campaign-make-all                     make .campaign files for every campaign xml\n"
+ "\n"
+ "reverse (blf -> xml), from tools/mapinfo_to_xml.py:\n"
+ "  gamefile-scenario-file-make-from-mapinfo       make one scenario xml from a .mapinfo file\n"
+ "  gamefile-campaign-file-make-from-mapinfo       make one campaign xml from a .campaign file\n"
+ "  gamefile-scenario-make-from-mapinfo-all        make scenario xml for every .mapinfo file\n"
+ "  gamefile-campaign-make-from-mapinfo-all        make campaign xml for every .campaign file";

        sealed class Parsed
        {
            public readonly List<string> Positional = new List<string>();
            public readonly Dictionary<string, string> Options = new Dictionary<string, string>();
            public bool Help;
            public string UnknownOption;
            public string MissingValue;

            public string Opt(string name)
            {
                string v;
                return Options.TryGetValue(name, out v) ? v : null;
            }
            public bool HasFlag(string name) => Options.ContainsKey(name);
            public string Pos(int i) => i < Positional.Count ? Positional[i] : null;
        }

        static int Main(string[] args)
        {
            Blf.Verify();

            if (args.Length == 0)
                return UsageError("the following arguments are required: command");
            if (args[0] == "-h" || args[0] == "--help")
            {
                Diag.WriteLine(TopUsage);
                return 0;
            }

            string command = args[0];
            var rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);

            switch (command)
            {
                case "gamefile-scenario-file-make-from-xml":
                    return ForwardFile(command, rest, false);
                case "gamefile-campaign-file-make-from-xml":
                    return ForwardFile(command, rest, true);
                case "gamefile-scenario-make-all":
                case "gamefile-campaign-make-all":
                    return ForwardAll(command, rest);
                case "gamefile-scenario-file-make-from-mapinfo":
                case "gamefile-campaign-file-make-from-mapinfo":
                    return ReverseFile(command, rest);
                case "gamefile-scenario-make-from-mapinfo-all":
                case "gamefile-campaign-make-from-mapinfo-all":
                    return ReverseAll(command, rest);
                default:
                    return UsageError("argument command: invalid choice: " + Text.Repr(command)
                        + " (choose from gamefile-scenario-file-make-from-xml, gamefile-campaign-file-make-from-xml,"
                        + " gamefile-scenario-make-all, gamefile-campaign-make-all,"
                        + " gamefile-scenario-file-make-from-mapinfo, gamefile-campaign-file-make-from-mapinfo,"
                        + " gamefile-scenario-make-from-mapinfo-all, gamefile-campaign-make-from-mapinfo-all)");
            }
        }

        // ---------- forward

        static int ForwardFile(string command, string[] rest, bool campaign)
        {
            var valueOpts = new HashSet<string> { "--output", "--data-root", "--copy-signature-from" };
            var flagOpts = new HashSet<string> { "--zero-signature" };
            var p = Parse(rest, valueOpts, flagOpts);
            if (p.Help)
            {
                Diag.WriteLine("usage: Hammer " + command + " [-h] [-o OUTPUT] [--data-root DATA_ROOT]"
                    + " [--zero-signature | --copy-signature-from BLF] [" + (campaign ? "campaign" : "xml") + "]"
                    + " [signature_provider]");
                return 0;
            }
            if (p.UnknownOption != null) return UsageError("unrecognized arguments: " + p.UnknownOption);
            if (p.MissingValue != null) return UsageError("argument " + p.MissingValue + ": expected one argument");
            string inputLabel = campaign ? "campaign" : "xml";
            if (p.Positional.Count < 1) return UsageError("the following arguments are required: " + inputLabel);
            if (p.Positional.Count > 2) return UsageError("unrecognized arguments: " + string.Join(" ", p.Positional.GetRange(2, p.Positional.Count - 2)));

            string provider = p.Pos(1) != null ? p.Pos(1) : "secrets";
            int providerWarn = ValidateSignatureProvider(p, ref provider);
            if (providerWarn != 0) return providerWarn;

            var opt = new ForwardOptions { SignatureProvider = provider, CopySignatureFrom = p.Opt("--copy-signature-from") };
            string dataRoot = DataRootOf(p);
            string mapsInfoDir = MapsInfoDir();

            string xmlPath = Path.GetFullPath(p.Pos(0));
            if (!File.Exists(xmlPath))
            {
                Diag.Stderr("error: " + xmlPath + ": no such file");
                return 1;
            }

            try
            {
                if (!campaign)
                {
                    var level = ForwardConverter.ParseLevelXml(xmlPath, out _);
                    string outputPath = p.Opt("--output") != null ? Path.GetFullPath(p.Opt("--output")) : ForwardConverter.DefaultScenarioOutput(mapsInfoDir, level);
                    var result = ForwardConverter.ConvertScenario(xmlPath, outputPath, dataRoot, opt);
                    Diag.WriteLine(xmlPath + " -> " + outputPath + " ("
                        + (Blf.StartOfFileSize + Blf.ScenarioSize + Blf.EndOfFileWithRsaSize) + " bytes, map id "
                        + result.Id + ", flags 0x" + ((uint)result.Flags).ToString("X8") + ")");
                }
                else
                {
                    var campaignModel = ForwardConverter.ParseCampaignXml(xmlPath, out _);
                    string outputPath = p.Opt("--output") != null ? Path.GetFullPath(p.Opt("--output")) : ForwardConverter.DefaultCampaignOutput(mapsInfoDir, xmlPath);
                    var result = ForwardConverter.ConvertCampaign(xmlPath, outputPath, dataRoot, opt);
                    Diag.WriteLine(xmlPath + " -> " + outputPath + " ("
                        + (Blf.StartOfFileSize + Blf.CampaignSize + Blf.EndOfFileWithRsaSize) + " bytes, campaign id "
                        + result.Id + ", " + result.Scenarios.Count + " levels)");
                }
            }
            catch (IOException error) { Diag.Stderr("error: " + error.Message); return 1; }
            catch (UnauthorizedAccessException error) { Diag.Stderr("error: " + error.Message); return 1; }
            catch (ToolError error) { Diag.Stderr("error: " + error.Message); return 1; }
            return 0;
        }

        static int ForwardAll(string command, string[] rest)
        {
            var valueOpts = new HashSet<string> { "--data-root", "--copy-signature-from" };
            var flagOpts = new HashSet<string> { "--zero-signature", "--include-missing-strings" };
            var p = Parse(rest, valueOpts, flagOpts);
            if (p.Help)
            {
                Diag.WriteLine("usage: Hammer " + command + " [-h] [--data-root DATA_ROOT]"
                    + " [--zero-signature | --copy-signature-from BLF] [--include-missing-strings]"
                    + " [signature_provider]");
                return 0;
            }
            if (p.UnknownOption != null) return UsageError("unrecognized arguments: " + p.UnknownOption);
            if (p.MissingValue != null) return UsageError("argument " + p.MissingValue + ": expected one argument");
            if (p.Positional.Count > 1) return UsageError("unrecognized arguments: " + string.Join(" ", p.Positional.GetRange(1, p.Positional.Count - 1)));

            string provider = p.Pos(0) != null ? p.Pos(0) : "secrets";
            int providerWarn = ValidateSignatureProvider(p, ref provider);
            if (providerWarn != 0) return providerWarn;

            var opt = new ForwardOptions { SignatureProvider = provider, CopySignatureFrom = p.Opt("--copy-signature-from") };
            string dataRoot = DataRootOf(p);
            string mapsInfoDir = MapsInfoDir();
            string wantedRootTag = command.EndsWith("scenario-make-all") ? "scenario" : "campaign";
            return ForwardConverter.BulkMakeAll(dataRoot, mapsInfoDir, wantedRootTag, opt, p.HasFlag("--include-missing-strings"));
        }

        static int ValidateSignatureProvider(Parsed p, ref string provider)
        {
            if (p.HasFlag("--zero-signature") && p.Opt("--copy-signature-from") != null)
                return UsageError("--zero-signature and --copy-signature-from are mutually exclusive");
            if (p.HasFlag("--zero-signature"))
                provider = "none";
            if (provider != "secrets" && provider != "none")
                return UsageError("unknown signature provider " + Text.Repr(provider) + " (expected `secrets` or `none`)");
            if (provider == "secrets")
                Diag.Warn("signature provider `secrets`: the secure secrets key file is not part of "
                    + "this repository, so rsa signatures cannot be produced; an existing output "
                    + "file's signature is preserved and new files get a zero signature");
            return 0;
        }

        // ---------- reverse

        static int ReverseFile(string command, string[] rest)
        {
            var valueOpts = new HashSet<string> { "--output", "--name", "--data-root", "--strings-file", "--info-dir" };
            var flagOpts = new HashSet<string> { "--no-strings" };
            var p = Parse(rest, valueOpts, flagOpts);
            if (p.Help)
            {
                Diag.WriteLine("usage: Hammer " + command + " [-h] [-o OUTPUT] [--name NAME] [--data-root DATA_ROOT]"
                    + " [--strings-file STRINGS_FILE] [--no-strings] [--info-dir INFO_DIR] "
                    + (command.Contains("campaign") ? "campaign" : "mapinfo"));
                return 0;
            }
            if (p.UnknownOption != null) return UsageError("unrecognized arguments: " + p.UnknownOption);
            if (p.MissingValue != null) return UsageError("argument " + p.MissingValue + ": expected one argument");
            string required = command.Contains("campaign") ? "campaign" : "mapinfo";
            if (p.Positional.Count < 1) return UsageError("the following arguments are required: " + required);
            if (p.Positional.Count > 1) return UsageError("unrecognized arguments: " + string.Join(" ", p.Positional.GetRange(1, p.Positional.Count - 1)));

            var opt = BuildReverseOptions(p);

            string path = Path.GetFullPath(p.Pos(0));
            if (!File.Exists(path))
            {
                Diag.Stderr("error: " + path + ": no such file");
                return 1;
            }
            try
            {
                if (command.Contains("campaign"))
                    ReverseConverter.ConvertCampaign(path, opt);
                else
                    ReverseConverter.ConvertScenario(path, opt);
            }
            catch (IOException error) { Diag.Stderr("error: " + error.Message); return 1; }
            catch (UnauthorizedAccessException error) { Diag.Stderr("error: " + error.Message); return 1; }
            catch (ToolError error) { Diag.Stderr("error: " + error.Message); return 1; }
            return 0;
        }

        static int ReverseAll(string command, string[] rest)
        {
            var valueOpts = new HashSet<string> { "--data-root", "--strings-file", "--info-dir" };
            var flagOpts = new HashSet<string> { "--no-strings" };
            var p = Parse(rest, valueOpts, flagOpts);
            if (p.Help)
            {
                Diag.WriteLine("usage: Hammer " + command + " [-h] [--data-root DATA_ROOT]"
                    + " [--strings-file STRINGS_FILE] [--no-strings] [--info-dir INFO_DIR]");
                return 0;
            }
            if (p.UnknownOption != null) return UsageError("unrecognized arguments: " + p.UnknownOption);
            if (p.MissingValue != null) return UsageError("argument " + p.MissingValue + ": expected one argument");
            if (p.Positional.Count > 0) return UsageError("unrecognized arguments: " + string.Join(" ", p.Positional));

            var opt = BuildReverseOptions(p);
            string wantedExtension = command.Contains("scenario") ? "mapinfo" : "campaign";
            return ReverseConverter.MakeAll(opt, MapsInfoDir(), wantedExtension);
        }

        static ReverseOptions BuildReverseOptions(Parsed p)
        {
            string stringsFile = p.Opt("--strings-file") != null ? Path.GetFullPath(p.Opt("--strings-file")) : null;
            string infoDir = p.Opt("--info-dir") != null ? Path.GetFullPath(p.Opt("--info-dir")) : null;
            return new ReverseOptions
            {
                DataRoot = DataRootOf(p),
                Output = p.Opt("--output"),
                Name = p.Opt("--name"),
                StringsFile = stringsFile,
                NoStrings = p.HasFlag("--no-strings"),
                InfoDir = infoDir,
            };
        }

        // ---------- shared

        // default inputs/outputs are relative to the current working directory, which
        // is assumed to contain `data` (level descriptors + strings) and `maps` (the
        // .mapinfo / .campaign files under maps/info). everything stays overridable.
        static string DataRootOf(Parsed p)
        {
            string provided = p.Opt("--data-root");
            if (provided != null)
                return Path.GetFullPath(provided);
            return Path.Combine(Directory.GetCurrentDirectory(), "data");
        }

        static string MapsInfoDir()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "maps", "info");
        }

        static Parsed Parse(string[] tokens, HashSet<string> valueOptions, HashSet<string> flagOptions)
        {
            var result = new Parsed();
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (t == "-h" || t == "--help")
                {
                    result.Help = true;
                    continue;
                }
                string name = t;
                string inlineValue = null;
                if (t.StartsWith("--") && t.IndexOf('=') >= 0)
                {
                    int eq = t.IndexOf('=');
                    name = t.Substring(0, eq);
                    inlineValue = t.Substring(eq + 1);
                }
                if (name == "-o")
                    name = "--output";

                if (flagOptions.Contains(name))
                {
                    result.Options[name] = "";
                    continue;
                }
                if (valueOptions.Contains(name))
                {
                    string val;
                    if (inlineValue != null)
                    {
                        val = inlineValue;
                    }
                    else
                    {
                        i++;
                        if (i >= tokens.Length)
                        {
                            result.MissingValue = name;
                            return result;
                        }
                        val = tokens[i];
                    }
                    result.Options[name] = val;
                    continue;
                }
                if (t.Length > 1 && t[0] == '-')
                {
                    result.UnknownOption = name;
                    return result;
                }
                result.Positional.Add(t);
            }
            return result;
        }

        static int UsageError(string message)
        {
            Diag.Stderr("Hammer: error: " + message);
            return 2;
        }
    }
}
