# McLazer fork changes

Every edit to an upstream file is listed here, so upstream merges can be checked hook by hook.
Fork-only code lives in `osu.Game/Fork/` and is not listed line by line.

Upstream base: `ppy/osu` commit `77df204` (6 Nov 2026).

## M0: McLazer boots

| File | What changed | Why |
| --- | --- | --- |
| `osu.Desktop/osu.Desktop.csproj` | `AssemblyName`, `AssemblyTitle`, `Title`, `Product` are `McLazer`. `ApplicationIcon` and the embedded icon are `mclazer.ico`. | No "osu!" branding. The binary is `McLazer`, not `osu!`. |
| `osu.Desktop/mclazer.ico` (new) | Placeholder icon. | Replaces `lazer.ico` as the window icon. |
| `osu.Desktop/OsuGameDesktop.cs` | `SetHost` loads `mclazer.ico`. | Window icon. |
| `osu.Desktop/OsuGameDesktop.cs` | `IsPackageManaged` is always `true`. | Skips Velopack setup in `Program.cs` and the Velopack restart. |
| `osu.Desktop/OsuGameDesktop.cs` | `CreateUpdateManager` returns the base `UpdateManager`. | `NoActionUpdateManager` polls `api.github.com` for ppy releases. The base class never checks. |
| `osu.Desktop/Program.cs` | `base_game_name` is `mclazer` (`mclazer-development` in Debug). | Own data folder: `~/.local/share/mclazer`. |
| `osu.Desktop/Program.cs` | Sets `OSU_DISABLE_ERROR_REPORTING=1` at the top of `Main`. | `SentryLogger` returns before initialising. No edit to `SentryLogger` itself. |
| `osu.Desktop/Program.cs` | The legacy IPC provider (TCP port 45357) is not started. | It would take the port from a running lazer. |
| `osu.Game/OsuGameBase.cs` | `GAME_NAME` is `McLazer` (`McLazer (development)` in Debug). | Window title and host name. |
| `osu.Game/OsuGame.cs` | `IPC_PIPE_NAME` is `mclazer` (`mclazer-debug` in Debug). | With lazer running, McLazer would otherwise act as lazer's second instance. |
| `.vscode/launch.json`, `.idea/.../osu_.xml`, `.idea/.../Tournament.xml`, `.run/osu! (Second Client).run.xml` | `osu!.dll` is `McLazer.dll`. | Follows the `AssemblyName` change. |
| `osu.Game/OsuGameBase.cs` | Adds a resource store for `osu.Game/Resources` before the `osu.Game.Resources` store. | Fork assets replace upstream assets of the same name with no edit at the use site. |
| `osu.Game/Resources/Textures/Menu/logo.png` (new) | Placeholder menu logo. | Replaces the osu! logo wherever `Menu/logo` is drawn. |

## Spike: seek engine

Fork code: `osu.Game/Fork/Practice/` (`PracticePlayer`, `SeekSession`). Tests: `osu.Game.Tests/Fork/Practice/`.

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Screens/Select/SoloSongSelect.cs` | Creates `PracticePlayer` where it created `SoloPlayer`. | All local play goes through `PracticePlayer`. |
| `osu.Game/Screens/Play/Player.cs` | `IsResuming` has a protected setter (was private). | A practice seek cancels a resume in progress. |
| `osu.Game/Rulesets/UI/RulesetInputManager.cs` | The `Recorder` setter ends the old recorder's lifetime with `LifetimeEnd = double.MinValue` instead of `Expire()`. | `Expire()` ends the lifetime at the current time. A rewind then brings the recorder back to life, and it records replay-driven input into the run. |
| `osu.Game/Input/Handlers/ReplayInputHandler.cs` | New virtual `StartFrom(double time)`, empty by default. | See next row. |
| `osu.Game/Rulesets/Replays/FramedReplayInputHandler.cs` | Overrides `StartFrom` to set the current frame from a time. | A new handler starts before the first frame. Attached mid-run, it jumps the clock to the start and plays the whole run forward again. `SeekSession` starts it at the current time, so a rewind only walks back. |

## M2: Offline

Fork code: `osu.Game/Fork/McLazerGame.cs`, `osu.Game/Fork/Offline/OfflineAPIAccess.cs`. Tests: `osu.Game.Tests/Fork/Offline/`, `osu.Game.Tests/Fork/McLazerGameTestScene.cs`.

Hooks marked "offline only" check `api is OfflineAPIAccess`, so upstream tests (which use `DummyAPIAccess`) still see upstream behaviour.

| File | What changed | Why |
| --- | --- | --- |
| `osu.Desktop/OsuGameDesktop.cs` | Derives from `McLazerGame` (was `OsuGame`). | `McLazerGame` sets the offline API and endpoints. Tests can run the same class. |
| `osu.Desktop/OsuGameDesktop.cs` | `DiscordRichPresence` is not loaded. | It publishes activity through Discord under osu!'s application ID. |
| `osu.Desktop/Program.cs` | The `--tournament` switch and `TournamentGame` are removed. | The tournament client has its own online API. |
| `osu.Desktop/osu.Desktop.csproj` | No references to the Catch, Mania, Taiko and Tournament projects. `LegacyIpc/**` is excluded from the build. | osu! standard only: the other rulesets are not shipped. The legacy IPC provider needs all four and is never started. |
| `osu.Game/Online/TrustedDomainOnlineStore.cs` | `GetLookupUrl` always returns an empty string. | No resource is fetched online: avatars, covers, song previews. One place covers all three stores that use it. |
| `osu.Game/Beatmaps/LocalCachedBeatmapMetadataSource.cs` | New `offline_build` flag: `FetchCache` returns at once (two callers: the constructor and `BackgroundDataStoreProcessor`), and `Available` is always false. | The beatmap-status cache from `assets.ppy.sh` is never downloaded, and no startup job (user tags, ranked dates) runs from an old copy. Lazer's sync supplies that data. |
| `osu.Game/Screens/Menu/MainMenu.cs` | Offline only: the menu banner does not fetch content, and the supporter display is removed. | The banner request goes straight to `assets.ppy.sh`, not through the API. |
| `osu.Game/Screens/Menu/ButtonSystem.cs` | Offline only: no Multi, Playlists, Daily Challenge or Browse buttons. | No online play, no beatmap listing. |
| `osu.Game/Overlays/Toolbar/Toolbar.cs` | Offline only: no news, changelog, wiki, rankings, beatmap listing, chat or social buttons. | Without the buttons, their hotkeys are gone too. |
| `osu.Game/Screens/Select/BeatmapDetailsArea.Header.cs` | Offline only: the leaderboard scope dropdown offers Local only. | No online leaderboards. |
| `osu.Game/Overlays/FirstRunSetupOverlay.cs` | Offline only: no bundled-beatmap download step and no osu!stable import step. | Beatmaps come from the linked lazer folder. |

Upstream tests that fail by design:

- `TestSceneMainMenu.TestOnlineMenuBannerUntrustedDomain` downloads a banner image from `assets.ppy.sh`. With online resource lookups blocked, the image never loads.
- `BeatmapImporterTests.TestImportFailsWithFilenamesDifferingOnlyInCase` also fails on unmodified upstream on this machine (the archive library rejects the test's own fixture).

No editors (decided 10 Nov 2026: McLazer is strictly a practice client). All offline only:

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Screens/Menu/ButtonSystem.cs` | No top-level Edit button. | The beatmap and skin editor buttons sit behind it. |
| `osu.Game/OsuGame.cs` | `GlobalAction.ToggleSkinEditor` is not handled. | The skin editor hotkey (Ctrl+Shift+S). |
| `osu.Game/Overlays/Settings/Sections/SkinSection.cs` | The skin layout editor button is hidden. | The settings route into the skin editor. |
| `osu.Game/Screens/Select/SoloSongSelect.cs` | `Edit()` returns at once, and the Edit forward action is not offered. | Every song select route into the beatmap editor ends at `Edit()`. |
| `osu.Game/Screens/Select/PanelBeatmapSet.cs` | No Edit item in the set context menu. | Same. |

## M1: linked lazer folder

Fork code: `osu.Game/Fork/Lazer/` (`LazerFolder`, `LazerSync`, `FallbackReadStorage`, `ScreenLazerFolder`), wired in `osu.Game/Fork/McLazerGame.cs`. Tests: `osu.Game.Tests/Fork/Lazer/`.

Beatmaps, skins, collections and key bindings are copied from a snapshot of lazer's database at each start. Lazer's `game.ini` replaces McLazer's at each start, without the login token. Lazer's `input.json` (mouse, tablet) and `framework.ini` (window, volume, frame limiter) replace McLazer's too, in `McLazerGame.CreateStorage`, before the host loads them. None of this needs a hook. Lazer's scores are copied too (see M6).

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Database/RealmFileStore.cs` | New static `ReadFallback` storage and `WithReadFallback(files)`. When set, the store's storage is wrapped so missing files are read from it. | Beatmap files stay in lazer's `files/` folder. Writes and deletes still only touch McLazer's own folder. |
| `osu.Game/OsuGameBase.Importing.cs` | `Import(params string[])` is virtual. | `McLazerGame` refuses every import: drag-and-drop, file arguments and IPC. |
| `osu.Game/Overlays/FirstRunSetupOverlay.cs` | Adds the `ScreenLazerFolder` step when the game is a `McLazerGame` with no lazer folder linked. | The folder picker. The choice is saved in `mclazer.ini` in McLazer's own folder. |
| `osu.Game/Skinning/SkinManager.cs`, `osu.Game/Database/LegacyExporter.cs` | Their own `files` storage goes through `RealmFileStore.WithReadFallback`. | Both open the `files` folder themselves, not through `RealmFileStore`. Without this, skins load with no files and look like the default legacy skin. |

## M3: profiles

Fork code: `osu.Game/Fork/Profiles/` (`ProfileStore`, `ProfilePanel`), wired in `osu.Game/Fork/McLazerGame.cs`. Tests: `osu.Game.Tests/Fork/Profiles/`.

Profiles live in `profiles.json` in McLazer's own folder. The active profile is the API's local user, so new scores carry its ID with no change to scoring code. IDs start at 2,000,000,000 and are never reused. All hooks are offline only.

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Overlays/LoginOverlay.cs` | Shows `ProfilePanel` instead of `LoginPanel`. | The toolbar user button opens the profile picker. There is no login. |
| `osu.Game/Online/Leaderboards/LeaderboardManager.cs` | The Local query also matches `User.OnlineID` to the local user, and a change of local user refetches. | Each profile sees only its own scores. |
| `osu.Game/OsuGame.cs` | `ShowUser` is virtual. `McLazerGame` overrides it to do nothing. | Clicking a player opened the online profile overlay, which only shows failed requests. |
| `osu.Game/Screens/Menu/MainMenu.cs` | The login overlay does not open on the first logo click. | The profile picker opens only from the toolbar. The active profile plays until the player switches. |

## M4: seek engine

Fork code: `osu.Game/Fork/Practice/` (`PracticePlayer`, `SeekSession`). Tests: `osu.Game.Tests/Fork/Practice/` (`TestSceneSeekBenchmark` is explicit: run it in a Release build).

Left and Right seek 5 s through the replay seek bindings (`GlobalAction.SeekReplayBackward`, `SeekReplayForward`), so they follow lazer's key bindings.

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Screens/Play/Player.cs` | New virtual `SeekingAllowed`, by default `DrawableRuleset.HasReplayLoaded`. | `PracticePlayer` turns progress bar seeking off. The bar is optional and skins can leave it out, so seeks come from keys only. Upstream would turn it on during every seek session, because a replay is loaded then. |
| `osu.Game/Screens/Play/HUD/SongProgress.cs` | `Interactive` binds to `player.SeekingAllowed` instead of `drawableRuleset.HasReplayLoaded`. | Same. Upstream players keep the old behaviour through the default. |
| `osu.Game/Rulesets/UI/ReplayRecorder.cs` | `RecordFrame` finds its input manager itself if `LoadComplete` has not run yet. | After a seek, the recorder is attached mid-run. A hit in that same frame recorded a frame before `LoadComplete` and crashed. |

## M5: practice UI

Fork code: `osu.Game/Fork/Practice/` (`ScrubOverlay`, `CheckpointStore`, `PracticeHud`, `PracticeMenuOverlay`, `FooterButtonPractice`). Tests: `osu.Game.Tests/Fork/Practice/`.

- Hold Shift: the map's difficulty graph (aim and speed), as in McOsu. Click to jump and hold there; right-click to set the checkpoint; release Shift to resume. While Shift is held, Left/Right and comma/period seek 5 s and one frame.
- F6 saves the checkpoint (`practice/<profile id>/<beatmap MD5>.json`), F7 loads it.
- A practice tool used in a normal run asks "practice this run?" first. Saving a checkpoint does not ask.
- The Practice footer button at song select starts runs as practice.
- A practice run shows a "PRACTICE" panel with the current section's stats (since the last resume), and at the end of the map offers "Load checkpoint", "Play from the start" and "Results".

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Input/Bindings/GlobalActionContainer.cs` | New `GlobalActionCategory.Practice` and `GlobalAction.PracticeScrub = 10000` (Shift), `PracticeQuickSave = 10001` (F6), `PracticeQuickLoad = 10002` (F7). Listed before the overlay bindings. | Fork actions start at 10,000, so upstream's new actions never shift their saved numbers. Before the overlays, so F6 does not also open now playing. |
| `osu.Game/Overlays/Settings/Sections/Input/KeyBindingPanel.cs` | A "Practice" subsection under gameplay. | The practice keys can be rebound. |
| `osu.Game/Rulesets/Difficulty/DifficultyCalculator.cs` | New `CalculateObjectDifficulties`: each skill's difficulty for every object. | The scrub graph. The skills are only reachable from inside the calculator. |
| `osu.Game/Screens/Play/Player.cs` | New virtual `CanPauseWithReplayLoaded` (false), checked where replays refuse to pause. | A seek session plays the run's own input as a replay. Esc paused nothing and left the map. |
| `osu.Game/Screens/Play/Player.cs` | New `ProgressToResultsNow()`. | A practice run shows results only when the player asks at the end of the map. |
| `osu.Game/Screens/Select/SoloSongSelect.cs` | Adds the Practice footer button and passes it to `PracticePlayer.StartAsPractice`. | The practice toggle. |

## M6: stats

Fork code: `osu.Game/Fork/Stats/` (`LocalStats`, `LocalProfileOverlay`), score sync in `osu.Game/Fork/Lazer/LazerSync.cs`, wired in `osu.Game/Fork/McLazerGame.cs`. Tests: `osu.Game.Tests/Fork/Profiles/`, `osu.Game.Tests/Fork/Lazer/LazerSyncTest.cs`.

- Lazer's scores are copied at each sync, with " (lazer)" after the player's name. They show on the "All profiles" leaderboard and never earn pp. Their replays stay in lazer's folder.
- pp is calculated for the profiles' own passed scores on ranked or approved maps (ranked status from lazer), and stored in `ScoreInfo.PP`. Total pp: the best score per map, sorted by pp, each counting 95% as much as the one before. No bonus pp.
- The profile screen opens from "View profile" in the profile picker, and by clicking a profile's name on a leaderboard.

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game/Online/Leaderboards/LeaderboardManager.cs` | New `ShowAllProfiles`. When set, the Local query does not filter by the active profile; a change refetches. | The "All profiles" leaderboard. |
| `osu.Game/Screens/Select/BeatmapDetailsArea.Header.cs` | Offline only: an "All profiles" toggle bound to `LeaderboardManager.ShowAllProfiles`, in place of the one-item scope dropdown. | Same. |

## M7: coverage

Tests: `osu.Game.Tests/Fork/Practice/TestSceneSeekWithMods.cs` rewinds with HR, DT, HT, HD, FL, Adaptive Speed, Wind Up, Wind Down, Magnetised and Repel.

| File | What changed | Why |
| --- | --- | --- |
| `osu.Game.Rulesets.Osu/Mods/OsuModRepel.cs`, `osu.Game.Rulesets.Osu/Mods/OsuModMagnetised.cs` | Objects do not move in a frame where gameplay time stands still or runs backwards. | Damping with negative time pushed objects away without limit during rewinds. Repel also crashed with a NaN position when the cursor sat exactly on an object in a frame with no time. |

## M8: packaging

`package-linux.sh` builds a self-contained 64-bit Linux McLazer into `dist/McLazer-linux-x64.tar.gz` (ignored by git), with a `.desktop` file and `install.sh`, which installs to `~/.local/opt/McLazer` and adds McLazer to the application menu. No .NET install is needed to run it. Only the osu! ruleset is shipped.

Lazer's `Version` line is no longer copied with its `game.ini`: McLazer keeps its own, so it does not post "updated" whenever lazer's version differs (`McLazerGame.borrowLazerSettings`). No upstream file changed.
