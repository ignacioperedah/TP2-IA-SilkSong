using System.Globalization;
using System.IO;
using BepInEx;

namespace SilksongRL
{
    public static class DatasetLogger
    {
        private static readonly string CsvPath =
            Path.Combine(Paths.GameRootPath, "SilksongRL_Dataset", "lace1_dataset.csv");

        private const string Header =
            "episode_id,tick,hero_x,hero_y,hero_vel_x,hero_vel_y,hero_hp,boss_x,boss_y,boss_vel_x,boss_vel_y," +
            "boss_hp,attack_idle,attack_comboslash,attack_counter,attack_rapidslash,attack_jslash," +
            "attack_downstab,attack_charge,attack_evade,attack_crossslash,attack_stun,attack_multihit," +
            "move,look,jump,attack,dash,outcome";

        private const int ExpectedObsSize = 21; // Lace 1 (LaceEncounter.vectorObsSize)

        private static StreamWriter writer;
        private static int lastEpisodeId = int.MinValue;
        private static int tickInEpisode = -1;

        private static void EnsureInitialized()
        {
            if (writer != null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(CsvPath));
            bool fileExists = File.Exists(CsvPath);

            writer = new StreamWriter(CsvPath, append: true) { AutoFlush = true };
            if (!fileExists)
                writer.WriteLine(Header);
        }

        public static void LogRow(int episodeId, float[] obs, Action action, int outcome)
        {
            if (obs == null || obs.Length != ExpectedObsSize)
            {
                RLManager.StaticLogger?.LogWarning($"[DatasetLogger] Unexpected observation size: {obs?.Length.ToString() ?? "null"}, skipping row");
                return;
            }

            EnsureInitialized();

            tickInEpisode = (episodeId == lastEpisodeId) ? tickInEpisode + 1 : 0;
            lastEpisodeId = episodeId;

            var fields = new string[2 + obs.Length + 5 + 1];
            int i = 0;
            fields[i++] = episodeId.ToString(CultureInfo.InvariantCulture);
            fields[i++] = tickInEpisode.ToString(CultureInfo.InvariantCulture);
            for (int o = 0; o < obs.Length; o++)
                fields[i++] = obs[o].ToString(CultureInfo.InvariantCulture);
            fields[i++] = ((int)action.move).ToString(CultureInfo.InvariantCulture);
            fields[i++] = ((int)action.look).ToString(CultureInfo.InvariantCulture);
            fields[i++] = (action.jump ? 1 : 0).ToString(CultureInfo.InvariantCulture);
            fields[i++] = (action.attack ? 1 : 0).ToString(CultureInfo.InvariantCulture);
            fields[i++] = (action.dash ? 1 : 0).ToString(CultureInfo.InvariantCulture);
            fields[i++] = outcome.ToString(CultureInfo.InvariantCulture);

            writer.WriteLine(string.Join(",", fields));
        }

        public static void Close()
        {
            writer?.Flush();
            writer?.Close();
            writer = null;
        }
    }
}
