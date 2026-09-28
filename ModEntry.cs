using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;
using StardewValley.Menus;

namespace FixTitleMuteButton
{
    public class ModEntry : Mod
    {
        public static ModEntry Instance = null!;
        public ModConfig Config = null!;
        public static bool IsMutedOnTitle = false;
        public static string IntendedTitleTrack = "spring_day_ambient";

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            this.Config = helper.ReadConfig<ModConfig>();

            // Ensure in-game audio wakes back up when leaving the title screens
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.Patch(
                original: AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.applyPreferences)),
                postfix: new HarmonyMethod(typeof(TitleMenuPatches), nameof(TitleMenuPatches.ApplyPreferences_Postfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.receiveLeftClick)),
                prefix: new HarmonyMethod(typeof(TitleMenuPatches), nameof(TitleMenuPatches.ReceiveLeftClick_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.draw), new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(TitleMenuPatches), nameof(TitleMenuPatches.Draw_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), nameof(Game1.changeMusicTrack), new[] { typeof(string), typeof(bool), typeof(MusicContext) }),
                prefix: new HarmonyMethod(typeof(TitleMusicPatches), nameof(TitleMusicPatches.ChangeMusicTrack_Prefix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), "updateMusic"),
                prefix: new HarmonyMethod(typeof(TitleMusicPatches), nameof(TitleMusicPatches.UpdateMusic_Prefix))
            );
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e) => RestoreInGameAudio();

        // Catches new farm starts where SaveLoaded does not fire before the intro cutscenes
        private void OnDayStarted(object? sender, DayStartedEventArgs e) => RestoreInGameAudio();

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            // Snapshot slider changes made mid-game so unmuting later respects them
            if (Game1.options != null && Game1.options.musicVolumeLevel > 0f)
            {
                SaveVolumeConfig(Game1.options.musicVolumeLevel, Game1.options.ambientVolumeLevel);
            }
        }

        public static void SilenceTitleAudio()
        {
            if (Game1.options == null) return;

            Game1.options.musicVolumeLevel = 0f;
            Game1.options.ambientVolumeLevel = 0f;
            Game1.musicCategory?.SetVolume(0f);
            Game1.ambientCategory?.SetVolume(0f);
            Game1.musicPlayerVolume = 0f;
            Game1.ambientPlayerVolume = 0f;
        }

        public static void UnmuteOnTitle()
        {
            RestoreInGameAudio();
            Game1.changeMusicTrack(GetContextualTitleTrack());
        }

        public static string GetContextualTitleTrack()
        {
            // Select track based on which UI menu is currently open
            if (TitleMenu.subMenu is CharacterCustomization || Game1.activeClickableMenu is CharacterCustomization)
                return "CloudCountry";

            if (!string.IsNullOrEmpty(IntendedTitleTrack) && IntendedTitleTrack != "none" && IntendedTitleTrack != "silence")
                return IntendedTitleTrack;

            return "spring_day_ambient";
        }

        public static void RestoreInGameAudio()
        {
            if (Game1.options == null) return;

            float targetMusic = Instance.Config.SavedMusicVolume > 0f ? Instance.Config.SavedMusicVolume : 0.75f;
            float targetAmbient = Instance.Config.SavedAmbientVolume > 0f ? Instance.Config.SavedAmbientVolume : 0.75f;

            Game1.options.musicVolumeLevel = targetMusic;
            Game1.options.ambientVolumeLevel = targetAmbient;
            Game1.musicCategory?.SetVolume(targetMusic);
            Game1.ambientCategory?.SetVolume(targetAmbient);
            Game1.musicPlayerVolume = targetMusic;
            Game1.ambientPlayerVolume = targetAmbient;
        }

        public void SaveVolumeConfig(float music, float ambient)
        {
            if (music > 0f) this.Config.SavedMusicVolume = music;
            if (ambient > 0f) this.Config.SavedAmbientVolume = ambient;
            this.Helper.WriteConfig(this.Config);
        }
    }

    public static class TitleMusicPatches
    {
        public static bool IsOnTitleMenu()
        {
            // Strictly check if TitleMenu or one of its child submenus is active
            return Game1.activeClickableMenu is TitleMenu || TitleMenu.subMenu != null;
        }

        public static bool ChangeMusicTrack_Prefix(ref string newTrackName)
        {
            if (IsOnTitleMenu())
            {
                // Cache the requested cue so we know what to restore on unmute
                if (newTrackName != "none" && newTrackName != "silence")
                {
                    ModEntry.IntendedTitleTrack = newTrackName;
                }

                if (ModEntry.IsMutedOnTitle && newTrackName != "none" && newTrackName != "silence")
                {
                    newTrackName = "none";
                }
            }

            return true;
        }

        public static void UpdateMusic_Prefix()
        {
            // Prevent the engine from auto-ramping music volume back up while on title menus
            if (IsOnTitleMenu() && ModEntry.IsMutedOnTitle)
            {
                if (Game1.options.musicVolumeLevel > 0f || Game1.musicPlayerVolume > 0f)
                {
                    ModEntry.SilenceTitleAudio();
                }
            }
        }
    }

    public static class TitleMenuPatches
    {
        private const int ICON_UNMUTED_X = 128;
        private const int ICON_MUTED_X = 137;

        public static void ApplyPreferences_Postfix(TitleMenu __instance)
        {
            if (__instance.startupPreferences == null || __instance.muteMusicButton == null)
                return;

            ModEntry.IsMutedOnTitle = __instance.startupPreferences.startMuted;

            // Remember pre-existing slider settings
            if (Game1.options != null && Game1.options.musicVolumeLevel > 0f)
            {
                ModEntry.Instance.SaveVolumeConfig(Game1.options.musicVolumeLevel, Game1.options.ambientVolumeLevel);
            }

            if (ModEntry.IsMutedOnTitle)
            {
                ModEntry.SilenceTitleAudio();
                __instance.muteMusicButton.sourceRect.X = ICON_MUTED_X;
            }
            else
            {
                __instance.muteMusicButton.sourceRect.X = ICON_UNMUTED_X;
            }
        }

        public static void Draw_Prefix(TitleMenu __instance)
        {
            if (__instance.muteMusicButton == null || __instance.startupPreferences == null)
                return;

            // Guard against vanilla UV desyncs caused by window resize or submenu returns
            int targetX = ModEntry.IsMutedOnTitle ? ICON_MUTED_X : ICON_UNMUTED_X;
            if (__instance.muteMusicButton.sourceRect.X != targetX)
            {
                __instance.muteMusicButton.sourceRect.X = targetX;
            }
        }

        public static bool ReceiveLeftClick_Prefix(TitleMenu __instance, int x, int y)
        {
            if (__instance.HasActiveUser && __instance.muteMusicButton != null && __instance.muteMusicButton.containsPoint(x, y))
            {
                // Invert state intentionally rather than doing vanilla's blind texture swap
                bool willBeMuted = !__instance.startupPreferences.startMuted;
                __instance.startupPreferences.startMuted = willBeMuted;
                ModEntry.IsMutedOnTitle = willBeMuted;

                if (willBeMuted)
                {
                    if (Game1.options.musicVolumeLevel > 0f)
                    {
                        ModEntry.Instance.SaveVolumeConfig(Game1.options.musicVolumeLevel, Game1.options.ambientVolumeLevel);
                    }

                    ModEntry.SilenceTitleAudio();
                    __instance.muteMusicButton.sourceRect.X = ICON_MUTED_X;
                }
                else
                {
                    ModEntry.UnmuteOnTitle();
                    __instance.muteMusicButton.sourceRect.X = ICON_UNMUTED_X;
                }

                Game1.playSound("drumkit6");
                __instance.startupPreferences.savePreferences(async: false);

                return false; // Skip vanilla toggle logic
            }

            return true;
        }
    }
}
