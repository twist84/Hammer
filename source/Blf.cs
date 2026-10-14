using System;
using System.Collections.Generic;
using System.Text;

namespace Hammer
{
    static class Blf
    {
        public const int HeaderSize = 12;
        public const int StartOfFileSize = 48;
        public const int EndOfFileWithRsaSize = 273;
        public const int EndOfFileWithRsaSignatureSize = 256;
        public const int RsaAuthenticationType = 3;

        public static readonly byte[] StartOfFileType = Encoding.ASCII.GetBytes("_blf");
        public static readonly byte[] ScenarioType = Encoding.ASCII.GetBytes("levl");
        public static readonly byte[] CampaignType = Encoding.ASCII.GetBytes("cmpn");
        public static readonly byte[] EndOfFileType = Encoding.ASCII.GetBytes("_eof");

        public const int StartOfFileMajor = 1;
        public const int StartOfFileMinor = 2;
        public const int EndOfFileMajor = 1;
        public const int EndOfFileMinor = 1;

        // byte order mark as written by s_blf_chunk_start_of_file::initialize_BE.
        public static readonly byte[] ByteOrderMarkBytes = new byte[] { 0xFF, 0xFE };

        // ---------- s_blf_chunk_scenario (19792 bytes)

        public const int ScenarioMajor = 3;
        public const int ScenarioMinor = 1;
        public const int ScenarioSize = 19792;

        public const int ScenarioMapIdOffset = 0x0C;
        public const int ScenarioFlagsOffset = 0x10;
        public const int ScenarioNameOffset = 0x14;
        public const int ScenarioDescriptionOffset = 0x314;
        public const int ScenarioImageFileBaseOffset = 0xF14;
        public const int ScenarioScenarioFileOffset = 0x1014;
        public const int ScenarioPresenceContextIdOffset = 0x1114;
        public const int ScenarioSortOrderOffset = 0x1118;
        public const int ScenarioMinimumDesiredPlayersOffset = 0x111C;
        public const int ScenarioMaximumDesiredPlayersOffset = 0x111D;
        public const int ScenarioMaximumTeamsOffset = 0x111E;
        public const int ScenarioInsertionPointsOffset = 0x1130;

        public const int ImageFileBaseLength = 256;
        public const int ScenarioFileLength = 256;
        public const int MaximumTeamsCount = 11;
        public const int InsertionPointCount = 4;

        // s_scenario_insertion_point (3848 bytes)

        public const int InsertionPointSize = 3848;
        public const int InsertionPointZoneSetOffset = 0x2;
        public const int InsertionPointNameOffset = 0x8;
        public const int InsertionPointDescriptionOffset = 0x308;

        // ---------- s_blf_chunk_campaign (4888 bytes)

        public const int CampaignMajor = 1;
        public const int CampaignMinor = 1;
        public const int CampaignSize = 4888;

        public const int CampaignIdOffset = 0x0C;
        public const int CampaignFlagsOffset = 0x10;
        public const int CampaignNameOffset = 0x14;
        public const int CampaignDescriptionOffset = 0x614;
        public const int CampaignScenariosOffset = 0x1214;
        public const int CampaignPadOffset = 0x1314;

        public const int CampaignScenarioCount = 64;

        // ---------- shared level string limits

        public const int NameCount = 12;
        public const int NameLength = 32;
        public const int DescriptionCount = 12;
        public const int DescriptionLength = 128;
        public const int CampaignNameLength = 64;
        public const int CampaignDescriptionLength = 128;

        // ---------- flag bit indices

        public const int LevelFlagFromDlc = 0;
        public const int LevelFlagUnlockable = 1;
        public const int LevelFlagVisibleInUi = 2;
        public const int LevelFlagAllowsSavedFilms = 3;
        public const int LevelFlagIsMainMenu = 4;
        public const int LevelFlagIsCampaign = 5;
        public const int LevelFlagIsMultiplayer = 6;
        public const int LevelFlagInDlcDirectory = 7;
        public const int LevelFlagInTestDirectory = 8;
        public const int LevelFlagInTempDirectory = 9;

        // @type drives one flag bit; `ui` and `mainmenu` both map to the main menu bit.
        public static readonly Dictionary<string, int> LevelTypeFlagBits;
        public static readonly Dictionary<string, int?> TagsLocationFlagBits;

        // reverse-derived tables (mainmenu/`default` aliases dropped)
        public static readonly (int bit, string type)[] TypeBitToType =
        {
            (LevelFlagIsMainMenu, "ui"),
            (LevelFlagIsCampaign, "solo"),
            (LevelFlagIsMultiplayer, "multi"),
        };
        public static readonly (int bit, string name)[] LocationBitToName =
        {
            (LevelFlagInDlcDirectory, "dlc"),
            (LevelFlagInTestDirectory, "test"),
            (LevelFlagInTempDirectory, "temp"),
        };
        public static readonly string[] DirectoryNames = { "dlc", "test", "temp" };

        // (iso-639-1 code, e_language index)
        public static readonly (string code, int index)[] Languages =
        {
            ("en", 0), ("ja", 1), ("de", 2), ("fr", 3), ("es", 4), ("es-mx", 5),
            ("it", 6), ("ko", 7), ("zh-tw", 8), ("zh-cn", 9), ("pt", 10), ("pl", 11),
        };
        public const int LanguageCount = 12;

        // (xml tag, e_game_engine_type index)
        public static readonly (string tag, int index)[] GameEngineTags =
        {
            ("engineCtf", 1), ("engineSlayer", 2), ("engineOddball", 3), ("engineKing", 4),
            ("engineSandbox", 5), ("engineVip", 6), ("engineJuggernaut", 7),
            ("engineTerritories", 8), ("engineAssault", 9), ("engineInfection", 10),
        };
        public static readonly Dictionary<string, int> GameEngineTagByName;

        public const string StringsDisplayField = "display";
        public const string StringsDescriptionField = "description";

        static Blf()
        {
            LevelTypeFlagBits = new Dictionary<string, int>
            {
                { "ui", LevelFlagIsMainMenu },
                { "mainmenu", LevelFlagIsMainMenu },
                { "solo", LevelFlagIsCampaign },
                { "multi", LevelFlagIsMultiplayer },
            };
            TagsLocationFlagBits = new Dictionary<string, int?>
            {
                { "default", null },
                { "dlc", LevelFlagInDlcDirectory },
                { "test", LevelFlagInTestDirectory },
                { "temp", LevelFlagInTempDirectory },
            };
            GameEngineTagByName = new Dictionary<string, int>();
            foreach (var (tag, index) in GameEngineTags)
                GameEngineTagByName[tag] = index;
        }

        // fourcc of a chunk type, matching python `int.from_bytes(chunk_type, "big")`.
        public static uint Fourcc(byte[] chunkType)
        {
            return ((uint)chunkType[0] << 24) | ((uint)chunkType[1] << 16)
                | ((uint)chunkType[2] << 8) | chunkType[3];
        }

        // container-size self-check (the plan's `static_assert`-style guard): catches
        // constant-transcription errors before byte-parity testing.
        public static void Verify()
        {
            if (StartOfFileSize + ScenarioSize + EndOfFileWithRsaSize != 20113)
                throw new InvalidOperationException("scenario container size mismatch");
            if (StartOfFileSize + CampaignSize + EndOfFileWithRsaSize != 5209)
                throw new InvalidOperationException("campaign container size mismatch");
            if (ScenarioSize != ScenarioInsertionPointsOffset + InsertionPointCount * InsertionPointSize)
                throw new InvalidOperationException("scenario size does not cover insertion points");
            if (ScenarioDescriptionOffset != ScenarioNameOffset + NameCount * NameLength * 2)
                throw new InvalidOperationException("scenario name/description offsets mismatch");
            if (ScenarioImageFileBaseOffset
                != ScenarioDescriptionOffset + DescriptionCount * DescriptionLength * 2)
                throw new InvalidOperationException("scenario description/image offsets mismatch");
            if (CampaignDescriptionOffset != CampaignNameOffset + NameCount * CampaignNameLength * 2)
                throw new InvalidOperationException("campaign name/description offsets mismatch");
            if (CampaignScenariosOffset
                != CampaignDescriptionOffset + NameCount * CampaignDescriptionLength * 2)
                throw new InvalidOperationException("campaign description/scenarios offsets mismatch");
            if (CampaignPadOffset != CampaignScenariosOffset + CampaignScenarioCount * 4)
                throw new InvalidOperationException("campaign scenarios/pad offsets mismatch");
            if (CampaignSize != CampaignPadOffset + 4)
                throw new InvalidOperationException("campaign size does not end after pad word");
        }
    }
}
