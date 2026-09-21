using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongRL
{
    [BepInPlugin("silksongrl", "SilksongRL", "1.0.0")]
    public class RLManager : BaseUnityPlugin
    {
        // Config entries
        private ConfigEntry<string> configTargetBoss;
        private ConfigEntry<float> configStepInterval;

        public static bool isLoggingEnabled = false;

        // Hero and Boss references (tracked via Harmony patches)
        public static HeroController Hero { get; private set; }
        public static HealthManager Boss { get; private set; }

        // Static logger reference for use in Harmony patches and other classes
        public static BepInEx.Logging.ManualLogSource StaticLogger;

        private float stepInterval;

        private static IBossEncounter currentEncounter;

        private TrainingEpisodeManager episodeManager;

        private float lastStepTime = 0f;

        private int currentEpisodeId = 0;
        private float[] lastLoggedObs;
        private Action lastLoggedHumanAction;

        private void Awake()
        {
            StaticLogger = Logger;
            StaticLogger.LogInfo("SilksongRL Mod loaded.");

            SceneManager.sceneLoaded += OnSceneLoaded;

            configTargetBoss = Config.Bind("Training", "TargetBoss", "Lace_1",
                "Target boss encounter (e.g., Lace_1)");
            configStepInterval = Config.Bind("Training", "StepInterval", 0.1f,
                "Time interval between logging steps in seconds");

            stepInterval = configStepInterval.Value;

            var harmony = new Harmony("silksongrl");
            harmony.PatchAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RLManager.StaticLogger?.LogInfo($"[ScreenCapture] Scene loaded: {scene.name}");

            if (scene.name == "Pre_Menu_Loader" || scene.name == "Pre_Menu_Intro") return;

            // Initialize encounter based on config
            currentEncounter = CreateEncounter(configTargetBoss.Value);
            if (currentEncounter == null)
            {
                StaticLogger.LogError($"[RL] Unknown boss encounter: {configTargetBoss.Value}");
                return;
            }

            // Initialize screen capture updater for hybrid encounters
            // This isn't done in Awake because main camera isn't available yet
            if (currentEncounter.GetObservationType() == ObservationType.Hybrid)
            {
                var screenCapture = currentEncounter.GetScreenCapture();
                if (screenCapture != null)
                {
                    var updater = gameObject.AddComponent<ScreenCaptureUpdater>();
                    updater.Initialize(screenCapture);
                    StaticLogger.LogInfo("[RL] Screen capture updater initialized for hybrid observation");
                }
            }

            episodeManager = new TrainingEpisodeManager(currentEncounter);
            episodeManager.OnSimulateKeyPress = SimulateKeyPress;

            StaticLogger.LogInfo($"[RL] Initialized with encounter: {currentEncounter.GetEncounterName()}");
            StaticLogger.LogInfo($"[RL] Observation size: {currentEncounter.GetObservationSize()}");

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private IBossEncounter CreateEncounter(string bossName)
        {
            switch (bossName)
            {
                case "Lace_1":
                    return new LaceEncounter();
                case "Lace_2":
                    return new LaceSecondEncounter();
                case "Savage_Beastfly":
                    return new SavageBeastflyEncounter();
                default:
                    return null;
            }
        }

        private void OnDestroy()
        {
            DatasetLogger.Close();
        }

        private void Update()
        {
            // Toggle logging (human play, dataset capture) when pressing O
            if (Input.GetKeyDown(KeyCode.O))
            {
                isLoggingEnabled = !isLoggingEnabled;
                if (!isLoggingEnabled)
                    DatasetLogger.Close(); // libera el .csv (nada de lock) mientras el juego sigue corriendo

                StaticLogger.LogInfo($"[RL] Logging {(isLoggingEnabled ? "enabled" : "disabled")}. Hero: {(Hero != null ? "Found" : "Not found")}, Boss: {(Boss != null ? "Found" : "Not found")}");
            }

            // Log resolution diagnostics when pressing L
            if (Input.GetKeyDown(KeyCode.L))
            {
                gameObject.AddComponent<ScreenCaptureTest>();
                ResolutionDiagnostics.LogResolutionInfo(StaticLogger);
                ResolutionDiagnostics.CheckForPotentialIssues(StaticLogger);
            }
        }

        private void FixedUpdate()
        {
            if (!isLoggingEnabled)
                return;

            var previousLoggingState = episodeManager.CurrentState;
            episodeManager.UpdateEpisodeState(Hero, Boss);

            int outcome = -1; // -1 = en curso, 0 = murió Hornet, 1 = murió el boss
            if (previousLoggingState == TrainingEpisodeManager.EpisodeState.Training &&
                (episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.HeroDead ||
                 episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.BossDead ||
                 episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.HeroStuck))
            {
                outcome = (episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.BossDead) ? 1 : 0;

                // Loggear la fila terminal ACA, antes de HandleResetSequence: ese método
                // dispara el reset (F5) en este mismo frame y devuelve true, lo que cortaría
                // la ejecución antes de llegar al LogRow/currentEpisodeId++ de más abajo.
                // Si murió el boss, Boss ya es null en este frame (así lo detecta
                // TrainingEpisodeManager), así que no hay estado fresco para extraer:
                // reusamos la última observación capturada.
                float[] obsToLog = (Hero != null && Boss != null)
                    ? currentEncounter.ExtractObservationArray(Hero, Boss)
                    : lastLoggedObs;
                Action actionToLog = (Hero != null && Boss != null)
                    ? HumanInputReader.ReadCurrentAction()
                    : lastLoggedHumanAction;

                if (obsToLog != null && actionToLog != null)
                    DatasetLogger.LogRow(currentEpisodeId, obsToLog, actionToLog, outcome);

                currentEpisodeId++; // nuevo intento a partir de la próxima fila
            }

            if (episodeManager.HandleResetSequence(Hero, Boss))
                return; // no loggear durante el reset

            if (Time.fixedTime - lastStepTime >= stepInterval)
            {
                lastStepTime = Time.fixedTime;
                if (Hero != null && Boss != null)
                {
                    float[] obs = currentEncounter.ExtractObservationArray(Hero, Boss);
                    Action humanAction = HumanInputReader.ReadCurrentAction();
                    DatasetLogger.LogRow(currentEpisodeId, obs, humanAction, -1);
                    lastLoggedObs = obs;
                    lastLoggedHumanAction = humanAction;
                }
            }
        }

        // Static flag for F5 simulation
        public static bool simulateF5Press = false;

        private void SimulateKeyPress(KeyCode key)
        {
            if (key == KeyCode.F5)
            {
                simulateF5Press = true;
                StaticLogger.LogInfo("[RL] Simulating F5 key press");
            }
        }

        /// <summary>
        /// Harmony patch to automatically catch Hero spawns.
        /// </summary>
        [HarmonyPatch(typeof(HeroController), "Awake")]
        public static class HeroController_Awake_Patch
        {
            static void Postfix(HeroController __instance)
            {
                Hero = __instance;
                StaticLogger.LogInfo("[RL] Hero found and assigned (Harmony patch)");
            }
        }

        /// <summary>
        /// Harmony patch to automatically catch Boss spawns.
        /// </summary>
        [HarmonyPatch(typeof(HealthManager), "Awake")]
        public static class HealthManager_Awake_Patch
        {
            static void Postfix(HealthManager __instance)
            {
                // Only assign if we have an encounter configured and this matches
                if (currentEncounter != null && currentEncounter.IsEncounterMatch(__instance))
                {
                    Boss = __instance;
                    StaticLogger.LogInfo($"[RL] Boss locked: {__instance.name} (Harmony patch)");
                }
            }
        }
    }
}
