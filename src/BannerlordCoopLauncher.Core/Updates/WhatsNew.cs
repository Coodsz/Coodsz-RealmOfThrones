using BannerlordCoopLauncher.Core.IO;
namespace BannerlordCoopLauncher.Core.Updates;

/// <param name="Picture">Optional picture shown under the lines: one of the WhatsNew.*Picture resource names.</param>
public sealed record WhatsNewEntry(string Version, IReadOnlyList<string> Lines, string? Picture = null);

/// <summary>
/// The "What's new" window each tool shows once after an update. Every release adds an
/// entry at the top of its list (a test checks the top entry matches ReleaseNotice), in
/// plain player words. The lines go through the translation tables like any other text,
/// so run tools\ExtractUiText and translate the new lines before a release.
/// </summary>
public static class WhatsNew
{
    private const string WhatsNewWindow = "A What's new window like this one now shows once after each update, in your language.";
    private const string Languages = "Pick your language with the 🌐 button at the top: Arabic, German, French, Portuguese, Russian or Turkish.";
    // Chinese, Korean and Japanese (Translator.Languages): goes into the next entry of every tool's list
    // (Coop and Friends, AD1259, TAOM, Burning Empires) when that tool's release is built.
    public const string MoreLanguages = "The tools now come in Simplified Chinese, Korean and Japanese too. Pick yours with the 🌐 button.";
    private const string LanguagesDetail = "Buttons, help text, dropdowns and the activity logs are translated. Log zips stay in English so anyone helping you can read them.";
    private const string MenuGuard = "Fixed a freeze when you walk into a village or town. The menu now opens even when Coop sent one of the parties inside without all its data.";
    private const string ShadersOnce = "Your game rebuilds its shaders once after this update, because the launcher now adds a small Coodsz module. After that it keeps them.";
    private const string ImportList = "Import mod list now does the whole job: it makes a new setup when needed, downloads the Workshop mods from the list that you don't have, turns them on, and shows what it did.";
    private const string ShareLinks = "Share mod list now saves each mod's Workshop link, so a friend's import can download it.";
    private const string TaomRescue = "Rescue save is now on the TAOM server tool, next to Stop. It fixes a save that crashes on load, a frozen map, lords stuck in a battle, and lords stuck with an empty party.";
    private const string EmptyList = "Fixed an empty mod list (\"The mod list could not be read yet\") on new installs and reinstalls. It came from an online profile the launcher reads, which changed today.";
    private const string RescueSteps = "Rescue save is easier to follow: shorter steps, and it tells you not to press Start server yourself. The ? next to Rescue save shows the steps, the same in every server tool.";
    private const string InstallLoop = "Install mods no longer gets stuck when Steam won't download a Workshop update. The copy on your PC is used instead, and mods that were removed from the Workshop now tell you to get them from Nexus or ModDB.";
    private const string TaomSteamGet = "Getting Coop 0.1.6 through Steam is more reliable. The launcher always asks Steam to download it, even when Steam says your copy is already up to date.";
    private const string LordReset = "Rescue save can now reset lords who are stuck on the map with an empty party. Their party is removed and they come back in a town or castle to raise a new one. No lord is killed.";
    private const string CheckServer = "Check server now gets a real answer from the Coop server, so a working server shows as online instead of No UDP reply. When nothing answers, it tells you what to check.";
    private const string SteamCurrent = "Install mods no longer keeps asking for an update that Steam has not offered yet. When Steam says your copy is the newest it has, the launcher uses it and Connect unlocks.";
    private const string NoReport = "The Report a problem button is gone. To send what happened, use Save all logs to one zip.";
    private const string BattleSize = "Battle size on the Settings page now really sets coop battles for everyone. Start server fixes True or False written with capitals in mod-config.json, and settings saved by the tool now land where the server reads them.";
    private const string HoverTips = "Hover over any button to see what it does.";
    private const string AskDeleteSetup = "Delete setup in the server tool now asks before it deletes the setup and its MCM settings.";
    private const string AskRemoveCommand = "Remove on a saved cheat command now asks before it deletes the command.";
    private const string TaomLogsServer = "Save logs for help now also packs TAOM's stall reports, the logs of a TAOM server on this PC, and every log of each game and server session. The join password is taken out of the game's command line in those logs.";
    private const string TaomLowMemory = "The client launcher shows a warning under Connect when your PC is low on memory, or when a dedicated server runs on the same PC with less than 24 GB. The server tool warns at Start server too.";
    private const string RbmNewFile ="When RBM had no settings file yet, the server tool now writes one RBM keeps, so RBM's Campaign part stays off as you set it.";

    // v1.2.57: a locked Plain Coop setup in Coodsz Host and Coop and Friends.
    private const string PlainCoop = "New locked setup Plain Coop (no mods) in Coodsz Coop Server Tool and Coop and Friends. The host starts a Coop server with no mods at all, and friends pick the same setup to join it.";
    private const string AddModsBox = "The Add mods to this campaign box lists the new mods by name, says a backup of the save is made first, and warns that taking them out later can break the save.";
    private const string SaveCheckLibs = "Harmony and UIExtenderEx keep nothing in the save, so adding them to a campaign no longer asks about a mod change.";
    private const string OlderPatchSave = "A save from an older patch of the same game version, like Coop's own v1.4.6 start save on a v1.4.8 server, now loads.";
    private const string SetupNames = "The two working setups are now named after Coop 0.1.6, like Working stable mods for Coop 0.1.6 - Vanilla campaign Map. Your setup and its settings carry over.";

    // v1.2.57, v1.1.36 and v1.0.16: translation and clarity fixes a Turkish player found.
    private const string ChoiceLabels = "The dropdowns on the Settings page show On, Off, Very easy and the other choices in your language, and the notes next to them use the same words. The files the server reads are not changed.";
    private const string OtherHints = "The number settings under Other on the Settings page now say what a higher or lower value does, and the range you can use.";
    private const string CommunityChips = "The Community filter buttons on the Modules page are translated now.";
    private const string CheatsNames = "The Cheats page and the player profile show each player's hero name, or their Steam name while the server is stopped, with the hero id under it. Streamer mode still shows Player 1, Player 2.";
    private const string Wording = "Clearer wording in every language for the mods page, Save module config and Auto dependency order.";

    // v1.2.72, v1.1.50 and v1.0.27: crash reports and log zips keep four part version numbers instead of
    // hiding them as addresses. Friends and AD1259 only: Start server refuses an MCM older than v5.12.
    private const string OldMcmRefused = "Start server now stops before the game loads when Mod Configuration Menu is older than v5.12, because an old MCM crashes the dedicated server. The tool tells you to get v5.12 or newer from the Steam Workshop or Nexus Mods.";
    // All three: Coop Save Rescue fills a hero's missing first or full name before Coop 0.1.6 syncs it (Coop #4104).
    private const string MaleBirth = "A male child being born no longer stops the server. Coop 0.1.6 crashed on the daily tick when a boy was born, and Coop Save Rescue now fills in the missing name first so the server keeps running.";
    private const string VersionsKept = "Crash reports and log zips now show version numbers like 3.0.3.0 as they are, instead of hiding them like an address. Real IP addresses are still taken out.";

    // v1.2.73, v1.1.51 and v1.0.28: the client launchers probe a Direct address before Connect, and the host tools
    // turn Steam lobby on once for a shared address (CGNAT) and never send friends to it while it is off.
    private const string JoinCheck = "Connect now checks the server first. If nobody answers, or the address is your own PC, router or VPN, the launcher tells you and offers Steam lobby instead of sitting on Contacting the server forever. Connect anyway is still there.";
    private const string SteamLobbyShared = "When your internet shares its address with other homes, the server tool turns Steam lobby on for you once and the invite tells friends to use it. If you turn it off again, it stays off.";
    // All three clients: Connect offers to close a Bannerlord left running (Coodsz-Crash-Reports#29), and a mod in
    // two Workshop items is named with the copy to unsubscribe (#31). Friends and AD1259 hosts: exit code 1 evidence (#12).
    // v1.2.75 (Friends clients): lines that named a Refresh button the window lacks now say Check mods.
    private const string RefreshNamed = "Some messages said to click Refresh where the window has no Refresh button. They now say Check mods.";
    // v1.0.30 (AD1259 host): Start turns the locked set back on after the old SandBox map bug turned the pack mods off.
    private const string AD1259SetBack = "An AD1259 server with an extra map in SandBox no longer starts a new world with its pack mods turned off. Start turns the whole AD1259 set back on.";
    // v1.2.74 and v1.0.29 (Friends and AD1259 hosts): Start moves an extra SandBox campaign map aside (Fraus, AD1259).
    private const string ExtraSandBoxMap = "Start server now finds an extra campaign map inside SandBox that stopped servers with a map mod like AD1259, and offers to move it aside. If a start still fails with two maps, the tool says which folder to move.";
    private const string GameStillOpen = "Connect now asks to close a Bannerlord that is still running, like a game that froze in the background, instead of seeming to do nothing.";
    private const string WorkshopTwinsNote = "The launcher now warns when a mod is in two Steam Workshop items, like an old second upload of Harmony, ButterLib or UIExtenderEx, and says which one to unsubscribe. If the game crashes at startup, it names that first.";
    private const string ExitEvidence = "When the server stops with exit code 1 and no error, the logs the server tool sends now include the Windows event log entries from that moment, so the cause can be found.";

    // v1.0.0 of the Burning Empires server tool and client launcher: the first release, built from the TAOM tools.
    private const string BeFirst = "The first Coodsz Burning Empires tools. The server tool runs a Burning Empires Coop server, and the client launcher gets your game ready and joins it.";
    private const string BeSetups = "One locked setup, Burning Empires, loads the whole Burning Empires core with Elephants, the way Burning Empires asks. Players join with the same setup as the host.";
    private const string BeInstall = "Install or repair Burning Empires uses the Burning Empires files you downloaded from Nexus Mods, main file first and then the hotfix. Your Burning Empires saves are kept.";
    private const string BeCompat = "Burning Empires Coop Compat comes with both tools. Harmony, ButterLib and UIExtenderEx from the Workshop, and MCM from Nexus Mods, are checked before you join or start.";

    // v0.1.0 of the Realm of Thrones server tool and client launcher: the first release (RotCoopInstall, RotServerBuild,
    // RotStoryPopups in the compat module, RotEncyclopedia, RotSetupBook).
    private const string RotFirst = "The first Coodsz Realm of Thrones tools. The server tool runs a Realm of Thrones Coop server, and the client launcher gets your game ready and joins it.";
    private const string RotCoopBuild = "Both tools carry a Realm of Thrones build of Coop, so the host and every player run the same one. Connect puts it in the game's Modules folder. Another Coop there is set aside after asking and goes back when you close the launcher.";
    private const string RotPrepare = "Prepare server builds the server in C:\\RotServer from the Workshop Coop server, War Sails from your own game and the Realm of Thrones build of Coop.";
    private const string RotStory = "Every player sees the Realm of Thrones story questions. The first player to answer decides for everyone, and after 60 seconds the default answer goes ahead.";
    private const string RotEncyclopedia = "The Encyclopedia page opens the Realm of Thrones wiki and the community tables of troops, armor, weapons, cultures, kingdoms and clans in your browser.";
    private const string RotSetups = "Plain Realm of Thrones is the locked setup with only Realm of Thrones and its helper mods. New setup or Copy makes your own setup with extra mods, and every player loads the same ones.";

    private const string RotHousehold = "The server now charges the gold and adds household troops to your party when you buy them from a castellan. They stay with you after you rejoin.";
    private const string RotHostLogs = "The Server Tool does less work when the server writes a lot of log messages.";
    private const string RotMapCheck = "The Client Launcher checks that your Realm of Thrones map has the terrain and borders it needs before starting the game.";

    // RoT v0.1.3: c924f163 (RoT group moves as one, BeforeRealmOfThrones slot, refused moves), 08067849 (ROT-Core row
    // arrows in the Server Tool) and 211ed68e (disabled rows and LoadsBefore in the move and launch checks, Auto sort
    // reports a dependency cycle on its own, the Client Launcher's Mods page shows a refused move's reason).
    private const string RotGroupMoves = "The four Realm of Thrones modules now move up and down together as one group in the mod list, and extra mods can go before Realm of Thrones too.";
    private const string RotCoreArrows = "In the Server Tool, the up and down arrows on the ROT-Core row work now. They move the whole Realm of Thrones group.";
    private const string RotMoveRefused = "A move that would break the load order is refused, and the tool says why. The Client Launcher's Mods page shows the reason too.";
    private const string RotSortCycle = "Auto sort makes every move it can. When some mods each need the other to load first, it says so on its own line and keeps their order as it was.";
    private const string RotLaunchCheck = "The load order check before launch now also counts mods that are turned off and mods that ask to load before another mod, so a mod in the wrong place is caught as soon as you turn it on.";

    // RoT v0.1.2: f57930fd (Console output rows from BE #14), 1dccce9f (HostPlayerLog.Read keeps the last good list) and 8aa78ff9 (CoodszSettingsSync SentLedger, rebuilt sync DLL).
    private const string RotConsoleBox = "The output box on the Console page is taller and easier to read.";
    private const string RotQueueRestart = "A command still waiting in the Server Tool when the server restarts is now dropped instead of going to the new run. When the console is slow, the tool says the command may still arrive instead of saying it was sent.";
    private const string RotSyncJoin = "Players already on the server now get the mod settings you change, even when someone joins right after the change. Before, they could keep the old value.";
    private const string RotPlayersKept = "When the server log cuts off a players line, the player list now keeps the players it shows instead of going empty.";
    // v0.1.1 (Realm of Thrones server tool): the Server page shows the note under Start server too (8a966595), so a
    // Prepare server that stopped, like for a missing Workshop Coop, no longer said so only on the Dashboard.
    private const string RotServerPageNote = "Prepare server no longer looks like it does nothing. When something stops it, like Coop missing from the Steam Workshop, the reason now shows on the Saves page too, not only on the Dashboard.";

    // v1.2.76 and v1.0.31 (Friends and AD1259 hosts): the tool does the automatic restart itself, since the Coop server ignores autoRestart.
    private const string AutoRestartWorks = "Automatic restart on the Settings page now works. While the server tool is open it saves and restarts the server after the hours you set, and warns players 30 seconds before. It starts once you press Save settings, so check the box and the hours first.";
    // v1.2.76 and v1.0.31: MCM from Nexus only, the official Workshop item 2859238197 was removed.
    private const string McmFromNexus = "Mod Configuration Menu (MCM) now comes from Nexus Mods. Its official Steam Workshop item was removed, so the tools no longer ask Steam for it, and Start server copies a newer MCM from the game's Modules folder to the server.";

    // v1.2.76, v1.0.31 and v1.1.52 (every client and server tool): crash reports and Send my logs can add the crash dump.
    // It replaces the "crash dumps are never sent" part of CrashPrivacy (v1.2.62, v1.0.21, v1.1.42).
    private const string CrashDumpSend = "Send crash report and Send my logs can now add the crash dump, so Coodsz can see where the game or server crashed. A crash dump is a snapshot of the game's memory and is not cleaned. It can hold your Windows user name, Steam id, the join password, player addresses and anything the game had open. Only Coodsz can open it. Untick Add the crash dump to leave it out.";
    // v1.2.76 and v1.0.31 (Friends and AD1259 hosts): Restart no longer deadlocks the window against the server session (d8cc854).
    private const string RestartNoFreeze = "Restart no longer freezes the server tool. Before, the window could go black after the server stopped, and the server did not start again.";
    // v1.2.76, v1.0.31 and v1.1.52 (all three server tools): player reports and a Report link on the Modules page rows.
    private const string ServerModReports = "The Modules page of the server tool now shows what players reported about each mod, with a Report link on each row, like the client launchers.";
    // v1.1.52 (TAOM client): the TAOM core mod rows get player reports and the Report link.
    private const string TaomCoreReports = "The TAOM core mod rows in the client launcher now show what players reported and the Report link too.";
    // v1.2.76, v1.0.31 and v1.1.52 (server tools): backups wait for a save still being written, and a non ASCII save name is backed up (TAOM crash repo #20).
    private const string BackupsWaitForSave = "Save backups now wait until the server has finished writing the save. Before, a backup made while the server was still saving could fail to load later. A save with letters like Ü or İ in its name now gets backed up too.";
    // v1.1.52 (TAOM server tool): the server console is read in the OEM code page it writes.
    private const string TaomConsoleText = "The TAOM server tool now reads the server window's text in the right code page, so names with letters like Ü or İ no longer come out garbled.";
    // v1.2.76 and v1.0.31 (Friends and AD1259 hosts): the crash bar line comes from this run, and "may still crash" warnings are not the cause.
    private const string CrashLineThisRun = "The server crash bar now takes its crash line from this run only, and a warning that something may still crash is no longer named as the cause.";
    // v1.1.52 (TAOM server tool): only the "may still crash" part of CrashLineThisRun applies.
    private const string TaomCrashLine = "The server crash bar no longer names a warning that something may still crash as the cause.";
    // v1.2.76 and v1.0.31 (Friends and AD1259 hosts): No to adding mods shows three lines in the status box.
    private const string DeclinedMods = "When you answer No to adding mods to a campaign, the status box now says what happened, which mods, and what to do, on three short lines.";

    // v1.2.77 (Friends clients): ModuleResolver.ChooseCopy skips a retired Workshop item, so the Nexus MCM in Modules wins (CR-J5AUW).
    // The AD1259 launcher does not use ModuleResolver, so it is not in v1.0.32.
    private const string McmNexusWins = "The launcher now uses the MCM from Nexus in the game's Modules folder, not the old download from the removed Workshop item, also in a setup saved before v1.2.76. Joining no longer fails on an MCM version the host doesn't run. If the old download is still there, the launcher says to unsubscribe from it in Steam.";
    // v1.2.77 and v1.0.32 (Friends and AD1259 hosts): the server window is read and written in the PC's OEM code page.
    private const string HostConsoleText = "The server window in the server tool now shows lines with letters like Ü or İ right instead of question marks.";
    // v1.1.53 (TAOM server tool): commands go to the server in the OEM code page it reads (v1.1.52 fixed reading only).
    private const string TaomCommandText = "Commands you send from the TAOM server tool, like say, now reach the server with letters like Ü or İ intact.";
    // v1.1.53 (TAOM server tool): Start server restores missing wrapper files from the host kit (TAOM crash report #21).
    private const string TaomServerFilesBack = "When BannerlordCoopServer.exe or other server program files go missing from the server folder, most likely removed by antivirus, Start server now puts them back from the TAOM host kit. It checks each file against the kit first and never touches the Data folder or your saves. Prepare server no longer leaves you stuck and points you to Start server instead.";
    // v1.2.77, v1.0.32 and v1.1.53 (every tool): the crash dump privacy sentence shows only when there is a dump to add.
    private const string DumpSentenceOnlyWithDump = "The crash report window now mentions the crash dump only when there is a dump to add.";

    // v1.2.79, v1.0.34 and v1.1.55 (every tool): updates remove only files the tool owns, never a sibling tool or a player's own file.
    private const string UpdateKeepsSiblings = "Updates no longer remove another Coodsz tool or your own files from the same folder. Keep each Coodsz tool in its own folder.";

    // v1.3.0, v1.2.0 and v1.1.0 (the two exe release): new exe names (ToolNames), and ToolRename repoints
    // shortcuts and removes the old exe of the same role on the new exe's first start.
    private const string CoopNewNames = "Coodsz Host is now Coodsz Coop Server Tool, and Coodsz Coop and Friends is now Coodsz Coop Client Launcher. On its first start each new program moves your shortcuts to itself and removes the old one.";
    private const string TaomNewName = "Coodsz Dedicated TAOM Server Tool is now Coodsz TAOM Server Tool. On its first start it moves your shortcuts to itself and removes the old one.";
    private const string AD1259NewName = "Coodsz AD1259 Server is now Coodsz AD1259 Server Tool. On its first start it moves your shortcuts to itself and removes the old one.";
    // Every tool: one zip holds exactly the two exes, and the modules and bundled mods ride inside the exe (ToolPayload).
    private const string OneZip = "Both programs now come in one zip with nothing else in it. The modules and files they need are inside each program.";
    // Every tool: ToolData.Start copies the old settings from beside the exe to %LOCALAPPDATA%\Coodsz\<Product> once.
    private const string SettingsMove = "Your settings now live in %LOCALAPPDATA%\\Coodsz. Each program copies them there by itself on its first start, so keep the old folder until then.";
    // TAOM: both TAOM tools are now published self contained (Make-Release.ps1), like the Coop and AD1259 tools.
    private const string TaomNoDotNet = "The TAOM tools now carry their own .NET, so you no longer need to install the .NET Desktop Runtime.";
    // TAOM server tool: Start server restores the module DLLs the staging copied from the game's Modules folder, and a crash with one missing is no load crash (CR-KFRG6).
    private const string TaomModuleDllsBack = "When antivirus removes StoryMode.dll, MCMv5.dll or other module files from the TAOM server, Start server now puts them back from the game's Modules folder after checking each one, or names the missing files when it can't. A crash from these missing files no longer counts as a save that crashes on load.";
    // TAOM: TaomServerPath.NotTaomCampaign at Prepare and Start, and busy guards that no longer say the game was not found.
    private const string TaomPlainRefusals = "The TAOM Server Tool now says plainly when a save is not a TAOM campaign. While a tool is busy, Prepare server, Connect and Find the crashing mod now ask you to wait.";
    // TAOM server tool: GameplaySettingsEditor.AdvancedDefinitions, party.troopWeight under a collapsed Advanced section, only when the kit has the key.
    private const string TaomTroopWeight = "The TAOM Server Tool has a new switch under Advanced, Heavy troops count more toward party size (TAOM troop weight). It's off by default.";
    // Every tool: Vdf throws VdfException on a cut off file, and SteamInstallFinder and BannerlordFinder fall back to the Steam folder.
    private const string DamagedSteamLibrary = "A damaged Steam library file no longer stops the tools from finding Bannerlord and the Workshop mods.";
    // v1.3.1 and v1.2.1 (Coop and TAOM): the Improved Garrisons party size hand
    // back (compat\ImprovedGarrisonsCoopCompat\PartySizeHandBack.cs) in the client add on, Coodsz War Sync and
    // Improved Garrisons Coop Compat. AD1259 has no Improved Garrisons, so it gets no line.
    internal const string TaomIgPartySize = "With Improved Garrisons in your setup, TAOM's own party sizes count again, and so do the Party settings on the TAOM Server Tool's Settings page. Improved Garrisons used to replace them on the server and on every player's PC.";
    internal const string CoopIgPartySize = "With Improved Garrisons and another mod that changes party sizes, that mod's party sizes now count too, on the server and for every player. With Improved Garrisons alone nothing changes.";
    // v1.2.1 (TAOM): the client launcher carries the CompatOnly CLIENT zip
    // and the server tool the HOST-OPERATOR zip (TaomCompatZip); a copy on this PC with the pinned hash is used first.
    internal const string TaomCompatBuiltIn = "The TAOM tools now have the CompatOnly zips built in, so you no longer need to download them. A copy you already have on this PC is still used first.";
    // v1.3.1, v1.2.1 and v1.1.1 (every server tool): Coop Save Rescue fills a parent's missing culture or body shape
    // and copies a player father's boy from a lord template before the birth (Coop #4104, TAOM #25 and #28).
    private const string PlayerChildBirth = "A child born to a player's character no longer stops the server. Coop Save Rescue now fills in what the game needs before the birth, so the server keeps running.";
    // v1.3.1, v1.2.1 and v1.1.1 (every server tool): ConsoleQueue waits while the result file is still empty.
    private const string CommandWaitsForAnswer = "Commands you send to the server now wait for its answer, so a command the server turned down no longer shows as done.";
    // v1.2.1 (TAOM server tool): the busy guards also wait for _startingServer.
    private const string TaomWaitForStart = "While Start server is getting ready, Stop, Restart, Save game, Last good save, Install mods and Test mods on the server now ask you to wait.";
    // v1.1.1 (AD1259 tools): AD1259ModScan names a mod newer than the tool's pinned version (AD1259Stack).
    private const string AD1259NewerMod = "When one of your AD1259 mods is newer than the tools support, they now say so plainly and tell you to update the tools first.";
    // Coop: the Coodsz and Friends build became the client launcher's More Nations switch (MoreNationsFoldIn).
    private const string MoreNationsSwitch = "Coodsz and Friends is now the More Nations switch in the client launcher, under Campaign map. If you used Coodsz and Friends, its update keeps your setups and turns the switch on.";
    // v1.3.2, v1.2.2, v1.1.2 and BE v1.0.1 (every tool): the Find Bannerlord button shows when the
    // Steam search fails, and the picked folder is checked (GameFolderPick) and saved in the tool's settings.
    internal const string FindBannerlordPick = "When a tool can't find Bannerlord, the new Find Bannerlord button lets you pick the game folder yourself. The tool remembers it.";
    // BE v1.0.1 (Burning Empires server tool and client): Install or repair asks
    // before the Nexus page opens and only when a new download helps (AskOpenNexus, BeArchiveException.DownloadHelps),
    // a copy by hand is asked only for the hotfix (BeArchive.NeedsMainFile), a copy still running is waited for
    // (BeArchive.ChangedRecently), and with Steam closed nothing is subscribed and no steam: link opens (BeSteamPage).
    private const string BeNexusAsks = "Install or repair Burning Empires asks before it opens Nexus Mods in your browser, and says why.";
    private const string BeHotfixOnly = "If you copied Burning Empires into Modules yourself, Install or repair only asks for the hotfix.";
    private const string BeCopyWait = "Install or repair tells you to wait while you are still copying Burning Empires into Modules.";
    private const string BeSteamClosed = "With Steam closed, the tools ask you to open Steam instead of opening Workshop pages.";
    // BE v1.0.2 (Burning Empires server tool): BeServerPrepare.Prepare takes Start-ModdedServer.ps1 from the tool's own
    // copy, since the Workshop Coop build ships a DedicatedServer without it (raven, CR-X7WJQ).
    private const string BeThinWorkshop = "The Server Tool's Prepare server now works with a fresh Steam Workshop download of Bannerlord Coop. It used to say the Coop dedicated server was not found, even when Coop was there.";
    // BE v1.0.3 (Burning Empires server tool): StartServerCoreAsync prepares a server folder that is not ready (Start
    // server clicks only), and Prepare checks the Workshop Coop server first (AntivirusFiles.WorkshopServerProblem with
    // the Steam download state, CR-P75R3, and its engine files, Sturner22).
    private const string BeStartPrepares = "In the Server Tool, Start server now prepares the server folder by itself when it isn't ready, instead of asking you to click Prepare server first.";
    private const string BePrepareChecks = "Prepare server now checks the Bannerlord Coop download from the Steam Workshop first. It tells you to wait for Steam or to subscribe again, instead of only saying the server was not found.";
    // BE v1.0.3 (Burning Empires server tool): the empty step 2 save box (SaveEmpty) and the guide's own step numbers.
    private const string BeNoCampaignYet = "With no campaign yet, the Server Tool tells you to type a name in step 1 and click New campaign. You don't need a single player save.";
    // v1.3.2 and v1.1.2 (Coop and AD1259 server tools, one CoodszHost source): SaveRefusal checks
    // ServerSession.LoadFailed before Save game, Stop, Restart, backups and the scheduled restart (AutoRestart).
    private const string FailedLoadNoSaveAuto = "When a campaign fails to load, the server tool no longer saves over your save. Save game, Stop, Restart, backups and the automatic restart leave it as it was.";
    // v1.2.2 and BE v1.0.1 (TAOM and Burning Empires server tools): the same failed load guard on Save game, Stop,
    // Restart, Rescue save and backups. These hosts have no scheduled restart.
    private const string FailedLoadNoSave = "When a campaign fails to load, the server tool no longer saves over your save. Save game, Stop, Restart, Rescue save and backups leave it as it was.";
    // v1.3.2 and v1.2.2 (Coop client and server tool, TAOM server tool and client): ShellOpen.TryOpen logs a line
    // when Windows can't open a steam: or web link, where the throw used to close the tool.
    private const string SafeLinks = "Steam Workshop and web links that Windows can't open now show a message instead of closing the tool.";
    // v1.1.2 (AD1259 server tool, CoodszHost source): the bannerlordcoop.com/cheats link on the Cheats page goes
    // through ShellOpen.TryOpen. The AD1259 client already caught a failed link, so it gets no line.
    private const string AD1259CheatsLink = "The bannerlordcoop.com/cheats link on the Cheats page now shows a message when Windows can't open it, instead of closing the server tool.";
    // v1.3.3 and v1.1.3 (Coop and AD1259 server tools, one CoodszHost source): PlayHold keeps Resume game on through
    // Coop's own auto pause until Pause game. Wording approved by Coodsz.
    private const string PlayHoldHost = "After Resume game, the game keeps running until you press Pause game. To stop Coop pausing at all, turn Auto pause off on Settings.";
    // v1.2.3 (TAOM server tool): the new Play button holds the same way until Pause.
    private const string PlayHoldTaom = "Play now stays on until you press Pause, also when every player is in a town or a battle.";
    // v1.3.3, v1.2.3 and v1.1.3 (every tool): the Donate button after What's new opens Donate.Url.
    private const string DonateButton = "The new Donate button next to What's new lists Coodsz's Linktree with Patreon and PayPal, and Buy Me a Coffee under it. The tools stay free either way.";
    // v1.3.3, v1.2.3 and v1.1.3 (every tool): AntivirusWatch remembers the files a good Prepare, Install or Start
    // left, so a gone one is worded as antivirus instead of a plain not found, and the Fix antivirus window (AntivirusWindow).
    private const string AntivirusMissing = "When a file the tool put in place goes missing later, the tool now says your antivirus most likely removed it and how to get it back, instead of only saying it was not found.";
    private const string FixAntivirus = "The new Fix antivirus button lists the folders your antivirus should skip and opens Windows Security. It changes nothing on your PC by itself.";
    // v1.3.3, v1.2.3 and v1.1.3 (every tool): UpdateApply installs through a copy of the tool with a checked .new swap
    // instead of a hidden PowerShell script, and puts the old exe back on a failure.
    private const string SaferUpdate = "Install update is safer. The tool updates itself without a hidden PowerShell script, and when an update fails it puts the old version back so the tool still starts.";
    // v1.3.3 Coop: Start server warns before a new campaign on a map mod (the Coop DS loads SandBox's distance cache, crash report #48).
    private const string MapModWarned = "Start server now warns before a new campaign on a map mod like Europe1100, because the Coop server crashes a few minutes into one. More Nations Remastered is fine.";
    // v1.3.3 Coop: the PvP rules (Coodsz Server Tools module, PvpRules.cs), both switches off by default.
    private const string PvpRulesAdded = "New PvP rules on the Settings page, both off by default. Block leaving in player battles, and Start the battle when the caught player doesn't answer. They only count for players whose factions are at war, and trades are never touched.";
    // v1.3.3, v1.2.3 and v1.1.3: crash-stack.txt in the crash report, read from the dump on this PC (DumpStackSummary).
    private const string CrashStackAdded = "When the game or the server crashes and Windows kept a crash dump, the crash report now adds a short text of the crash stack, read on your PC. The dump itself is only sent when you tick it.";
    // v1.3.3, v1.2.3 and v1.1.3: BackupService backs up and restores a save set without a .json or a .sav.
    private const string SaveBackupsFixed = "Save backups now work for a campaign that lost its .json file or its .sav, and putting a backup back over a missing save can always be undone.";
    // v1.3.3 Coop: SaveSet.SandboxOnlyNote after a single player import.
    private const string SandboxNote = "After you import a single player save, the server tool reminds you that only Sandbox campaigns run on a Coop server.";

    // v1.3.4 Coop: the PvP response timer countdown (PvpCountdownServer in the settings sync, PvpCountdownClient in the
    // join password module). Coop only then: the AD1259 build had no Coodsz Server Tools until feat/ad1259-host-rules, and TAOM has no PvP rules.
    private const string PvpCountdown = "With Start the battle when the caught player doesn't answer turned on, the caught player now sees a countdown like Answer in 30 s at the bottom of the screen, and the player who caught them sees how long is left. It stops on an answer, on peace, when the time runs out or when the host turns the timer off.";
    private const string PvpCountdownClient = "Players see the countdown when they join with the Coodsz Coop Client Launcher. Without it the timer works the same, just with no countdown.";
    // v1.3.4, v1.2.4, v1.1.4 and BE v1.0.4 (every tool, ReportReplyWindow): the unread badge, the status badges and
    // Did this fix it? with Yes, it's fixed (POST /replies/solved) and No, still broken (ReportFollowUps).
    private const string MyReportsBadge = "A red badge on My reports now counts the reports with a new answer from Coodsz.";
    private const string MyReportsStatus = "Each report shows its status (Looking into it, Answered, Solved or Fixed in a version), and the steps in Coodsz's reply show as a list.";
    private const string MyReportsAnswer = "Under each answer, Did this fix it? has two buttons. Yes, it's fixed tells Coodsz it worked. No, still broken opens a short form, and what you write, your new logs and a screenshot if you add one go to the same report.";
    // v1.3.5, v1.2.5, v1.1.5 and BE v1.0.5 (every tool, ReportReplyWindow): My reports as a real button with the gold
    // glow, and the Coodsz answered your report banner.
    private const string MyReportsVisible = "My reports is now a real button next to Send my logs. When Coodsz answers one of your reports, the button glows gold with a red count, and a banner at the top names the report until you read the answer.";
    // v1.3.5 every Server Tool: long sidebar words scale to fit (ReportRepliesUi.FitWidth, the Join my server Viewbox);
    // Coop, AD1259 and TAOM: the page list scrolls on a small window (NavRail.Fit now), it was squeezed away at 720 px.
    private const string SidebarWordsFit = "Long button names in the Server Tool's sidebar now fit in every language instead of being cut off.";
    private const string PageListScrolls = "On a small window the Server Tool's page list now scrolls instead of disappearing.";
    // v1.3.5 Coop and AD1259 (SaveModChange.AddsMap, MapModWarning.RefusesAdding): AD1259 crash report #9.
    private const string MapModRefused = "Start server now stops before it adds a map mod to a campaign made on another map, because that save would not load. Start a new campaign on that map instead.";
    // v1.1.5 AD1259 (AD1259Server.Problem with PcVersions): AD1259 crash report #8.
    private const string AD1259SetVersions = "When the AD1259 set is not complete, the Server Tool now shows the version each mod needs, the one the server has and the one on this PC.";
    // v1.3.5 Coop only (HostSettingsFiles.Apply in CoodszSettingsSync; AD1259 servers do not load the sync): MCM read its files before Coop's
    // redirect, so a server without CoopModPatch kept the defaults and shared them.
    private const string HostMcmValues = "A server without CoopModPatch now uses the host's mod settings (MCM) and shares them with players. Before, the server and players got the mods' default values.";
    // v1.3.5 every tool (crash-signatures.json, the Send my logs hints, GameBinLeftovers before Play in the Client Launchers).
    private const string SetupProblems = "The crash screen and Send my logs now spot five common setup problems and show the fix. They are Smart App Control, a leftover Common.dll or 0Harmony.dll in the game's bin folder, a DNS changer like Cloudflare WARP blocking the Steam join, and Documents in OneDrive.";
    private const string BinLeftovers = "When you click Connect, the Client Launcher now checks the game's bin folder for a leftover Common.dll or 0Harmony.dll and says which file to move out.";
    // v1.3.5 every tool: reply push (the worker gets Coodsz's answers from GitHub right away, ReplySchedule).
    private const string FasterReplies = "Answers to your reports now reach the tool much sooner. Right after you send a report it checks every half minute, then every few minutes while you wait, instead of every 30 minutes. It does not check while it is minimized.";
    // v1.3.5 Coop and AD1259 Server Tools (Coop #54, #55).
    private const string ConsoleBuiltins = "Typing save, status, players, say, kick or help on the Server Tool's Console page now works. Before, the server refused them. To stop the server, click Stop server, which saves first.";
    private const string TimeButtonsOn = "Players' pause, play and fast forward buttons now work on servers whose settings file came from an older Coop version. The Server Tool turns them on at the next Start, the way its Settings page already showed them.";
    private const string BlockedWrite = "When something on your PC blocks the start script from writing a file, the Server Tool now says so and what to check in Windows Security, instead of blaming one of your mods.";
    // v1.3.6, v1.1.6 and BE v1.0.6 client launchers: Coop 0.1.7 unzipped by hand (CoopInstall, the Coop row's Modules or Workshop).
    private const string ManualCoop = "Coop v0.1.7 from Coop's GitHub release or ModDB now works when it ends up as Modules\\Coop. The GitHub zip already holds a Coop folder, so unzip it into the game's Modules folder. The ModDB download has no Coop folder inside, so make a folder named Coop in Modules and unzip it into that. The game loads that copy before the Workshop Coop, so the tools check that one and say where it comes from.";
    // Coop, AD1259 and BE client launchers (CoopInstall.CheckModulesCoop on Check mods and before the game starts; TAOM's
    // launcher runs it too, but its What's new points to Use Workshop Coop 0.1.6 instead).
    private const string CoopWrongFolder = "When Coop is in the wrong place in Modules, like a folder with another name, a second copy, a Coop inside another folder, files unzipped straight into Modules or one folder too deep, Check mods says what to rename or move.";
    private const string CoopUnblocked = "The tools now unblock Coop files that Windows marked as downloaded from the internet, on Check mods and before the game starts, so the game can load them.";
    // Every client launcher (ClientAddOns.Install: no SubModule.xml in the module folder, nothing blamed on antivirus).
    private const string AddOnNotAntivirus = "The Client Launcher no longer blames antivirus for a Coodsz add on that was never installed.";
    // Coop, AD1259 and BE (JoinResultExplain reads 'Display Name' (Id)).
    private const string JoinErrors017 = "When Coop v0.1.7 refuses a join over a missing or wrong mod, the tools explain it again. Coop v0.1.7 writes mod names in a new way.";
    // v1.1.6 AD1259 (AD1259Stack Coop v0.1.7 with AlsoAccepted v0.1.6).
    private const string AD1259Coop017 = "The AD1259 set now takes Coop v0.1.7 and still takes v0.1.6. The host and every player need the same Coop version to join.";
    // v1.3.6 and v1.1.6 Server Tools (CoodszHost): the v0.1.7 server package, ServerFolderIn, ClearServerMarks, ActiveServerLine.
    private const string ServerPackage017 = "The Server Tool works with Coop's v0.1.7 dedicated server package. Unzip it to a short folder like C:\\CoopServer, click Set server folder and choose BannerlordCoopServer.exe.";
    private const string ServerUnblocked = "Start server now unblocks the server's files that Windows marked as downloaded from the internet.";
    private const string ActiveCoopSource = "The Active Coop line on the Modules page now says whether the server's Coop comes from the Steam Workshop, the game's Modules folder or the server folder you chose.";
    private const string SmithingDefault017 = "On a Coop v0.1.7 server, Smithing stamina recovery multiplier starts at 0.6, Coop's new default. Coop v0.1.6 servers keep 0.1.";
    // Coop #60 (ServerConfigFlags.Defaults, ApplyAtStart, PasswordChangeLine).
    private const string SteamStaysOn = "A fresh Server Tool setup no longer turns Steam advertise off. Steam advertise and Write server log now start ticked, the way Coop has them.";
    private const string PasswordAtStart = "Start server now writes the Server password and Steam advertise into the server's settings, so you don't need Save settings first. An empty password box clears the password, and the log says so.";
    // KeepNewCampaignIfNotSaved (a crash or CoopModPatch's first time setup stop before the first save; a Stop click doesn't keep it).
    private const string NewCampaignStaysNew = "A new campaign now stays new after a crash or CoopModPatch's first setup stop before its first save. Before, the next Start server refused it with That save is not in the dedicated server Game Saves folder.";
    private const string MovementBoxesGone = "The two movement limit boxes on the Settings page are gone, because Coop never read them.";
    // v1.2.6 TAOM (CoopStaging.WrongWorkshopCoop, the TAOM launcher's CoopVersionProblem).
    private const string TaomStays016 = "TAOM still needs Coop v0.1.6. When a newer Coop is in the game's Modules folder, the Client Launcher says so and points you to Settings, Use Workshop Coop 0.1.6.";
    private const string TaomNewerCoop = "When the Workshop Coop is newer than v0.1.6, the tools now say TAOM needs a tools update for it, instead of telling you to wait for Steam or verify Bannerlord.";
    // TAOM and BE client launchers (the Coop row's Modules or Workshop).
    private const string CoopRowSource = "The Coop row in the mod list now says whether Coop comes from the game's Modules folder or the Steam Workshop.";
    // v1.0.6 BE Server Tool (Choose Coop server, BeServerPrepare).
    private const string BeChooseCoopServer = "New Choose Coop server button in the Server Tool, for when the Workshop Coop server is missing or older. Pick the folder you unzipped Coop's v0.1.7 dedicated server package to, and Prepare server and Start server copy that server in.";
    private const string BeNoOlderCoop = "Prepare server now asks before it puts an older Coop over a newer one, and Start server keeps the newer Coop.";
    // v1.1.7 AD1259 Server Tool (ServerModuleInstall short copy check before Start, MissingCampaignMap, Start-ModdedServer.ps1 exit 20).
    private const string ShortCopyAgain = "Start server now checks the AD1259 set mods on the server. When it finds missing server files and this PC has the same version of that mod, it copies the mod again. A big copy can take a while, so leave the tool open.";
    private const string MapMissingStops = "When the campaign map files are missing on the server, Start server now stops and says what to do. Before, the server started a plain Calradia world and every player who joined crashed.";
    private const string FailedCopyOnce = "When a copy fails or still misses files, this check leaves it until you reopen the tool.";
    // v1.3.7, v1.2.7, v1.1.8 and BE v1.0.7 (every tool): the redesign (docs/design/redesign/build-plan.md), the shared frame
    // (NavRail, BannerStack, Keep.BackdropArt), the Dashboard and the clients' Play page.
    private const string RedesignLook = "The tools have a new look. A sidebar holds the pages, the Server Tool opens on the new Dashboard and the Client Launcher on Play, a picture sits behind the pages, and one notice shows at a time.";
    // Every Server Tool: ModListBlock and the TAOM and BE MainWindow.Frame running block; the setup pick waits for Start.
    private const string ModsLockedRunning = "While the server runs, the Server Tool's mod list is locked, so the server keeps the mods the players joined with. Stop the server to change mods. A setup you pick meanwhile is used at the next Start server.";
    // ModChips: Required is Always on, Update is Needs update, Version is Wrong version, MissingOnWorkshop.
    private const string ChipWords = "The mod list chips have clearer words. Required is now Always on, Update is Needs update and Version is Wrong version, and a Workshop mod that is not on this PC says Missing on Workshop.";
    // AD1259 and BE: their mod rows have no Needs update or Missing on Workshop chips.
    private const string ChipWordsShort = "The mod list chips have clearer words. Required is now Always on and Version is Wrong version.";
    // TAOM: the Server Tool's sealed rows say Always on; TAOM rows have no Wrong version chip.
    private const string ChipWordsTaom = "The mod list chips have clearer words. Required is now Always on.";
    // The Coop Server Tool's Dashboard: SideModsCard and MadeForCard titled Versions (the Coop build pins nothing).
    private const string CoopDashboardCards = "The Server Tool's Dashboard shows how many mods are on in its Mods card, and the Bannerlord and Coop versions the server has in its Versions card.";
    // TAOM and AD1259: SideModsCard and MadeForCard on the Server Tool's Dashboard, MadeForCard on the client's Play page.
    private const string MadeForCards = "The Server Tool's Dashboard has a Mods card with how many mods are on, and a Made for card with the Bannerlord, Coop and overhaul versions these tools are made for. A green tick means the server matches. The Client Launcher's Play page has the Made for card too.";
    // BE: the same cards, but BE pins no Coop version, so the Made for card shows the Coop version the server has.
    private const string BeMadeForCards = "The Server Tool's Dashboard has a Mods card with how many mods are on, and a Made for card with the Bannerlord and Burning Empires versions these tools are made for and the Coop version the server has. A green tick means the server matches. The Client Launcher's Play page has the Made for card too.";
    // Every Server Tool: the crash bar waits for the load mode retry (fix/after-136, TAOM #42).
    private const string CrashBarWaitsRetry = "When the Server Tool tries a load crash again by itself, the Send server crash report bar waits for that try and shows only if it crashes too.";
    // CoodszSettingsSync: the empty answer for a host with 0 settings objects.
    private const string SettingsSyncEmpty = "When the host has no mod settings to share, the server now says so, and the player's game no longer waits 30 seconds for them.";
    // ReportReplies: a report dated ahead of now after the clock went back counts as sent 10 minutes ago.
    private const string MyReportsClock = "My reports keeps checking for Coodsz's answers after the PC clock was set back.";
    // TAOM client: CoopParking and SetAsideCoopWindow (Set aside and play, Cancel, Don't ask again).
    private const string TaomSetAside = "When another Coop, like Coop v0.1.7, is in the game's Modules folder, the Client Launcher now asks before it sets it aside, and puts it back when you close the launcher.";
    // TAOM Server Tool: Start server puts back a TAOM.CoopCompat DLL from the host kit (TAOM crash report CR-HZYX5).
    private const string TaomCompatRepair = "When antivirus removed a TAOM.CoopCompat file from the server, Start server now puts it back. If it goes again, click Fix antivirus.";
    // TAOM and BE Server Tools: SaveSwitchWarning (TAOM report #44) and the campaign day in the save box.
    private const string SaveSwitchAsk = "The Server Tool now asks before it hosts or copies a save that is earlier in the campaign than the one the server last ran, and the save list shows each save's campaign day.";
    // TAOM and BE Server Tools: Prepare server refuses while the server runs.
    private const string PrepareWhileRunning = "While the server runs, Prepare server now says to stop the server first instead of changing its files.";
    // The compat lists at Coop v0.1.7 (ad1259-compat, be-compat, the BE copy built into the tools).
    private const string AD1259Compat017 = "The AD1259 community mod list now covers Coop v0.1.7 as well as v0.1.6.";
    private const string BeCompat017 = "The Burning Empires community mod list, and the copy built into the tools, are now for Coop v0.1.7.";
    // v1.3.8 Coop and v1.1.9 AD1259: CoopServerDownload, the button under Advanced: server folder and in CoopBehindLine;
    // the missing server lines name it and open that group (SetControl, SendsToServerFolder).
    private const string DownloadCoopServer = "New Download Coop server button under Advanced: server folder on the Settings page. It opens Coop's dedicated server download (about 4.5 GB) in your browser. Unzip it to a short folder like C:\\CoopServer, then click Set server folder. When the server is not found, the message names this button and opens that group.";
    // v1.3.8 Coop and v1.1.9 AD1259: CoopInstall.ServerBehindLine under Active Coop on the Mods page (Maylay).
    private const string CoopServerBehind = "When the game's Modules folder has a newer Coop than the server runs, the Mods page now says so under Active Coop, with Download Coop server and Set server folder buttons.";
    // BE v1.0.8: the same button in the BE Server Tool's Advanced: server folder group, picked with Choose Coop server.
    private const string BeDownloadCoopServer = "New Download Coop server button under Advanced: server folder on the Settings page. It opens Coop's dedicated server download (about 4.5 GB) in your browser. Unzip it to a short folder like C:\\CoopServer, then click Choose Coop server.";
    // v1.3.8, v1.2.8, v1.1.9, v1.0.8 every tool: AnnouncementUi shows compat-list/announcement.json after the crash, report and reply bars.
    private const string CoodszNotice = "Notices from Coodsz now show at the top of the tool in your language, for example when he is on vacation. Dismiss hides a notice until its words change.";
    // v1.2.8 TAOM: PlaytestZipFinder and TaomCompatZip skip a folder that cannot be read (SinGreed).
    private const string TaomZipSearchSkips = "When the tools look for the CompatOnly zips on this PC, they now skip a folder Windows cannot read and keep looking, instead of stopping with a Windows error.";
    // v1.1.9 AD1259: PrepareAD1259Server adds the compat module for every setup, StableCoop.CreateFresh picks the AD1259
    // map, and AD1259Server.Problem no longer sends players to the Workshop for the compat module (AD1259 crash report #20).
    private const string AD1259NewSetupStarts = "A setup made with New setup no longer refuses to start with AD1259 - Coop Compatibility missing. Start server now adds that module itself, and a new setup starts on the AD1259 map.";
    private const string AD1259CompatNoWorkshop = "If that module still cannot be added, the message says it comes with this tool and to start again, instead of sending you to the Workshop.";
    // v1.3.9 Coop and v1.1.10 AD1259: CoopInstall.NotOnSteamYet is false since Workshop item 3770450698 got Coop 0.1.7
    // (2026-10-30), so the GitHub and server download lines are gone and DedicatedServerFinder's Workshop folder is the one to use.
    private const string Coop017OnWorkshop = "Coop v0.1.7 is on the Steam Workshop now, so the tools no longer say to get it from Coop's GitHub release. The Server Tool finds the dedicated server in Workshop item 3770450698 by itself. Download Coop server under Advanced: server folder is still there for a server you set with Set server folder.";
    // v1.3.9 Coop and v1.1.10 AD1259: CoopServerBin.EnsureStoryMode copies the game's StoryMode.dll into a server engine bin that has none (Coop crash report #73).
    private const string StoryModeDllCopied = "Start server now copies StoryMode.dll from the game into a Coop 0.1.7 server that leaves it out. Improved Garrisons needs it on a new campaign.";
    // BE v1.0.9: the same lines are gone; Prepare server copies the Workshop server unless one was picked and the Workshop's Coop is not newer (BeServerPrepare.Source).
    private const string BeCoop017OnWorkshop = "Coop v0.1.7 is on the Steam Workshop now, so the tools no longer say to get it from Coop's GitHub release. Prepare server copies the dedicated server in Workshop item 3770450698. A server you picked with Choose Coop server is used instead, unless the Workshop's Coop is newer. Download Coop server under Advanced: server folder is still there for that.";
    // v1.2.9 TAOM: CoopStaging.WrongWorkshopCoop's newer Coop text, which CoopVersionProblem now shows before ModulesCoopNotPinned.
    private const string TaomWaitsForCompat = "TAOM still needs Coop v0.1.6, since TAOM.CoopCompat 0.3.25 only works with it. Steam now has Coop v0.1.7, so TAOM in Coop waits for a TAOM.CoopCompat made for it, and the tools say so. Players can still join with a Coop 0.1.6 copy of their own in Modules, and the launcher still offers to set other Coop copies aside for it. Hosting a TAOM server needs a Coop 0.1.6 dedicated server until then. The Coodsz TAOM tools will be updated once that TAOM.CoopCompat is out.";
    // v1.3.10 round, TAOM Server Tool (fix/taom-016-server): KitCoopServer.Pick and the Choose Coop 0.1.6 server button, while
    // the Workshop has Coop 0.1.7 and TAOM.CoopCompat 0.3.25 needs 0.1.6.
    private const string TaomChooseCoop016Server = "TAOM hosts can prepare a server again while Steam has Coop v0.1.7. Download the Coop 0.1.6 dedicated server from Coop's GitHub release or Nexus Mods, unpack it with 7-Zip, then click Choose Coop 0.1.6 server on the Saves page. Prepare server and Start server copy from that folder and never change it.";
    // v1.3.10 round, every Client Launcher (fix/after-139): ClientJoinLog reads Coop_client_<pid>.log too, and the pill says
    // ClientNextStep.GameOpen when the join watch ends with both client logs empty.
    private const string JoinPillGameOpen = "The status at the top of the Client Launcher no longer stays on Joining while you play on Coop 0.1.7. When Coop's client log stays empty, it says Game open instead.";
    // v1.3.10 round, Coop and AD1259 Server Tools (fix/after-139, Vanquish): CoopInstall.ChosenServerOlderLine and the Use the
    // Steam server button under Active Coop on the Mods page.
    private const string UseSteamServer = "When the server folder you picked has an older Coop than the Steam Workshop server, the Mods page now says so under Active Coop. Click Use the Steam server to go back to the Workshop server. Your picked folder stays on disk.";
    // v1.3.10 round, AD1259 Server Tool and Client Launcher (fix/after-139): the Open Source 1.x set is accepted next to 2.0
    // (AD1259Stack AlsoAccepted, OpenSourceSet), and the server gets the copy the host's game loads.
    private const string OpenSourceOlderSet = "The AD1259 tools now also take the older Open Source set: Armory 1.18, Saddlery 1.8 and Weaponry 1.8 from Nexus Mods (the files named FOR v1.2.x). The AD1259 team recommends it for AD1259 1.4.6, since with 2.0 some gear can show missing textures. Either set works, but the host and every player must use the same one. The tools say which set you have.";
    // v1.3.10 round, Coop and AD1259 Server Tools (fix/after-139, AD1259 crash report #19): the exit text quotes the server's
    // FATAL line (ServerCrashExplain.FatalLines), and CoopServerBin.ClientCoopInServer warns about a game Coop in the
    // server's own Coop folder.
    private const string ServerFatalLine = "When the server stops itself with a FATAL line, Coodsz Coop Server Tool now quotes that line instead of saying the server was closed from outside. It also warns when a game (client) Coop was copied into the server's own Coop folder, and says how to get a clean one.";
    private const string ServerFatalLineAD1259 = "When the server stops itself with a FATAL line, the server tool now quotes that line instead of saying the server was closed from outside. It also warns when a game (client) Coop was copied into the server's own Coop folder, and says how to get a clean one.";
    // v1.3.10 Coop and v1.1.11 AD1259 Server Tools: MainWindow.Modules MissingMapText for a locked setup whose map has no
    // SceneObj\Main_map on the server (Coop crash report CR-JTQ45), instead of a blank map box.
    private const string MapMissingLabel = "When a setup's map files are missing on the server, the Server Tool's map box now shows that map with (map files missing on the server) instead of staying blank.";
    // v1.2.10 TAOM Client Launcher: CheckMods shows CoopVersionProblem in JoinStatus when Coop is the only problem.
    private const string TaomPlayCoopLine = "When Coop is the only problem, Check mods now shows the Coop line on the Play page instead of only in the log.";
    // Next TAOM Client Launcher (fix/taom-coop016-github, TAOM crash reports CR-HASSH and CR-28WCC): CoopStaging.WrongWorkshopCoop's
    // newer Coop text for a player and the Get Coop 0.1.6 button on the Next step card (KitCoopServer.GitHubRelease).
    private const string TaomCoop016GitHub = "TAOM players can join again while Steam has Coop v0.1.7. Get BannerlordCoop-v0.1.6.zip from Coop's GitHub v0.1.6 release and unzip it into the game's Modules folder. Get Coop 0.1.6 on the Play page opens that release.";
    // v1.3.10, v1.2.10, v1.1.11 and BE v1.0.10 Client Launchers: ClientLogZip.CoopProcessLogs packs the newest
    // CoopProcessLogCount Coop_client_<pid>.log files, and the known causes read the newest one as the Coop client log.
    private const string SendLogsClientPid = "Send my logs now also packs the newest three numbered Coop client logs that Coop 0.1.7 writes, so the tool can spot a known problem in them.";
    // BE v1.0.11 Client Launcher: the join password module's MapCheckGuard skips a broken party, army, map event or
    // settlement in Campaign.CheckMapUpdate while a Coop client loads the server's save (BE CR-EH6KT, CR-A9XVH, CR-PM83C,
    // CR-XG5NC, CR-8ZXDY). Peaches' exact save was not reproduced, so the line says should.
    private const string MapCheckGuardJoin = "Joining a Coop server should no longer crash while the game checks the campaign map. When one party, army, battle or settlement on the map is broken, the game now skips it, writes it to Documents\\Mount and Blade II Bannerlord\\Client Diagnostics\\CoodszJoinPassword.log and keeps loading. If the game still crashes, click Send my logs in the Client Launcher again.";
    // BE v1.0.11 Server Tool: ServerFolderRepair.Ensure collapses repeated entries in DedicatedServer.Windows\SubModule.xml
    // (ServerSubModuleXml.Collapse, peaches CR-A9XVH had 7 copies) after backing it up as SubModule.xml.coodsz-original.
    private const string ServerSubModuleDedupe = "Start server no longer adds Launcher Cheats and Launcher Battle Size to the server's DedicatedServer.Windows module again every time. Start server now removes the repeats and says how many went. It backs up the file first as SubModule.xml.coodsz-original.";
    // v1.3.11, v1.2.11, v1.1.13 and BE v1.0.12 Client Launchers: TerrainShaderCache.ClearAsync on Connect reads the newest
    // engine logs for "failure on remove_all_files_with_extension" in Shaders\TerrainShaders, renames that module's folder
    // to .coodsz-old, and on access denied asks once and removes it with one elevated rd (AD1259 CR-TXY4V, CR-7ZKZ5,
    // CR-VG6VV). No player has confirmed the crash gone yet, so the line says should.
    private const string TerrainShaderClear = "The game should no longer crash on the campaign map because of old map shader files Bannerlord can't remove. When you click Connect and the game's logs show such files, the Client Launcher moves them aside and the game builds new ones. If Windows won't let it, most likely because the game once ran as administrator, the Client Launcher asks once and Windows asks for permission. Say No and Connect goes on as before.";
    // TAOM v1.2.11 and BE v1.0.12 Server Tools: Settings shows DifficultyMissingNote when Coop's mod-config.json has no
    // difficulty block yet, opening Settings again fills only the greyed boxes, and Save settings says they were not
    // saved (szymon, BE).
    private const string DifficultyGreyedNote = "Settings now says why some difficulty boxes are greyed out. Coop's mod-config.json doesn't have those settings until the server has started once, so start the server, then open Settings again. Save settings now says when the greyed boxes were not saved.";
    // v1.1.12 AD1259 Server Tool (fix/ad1259-companion-guard, AD1259 crash report #24, CR-U2W9J): AD1259Stack.CompatVersion
    // v1.1.1, whose CompanionTemplateGuard covers Coop 0.1.7's companion pick when AD1259 has no wanderer template for a type.
    private const string AD1259CompanionGuard = "A new AD1259 campaign on Coop 0.1.7 no longer stops the server. Start server puts the fixed AD1259 - Coop Compatibility module v1.1.1 by SkyApolo in place.";
    // Next AD1259 release, Server Tool and Client Launcher (fix/ad1259-mcm-5131): AD1259Stack MCM v5.13.1 with AlsoAccepted
    // v5.12.3, AD1259ModScan.ModulesCopyWins for MCM, and RefreshWrongVersions gives the server the MCM the host's game loads.
    private const string AD1259Mcm5131 = "The AD1259 tools now take MCM v5.13.1 from the Steam Workshop and still take v5.12.3 from Nexus Mods. An MCM in the game's Modules folder next to the Workshop one no longer counts as installed twice. Start server gives the server the MCM version your game loads. Every player needs that same version to join.";
    // Next Coop and Friends Server Tool (fix/nightly-save-version-id, Coop crash report #78, CR-P4984): Start-ModdedServer.ps1
    // compares CoopNightly's version under its own id, and names the mod and both versions when a version change hides a save.
    private const string NightlySavesShown = "A campaign played on the Coop nightly no longer disappears from the saves after its first stop. When a mod's version changed since a save was made, the Logs page now names that mod and both versions instead of saying the mods don't match.";
    // Next AD1259 Server Tool (fix/nightly-save-version-id): the same Start-ModdedServer.ps1 version line, without the
    // nightly half, since the AD1259 tools never take the Coop nightly.
    private const string SaveVersionNamed = "When a mod's version changed since a save was made, the Logs page now names that mod and both versions instead of saying the mods don't match.";
    // Next Coop and Friends and AD1259 Server Tools (fix/nightly-save-version-id, Codex): LogLineFormatter.ShownMessage looks
    // up a report line whole with its label, so the Logs page shows it translated.
    private const string LogsPageTranslated = "The Logs page now shows its What happened, What to do and What not to do lines in the language you picked. Before, they stayed in English.";
    // Coop and Friends, TAOM, AD1259 and Burning Empires Server Tools (fix/save-rescue-captive-host, TAOM CR-DFX5S): Coop Save Rescue
    // v1.3.0 skips PlayerCaptivity.Update on the server, so a captive server hero no longer freezes the map.
    private const string SaveRescueCaptiveHost = "Save Rescue now keeps the map moving when a save has the server's own hero, the one nobody plays, as a prisoner. Before, the server kept opening a captivity menu and every party stood still. The hero stays a prisoner.";
    // v1.3.13 Coop Server Tool (feat/mlw-recruit-accept): Coodsz Server Tools v1.8.0 (MlwRecruit.cs) lets Coop's recruit code take
    // a My Little Warband custom troop as the normal troop in that slot, then swaps it in, so gold is the normal troop's price.
    private const string MlwRecruits = "Players can now recruit their own My Little Warband troops in Coop. Before, the server turned them down. The server and every player need CoopModPatch and My Little Warband. Gold is charged at the normal troop's price, not the price the recruit screen shows.";
    // v1.3.13 Coop Server Tool (fix/copy-setup-tooltip): the Copy setup tooltip in MainWindow.xaml says what NewProfile_Click copies.
    private const string CopySetupTip = "The tip on the Copy setup button no longer says it copies the save and the map. It copies the mods, ports and MCM. Pick a save and a map for the new setup after.";
    // v1.3.14, v1.2.13, v1.1.15 and BE v1.0.14 (every tool, fix/newer-bannerlord-notice): NewerGame.Notice reads the game's
    // Modules\Native version and shows its line in the Made for card and under Connect or Start server.
    private const string NewerGameNote = "When your Bannerlord is newer than 1.4.8, the tools now say so in the Made for card and under Connect or Start server, with how to switch back to 1.4.8 in Steam.";
    // Coop and Friends: the Server Tool's card is titled Versions, and the Client Launcher shows the line under Connect only.
    private const string CoopNewerGameNote = "When your Bannerlord is newer than 1.4.8, the tools now say so under Connect or Start server and in the Server Tool's Versions card, with how to switch back to 1.4.8 in Steam.";
    // v1.3.14, v1.2.13, v1.1.15 and BE v1.0.14 Server Tools (fix/terms-button-key): the AcceptButton of ServerEula,
    // ServerTerms and BeServerTerms is translated.
    private const string TermsButtonTranslated = "The I accept button in the Coop server terms window now shows in your language.";
    // v1.3.14 Coop Server Tool (feat/coop-config-keys): ModSettings Since keys, greyed with Nightly only or Needs Coop 0.1.7
    // and never written to mod-config.json when the server's Coop is older.
    private const string CoopNewerSettings = "Show more Coop settings on the Settings page now has the kingdom decision vote time. On a Coop nightly server it also has how long AI parties leave a player alone after a battle, what happens to clans when their kingdom is destroyed, and Preferred battle hosts. Settings your server's Coop is too old for are greyed out and never written.";
    // v1.1.17 AD1259 Server Tool (feat/ad1259-host-rules, 70f5ef73): the Console page's output row has a MinHeight that fits
    // the docked rows plus HistoryBox's MinHeight, and the recent command chips stop at two rows, so the browser gives way.
    // The page is shared, so the next Coop and Friends release can use this line too.
    private const string ConsoleOutputStays = "On a small window the Console page now keeps several lines of server output in view. The command list below gets smaller instead.";
    // v1.1.17 AD1259 Server Tool (feat/ad1259-host-rules): ServerSession.TryConsoleLine writes through
    // ServerCommandQueue.WriteBounded with a 5 second deadline, and Start runs ConsoleQueue.ClearPending first.
    // Shared Coodsz Host code, so the next Coop and Friends release can use this line too.
    private const string StopNoHang = "Stop server and Restart no longer hang when the server stops answering. A command left waiting when the tool was closed no longer runs when the server starts again.";
    // v1.1.17 AD1259 Server Tool (2ee4847f): ServerSession writes the server's console input as UTF-8 without a BOM
    // (ServerConsoleEncoding.Input), which the dedicated server reads. The restart warnings are English text, so the line
    // names say. Shared Coodsz Host code, so the next Coop and Friends release can use this line too.
    private const string SayLetters = "Text you send to the server, like a say message on the Console page, now keeps letters like ü, ñ, Cyrillic or Chinese. Before, players saw boxes instead.";
    // v1.1.17 AD1259 Server Tool and Client Launcher (feat/ad1259-host-rules): AD1259Stack.IsNightlyCoop, the Nightly
    // rows of AD1259ModScan and AD1259Server.NightlyServer.
    private const string AD1259NoNightly = "The AD1259 tools now say plainly when they find the Coop nightly, and how to switch back to Coop 0.1.7 from the Steam Workshop, instead of telling you to update the tools.";
    // v1.1.17 AD1259 Server Tool (feat/ad1259-host-rules): both builds ship Coodsz Server Tools, so the AD1259 server
    // loads it with the settings sync, and the Settings page shows its cards. Both AD1259 exes show this list, so it names
    // the Server Tool.
    private const string AD1259HostRules = "The Server Tool now pauses the game while a player joins and freezes the skill XP of offline players. Both are on by default. In Advanced: server rules on the Settings page you can also pick Always to protect player kingdoms from AI wars while their players are online too. Players who join with the Client Launcher use your MCM settings while they play, and their own files never change. Max clans per kingdom and the PvP rules are on the Settings page too.";
    // v1.1.15 AD1259 Server Tool: the same keys, but the AD1259 server runs Workshop Coop 0.1.7, so only the vote time.
    private const string AD1259VoteTime = "Show more Coop settings on the Settings page now has the kingdom decision vote time.";
    // TAOM v1.2.13 Client Launcher (fix/coop-nightly-put-back): CoopParking puts back every parked Coop folder on close or on
    // the next start, from its .coodsz-put-back tag.
    private const string CoopPutBack = "When the Client Launcher sets a Coop folder aside, a Coop nightly too, it now puts it back into Modules when you close the launcher, or on its next start after a crash.";
    // BE v1.0.14 Client Launcher: the same, and BeCoopCopy no longer moves extra Coop copies out for good.
    private const string BeCoopPutBack = "When the Client Launcher sets a Coop folder aside, a Coop nightly too, it now puts it back into Modules when you close the launcher, or on its next start after a crash. Before, it moved extra Coop copies into the game folder for good.";
    // BE v1.0.14 Server Tool (fix/be-client-time, BE crash reports #11 and #12): Start server writes allowClientTimeControls
    // true into an older server-config.json that has none.
    private const string BeTimeButtons = "Start server now turns on players' time buttons (pause and 2x) when an older server-config.json has no setting for them. Before, players on those servers could not pause or use 2x.";
    // BE v1.0.16 Server Tool (BE #14, CR-7DZWB, fix/console-output-height-all): the ConsolePage output row is 3* MinHeight 280.
    private const string BeConsoleReadable = "The server output box on the Console page is tall enough to read again. Before, it could shrink to one thin line.";
    // BE v1.0.16 (632a7fe3, WarSailsCheck): Start server and the mod test refuse "warSails" true or NavalDLC.
    private const string BeWarSails = "Start server and Test mods on the server now stop with a clear line when War Sails is turned on, because these tools can't host War Sails yet. The line says where to turn it off.";
    // BE v1.0.16 (8a966595, c2d3d987, the page the nav calls Saves) and 855bd782 (ShowBlockedDllFix opens Settings).
    private const string BeServerPageNote = "When Prepare server stops, the Saves page now shows why, not only the Dashboard.";
    // BE v1.0.16 Client Launcher (b6be3de4, 1b6e6c3c, TaomLaunch.Shown): the Connect command line hides only the password.
    private const string BeConnectPasswordHidden = "The Client Launcher now hides your join password in the game command line it shows when you click Connect, so it stays off streams and screenshots.";
    // BE v1.0.16, the Chaos in Calradia chat's finds (fabf75a1): DescribeServerModule reads HostMods first, RememberHostList
    // after the locked setup question (no line: that question never shows), BeLoadOrder.ExtrasById for a mod listed twice.
    private const string BeSharedListFresh = "Share mod list now writes each mod's version from the copy your next Start server uses, so a Workshop update shows in the list right away.";
    private const string BeModTwice = "A mod listed twice in a setup no longer crashes Share mod list, the mod checks, Auto sort or Find the crashing mod.";
    // BE v1.0.16 Server Tool, from the TAOM v1.2.16 security review (cfd3937e): App DispatcherUnhandledException net and
    // HostPlayerLog.Read keeping the last good list (only a players event at the start of its own line counts).
    private const string BeKeepsRunning = "When something goes wrong that the Server Tool did not expect, it now says so on the status line and keeps running, instead of closing while the server is still up.";
    private const string BePlayerListKept = "The player list no longer goes blank when the server writes a broken players line, and a player name or chat line can no longer change the list.";
    // TAOM v1.2.15 Client Launcher (fix/taom-safe-mode-double-click, TAOM crash reports CR-W4H9X and CR-2RV83): one
    // Connect at a time (OneConnect), each Safe mode click keeps Plain TAOM, the Plain TAOM host line, and
    // StartupCrashExplain no longer reads BetterExceptionWindow.dll as an exception.
    private const string TaomOneConnect = "Clicking Connect again while the game starts no longer opens a second Bannerlord. Before, a few quick clicks could start two games, and the second one crashed.";
    private const string TaomSafeModeClicks = "Each Safe mode click now joins with Plain TAOM, even two quick clicks in a row. Safe mode also says before the game starts that it only works when the host runs Plain TAOM too.";
    private const string TaomCrashBlame = "After a crash, the Client Launcher no longer blames Better Exception Window just because it loaded last.";
    // TAOM v1.2.16 Server Tool (feat/taom-next): the host rules and mod settings ported from the Realm of Thrones Server
    // Tool (MainWindow.ServerRules.cs, MainWindow.ModSettings.cs, HostServerRules.Defaults: pause on join and the offline
    // XP freeze on, unstuck allowed, kingdom protection follows Coop's option until the host picks one). A behaviour change.
    private const string TaomHostRules = "The Server Tool now pauses the game while a player joins, and offline players gain no skill XP until they come back. Both are on by default. Turn them off in Advanced: server rules on the Settings page, where you can also limit unstuck and protect player kingdoms from AI wars (it keeps your Coop setting until you pick).";
    private const string TaomModSettings = "Players who join now use your MCM settings for the session, with no setup. Every Prepare server and Start server copies the MCM values from your own game to the server, except the ones you changed in the tool, which stay as you set them. Change them for everyone in Advanced: mod settings for players on the Settings page. Looks mods (interface, graphics or sound) stay each player's own, and players' own files stay as they are. Players need nothing new.";
    // TAOM #85 (CR-XXP67, fix/taom-85-new-compat): NewTaomCompat leaves the official TAOM.CoopCompat for 1.5.7 alone, and
    // Start server puts back the pinned 0.3.25 file or SubModule.xml from the host kit.
    private const string TaomNewCompatAlone = "Install or repair TAOM and Start server now leave the new TAOM.CoopCompat from the TAOM download for Bannerlord 1.5.7 alone and say why. These tools run TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6.";
    private const string TaomCompatPutBack = "Start server now puts back the TAOM.CoopCompat 0.3.25 file or SubModule.xml from the host kit when one is changed or missing. Before, the server stopped the same way on every start. The server window and the crash report now explain both stops.";
    // TAOM v1.2.16: ServerCommandQueue (one ordered queue, a command keeps the server run it was queued for, ConsoleSlow).
    private const string TaomConsoleQueue = "Commands the Server Tool sends to the server now go one at a time, in order, and a command still waiting in the tool when the server restarts is dropped instead of going to the new run. When the console is slow, the tool says the command may still arrive instead of saying it failed and sending it again.";
    // 8a966595 and c2d3d987 (ServerPageStatusCard, on the page the nav calls Saves), 855bd782 (ShowBlockedDllFix opens Settings).
    private const string TaomServerPageNote = "When Prepare server or Choose Coop 0.1.6 server stops, the Saves page now shows why, not only the Dashboard.";
    private const string BlockedDllSettings = "When Windows blocks a DLL the server needs, the Server Tool now opens Settings, where the red Unblock DLLs button is. Before, it opened the Saves page, which has no such button.";
    // TAOM v1.2.16 (826b2478): LICENSE.txt names the Coodsz Realm of Thrones tools in its scope line, so ToolLicense asks once more.
    public const string LicenseRotTools = "The license now also names the Coodsz Realm of Thrones tools. You are asked to accept it once more.";
    // v1.3.15, v1.2.14, v1.1.16 and BE v1.0.15 (every tool): LICENSE.txt point 1 reworded and point 5 added, like
    // compat/MODULE-LICENSE.txt. ToolLicense hashes the text, so every tool asks once more.
    public const string LicensePaidServers = "The license now says exactly what counts as a paid server, and it also rules out streams or videos that earn money, unless Coodsz says yes. You are asked to accept it once more.";
    // Same round (feat/copy-marks): tools/Write-ModuleLicenses.ps1 puts compat/MODULE-LICENSE.txt in every Coodsz module folder the tools carry.
    public const string ModuleFolderLicense = "Every Coodsz module folder the tools put in place now has a LICENSE.txt.";
    // Same round (feat/copy-marks): ModuleFingerprints writes coodsz-modules.txt into Send my logs and crash reports.
    public const string ReportModuleVersions = "Send my logs and crash reports now list the versions of your Coodsz modules, so a problem is quicker to find.";
    // v1.3.16 Coop (feat/coop-nightly-157, 721d3182 and 89c69dde): ModuleResolver takes a Modules CoopNightly made for the
    // installed game, NewerGame.Line(nightlyFor) drops the warning for it, JoinResultExplain NightlySameBuild.
    public const string CoopNightly157 = "The tools now work on Bannerlord 1.5.7 with Coop 0.1.8 from Coop's GitHub release, unzipped into the game's Modules folder as Coop, or with the Coop nightly. A Coop nightly in the game's Modules folder that was made for your Bannerlord is used instead of an older Workshop Coop. With a Coop made for your Bannerlord, the newer Bannerlord warning no longer shows. When a join fails over the game version, the join advice now asks for the same Coop build instead of a Workshop update.";
    // v1.3.16 Coop (ecd75194): the NavalDLC row only on a nightly server outside AD1259, Start writes "warSails" to match,
    // CustomModCatalog.NavalName in the client's Add mods with the nightly.
    public const string CoopWarSailsNightly = "The Server Tool can now host War Sails on a Coop 0.1.8 server, the official one or the nightly. It is new and may not work on every server build yet. Turn NavalDLC on or off on the Mods page of a setup of your own (the built in setups are locked, so click New setup on the Saves page), and Start server sets the server to match. Players with Coop 0.1.8 add War Sails (NavalDLC) with Add mods on the Client Launcher's Mods page.";
    // v1.3.16 Coop (6eb52f28): the settings sync messages implement ICommand only, so the sync no longer turns itself off
    // on a Coop that removed IServerToClientCommand (the nightly).
    public const string SyncOnNightly = "Players who join a Coop nightly server now get your MCM settings too. Before, the settings sync turned itself off on the nightly.";
    // v1.3.16 Coop (6b0ddfee and 4a184543): HeldNotes in the client settings sync, once per mod per session.
    public const string HostSetsThis = "When a player changes a mod setting the host sets, the game now says so once and tells them their own value comes back when they leave the server.";
    // v1.3.16 Coop Server Tool (25cad59d, b91258b0): KeepHostPause after Coop's own resume line, and JoinPauseGate makes a
    // host time command wait for a join pause step on its way.
    public const string HostPauseKept = "Pause game now holds when a player joins. Before, the server let the game run again 5 seconds after they came in. If you press Pause game or Resume game while the game pauses or resumes for a joining player, your choice waits for that and is never undone.";
    // v1.3.16 Coop (Coodsz, 2026-11-21): the last Coop Server Tool, DiscontinuedBanner on its Dashboard. Both Coop tools share
    // this list, so the one line also tells players the Client Launcher stays.
    public const string ServerToolDiscontinued = "The Coodsz Coop Server Tool is discontinued, and this is its last version. Hosts move to the official Bannerlord Coop Dedicated Server manager, which comes inside the Coop dedicated server download. Click Get the official server manager on the Server Tool's Dashboard to download it. The Coodsz Coop Client Launcher stays and keeps getting updates.";
    // v1.3.16 Coop (de3b3ead): CoopInstall keeps the newest of several misplaced Coop folders, the others get DuplicateLine.
    public const string CoopSeveralFolders = "When several Coop folders sit in Modules, like Coop.10 and Coop.11, Check mods now names the one to keep and rename, and says every other one is a second copy to move out.";
    // v1.3.16 Coop (de3b3ead): the client-coop-naval-dll-blocked crash signature.
    public const string CoopNavalDllBlocked = "When Coop's War Sails file did not load, because Windows blocked it or Coop sits in the wrong folder, the crash screen now says how to put Coop in again.";
    public const string ClientCoopVersionCheck = "The launcher skips Steam's version check for Coop. Your Coop version still needs to match the server.";

    // v1.3.17 Coop (Coodsz, 2026-11-21): the release ships the Client Launcher alone (Make-Release.ps1, release-assets.json
    // fromVersion v1.3.17), so from now on only the Client Launcher shows this list.
    public const string ServerToolNotInDownload = "The Coodsz Coop Server Tool is no longer part of the download. Hosts use the official Bannerlord Coop Dedicated Server Manager instead. A Server Tool you already have keeps working, but it gets no more updates.";
    // v1.3.17 Coop (98871f4c, 842d2c24, 2cd3cae2, 513b6de9 and their follow ups): the ManagerLook theme.
    public const string ClientManagerLook = "The Client Launcher has a new look, like the official Bannerlord Coop Dedicated Server Manager. It has a dark window with gold buttons, the manager's fonts and the Bannerlord Coop logo at the top of the sidebar. The small windows, menus and text boxes look the same way.";
    // v1.3.17 Coop (5cac0c82): custom setups can turn off the four libraries; an import turns off the ones the list lacks.
    public const string ClientLibrariesOff = "In a custom setup you can now turn off Harmony, ButterLib, UIExtenderEx and MCM, so the setup can join a plain Coop server. Import mod list turns off the ones the list does not have, and keeps their places in the load order.";
    // v1.3.17 Coop (5cac0c82, 7f6c98c0): NewerGame.NeedsGitHubCoop, the blocked Connect status and the missing Coop steps.
    public const string ClientCoop018GitHub = "Coop 0.1.8 is on Coop's GitHub, not on the Steam Workshop. When Connect is blocked because you need Coop 0.1.8 or newer, the launcher now sends you to Coop's GitHub releases and then to Check mods.";
    // v1.3.17 Coop (7f6c98c0): OfficialServerManager_Click under Check server on the Play page opens CoopInstall.ReleaseUrl.
    public const string ClientHostHint = "Want to host? A new link under Check server on the Play page opens Coop's GitHub release, where the official Bannerlord Coop Dedicated Server Manager comes with the Coop dedicated server.";
    // v1.3.17 Coop (2e2fcad8, 966c2850): the footer's Bannerlord version.
    public const string ClientFooterVersion = "The footer now shows the Bannerlord version on your PC, or else the one your Coop is made for.";
    // v1.3.17 Coop (78db427f, a6643896, d7166073, 88c2b052, fc59c20e, 67215aa0, 9c366f30): smaller Client Launcher fixes.
    public const string ClientSmallFixes = "A maximized window fits its screen again, also after you move it to another screen. Text boxes have a Cut, Copy and Paste menu, and Tab no longer stops on empty places. In a small window the last mod row keeps its Move and Remove buttons. Free starter reward shows in your language again, and crash reports say when the crash dump was cut short.";
    // TAOM v1.2.17 Server Tool (MinimalCash63, PRs 22, 30 and 32): the Bannerlord 1.5.7 path picked by Taom157Start.Pick
    // (coop-pin.json and a 1.5 Native). Prepare server copies the host's TAOM (Taom157Prepare, stamps per module), Start
    // server runs Taom157ServerSetup, Taom157ServerMap and Taom157ServerSettings with --no-watchdog and the engine's report
    // switches. Coop 0.1.8 is GitHub only, so the line never sends anyone to the Workshop for it.
    public const string Taom157Host = "The Server Tool can now host TAOM on Bannerlord 1.5.7 with the Coop v0.1.8 dedicated server. Coop 0.1.8 is not on the Steam Workshop, so download its dedicated server from Coop's GitHub release and unpack it into a folder of its own. On a 1.5.7 game, Prepare server on the Saves page asks for that folder and copies your own TAOM into it, about 2 GB instead of about 25 GB. After a TAOM update, click Prepare server again and it copies only what changed.";
    public const string Taom157StartServer = "On a 1.5.7 server, Start server does the setup for you. It puts TAOM's helper files and TAOM's map on the server, copies your TAOM Mod Options to it, and keeps the server from filling your drive with crash reports about missing animations. Extra mods, Rescue save and Test mods don't work on a 1.5.7 server yet, and the tool says so.";
    // TAOM v1.2.17 (937e898a, ce33e43b): Taom157Start.HostOnLine157 hides the 1.4.8 notices and shows the server's versions.
    public const string Taom157Card = "On a 1.5.7 server, the Made for card shows the Bannerlord, TAOM and Coop versions in that server, and the 1.4.8 notes are gone.";
    // TAOM v1.2.17 Client Launcher (MinimalCash63, PR 26): TaomGameLine, TaomModules157 and TaomInstallPlan.Check157. Connect
    // still checks for Coop 0.1.6 (TaomStack.CoopVersion) on both lines, so the line says Connect comes later.
    public const string Taom157Client = "On Bannerlord 1.5.7 the Client Launcher no longer shows TAOM as the wrong version. Install or repair TAOM only checks your TAOM there and says what is wrong, since TAOM for 1.5.7 comes from TAOM's own download. A missing TAOM.CoopCompat is fine there for now. Connect on Bannerlord 1.5.7 comes in a later update.";
    // TAOM v1.2.17 (MinimalCash63, PR 24): the TAOM_AIO_Compat row in taom-mod-compat.json (Risky) and TaomModNotes.
    public const string TaomAioPatch = "The mod list now has the TAOM AIO Compatibility Patch for Bannerlord 1.5.7, marked Risky until it is tested in Coop. When it is on your setup, the tools remind you that the host and every player need the same version and the same switches in its MCM settings.";
    // TAOM v1.2.17, shared with v1.3.16 Coop: JoinResultExplain.NightlySameBuild for a server on a Bannerlord newer than 1.4.8.
    public const string TaomJoinSameBuild = "When a join fails because the server runs a newer Bannerlord than 1.4.8, the advice now asks for the same Coop build on every PC instead of a Workshop update.";
    // TAOM v1.2.17 (PR 27): both TAOM windows' sidebar brand credits MinimalCash63 and Haterade.
    public const string TaomCredits = "Both tools now credit MinimalCash63 and Haterade under the Coodsz name in the sidebar, for their work on TAOM in Coop.";
    // TAOM v1.2.17, shared (9c366f30): DumpStackSummary says when a dump is cut short.
    public const string DumpCutShort = "Crash reports now say when the crash dump was cut short.";
    // TAOM v1.2.18 Client Launcher (PR 34, f1250d34): TaomExpected wants Coop v0.1.8 on TaomLine.Line157, Get Coop 0.1.8
    // opens Coop's v0.1.8 GitHub release, and Use Workshop Coop and the extra Coop set aside stay off on 1.5.7.
    public const string Taom157Connect = "On Bannerlord 1.5.7 the Client Launcher can now Connect to a TAOM server with Coop v0.1.8. Coop 0.1.8 is not on the Steam Workshop, so click Get Coop 0.1.8, download BannerlordCoop-v0.1.8.zip from Coop's GitHub release and unzip it into Bannerlord's Modules folder. The 1.4.8 steps work as before.";
    // TAOM v1.2.18 (PR 34): TaomModules157.MeetsMinimum and UpdateTaom, in the Client Launcher and before a 1.5.7 Start server.
    public const string Taom157NeedsNewTaom = "On Bannerlord 1.5.7 both tools need TAOM 2.0.41 or newer. With an older TAOM they tell you to update it.";
    // TAOM v1.2.18 (PR 34): TaomLoadOrder.LaunchModules leaves TAOM.CoopCompat out when it is not installed on 1.5.7.
    public const string Taom157CompatOptional = "On Bannerlord 1.5.7 a missing TAOM.CoopCompat no longer blocks Connect. If your host uses it, the launcher says to get its folder from the TAOM download page.";
    // TAOM v1.2.18 (PR 34): Taom157Start.ReadRegion reads region from the host's server-config.json, EU when none is set.
    public const string Taom157Region = "On a 1.5.7 server, Start server now keeps the region set in server-config.json. It uses EU only when none is set.";
    // TAOM v1.2.18 (PR 34): Taom157Prepare.StampFolderRefused before any staging record is written or deleted.
    public const string Taom157StagedLink = "Prepare server now stops when the server's .coodsz-staged folder is a link to another folder, and asks for a real folder there.";
    // TAOM v1.2.18 (PR 34): the TaomClient getting started guide splits its 1.4.8 and 1.5.7 sections.
    public const string Taom157ClientGuide = "The Client Launcher's getting started guide now has separate steps for Bannerlord 1.4.8 and 1.5.7.";
    // v1.3.4 Coop: COOP Server Settings Lock left the setup (RetiredSettingsLock), its old values became the host's MCM
    // defaults (SettingsLockDefaults, not in AD1259), and the settings sync holds the host's values while joined.
    private const string SettingsLockGone = "COOP Server Settings Lock is gone from the Steam Workshop, Nexus Mods and ModDB, so the locked setups no longer use it. If a setup of yours still has it on, the tools tell you to turn it off, and every player needs to turn it off too. Saves made with it still load.";
    private const string SettingsLockValues = "Where your setup has no MCM values of its own for Xorberax's Legacy, Diplomacy, Settlement Culture Drift or Agriculture Estate, the Server Tool gives them the values Settings Lock used. You can change them under MCM settings.";
    private const string SettingsHeld = "While you're joined, the MCM settings the host shares stay on the host's values, even if you change them. You get your own values back when you leave.";

    // v1.2.78 and v1.0.33 (Friends and AD1259 hosts): a new save name takes any letter or digit, checked against the server folder's start script.
    private const string AnyLetterSaveName = "A new campaign name can now use letters from any language, like YÜZÜK TEST. If the server folder still has an older start script that only takes A to Z, Start server tells you before the server starts.";
    // v1.0.33 (AD1259 server): the server copies the pinned MCM from a Workshop download the Coop and Friends launcher turned off.
    private const string AD1259ParkedMcm = "Start server no longer stops on an old server MCM when the Coop and Friends launcher turned off an old Workshop MCM download on the same PC. The server tool still finds the MCM version the pack needs there.";
    // v1.1.54 (TAOM server tool): Start server restores 0Harmony.dll and the GauntletUI DLLs in engine bin, and a missing files crash is no load crash.
    private const string TaomEngineDllsBack = "When antivirus removes 0Harmony.dll or the GauntletUI files from the server, Start server now puts them back from the Workshop Coop server after checking each one, or names the missing files when it can't. A crash from missing files no longer counts as a save that crashes on load, so there is no load retry or Coop Save Rescue for it.";

    // v1.1.49: Coop Save Rescue skips taking a dead hero off a troop list he is not on, so a destroyed clan no longer stops the TAOM server.
    private const string TaomRosterGuard = "The server no longer stops when a clan is destroyed and one of its heroes can't be taken off a party's troop list. Coop Save Rescue skips that one change and the game goes on.";

    // v1.2.71, v1.1.48 and v1.0.26: a Full mod list link on every tool's mods page opens that tool's community list
    // (MODS.md on GitHub). Friends only: the built in community list marks big overhauls Broken.
    private const string FullModList = "The mods page has a new Full mod list link. It opens the community list on GitHub, with every mod, its status and what players report.";
    private const string FullModListTaom = "The mods page has a new Full TAOM mod list link. It opens the TAOM community list on GitHub, with every mod, its status and what players report.";
    private const string FullModListAD1259 = "The mods page has a new Full AD1259 mod list link. It opens the AD1259 mod list on GitHub, with every pack mod, its version and what players report.";
    private const string OverhaulsBroken = "The built in mod list now marks big overhauls as Broken. Only TAOM and AD1259 work in Coop, through their own Coodsz tools. Eagle Rising, Burning Empires, Realm of Thrones, The Old Realms and Shokuho need a major overhaul compat patch first.";

    // v1.2.70, v1.1.47 and v1.0.25: the crash report windows ask for the player's Discord name before Send.
    private const string ReportDiscordName = "Send crash report and Send my logs now ask for your Discord name, so Coodsz can reach out to you about your report. The tool remembers it, so you only type it once.";

    // v1.2.69 (Friends only): Improved Garrisons Coop Compat, a server patch module Coodsz Host adds when
    // Improved Garrisons is on. Not in TAOM, and off for AD1259.
    private const string IgKeepSettings = "Improved Garrisons town settings now stay after a server restart. Your templates, recruitment and training are kept. Coodsz Coop Server Tool adds the fix by itself when Improved Garrisons is on, so players install nothing.";
    private const string IgOwnerChange = "When a town goes to another member of the same clan, its Improved Garrisons settings stay. When it goes to another clan, they are reset.";
    private const string IgOwnerOnly = "Only a town's owner can change its Improved Garrisons settings now. Before, any player could change any town.";

    // v1.2.68 and v1.1.46: CoopModPatch logs and the Improved Garrisons settings in the server log packs, the
    // graphics driver crash cause, and in the TAOM tools the crash auto triage and the TAOM.CoopCompat startup check stop.
    // The TAOM entry also carries BigBattlesWarning and the popup and Autosave box fixes that shipped in v1.2.67.
    private const string LogPackModPatch = "Send my logs in the server tool now also sends the CoopModPatch logs and the Improved Garrisons settings, so Improved Garrisons problems can be checked. They are cleaned like the other logs.";
    private const string GraphicsCrash = "The crash screen knows one more cause. When the game crashes with a graphics driver error while the map terrain loads, it tells you to update your graphics driver, and you don't need to send a report.";
    private const string TaomStartupStop = "When TAOM.CoopCompat stops a start during its own startup check, the server tool now says your save is fine and to press Start server again. It no longer runs the save fixes.";
    private const string TaomAutosaveBlank = "Save settings also works with an empty Autosave minutes box, and a box left empty shows the kept value again.";
    private const string PopupsFit = "Popup windows now always open inside the screen, so Send and every other button can be reached.";

    // v1.2.67 and v1.0.24: big battles with Limitless (Coodsz Host and the client launcher; the AD1259 mod set is
    // locked and cannot hold Limitless, so AD1259 gets no big battle line), and the crash report follow ups and auto triage.
    private const string BigBattles = "Big battles with Limitless. With Limitless and Limitless Coop Compat in the server's mods, Battle size goes up to 10000 soldiers on the field, with a RAM guide and a suggested size for your PC.";
    private const string BigBattlesAddPatch = "When the server has Limitless but not Limitless Coop Compat, the Settings page says so and adds it for you with one click.";
    private const string BigBattlesLiveSize = "With both on, a Battle size you save while the server runs applies from the next battle.";
    private const string BigBattlesSave = "Adding Limitless Coop Compat to a running campaign keeps the save usable.";
    private const string BigBattlesWarning = "Joining a big battles server on a PC with less than 16 GB of RAM shows a short warning. It never stops you from joining.";
    private const string CrashTriage = "When the game or server crashes, the crash screen now tells you what happened and how to fix it. If it's a known cause you don't need to send a report.";
    private const string RawAssets = "Servers no longer crash on mods that ship a raw Assets folder like Banner Fix, Raise your Banner and Raise your Torch. Their Assets folder is hidden in the server copy, and players keep theirs.";
    private const string ModPatchStop = "When CoopModPatch stops the server once after its first setup, it is no longer shown as a crash.";
    private const string DiplomacyWarning = "Using Diplomacy without Diplomacy Coop Compat now shows a warning.";
    private const string SilentExit = "When the server stops with exit code 1 and no error, Coodsz Coop Server Tool now explains it was likely closed from outside, for example by an antivirus, and the crash report adds the Windows event log.";
    private const string SilentExitAD1259 = "When the server stops with exit code 1 and no error, the server tool now explains it was likely closed from outside, for example by an antivirus, and the crash report adds the Windows event log.";
    private const string SaveOrder = "Changing the mod load order no longer hides your campaign save. Bannerlord does not check the order, so the server opens the save and only notes that the order changed. When a save really does not match the mods, Coodsz Coop Server Tool now says so instead of running the save fixes.";

    // v1.2.66, v1.1.45 and v1.0.23: War of the Ring sync, a note and screenshots in reports, and small fixes.
    private const string WotrSync = "With TAOM on a Coodsz Coop server, players who join now see the War of the Ring bar as soon as the full war starts, and its numbers move live. Their game also follows the server's war phase and peace rules. Before, the bar only showed after a rejoin and then stayed frozen.";
    private const string TaomWotrSync = "Players who join a TAOM server now see the War of the Ring bar as soon as the full war starts, and its numbers move live. Their game also follows the server's war phase and peace rules. Before, the bar only showed after a rejoin and then stayed frozen. The host and the players both need this version.";
    private const string ReportNotes = "Send my logs and Send crash report now have a box to tell Coodsz what happened in your own words. Names, addresses and passwords are taken out of it like in the logs.";
    private const string ReportScreenshots = "You can add up to 5 screenshots to a report with Add screenshots, or paste one with Ctrl+V. Their hidden details (like where and when a photo was taken) are removed, but check them for anything private before you send.";
    private const string ReportOptional = "Both are optional. Save a copy instead keeps them too.";
    private const string BlankNumberSave = "Save settings no longer stops on an empty number box. A setting your server's files do not have yet is left as it is, and the rest of your settings save.";
    private const string WindowFits = "On a small screen or with display scaling, the launcher window now opens inside the screen, so the Install update button is never off to the side. The update bar stays at the top while you scroll the page.";
    private const string LogBatches = "The server tool stays smooth during a burst of server log lines. It draws them a few times a second and only the newest ones the log keeps, and it still reads every line for players, join notes and saves.";
    private const string TaomReadyJoin = "Once the TAOM server is serving, the note under Start server says Server is ready. Players can join now. It no longer keeps the starting note, also in another language or after a start that switched the setup or loaded a backup.";
    private const string MoreCheatsGone = "The Cheats page lost its old More cheats section. Everything in it is on the page above: Set gold in Gold, Give influence in Clan, the player cards show who is online, and the Console page has the console.";
    private const string ReportsEasier = "Reporting how a mod worked is easier to find. A short line under the title points to Report a mod, each mod in your list has a small Report link, and one click on Send now sends, with what is sent shown in the window. With no extra mod to rate, Report a mod says so.";

    // v1.2.65: player kingdom protection in Coodsz Host's Advanced server rules.
    private const string KingdomGuard = "Coodsz Coop Server Tool can now protect player kingdoms from AI wars, in Advanced server rules on the Settings page. It starts on what your Coop setting already was, so nothing changes until you pick. Always keeps AI kingdoms from starting a war with any kingdom a player rules or serves, online or not. Wars your players start, a player attacking first and rebellions still happen.";

    // v1.2.64 and v1.1.44: the TAOM enlist guard in the client add on, and a calmer settings sync retry.
    private const string TaomEnlist = "When TAOM is in the game, enlisting with a lord is greyed out for players who join a server, since TAOM's enlisting does not work for them yet. Before, swearing the oath made them attack the lord instead.";
    private const string SyncCheckLess = "Settings sync checks less often for a mod you don't have, so the game stays smooth.";

    // v1.2.63, v1.1.43 and v1.0.22: settings sync, the save mods card, ModderLords verdicts, a calmer Your mods card.
    private const string SettingsSync = "Players who join a Coodsz Coop server now use the host's MCM mod settings by themselves, so every PC runs the mods the same way. It only lasts for that session and never changes their own settings files. Hotkeys, interface, sound and camera settings stay each player's own.";
    private const string SettingsAdvanced = "Hosts get an Advanced area on the Settings page to change a mod's values for players, or to let players keep their own settings for a mod. Settings sync is adapted from ModderLords by Ryvius, with his permission.";
    private const string SaveModsCard = "When a save needs mods your setup leaves out, the server tool shows a tidy card with the mod names and one button that fixes it, like turning the mods back on or switching to the setup the save was made with.";
    private const string ModderLordsVerdicts = "The Mods page now shows the tested verdict from ModderLords by Ryvius next to the player counts, like Tested works or Works with settings.";
    private const string ModderLordsDefaults = "When Ryvius tested a mod with certain settings, Coodsz Coop Server Tool uses them as the host defaults. Anything you set yourself still wins.";
    private const string CalmModsCard = "The Your mods card is calmer. It has one short line, Add mods as the main button next to Check mods or Install mods, and the rest in a More menu. Save mods only shows while a change is waiting.";
    private const string ServerMods = "Connect now gets the mods the server needs. When your setup has mods that are missing or out of date, one box shows them, and Get them and connect downloads them through Steam, turns them on and joins. Mods that are not on the Steam Workshop are listed by name with their Get it link.";
    private const string GetAllMissing = "Get all missing now has Steam subscribe to the missing Workshop mods of the pack and download them by itself. Nexus mods still open their page, since only you can download those.";

    // v1.2.63, v1.1.43 and v1.0.22: Coop Save Rescue keeps a war with a kingdom that has no leader from crashing the server.
    private const string WarNoLeader = "A kingdom with no leader no longer crashes the server when a war is declared, like it did on TAOM's War of the Ring day. Coop Save Rescue lets the war go ahead, gets past the call to war that crashed, and lists it with the other save fixes.";

    // v1.2.63: host server rules in Coodsz Host (pause on join, offline XP freeze, coop.unstuck).
    private const string PauseOnJoin = "Coodsz Coop Server Tool now pauses the game while a friend joins or loads, and puts your speed back once they are on the map. It lets go after 3 minutes, so a stuck join can't freeze the world.";
    private const string OfflineXp = "Players who are offline no longer gain skill XP on a Coodsz Coop server until they come back. Coop already freezes their gold and food. Adapted from ModderLords by Ryvius, with his permission.";
    private const string UnstuckRule = "Hosts can limit coop.unstuck in Advanced: server rules on the Settings page, so players can't use it to escape fights, prisons or nearby enemies, or more than once every 5 minutes. Host only leaves it to you.";

    // v1.2.62, v1.1.42 and v1.0.21: Coop Save Rescue on every start, the Stop check, cheats fixes.
    private const string AutoRescueStart = "Start server and Restart now load Coop Save Rescue every time. It fixes the known save problems by itself, keeps a backup from before the fixes, saves, and shows a note at the top until you dismiss it. Players can join while it is on.";
    private const string AutoRescueRetry = "If the server crashes while the save loads, it starts once more with the deeper save fixes, never more. Run Rescue save, which runs every fix once, is now on the Logs page.";
    private const string AutoRescueStop = "After Stop the server tool reads the log of that run and tells you when the save has a problem the next Start fixes. When Coop already freed parties from an ended battle by itself, it only says so in a short note.";
    private const string CustomToConsole = "Custom commands moved from the Cheats page to the Console page, which lists every Coop command and keeps your recent ones.";
    private const string VassalTierCheck = "Join a kingdom on the Cheats page no longer says Done when Coop will turn it down. A clan below tier 2 gets a note to use Set clan tier first.";
    private const string VassalTierNote = "Join a kingdom on the Cheats page now says that Coop still turns it down for a clan below tier 2, since Coop only says so in the server log.";

    // v1.2.62, v1.1.42 and v1.0.21: one click mod reports in Coop and Friends, the TAOM client and the AD1259 client.
    private const string ReportsHere = "Reporting a mod is right in the tool now. Click Report a mod at the top, pick Works or Broken for each mod, and send it. No GitHub account and no Discord message needed.";
    private const string ReportCrash = "After a crash a red bar asks you to tell everyone which mod broke. When the crash log names a mod, it is already marked Broken, so you only check it and send.";
    private const string ReportBar = "After a game a small bar asks how your mods worked. Close it three times in a row and it stays away.";
    private const string ReportCounts = "Each mod on the Mods page shows how many players say it works and how many say it is broken, so you can see which mods are safe before you play.";
    private const string ReportPrivacy = "The first time you send, the tool shows exactly what is sent. Reports are anonymous, and Coodsz checks them before they count.";

    // v1.2.62, v1.1.42 and v1.0.21: crash reports in all six tools.
    private const string CrashSend = "Crashed? Click Send crash report on the red bar. The tool packs your cleaned logs, shows you every file, and sends them only when you click Send. You get a short code like CR-7KQ2M to post in Discord instead of zip files.";
    private const string CrashServer = "The server tools show a big Send server crash report bar when the server crashes or stops by itself, with the server logs ready to go.";
    private const string CrashLogs = "Send my logs is always on the main page, for a stuck join, a desync or lag. Something went wrong? Send your logs sits next to Connect and Start server.";
    private const string CrashPrivacy = "Your Windows name, PC name, Steam ids, addresses and passwords are taken out before anything is packed, and crash dumps are never sent. Logs can still show in game player names. Save a copy instead keeps the same zip on your PC. The old log zip buttons are gone.";

    // v1.2.61, v1.1.41 and v1.0.20: join fix, server console, update bar, TAOM ready note and BattleSizeResized note.
    private const string JoinBaseline = "Joining no longer gets stuck on Final Synchronization when a few parties went missing while your game loaded the campaign. The client fix puts back the parties the server says are there.";
    private const string ConsoleNoStall = "The server tool now reads the server's console without waiting for its own window, so a burst of log lines can no longer slow the server down and time out a player's character on join.";
    private const string UpdateBarFits = "In a smaller window the update bar now stays at the top with its Install update button, instead of being pushed off to the side.";
    private const string TaomReadyNote = "Once the server is serving, the TAOM server tool now says Server is ready instead of still showing the starting note.";
    private const string BattleSizeNote = "When BattleSizeResized is in your setup, the TAOM tools now tell you it overrides the server's battle size and how to fix that.";
    private const string WorkshopCoopCore = "The TAOM client now also puts back the Steam Workshop copy of Coop.Core.dll when Coop and Friends edited it for the join password, so TAOM.CoopCompat no longer crashes the game at launch.";

    // v1.2.60, v1.1.40 and v1.0.19: Rescue save fixes killed lords still marked alive.
    private const string RescueDeadLords = "Rescue save now fixes a save that crashes the server on load because a lord who was executed or killed is still marked alive. It marks that lord dead, and one bad hero can no longer stop the whole save from loading.";

    // v1.2.59, v1.1.39 and v1.0.18: license, UIExtenderEx missing file fix, Steam not ready note, cheats in TAOM and AD1259.
    private const string LicenseOnce = "The tools now have a license. It shows once the first time you open a tool, and one I agree covers all Coodsz tools. The full text is in LICENSE.txt next to the program.";
    private const string UiexMissing = "When UIExtenderEx is missing one of its files, Check mods says so and tells you how to get it back. Until then Connect turns off its Compiled Prefabs for that game, so the game still starts without the dependency conflict popup.";
    private const string SteamNotReady = "When Steam was not open or logged in as the server started, Home now says the Steam lobby did not start and to press Restart, instead of waiting for a lobby that never comes.";
    private const string CheatsHere = "The new Cheats page from Coodsz Coop Server Tool is here too. Pick a player card, pick a cheat, fill in the boxes and press Run. Every cheat sends Coop's own command.";
    private const string ConsoleHere = "The Console page lists every Coop command by category, with a How to use guide, an example you can fill in with one click, and Find ids buttons.";

    // v1.2.58, v1.1.38 and v1.0.17: new Cheats and Console pages, lobby retry, non Steam copy notes.
    private const string NewCheats = "The Cheats page is new. Pick a player card, pick a cheat, fill in the boxes and press Run. Every cheat sends Coop's own command, and the page shows the exact line it sends. Undo works where it can.";
    private const string NewConsole = "The Console page lists every Coop command by category, with a How to use guide, an example you can fill in with one click, and Find ids buttons that list the ids you need.";
    private const string LobbyRetry = "Joining through a Steam lobby keeps looking for up to 30 seconds, so a lobby that is still starting is found. Coodsz Coop Server Tool shows on Home whether the lobby is really up.";
    private const string SharedSteamId = "The server log now says when two players join with the same Steam ID. That happens with non Steam copies of the game, and those players end up sharing one character.";
    private const string NonSteamFiles = "The launcher notes in its log and in Save logs for help when the game folder has files from a non Steam copy. Nothing is blocked.";
    private const string RbmTextOnly = "The RBM box in the server tool now says the truth. RBM runs in each player's own game, so players set it in their own launcher and the server does not use it.";
    private const string RbmClientText = "The RBM text in the launcher no longer says Match the server. Everyone in your group should pick the same RBM settings so battles feel the same.";
    private const string TaomIgClient = "CoopModPatch works with Improved Garrisons on the TAOM server. Improved Garrisons only comes with the game's client files, so the server tool now makes the server copy from them.";
    private const string TaomCrashZip = "Save logs for help now cleans the crash report zip before packing it, so your join password and addresses are taken out. A rar crash report is left out with a note.";

    // v1.1.37: Improved Garrisons server code for CoopModPatch.
    private const string TaomIgServer = "The server tool copies Improved Garrisons again when its server copy is missing the server DLL, so CoopModPatch works on the server.";

    // v1.2.57: Max clans per kingdom.
    private const string ClanCap = "Coodsz Coop Server Tool has a new Max clans per kingdom setting on the Settings page. A full kingdom cannot take more clans from players or the AI, so one kingdom cannot swallow every lord on a big server.";

    // v1.2.56 and v1.0.15: the join password out of the log zips.
    private const string LogsPassword = "Save logs for help now takes your join password out of the logs.";

    // v1.2.55 and v1.0.14: RBM's settings in each player's own game.
    private const string RbmCard = "Coodsz Coop Server Tool has a new RBM settings card on the Settings page when RBM is in your setup. It changes RBM's parts and campaign options for the game on this PC, without opening the game.";
    private const string RbmPlayers = "RBM's campaign rules run in each player's own game, not on the Coop server. Connect now turns off RBM's Lords buy equipment in your own RBM settings, so lords stop emptying the town markets. Untick Keep RBM coop friendly to keep your own setting.";
    private const string RbmNoServerWrite = "Start server no longer changes RBM's settings on the server PC. The Coop server does not run RBM's code, so they did nothing there.";

    // v1.2.53, v1.1.33 and v1.0.12: easy joining, the CoopKeep server features, TAOM mod setups.
    private const string JoinAddresses = "The server tool lists the addresses your friends can use, each with a Copy button. Copy invite puts a short message for Discord on your clipboard, without the password.";
    private const string JoinRouter = "The server tool can open the server port on your router by itself while the server runs. Allow through firewall adds the Windows Firewall rule after you say yes.";
    private const string JoinClient = "When the server runs on your own PC, the launcher fills in 127.0.0.1 for you. The ? next to Steam lobby and Direct opens a short guide for each way to join.";
    private const string CrashRestart = "New option Restart the server if it crashes. It waits a little longer after each crash and gives up after 3 crashes in 10 minutes.";
    private const string Backups = "A backup of your campaign is made after every save, and the last 10 are kept for each save. Load backup puts one back while the server is stopped.";
    private const string TaomBackups = "The server tool makes a backup of your campaign after every save. Restore save backup puts one back.";
    private const string SafeStop = "Stop and Restart ask the server to save and close by itself, so the last minutes of play are kept. Restart warns the players 30 and 10 seconds before.";
    private const string TaomStop = "Stop and Restart wait until the save is really written, so the next start no longer goes back to an older save. Restart warns the players 30 and 10 seconds before.";
    private const string ServerStatus = "The server status follows the server's own messages. It no longer says SERVING before the server is ready, and a crash after that is told as a crash.";
    private const string TaomStatus = "The server tool shows a gold loading bar while the server starts. When a save does not load, it shows FAILED with the reason instead of Serving.";
    private const string CpuRam = "While the server runs, a small line shows how much CPU and memory it uses.";
    private const string SafeBans = "Ban player only takes real Steam IDs, and the ban list is always written in one piece.";
    private const string AllSettings = "The Settings page shows every Coop setting, with the rest under Show more Coop settings. Numbers out of range are refused, and the settings are kept for each campaign save.";
    private const string CompatBadges = "Mods show a community badge, Works, Broken, Client only or Unknown. Hover it for the note, or click it to report how the mod worked for you.";
    private const string OwnResults = "Next to the badge you see your own result, like Worked for you, Crashed for you or Passed on your server. Mods no list knows show Unknown, not tested.";
    private const string HostModTest = "Coodsz Coop Server Tool has Test mods on the server on the Modules page. It tries each mod alone on a throwaway campaign and tells you which ones load. Your real saves are not touched.";
    private const string PatchSaveCheck = "Adding Diplomacy.CoopCompat to a running campaign no longer hides the save as not matching the mods.";
    private const string SaveLogsButton = "Save all logs to one zip and Open client logs are now one Save logs for help button, with a small arrow for the other choice.";
    private const string SteamRunning = "The Steam line no longer says Steam is closed while it is running.";
    private const string TaomSetups = "Mod setups are here. Plain TAOM stays the sealed set, TAOM plus is a locked set of tested mods, and you can make your own setup with Add mods.";
    private const string TaomModList = "The new Mod list window shows the TAOM mod list, where each entry comes from and when it was checked. Add to setup puts a mod in your setup.";
    private const string TaomHostEditing = "The server tool can now change your own setups like the client does, with Add mods, Save mods, move, remove and an on and off switch. Plain TAOM and TAOM plus show a Locked tag.";
    private const string TaomDependencies = "Before every launch, mods that ask for Harmony, ButterLib, UIExtenderEx or MCM are fixed to use TAOM.Dependencies, and their DLLs are unblocked. The mods go back as they were when the game closes.";
    private const string TaomLoadOrder = "Auto sort puts extra mods below TAOM in the order the TAOM team asks for. Connect and Start server check for missing or clashing mods first.";
    private const string TaomCrashTools = "Find the crashing mod starts the game with half of your extra mods at a time until it finds the one that crashes. Safe mode joins once with Plain TAOM.";
    private const string TaomButtons = "Fewer buttons in view. Install or repair TAOM is one button, Low FPS fix is an On and Off switch, and the rarer tools are in the More menus.";
    private const string TaomServerMods = "The server tool can now run a server with your extra mods, and it tells the host which mods players have to get from Nexus Mods or ModDB.";
    private const string TaomModTest = "Test mods on the server tries each mod alone on a copy of your save and tells you if it loads. Your real saves are not written.";
    private const string TaomOldSave = "Picking a save made on another TAOM version shows a warning and asks before Start.";
    private const string TaomCommunityMods = "TAOM community mods and patches with TAOM_ names, like TAOM_PlayerVoices, can now be added to a setup.";
    private const string KeepHome = "Coodsz Coop Server Tool opens on a new Home page with one Start button, Save game, Backup now, Restart and who is online. Create a world walks you through a new campaign.";
    private const string KeepPages = "New Players, Map, Encyclopedia and Guides pages. The Map shows the running campaign live, and the Encyclopedia lets you browse its heroes, clans, towns, troops and items.";
    private const string KeepCheats = "The Cheats page is new. Pick one player or several and give or take gold, health, troops, skills and items. Each player has a profile with skills, gear and army.";
    private const string KeepExtras = "World presets fill in Casual, Standard or Hardcore settings. Streamer mode hides IP addresses, codes, Steam ids and the password on screen.";
    private const string AddModsToCampaign = "You can add mods to a running campaign. Start server asks once and keeps a backup first. A save missing one of its mods is still refused.";
    private const string LockedTag = "Locked setups show a Locked tag next to the setup name, and the buttons that are off say why.";
    private const string GameSpeedCard = "The Server page shows whether 2x speed and the battle speed limit are on, with buttons to change them.";
    private const string CompatUpdate = "The community mod list has the Coop 0.1.6 verdicts from the Coodsz compatibility thread, with the patches each mod needs.";
    private const string TaomAddMods = "When your setup only adds mods to a save, the server tool asks once and keeps a copy of the save first.";
    private const string OpenAgain = "The new What's new button near the language button opens these notes again any time, with the last few versions.";
    private const string NewBadgeLine = "New things in this version have a gold NEW badge. Click one to try it and the badge goes away.";
    private const string TaomGuides = "Guides page with short help for hosting, joining, saves, mods, game speed and common problems. The server tool has it in the side menu, the launcher next to the language button.";

    // v1.3.2, v1.2.2, v1.1.2 and BE v1.0.1 (every tool): in tool replies (ReportReplies, ReportReplyWindow).
    internal const string RepliesInTool = "When Coodsz answers a report you sent, the answer now shows up right here in the tool. You don't have to wait for it in Discord.";
    internal const string MyReportsPage = "My reports next to Send my logs keeps your sent reports and every answer, with a search.";
    internal const string AskOnDiscord = "If a fix did not help, click Still not working? Ask Coodsz on Discord. It opens the Coop Discord and copies a short message with your report code.";

    public static readonly IReadOnlyList<WhatsNewEntry> Friends =
    [
        new("v1.3.18", [ClientCoopVersionCheck]),
        new("v1.3.17", [ServerToolNotInDownload, ClientManagerLook, ClientLibrariesOff, ClientCoop018GitHub, ClientHostHint, ClientFooterVersion, ClientSmallFixes]),
        new("v1.3.16", [ServerToolDiscontinued, CoopNightly157, CoopWarSailsNightly, CoopSeveralFolders, CoopNavalDllBlocked, SyncOnNightly, RotSyncJoin, HostSetsThis, HostPauseKept, StopNoHang, SayLetters, RotConsoleBox, LicenseRotTools]),
        new("v1.3.15", [LicensePaidServers, ModuleFolderLicense, ReportModuleVersions]),
        new("v1.3.14", [MoreLanguages, CoopNewerGameNote, TermsButtonTranslated, CoopNewerSettings]),
        new("v1.3.13", [MlwRecruits, CopySetupTip]),
        new("v1.3.12", [NightlySavesShown, LogsPageTranslated, SaveRescueCaptiveHost]),
        new("v1.3.11", [TerrainShaderClear]),
        new("v1.3.10", [ServerFatalLine, UseSteamServer, MapMissingLabel, JoinPillGameOpen, SendLogsClientPid]),
        new("v1.3.9", [Coop017OnWorkshop, StoryModeDllCopied]),
        new("v1.3.8", [CoodszNotice, DownloadCoopServer, CoopServerBehind]),
        new("v1.3.7", [RedesignLook, ModsLockedRunning, CoopDashboardCards, ChipWords, CrashBarWaitsRetry, SettingsSyncEmpty, MyReportsClock]),
        new("v1.3.6", [ManualCoop, CoopWrongFolder, CoopUnblocked, ServerPackage017, ServerUnblocked, ActiveCoopSource, SteamStaysOn, PasswordAtStart, NewCampaignStaysNew, SmithingDefault017, JoinErrors017, MovementBoxesGone, AddOnNotAntivirus]),
        new("v1.3.5", [MyReportsVisible, FasterReplies, TimeButtonsOn, ConsoleBuiltins, SetupProblems, BinLeftovers, BlockedWrite, HostMcmValues, MapModRefused, SidebarWordsFit, PageListScrolls]),
        new("v1.3.4", [PvpCountdown, PvpCountdownClient, MyReportsBadge, MyReportsStatus, MyReportsAnswer, SettingsLockGone, SettingsLockValues, SettingsHeld]),
        new("v1.3.3", [PlayHoldHost, DonateButton, AntivirusMissing, FixAntivirus, SaferUpdate, MapModWarned, PvpRulesAdded, CrashStackAdded, SaveBackupsFixed, SandboxNote], DonatePicture),
        new("v1.3.2", [FindBannerlordPick, RepliesInTool, MyReportsPage, AskOnDiscord, FailedLoadNoSaveAuto, SafeLinks]),
        new("v1.3.1", [CoopIgPartySize, PlayerChildBirth, CommandWaitsForAnswer]),
        new("v1.3.0", [CoopNewNames, OneZip, SettingsMove, MoreNationsSwitch, DamagedSteamLibrary]),
        new("v1.2.79", [UpdateKeepsSiblings]),
        new("v1.2.78", [AnyLetterSaveName]),
        new("v1.2.77", [McmNexusWins, HostConsoleText, DumpSentenceOnlyWithDump]),
        new("v1.2.76", [AutoRestartWorks, RestartNoFreeze, McmFromNexus, CrashDumpSend, ServerModReports, BackupsWaitForSave, CrashLineThisRun, DeclinedMods]),
        new("v1.2.75", [RefreshNamed]),
        new("v1.2.74", [ExtraSandBoxMap]),
        new("v1.2.73", [JoinCheck, SteamLobbyShared, GameStillOpen, WorkshopTwinsNote, ExitEvidence]),
        new("v1.2.72", [MaleBirth, OldMcmRefused, VersionsKept]),
        new("v1.2.71", [FullModList, OverhaulsBroken]),
        new("v1.2.70", [ReportDiscordName]),
        new("v1.2.69", [IgKeepSettings, IgOwnerChange, IgOwnerOnly]),
        new("v1.2.68", [LogPackModPatch, GraphicsCrash]),
        new("v1.2.67", [BigBattles, BigBattlesAddPatch, BigBattlesLiveSize, BigBattlesSave, BigBattlesWarning, CrashTriage, RawAssets, ModPatchStop, DiplomacyWarning, SilentExit, SaveOrder]),
        new("v1.2.66", [WotrSync, ReportNotes, ReportScreenshots, ReportOptional, BlankNumberSave, WindowFits, LogBatches, MoreCheatsGone, ReportsEasier]),
        new("v1.2.65", [KingdomGuard]),
        new("v1.2.64", [TaomEnlist, SyncCheckLess]),
        new("v1.2.63", [SettingsSync, SettingsAdvanced, ServerMods, PauseOnJoin, OfflineXp, UnstuckRule, SaveModsCard, ModderLordsVerdicts, ModderLordsDefaults, CalmModsCard, WarNoLeader]),
        new("v1.2.62", [AutoRescueStart, AutoRescueRetry, AutoRescueStop, CustomToConsole, VassalTierCheck, CrashSend, CrashServer, CrashLogs, CrashPrivacy, ReportsHere, ReportCrash, ReportBar, ReportCounts, ReportPrivacy]),
        new("v1.2.61", [JoinBaseline, ConsoleNoStall, UpdateBarFits]),
        new("v1.2.60", [RescueDeadLords]),
        new("v1.2.59", [LicenseOnce, UiexMissing, SteamNotReady]),
        new("v1.2.58", [NewCheats, NewConsole, LobbyRetry, SharedSteamId, NonSteamFiles, RbmTextOnly]),
        new("v1.2.57", [PlainCoop, ClanCap, AddModsBox, SaveCheckLibs, OlderPatchSave, SetupNames, ChoiceLabels, OtherHints, CommunityChips, CheatsNames, Wording]),
        new("v1.2.56", [LogsPassword]),
        new("v1.2.55", [RbmPlayers, RbmCard, RbmNoServerWrite]),
        new("v1.2.54", [HoverTips, AskDeleteSetup, AskRemoveCommand, RbmNewFile]),
        new("v1.2.53", [KeepHome, KeepPages, KeepCheats, KeepExtras, AddModsToCampaign, LockedTag, GameSpeedCard, CompatUpdate, OpenAgain, NewBadgeLine, JoinAddresses, JoinRouter, JoinClient, CrashRestart, Backups, SafeStop, ServerStatus, CpuRam, SafeBans, AllSettings, CompatBadges, OwnResults, HostModTest, PatchSaveCheck, SaveLogsButton, SteamRunning]),
        new("v1.2.52", [BattleSize]),
        new("v1.2.51", [SteamCurrent, NoReport]),
        new("v1.2.50", [CheckServer]),
        new("v1.2.49", [InstallLoop]),
        new("v1.2.48", [RescueSteps]),
        new("v1.2.47", [EmptyList]),
        new("v1.2.46", [ImportList, ShareLinks]),
        new("v1.2.45", [MenuGuard, ShadersOnce]),
        new("v1.2.44", [LordReset, WhatsNewWindow]),
        new("v1.2.43", [Languages, LanguagesDetail]),
    ];

    public static readonly IReadOnlyList<WhatsNewEntry> AD1259 =
    [
        new("v1.1.17", [AD1259NoNightly, AD1259HostRules, ConsoleOutputStays, StopNoHang, SayLetters]),
        new("v1.1.16", [LicensePaidServers, ModuleFolderLicense, ReportModuleVersions]),
        new("v1.1.15", [MoreLanguages, NewerGameNote, TermsButtonTranslated, AD1259VoteTime, CopySetupTip]),
        new("v1.1.14", [AD1259Mcm5131, SaveVersionNamed, LogsPageTranslated, SaveRescueCaptiveHost]),
        new("v1.1.13", [TerrainShaderClear]),
        new("v1.1.12", [AD1259CompanionGuard]),
        new("v1.1.11", [OpenSourceOlderSet, ServerFatalLineAD1259, UseSteamServer, MapMissingLabel, JoinPillGameOpen, SendLogsClientPid]),
        new("v1.1.10", [Coop017OnWorkshop, StoryModeDllCopied]),
        new("v1.1.9", [CoodszNotice, AD1259NewSetupStarts, AD1259CompatNoWorkshop, DownloadCoopServer, CoopServerBehind]),
        new("v1.1.8", [RedesignLook, ModsLockedRunning, MadeForCards, ChipWordsShort, CrashBarWaitsRetry, AD1259Compat017, MyReportsClock]),
        new("v1.1.7", [ShortCopyAgain, MapMissingStops, FailedCopyOnce]),
        new("v1.1.6", [AD1259Coop017, ManualCoop, CoopWrongFolder, CoopUnblocked, ServerPackage017, ServerUnblocked, ActiveCoopSource, SteamStaysOn, PasswordAtStart, NewCampaignStaysNew, SmithingDefault017, JoinErrors017, MovementBoxesGone, AddOnNotAntivirus]),
        new("v1.1.5", [AD1259SetVersions, MapModRefused, MyReportsVisible, FasterReplies, TimeButtonsOn, ConsoleBuiltins, SetupProblems, BinLeftovers, BlockedWrite, SidebarWordsFit, PageListScrolls]),
        new("v1.1.4", [MyReportsBadge, MyReportsStatus, MyReportsAnswer]),
        new("v1.1.3", [PlayHoldHost, DonateButton, AntivirusMissing, FixAntivirus, SaferUpdate, CrashStackAdded, SaveBackupsFixed, SandboxNote], DonatePicture),
        new("v1.1.2", [FindBannerlordPick, RepliesInTool, MyReportsPage, AskOnDiscord, FailedLoadNoSaveAuto, AD1259CheatsLink]),
        new("v1.1.1", [AD1259NewerMod, PlayerChildBirth, CommandWaitsForAnswer]),
        new("v1.1.0", [AD1259NewName, OneZip, SettingsMove, DamagedSteamLibrary]),
        new("v1.0.34", [UpdateKeepsSiblings]),
        new("v1.0.33", [AnyLetterSaveName, AD1259ParkedMcm]),
        new("v1.0.32", [HostConsoleText, DumpSentenceOnlyWithDump]),
        new("v1.0.31", [AutoRestartWorks, RestartNoFreeze, McmFromNexus, CrashDumpSend, ServerModReports, BackupsWaitForSave, CrashLineThisRun, DeclinedMods]),
        new("v1.0.30", [AD1259SetBack]),
        new("v1.0.29", [ExtraSandBoxMap]),
        new("v1.0.28", [JoinCheck, SteamLobbyShared, GameStillOpen, WorkshopTwinsNote, ExitEvidence]),
        new("v1.0.27", [MaleBirth, OldMcmRefused, VersionsKept]),
        new("v1.0.26", [FullModListAD1259]),
        new("v1.0.25", [ReportDiscordName]),
        new("v1.0.24", [CrashTriage, SilentExitAD1259]),
        new("v1.0.23", [ReportNotes, ReportScreenshots, ReportOptional, BlankNumberSave, WindowFits, LogBatches, MoreCheatsGone, ReportsEasier]),
        new("v1.0.22", [GetAllMissing, SaveModsCard, ModderLordsVerdicts, CalmModsCard, WarNoLeader]),
        new("v1.0.21", [AutoRescueStart, AutoRescueRetry, AutoRescueStop, CustomToConsole, VassalTierCheck, CrashSend, CrashServer, CrashLogs, CrashPrivacy, ReportsHere, ReportCrash, ReportBar, ReportCounts, ReportPrivacy]),
        new("v1.0.20", [JoinBaseline, ConsoleNoStall]),
        new("v1.0.19", [RescueDeadLords]),
        new("v1.0.18", [LicenseOnce, CheatsHere, UiexMissing, SteamNotReady]),
        new("v1.0.17", [RbmClientText, RbmTextOnly, NewConsole, LobbyRetry, SharedSteamId, NonSteamFiles]),
        new("v1.0.16", [ChoiceLabels, OtherHints, Wording]),
        new("v1.0.15", [LogsPassword]),
        new("v1.0.14", [RbmPlayers]),
        new("v1.0.13", [HoverTips, AskRemoveCommand, RbmNewFile]),
        new("v1.0.12", [JoinAddresses, JoinRouter, JoinClient, SaveLogsButton, CrashRestart, Backups, SafeStop, ServerStatus, CpuRam, SafeBans, AllSettings, OpenAgain]),
        new("v1.0.11", [BattleSize]),
        new("v1.0.10", [NoReport]),
        new("v1.0.9", [CheckServer]),
        new("v1.0.8", [RescueSteps]),
        new("v1.0.7", [ShareLinks]),
        new("v1.0.6", [MenuGuard]),
        new("v1.0.5", [LordReset, WhatsNewWindow]),
        new("v1.0.4", [Languages, LanguagesDetail]),
    ];

    private const string TaomPublicArmory = "Both tools accept Armory 2.0.41 from the public TAOM 2.0.42 download.";
    private const string TaomBundledCompatVersions = "Both tools include TAOM.CoopCompat for TAOM 2.0.41 and 2.0.42 and install the version your game needs.";
    private const string TaomSavedDualWield = "The server loads Dual Wield when your save or mod setup needs it. When you join your own server at 127.0.0.1, the launcher follows the server's choice. Friends can import your shared mod list.";
    private const string TaomJoinAndArmory = "The TAOM launcher now matches the modules your host uses. Prepare server refreshes an outdated Armory copy.";
    private const string TaomSettingsSave = "On Bannerlord 1.5.7, the TAOM Server Tool now saves fast forward settings without the old TAOM settings file.";
    private const string TaomReleaseContents = "Both TAOM tools include their compatibility kits. The release no longer has separate CompatOnly downloads.";
    private const string TaomDependenciesVersion = "Fixed the TAOM.Dependencies version check in the Client Launcher.";
    private const string TaomBundledCompat157 = "Both tools now carry TAOM.CoopCompat for TAOM 2.0.41 on Bannerlord 1.5.7. Use Install or repair TAOM on the launcher and Prepare server on the host.";

    public static readonly IReadOnlyList<WhatsNewEntry> Taom =
    [
        new("v1.2.24", [TaomPublicArmory, TaomBundledCompatVersions, TaomSavedDualWield]),
        new("v1.2.23", [TaomJoinAndArmory]),
        new("v1.2.22", [TaomSettingsSave]),
        new("v1.2.21", [TaomReleaseContents]),
        new("v1.2.20", [TaomDependenciesVersion]),
        new("v1.2.19", [TaomBundledCompat157]),
        new("v1.2.18", [Taom157Connect, Taom157NeedsNewTaom, Taom157CompatOptional, Taom157Region, Taom157StagedLink, Taom157ClientGuide]),
        new("v1.2.17", [Taom157Host, Taom157StartServer, Taom157Card, Taom157Client, TaomAioPatch, TaomCredits, TaomJoinSameBuild, DumpCutShort]),
        new("v1.2.16", [TaomHostRules, TaomModSettings, TaomNewCompatAlone, TaomCompatPutBack, TaomConsoleQueue, TaomServerPageNote, BlockedDllSettings, LicenseRotTools]),
        new("v1.2.15", [TaomOneConnect, TaomSafeModeClicks, TaomCrashBlame]),
        new("v1.2.14", [LicensePaidServers, ModuleFolderLicense, ReportModuleVersions]),
        new("v1.2.13", [MoreLanguages, NewerGameNote, TermsButtonTranslated, CoopPutBack]),
        new("v1.2.12", [TaomCoop016GitHub, SaveRescueCaptiveHost]),
        new("v1.2.11", [TerrainShaderClear, DifficultyGreyedNote]),
        new("v1.2.10", [TaomChooseCoop016Server, TaomPlayCoopLine, JoinPillGameOpen, SendLogsClientPid]),
        new("v1.2.9", [TaomWaitsForCompat]),
        new("v1.2.8", [CoodszNotice, TaomZipSearchSkips]),
        new("v1.2.7", [RedesignLook, TaomSetAside, TaomCompatRepair, SaveSwitchAsk, ModsLockedRunning, PrepareWhileRunning, MadeForCards, ChipWordsTaom, CrashBarWaitsRetry, SettingsSyncEmpty, MyReportsClock]),
        new("v1.2.6", [TaomStays016, TaomNewerCoop, CoopRowSource, AddOnNotAntivirus]),
        new("v1.2.5", [MyReportsVisible, FasterReplies, SetupProblems, BinLeftovers, SidebarWordsFit, PageListScrolls]),
        new("v1.2.4", [MyReportsBadge, MyReportsStatus, MyReportsAnswer]),
        new("v1.2.3", [PlayHoldTaom, DonateButton, AntivirusMissing, FixAntivirus, SaferUpdate, CrashStackAdded, SaveBackupsFixed], DonatePicture),
        new("v1.2.2", [FindBannerlordPick, RepliesInTool, MyReportsPage, AskOnDiscord, FailedLoadNoSave, SafeLinks]),
        new("v1.2.1", [TaomIgPartySize, TaomCompatBuiltIn, PlayerChildBirth, TaomWaitForStart, CommandWaitsForAnswer]),
        new("v1.2.0", [TaomNewName, OneZip, SettingsMove, TaomNoDotNet, TaomModuleDllsBack, TaomPlainRefusals, TaomTroopWeight, DamagedSteamLibrary]),
        new("v1.1.55", [UpdateKeepsSiblings]),
        new("v1.1.54", [TaomEngineDllsBack]),
        new("v1.1.53", [TaomServerFilesBack, TaomCommandText, DumpSentenceOnlyWithDump]),
        new("v1.1.52", [CrashDumpSend, ServerModReports, TaomCoreReports, BackupsWaitForSave, TaomConsoleText, TaomCrashLine]),
        new("v1.1.51", [JoinCheck, SteamLobbyShared, GameStillOpen, WorkshopTwinsNote]),
        new("v1.1.50", [MaleBirth, VersionsKept]),
        new("v1.1.49", [TaomRosterGuard]),
        new("v1.1.48", [FullModListTaom]),
        new("v1.1.47", [ReportDiscordName]),
        new("v1.1.46", [CrashTriage, GraphicsCrash, TaomStartupStop, LogPackModPatch, BigBattlesWarning, TaomAutosaveBlank, PopupsFit]),
        new("v1.1.45", [TaomWotrSync, TaomReadyJoin, ReportNotes, ReportScreenshots, ReportOptional, BlankNumberSave, WindowFits, MoreCheatsGone, ReportsEasier]),
        new("v1.1.44", [TaomEnlist, SyncCheckLess]),
        new("v1.1.43", [ServerMods, SaveModsCard, ModderLordsVerdicts, CalmModsCard, WarNoLeader]),
        new("v1.1.42", [AutoRescueStart, AutoRescueRetry, AutoRescueStop, CustomToConsole, VassalTierNote, CrashSend, CrashServer, CrashLogs, CrashPrivacy, ReportsHere, ReportCrash, ReportBar, ReportCounts, ReportPrivacy]),
        new("v1.1.41", [WorkshopCoopCore, JoinBaseline, ConsoleNoStall, TaomReadyNote, BattleSizeNote]),
        new("v1.1.40", [RescueDeadLords]),
        new("v1.1.39", [LicenseOnce, CheatsHere, ConsoleHere, UiexMissing]),
        new("v1.1.38", [TaomIgClient, TaomCrashZip, LobbyRetry, SharedSteamId, NonSteamFiles]),
        new("v1.1.37", [TaomIgServer]),
        new("v1.1.36", [AddModsBox, Wording]),
        new("v1.1.35", [TaomLogsServer, TaomLowMemory]),
        new("v1.1.34", [HoverTips, AskRemoveCommand]),
        new("v1.1.33", [TaomSetups, TaomModList, TaomHostEditing, TaomDependencies, TaomLoadOrder, TaomCrashTools, TaomButtons, TaomServerMods, TaomModTest, TaomStop, TaomStatus, TaomOldSave, TaomAddMods, GameSpeedCard, TaomBackups, OwnResults, TaomCommunityMods, JoinAddresses, JoinRouter, JoinClient, CpuRam, SteamRunning, OpenAgain, TaomGuides, NewBadgeLine]),
        new("v1.1.32", [NoReport]),
        new("v1.1.31", [CheckServer]),
        new("v1.1.30", [TaomSteamGet]),
        new("v1.1.29", [RescueSteps]),
        new("v1.1.28", [TaomRescue]),
        new("v1.1.27", [MenuGuard]),
        new("v1.1.26", [WhatsNewWindow]),
        new("v1.1.25", ["Campaign difficulty is on the server tool's Settings page. Pick Very easy, Easy or Realistic for each of the 9 options, then save and restart the server."]),
        new("v1.1.24", [Languages, LanguagesDetail]),
    ];

    /// <summary>Both Burning Empires tools (the server tool and the client launcher share one list, like TAOM).</summary>
    public static readonly IReadOnlyList<WhatsNewEntry> Be =
    [
        new("v1.0.16", [BeConsoleReadable, BeWarSails, BeServerPageNote, BlockedDllSettings, BeConnectPasswordHidden, BeSharedListFresh, BeModTwice, BeKeepsRunning, BePlayerListKept, LicenseRotTools]),
        new("v1.0.15", [LicensePaidServers, ModuleFolderLicense, ReportModuleVersions]),
        new("v1.0.14", [MoreLanguages, NewerGameNote, TermsButtonTranslated, BeCoopPutBack, BeTimeButtons]),
        new("v1.0.13", [SaveRescueCaptiveHost]),
        new("v1.0.12", [TerrainShaderClear, DifficultyGreyedNote]),
        new("v1.0.11", [MapCheckGuardJoin, ServerSubModuleDedupe]),
        new("v1.0.10", [JoinPillGameOpen, SendLogsClientPid]),
        new("v1.0.9", [BeCoop017OnWorkshop]),
        new("v1.0.8", [CoodszNotice, BeDownloadCoopServer]),
        new("v1.0.7", [RedesignLook, SaveSwitchAsk, PrepareWhileRunning, ModsLockedRunning, BeMadeForCards, CrashBarWaitsRetry, ChipWordsShort, BeCompat017, MyReportsClock]),
        new("v1.0.6", [BeChooseCoopServer, BeNoOlderCoop, ManualCoop, CoopWrongFolder, CoopUnblocked, JoinErrors017, AddOnNotAntivirus]),
        new("v1.0.5", [MyReportsVisible, FasterReplies, SetupProblems, BinLeftovers, SidebarWordsFit]),
        new("v1.0.4", [MyReportsBadge, MyReportsStatus, MyReportsAnswer]),
        new("v1.0.3", [PlayHoldTaom, DonateButton, BeStartPrepares, BePrepareChecks, BeNoCampaignYet, AntivirusMissing, FixAntivirus, SaferUpdate, CrashStackAdded, SaveBackupsFixed, SandboxNote], DonatePicture),
        new("v1.0.2", [BeThinWorkshop]),
        new("v1.0.1",
        [
            FindBannerlordPick,
            RepliesInTool,
            MyReportsPage,
            AskOnDiscord,
            FailedLoadNoSave,
            // Burning Empires only lines go below this one.
            BeNexusAsks,
            BeHotfixOnly,
            BeCopyWait,
            BeSteamClosed,
        ]),
        new("v1.0.0", [BeFirst, BeSetups, BeInstall, BeCompat, OneZip, SettingsMove]),
    ];

    /// <summary>Both Realm of Thrones tools (one list, like Burning Empires).</summary>
    public static readonly IReadOnlyList<WhatsNewEntry> Rot =
    [
        new("v0.1.4", [RotHousehold, RotHostLogs, RotMapCheck]),
        new("v0.1.3", [RotGroupMoves, RotCoreArrows, RotMoveRefused, RotSortCycle, RotLaunchCheck]),
        new("v0.1.2", [RotConsoleBox, RotQueueRestart, RotSyncJoin, RotPlayersKept]),
        new("v0.1.1", [RotServerPageNote]),
        new("v0.1.0", [RotFirst, RotCoopBuild, RotPrepare, RotStory, RotEncyclopedia, RotSetups]),
    ];

    public const string Title = "What's new";
    public const string Heading = "What's new in {0}";
    public const string Close = "Got it";
    public const string CloseTip = "Closes the What's new window.";
    public const string ButtonTip = "Shows what changed in this version and the ones before it.";
    public const string OpenAgainHint = "You can open this again with the What's new button near the language button.";

    // Pictures for an entry (Updates\WhatsNewPictures, embedded in Core). The client launchers'
    // top bar and the server tools' side menu, with the Donate button outlined in gold.
    public const string DonatePictureClient = "Coodsz.WhatsNew.whatsnew-donate-client.png";
    public const string DonatePictureServer = "Coodsz.WhatsNew.whatsnew-donate-server.png";

    // What an entry names for the Donate picture. A client launcher and its server tool share one list, so
    // each tool shows its own copy (PictureFor).
    public const string DonatePicture = "donate";

    /// <summary>True for a server tool's id (WhatsNewOnce): Coodsz Host and the ...-server tools.</summary>
    public static bool IsServerTool(string? tool) =>
        tool is not null && (tool.Equals("coodsz-host", StringComparison.OrdinalIgnoreCase) || tool.EndsWith("-server", StringComparison.OrdinalIgnoreCase));

    /// <summary>The picture resource an entry shows in this tool: the Donate picture as the tool's own copy, any other name as it is.</summary>
    public static string? PictureFor(string? picture, string? tool) =>
        picture == DonatePicture ? null : picture;
    public const string DonatePictureLabel = "Where the Donate button is";
    public const string PictureLabelOther = "A picture of what changed";

    /// <summary>What a screen reader says for a picture.</summary>
    public static string PictureLabel(string picture) =>
        picture is DonatePictureClient or DonatePictureServer ? DonatePictureLabel : PictureLabelOther;

    /// <summary>Opens an entry's picture, or null when there is no picture by that name.</summary>
    public static Stream? OpenPicture(string? picture) =>
        string.IsNullOrEmpty(picture) ? null : typeof(WhatsNew).Assembly.GetManifestResourceStream(picture);

    /// <summary>Folder for the "last seen" files; one per tool, next to the language pick.</summary>
    public static string SettingsFolder { get; set; } =
        ToolData.RoamingRoot;

    /// <summary>
    /// The entries to show: every one newer than the last version this tool showed (at most
    /// three), or only the newest when this tool never showed any.
    /// </summary>
    public static IReadOnlyList<WhatsNewEntry> Since(IReadOnlyList<WhatsNewEntry> entries, string? lastSeen, int max = 3)
    {
        if (entries.Count == 0)
            return [];
        if (string.IsNullOrWhiteSpace(lastSeen))
            return [entries[0]];
        return entries.Where(entry => ReleaseNotice.IsNewer(entry.Version, lastSeen)).Take(max).ToList();
    }

    /// <summary>
    /// The entries for the What's new button: this version first, then the ones before it,
    /// newest first (at most <paramref name="max"/>). Notes for a version newer than this
    /// copy are left out. Nothing is marked seen, so the popup after an update still comes.
    /// </summary>
    public static IReadOnlyList<WhatsNewEntry> Recent(IReadOnlyList<WhatsNewEntry> entries, string version, int max = 5)
    {
        var shown = entries.Where(entry => !ReleaseNotice.IsNewer(entry.Version, version)).ToList();
        shown.Sort((left, right) => ReleaseNotice.IsNewer(left.Version, right.Version) ? -1 : ReleaseNotice.IsNewer(right.Version, left.Version) ? 1 : 0);
        return shown.Take(max).ToList();
    }

    public static string? LastSeen(string tool)
    {
        try
        {
            var path = SeenPath(tool);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void MarkSeen(string tool, string version)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SeenPath(tool), version);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string SeenPath(string tool) => Path.Combine(SettingsFolder, "whats-new-" + tool + ".txt");
}
