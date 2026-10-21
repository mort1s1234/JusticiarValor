using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using JusticiarValor;
using RimWorld;
using Verse;
using Verse.AI;
using Vector3 = UnityEngine.Vector3;
#if RIMWORLD_1_6
using UnityEngine;
#else
using Microsoft.Extensions.Logging;
using Waylith;
using Waylith.Logging;
#endif

namespace JusticiarValorValidation
{
    // In-game validation of Justiciar Valor, active only with -jvvalidate. Runs on a quick-test colony: exercises every
    // feature against the real game, saves and reloads, then lays out every effect for screenshots. Writes a report
    // line by line (PASS/FAIL) and shuts the game down. With -jvrecord it records the effects instead (EffectRecorder).
    // RimWorld 1.7: driven by a native engine frame callback; RimWorld 1.6 (RIMWORLD_1_6): by a MonoBehaviour.
    [StaticConstructorOnStartup]
    public static class JusticiarValidation
    {
#if RIMWORLD_1_6
        private const string GameVersion = "1.6";
#else
        private const string GameVersion = "1.7";
#endif
        private const string SaveName = "JusticiarValor-Validation-" + GameVersion;
        public const string ShotPrefix = "jv-" + GameVersion + "-";
        private static readonly StringBuilder report = new StringBuilder();
        private static readonly ConcurrentQueue<string> engineLog = new ConcurrentQueue<string>();
        private static string reportPath;
        private static bool started;
        private static bool finished;
        private static int failures;
        private static int waitFrames;
        private static string pendingShot;
        private static EffectRecorder.Frame pendingFrame;
        // -jvrecord: record the effects (EffectRecorder) instead of validating.
        private static readonly bool recording = GenCommandLine.CommandLineArgPassed("jvrecord");
        private static Func<bool> waitUntil;
        // Condition waits are timed in real seconds: the 1.6 window can run at hundreds of frames a second.
        private static readonly System.Diagnostics.Stopwatch waitClock = new System.Diagnostics.Stopwatch();
        private const double WaitSeconds = 180;
        private static IEnumerator<object> routine;
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        // While set the game runs at normal speed; otherwise it is kept paused and advanced by the checks.
        public static bool RunGame;

        static JusticiarValidation()
        {
            if (!GenCommandLine.CommandLineArgPassed("jvvalidate"))
            {
                return;
            }
            if (!GenCommandLine.TryGetCommandLineArg("jvreport", out reportPath) || string.IsNullOrEmpty(reportPath))
            {
                reportPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "justiciarvalor-validation.txt");
            }
            Line("Game " + VersionControl.CurrentVersionStringWithRev + ", JusticiarValor.dll " + typeof(MapComponent_JusticiarFx).Assembly.Location);
#if RIMWORLD_1_6
            Application.logMessageReceivedThreaded += OnUnityLog;
            var host = new GameObject("JusticiarValorValidation");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<ValidationDriver>();
#else
            LogStream.MessageReceived += OnEngineLog;
            EngineApplication.Instance.OnUpdate += Update;
#endif
        }

#if RIMWORLD_1_6
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning && Mentions(condition))
            {
                engineLog.Enqueue(type + " [Unity] " + condition.Split('\n')[0].Trim());
            }
        }
#else
        private static void OnEngineLog(LogLevel level, string category, string message, string stackTrace)
        {
            if (level >= LogLevel.Warning || Mentions(message))
            {
                engineLog.Enqueue(level + " [" + category + "] " + message.Split('\n')[0].Trim());
            }
        }
#endif

        private static bool Mentions(string text)
        {
            return text != null && (text.IndexOf("Justiciar", StringComparison.OrdinalIgnoreCase) >= 0 || text.Contains("MJV_")
                || text.IndexOf("magenta", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static void Line(string text)
        {
            report.AppendLine(text);
            File.WriteAllText(reportPath, report.ToString());
        }

        private static void Check(bool value, string message)
        {
            if (value)
            {
                Line("PASS " + message);
            }
            else
            {
                failures++;
                Line("FAIL " + message);
            }
        }

        internal static void Update()
        {
            if (finished)
            {
                return;
            }
            try
            {
                if (clock.Elapsed.TotalSeconds > (recording ? 3600 : 900))
                {
                    throw new Exception("Validation timed out");
                }
                if (!started)
                {
                    if (LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
                    {
                        return;
                    }
                    started = true;
                    routine = Run();
                }
                // Not while a save is loading: the world is not there yet (TickManager.CurTimeSpeed asks it).
                if (Current.ProgramState == ProgramState.Playing && Find.World != null && Find.CurrentMap != null && !LongEventHandler.AnyEventNowOrWaiting)
                {
                    Find.TickManager.CurTimeSpeed = RunGame ? TimeSpeed.Normal : TimeSpeed.Paused;
                }
                if (waitFrames > 0)
                {
                    waitFrames--;
                    return;
                }
                if (waitUntil != null)
                {
                    if (!waitUntil())
                    {
                        if (waitClock.Elapsed.TotalSeconds > WaitSeconds)
                        {
                            throw new Exception("A wait did not complete within " + WaitSeconds + " s");
                        }
                        return;
                    }
                    waitUntil = null;
                }
                if (pendingShot != null)
                {
                    string path = Path.Combine(GenFilePaths.ScreenshotFolderPath, pendingShot + ".png");
                    if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    {
                        if (waitClock.Elapsed.TotalSeconds > 60)
                        {
                            throw new Exception("Screenshot " + pendingShot + " was not written");
                        }
                        return;
                    }
                    Line("Screenshot " + path);
                    pendingShot = null;
                }
                if (pendingFrame != null)
                {
                    if (!EffectRecorder.FrameCaptured(pendingFrame))
                    {
                        if (waitClock.Elapsed.TotalSeconds > 60)
                        {
                            throw new Exception("Frame " + pendingFrame.path + " was not captured");
                        }
                        return;
                    }
                    pendingFrame = null;
                }
                if (!routine.MoveNext())
                {
                    Finish();
                    return;
                }
                switch (routine.Current)
                {
                    case int frames:
                        waitFrames = frames;
                        break;
                    case Func<bool> condition:
                        waitUntil = condition;
                        waitClock.Restart();
                        break;
                    case string shot:
                        ScreenshotTaker.TakeNonSteamShot(shot);
                        pendingShot = shot;
                        waitClock.Restart();
                        break;
                    case EffectRecorder.Frame frame:
                        ScreenshotTaker.TakeNonSteamShot(frame.fileName);
                        pendingFrame = frame;
                        waitClock.Restart();
                        break;
                }
            }
            catch (Exception ex)
            {
                failures++;
                Line("FAIL " + ex);
                Log.Error("[Justiciar Valor validation] " + ex);
                Finish();
            }
        }

        private static IEnumerator<object> Run()
        {
            if (recording)
            {
                var recorder = new EffectRecorder(Find.CurrentMap);
                foreach (object step in recorder.Run())
                {
                    yield return step;
                }
                failures += recorder.Failures;
                yield break;
            }
            var scene = new ValidationScene(Find.CurrentMap);
            foreach (object step in scene.Logic())
            {
                yield return step;
            }
            bool effectsOnly = GenCommandLine.CommandLineArgPassed("jvfxonly");
            if (effectsOnly) Line("SCOPE effects, combat, gear, save/reload and staged sermon visuals; quest lifecycle excluded");
            var missions = new MissionValidation(Find.CurrentMap, ValidationScene.TestPawnIds);
            if (!effectsOnly)
            {
                foreach (object step in missions.BeforeSave())
                {
                    yield return step;
                }
            }
            scene.PrepareReload();
            foreach (object step in scene.SaveAndReload(SaveName))
            {
                yield return step;
            }
            var reloadedMissions = new MissionValidation(Find.CurrentMap, ValidationScene.TestPawnIds);
            if (!effectsOnly)
            {
                foreach (object step in reloadedMissions.AfterReload())
                {
                    yield return step;
                }
            }
            var visual = new ValidationScene(Find.CurrentMap);
            foreach (object step in visual.Visuals())
            {
                yield return step;
            }
            failures += scene.Failures + missions.Failures + reloadedMissions.Failures + visual.Failures;
        }

        private static void Finish()
        {
            finished = true;
            if (Find.UIRoot != null)
            {
                Find.UIRoot.screenshotMode.Active = false;
            }
#if RIMWORLD_1_6
            Application.logMessageReceivedThreaded -= OnUnityLog;
#else
            EngineApplication.Instance.OnUpdate -= Update;
            LogStream.MessageReceived -= OnEngineLog;
#endif
            string[] entries = engineLog.ToArray();
            foreach (string entry in entries)
            {
                Line("ENGINE " + entry);
            }
            LogMessage[] verse = Log.Messages.Where(m => m.type != LogMessageType.Message && Mentions(m.text)).ToArray();
            foreach (LogMessage message in verse)
            {
                Line("VERSE " + message.type + " " + message.text.Split('\n')[0].Trim());
            }
            bool engineErrors = entries.Any(e => (e.StartsWith("Error") || e.StartsWith("Critical") || e.StartsWith("Exception")) && Mentions(e));
            Check(verse.Length == 0 && !engineErrors, "no engine or Verse warning or error mentions Justiciar Valor or the magenta fallback shader");
            string result = recording ? "RECORDING" : "RUNTIME_VALIDATION";
            Line(failures == 0 ? result + "_PASSED" : $"{result}_FAILED ({failures} failures)");
            Root.Shutdown();
        }
    }

#if RIMWORLD_1_6
    public sealed class ValidationDriver : UnityEngine.MonoBehaviour
    {
        private void Update() => JusticiarValidation.Update();
    }
#endif
}
