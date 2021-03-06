using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using InfinityScript;
using System.IO;

namespace LambAdmin
{
    public partial class DGAdmin
    {
        volatile string MapRotation = "";
        public static partial class ConfigValues
        {
            private static string DayTime = "day";
            public static bool HellMode = false;

            public static int settings_warn_maxwarns
            {
                get
                {
                    return int.Parse(Sett_GetString("settings_maxwarns"));
                }
            }
            public static bool settings_groups_autosave
            {
                get
                {
                    return bool.Parse(Sett_GetString("settings_groups_autosave"));
                }
            }
            public static bool settings_enable_misccommands
            {
                get
                {
                    return bool.Parse(Sett_GetString("settings_enable_misccommands"));
                }
            }
            public static bool settings_enable_chat_alias
            {
                get
                {
                    return bool.Parse(Sett_GetString("settings_enable_chat_alias"));
                }
            }
            public static bool settings_enable_spree_messages
            {
                get
                {
                    return bool.Parse(Sett_GetString("settings_enable_spree_messages"));
                }
            }
            public static bool settings_enable_xlrstats
            {
                get
                {
                    return bool.Parse(Sett_GetString("settings_enable_xlrstats"));
                }
            }
            public static string settings_daytime
            {
                get
                {
                    return DayTime;
                }
                set
                {
                    switch (value)
                    {
                        case "night":
                        case "day":
                        case "morning":
                        case "cloudy":
                            DayTime = value;
                            System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\daytime.txt", new string[] { value });
                            break;
                    }
                }
            }
        }

        public volatile List<Command> CommandList = new List<Command>();

        public volatile List<string> BanList = new List<string>();

        public volatile List<string> XBanList = new List<string>();

        public volatile Dictionary<string, string> CommandAliases = new Dictionary<string, string>();

        public volatile SerializableDictionary<long, List<Dvar>> PersonalPlayerDvars = new SerializableDictionary<long, List<Dvar>>();

        public class Command
        {
            [Flags]
            public enum Behaviour
            {
                Normal = 1,
                HasOptionalArguments = 2,
                OptionalIsRequired = 4,
                MustBeConfirmed = 8
            };

            private Action<Entity, string[], string> action;
            private int parametercount;
            public string name;
            private Behaviour behaviour;

            public Command(string commandname, int paramcount, Behaviour commandbehaviour, Action<Entity, string[], string> actiontobedone)
            {
                action = actiontobedone;
                parametercount = paramcount;
                name = commandname;
                behaviour = commandbehaviour;
            }

            public void Run(Entity sender, string message, DGAdmin script)
            {
                string[] args;
                string optionalargument;
                if (!ParseCommand(message, parametercount, out args, out optionalargument))
                {
                    script.WriteChatToPlayer(sender, GetString(name, "usage"));
                    return;
                }
                if (behaviour.HasFlag(Behaviour.HasOptionalArguments))
                {
                    if (behaviour.HasFlag(Behaviour.OptionalIsRequired) && string.IsNullOrWhiteSpace(optionalargument))
                    {
                        script.WriteChatToPlayer(sender, "2" + GetString(name, "usage"));
                        return;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(optionalargument))
                {
                    script.WriteChatToPlayer(sender, GetString(name, "usage"));
                    return;
                }
                if (behaviour.HasFlag(Behaviour.MustBeConfirmed))
                {
                    if (sender.GetField<string>("CurrentCommand") != message)
                    {
                        script.WriteChatToPlayerMultiline(sender, new string[] {
                            "^5-->You trying ^1UNSAFE ^5command",
                            "^5-->^3" + message,
                            "^5-->Confirm (^1!yes ^5/ ^2!no^5)"
                        }, 50);
                        sender.SetField("CurrentCommand", message);
                        return;
                    }
                    else
                        sender.SetField("CurrentCommand", "");
                }
                try
                {
                    action(sender, args, optionalargument);
                }
                catch (Exception ex)
                {
                    script.WriteChatToPlayer(sender, GetMessage("DefaultError"));
                    script.MainLog.WriteError(ex.Message);
                    script.MainLog.WriteError(ex.StackTrace);
                    WriteLog.Error(ex.Message);
                    WriteLog.Error(ex.StackTrace);
                }
            }

            public static string GetString(string name, string key)
            {
                return CmdLang_GetString(string.Join("_", "command", name, key));
            }

            public static string GetMessage(string key)
            {
                return CmdLang_GetString(string.Join("_", "Message", key));
            }

            public static bool HasString(string name, string key)
            {
                return CmdLang_HasString(string.Join("_", "command", name, key));
            }
        }

        public class BanEntry
        {
            public int banid
            {
                get; private set;
            }
            public PlayerInfo playerinfo
            {
                get; private set;
            }
            public string playername
            {
                get; private set;
            }
            public DateTime until
            {
                get; private set;
            }

            public BanEntry(int pbanid, PlayerInfo pplayerinfo, string pplayername, DateTime puntil)
            {
                banid = pbanid;
                playerinfo = pplayerinfo;
                playername = pplayername;
                until = puntil;
            }
        }

        public void ProcessCommand(Entity sender, string name, string message)
        {
            string commandname = message.Substring(1).Split(' ')[0].ToLowerInvariant();

            WriteLog.Debug(sender.Name + " attempted " + commandname);

            //spy command
            foreach (Entity player in Players)
            {
                try
                {
                    if (player.isSpying())
                    {
                        if (commandname == "login")
                            WriteChatSpyToPlayer(player, sender.Name + ": ^6" + "!login ****");
                        else
                            WriteChatSpyToPlayer(player, sender.Name + ": ^6" + message);
                    }
                }
                catch (Exception ex)
                {
                    HaxLog.WriteInfo(string.Join(" ", "----STARTREPORT", ex.Message));
                    try
                    {
                        HaxLog.WriteInfo("BAD PLAYER:");
                        HaxLog.WriteInfo(ex.StackTrace);
                        HaxLog.WriteInfo(player.Name);
                        HaxLog.WriteInfo(player.GUID.ToString());
                        HaxLog.WriteInfo(player.IP.Address.ToString());
                        HaxLog.WriteInfo(player.GetEntityNumber().ToString());
                    }
                    finally
                    {
                        HaxLog.WriteInfo("----ENDREPORT");
                    }
                }
            }

            Command CommandToBeRun;
            string newcommandname;
            if (CommandAliases.TryGetValue(commandname, out newcommandname))
            {
                commandname = newcommandname;
            }
            CommandToBeRun = FindCommand(commandname);
            if (CommandToBeRun != null)
            {
                WriteLog.Debug("Command found");
                GroupsDatabase.Group playergroup = sender.GetGroup(database);
                if (commandname == "login" && (playergroup.CanDo("login")))
                {
                    WriteLog.Debug("Running command.");
                    CommandToBeRun.Run(sender, message, this);
                    return;
                }

                WriteLog.Debug("Checking permission");
                if (!sender.HasPermission(commandname, database))
                {
                    if (playergroup.CanDo(commandname))
                    {
                        WriteLog.Debug("Not logged in");
                        WriteChatToPlayer(sender, Command.GetMessage("NotLoggedIn"));
                    }
                    else
                    {
                        WriteLog.Debug("No permission");
                        WriteChatToPlayer(sender, Command.GetMessage("NoPermission"));
                    }
                    return;
                }

                WriteLog.Debug("Running command.");
                CommandToBeRun.Run(sender, message, this);
            }
            else
            {
                WriteChatToPlayer(sender, Command.GetMessage("CommandNotFound"));
                return;
            }
        }

        public void ProcessForceCommand(Entity sender, Entity target, string name, string message)
        {
            string commandname = message.Substring(1).Split(' ')[0].ToLowerInvariant();

            WriteLog.Debug(sender.Name + " attempted " + commandname);

            Command CommandToBeRun;
            string newcommandname;
            if (CommandAliases.TryGetValue(commandname, out newcommandname))
            {
                commandname = newcommandname;
            }
            CommandToBeRun = FindCommand(commandname);
            if (CommandToBeRun != null)
            {
                WriteLog.Debug("Command found");
                GroupsDatabase.Group playergroup = sender.GetGroup(database);
                GroupsDatabase.Group targetgroup = target.GetGroup(database);
                if (commandname == "login")
                {
                    if (!string.IsNullOrWhiteSpace(targetgroup.login_password) && (targetgroup.CanDo("login")))
                    {
                        WriteLog.Debug("Running command.");
                        CommandToBeRun.Run(target, message, this);
                        return;
                    }
                    else
                    {
                        WriteChatToPlayer(sender, "Target cannot log in!");
                        return;
                    }
                }

                WriteLog.Debug("Checking permission");
                if (!sender.HasPermission(commandname, database))
                {
                    if (playergroup.CanDo(commandname))
                    {
                        WriteLog.Debug("Not logged in");
                        WriteChatToPlayer(sender, Command.GetMessage("NotLoggedIn"));
                    }
                    else
                    {
                        WriteLog.Debug("No permission");
                        WriteChatToPlayer(sender, Command.GetMessage("NoPermission"));
                    }
                    return;
                }

                WriteLog.Debug("Running command.");
                CommandToBeRun.Run(target, message, this);
            }
            else
            {
                WriteChatToPlayer(sender, Command.GetMessage("CommandNotFound"));
                return;
            }
        }

        public void CMDS_OnServerStart()
        {
            if (!System.IO.Directory.Exists(ConfigValues.ConfigPath + @"Commands"))
                System.IO.Directory.CreateDirectory(ConfigValues.ConfigPath + @"Commands");

            PlayerConnected += CMDS_OnConnect;
            PlayerConnecting += CMDS_OnConnecting;
            PlayerDisconnected += CMDS_OnDisconnect;
            PlayerActuallySpawned += CMDS_OnPlayerSpawned;
            OnPlayerKilledEvent += CMDS_OnPlayerKilled;

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\bannedplayers.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\bannedplayers.txt", new string[0]);

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\xbans.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\xbans.txt", new string[0]);

            if (!System.IO.Directory.Exists(ConfigValues.ConfigPath + @"Commands\internal"))
                System.IO.Directory.CreateDirectory(ConfigValues.ConfigPath + @"Commands\internal");

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\spyingplayers.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\spyingplayers.txt", new string[0]);

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\warns.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt", new string[0]);

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\mutedplayers.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\mutedplayers.txt", new string[0]);

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\warns.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt", new string[0]);

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\apply.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\apply.txt", new string[1] { "Wanna join ^1DG^7? Apply at ^2dgnetworks.enjin.com" });

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\rules.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\rules.txt", new string[1] { "Rule one: ^1No Rules!" });

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Utils\cdvars.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Utils\cdvars.txt", new string[] {
                    "cg_chatTime=30000",
                    "cg_chatHeight=8",
                    "cg_hudChatIntermissionPosition=5 240",
                    "cg_hudChatPosition=5 240",
                    "cg_hudSayPosition=5 240",
                    "r_filmUseTweaks=0",
                    "r_filmTweakEnable=0",
                    "r_filmTweakDesaturation=0.2",
                    "r_filmTweakDesaturationDark=0.2",
                    "r_filmTweakInvert=0",
                    "r_glowTweakEnable=0",
                    "r_glowUseTweaks=0",
                    "r_glowTweakRadius0=5",
                    "r_filmTweakContrast=1.4",
                    "r_filmTweakBrightness=0",
                    "r_filmTweakLightTint=1.1 1.05 0.85",
                    "r_filmTweakDarkTint=0.7 0.85 1",
                });

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Utils\internal\PersonalPlayerDvars.xml"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Utils\internal\PersonalPlayerDvars.xml", new string[] {
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                    "<dictionary />",
                });

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Utils\chatalias.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Utils\chatalias.txt", new string[] { });

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\daytime.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\daytime.txt", new string[] { "day" });

            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\ChatReports.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\ChatReports.txt", new string[] { });

            InitCommands();
            InitCommandAliases();
            InitCDVars();

            BanList = System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\bannedplayers.txt").ToList();
            XBanList = System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\xbans.txt").ToList();
        }

        public void InitCommands()
        {
            WriteLog.Info("Initializing commands...");

            #region COMMANDS

            // VERSION
            CommandList.Add(new Command("version", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayer(sender, "^3DG Admin ^1" + ConfigValues.Version + "^1. ^3Do !credits for detailed info.");
                }));

            // CREDITS
            CommandList.Add(new Command("credits", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayerMultiline(sender, new string[]
                    {
                        string.Format("^1DG Admin ^3{0}", ConfigValues.Version),
                        "^1Credits:",
                        "^3Based on RG Admin v1.05",
                        "^3Modified by ^2F. Bernkastel",
                        "^3HKClan for trying to give me their help over on their forum",
                        "^3Creators of SAT for AntiKnife copypasta and HWID offsets",
                        "^5x86jmpstreet, ^3they know themselves. // L33T",
                        "^3All ^1RG ^3members for supporting me on this project",
                        "^3Special ^1RG ^3members: Moustache, Pepper",
                        "^3And lastly, ^1Lambder ^3for putting all this together",
                        "^1Guide creators:",
                        "^3Lambder, Arnie and ^2F. Bernkastel",
                    }, 1500);
                }));

            // PRIVATE MESSAGE // PM
            CommandList.Add(new Command("pm", 1, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    CMD_sendprivatemessage(target, sender.Name, optarg);
                    WriteChatToPlayer(sender, Command.GetString("pm", "confirmation").Format(new Dictionary<string, string>()
                    {
                        { "<receiver>", target.Name }
                    }));
                }));

            // ADMINS
            CommandList.Add(new Command("admins", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayer(sender, Command.GetString("admins", "firstline"));
                    //going to clean out certain group names

                    string[] cookies = database.GetAdminsString(Players);
                    WriteChatToPlayerCondensed(sender, cookies, 1000, 40, Command.GetString("admins", "separator"));
                }));

            // STATUS
            CommandList.Add(new Command("status", 0, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    string[] statusstrings;
                    if (string.IsNullOrEmpty(optarg))
                    {
                        statusstrings = (from player in Players
                                         select Command.GetString("status", "formatting").Format(new Dictionary<string, string>()
                                        {
                                            { "<namef>", player.GetFormattedName(database) },
                                            { "<name>", player.Name },
                                            { "<rankname>", player.GetGroup(database).group_name },
                                            { "<shortrank>", player.GetGroup(database).short_name },
                                            { "<id>", player.GetEntityNumber().ToString() },
                                        })).ToArray();
                    }
                    else
                    {
                        statusstrings = (from player in FindPlayers(optarg)
                                         select Command.GetString("status", "formatting").Format(new Dictionary<string, string>()
                                        {
                                            { "<namef>", player.GetFormattedName(database) },
                                            { "<name>", player.Name },
                                            { "<rankname>", player.GetGroup(database).group_name },
                                            { "<shortrank>", player.GetGroup(database).short_name },
                                            { "<id>", player.GetEntityNumber().ToString() },
                                        })).ToArray();
                    }
                    WriteChatToPlayer(sender, Command.GetString("status", "firstline"));
                    WriteChatToPlayerMultiline(sender, statusstrings);
                }));

            // LOGIN
            CommandList.Add(new Command("login", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (sender.isLogged())
                    {
                        WriteChatToPlayer(sender, Command.GetString("login", "alreadylogged"));
                        return;
                    }
                    GroupsDatabase.Group grp = sender.GetGroup(database);
                    if (string.IsNullOrWhiteSpace(grp.login_password))
                    {
                        WriteChatToPlayer(sender, Command.GetString("login", "notrequired"));
                        return;
                    }
                    if (arguments[0] != grp.login_password)
                    {
                        WriteChatToPlayer(sender, Command.GetString("login", "wrongpassword"));
                        return;
                    }
                    sender.setLogged(true);
                    WriteChatToPlayer(sender, Command.GetString("login", "successful"));

                    if (bool.Parse(Sett_GetString("settings_enable_spy_onlogin")) && grp.CanDo("spy"))
                    {
                        sender.setSpying(true);
                        WriteChatToPlayer(sender, Command.GetString("spy", "message_on"));
                    }
                }));

            // KICK
            CommandList.Add(new Command("kick", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }

                    WriteChatToAll(Command.GetString("kick", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                    }));

                    if (string.IsNullOrEmpty(optarg))
                        CMD_kick(target);
                    else
                        CMD_kick(target, optarg);
                }));

            // TMPBAN
            CommandList.Add(new Command("tmpban", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }

                    WriteChatToAll(Command.GetString("tmpban", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                    }));

                    if (string.IsNullOrEmpty(optarg))
                        CMD_tmpban(target);
                    else
                        CMD_tmpban(target, optarg);
                }));

            // BAN
            CommandList.Add(new Command("ban", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }

                    WriteChatToAll(Command.GetString("ban", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                    }));

                    if (string.IsNullOrEmpty(optarg))
                        CMD_ban(target);
                    else
                        CMD_ban(target, optarg);
                }));

            // SAY
            CommandList.Add(new Command("say", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    WriteChatToAll(optarg);
                }));

            // SAYTO
            CommandList.Add(new Command("sayto", 1, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    WriteChatToPlayer(target, optarg);
                }));

            // MAP
            CommandList.Add(new Command("map", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    string newmap = FindSingleMap(arguments[0]);
                    if (string.IsNullOrEmpty(newmap))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOneMapFound"));
                        return;
                    }

                    CMD_changemap(newmap);
                    WriteChatToAll(Command.GetString("map", "message").Format(new Dictionary<string, string>()
                    {
                        {"<player>", sender.Name },
                        {"<playerf>", sender.GetFormattedName(database) },
                        {"<mapname>", newmap },
                    }));
                }));

            // GUID
            CommandList.Add(new Command("guid", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayer(sender, Command.GetString("guid", "message").Format(new Dictionary<string, string>()
                    {
                        {"<guid>", sender.GUID.ToString() },
                    }));
                }));

            // HWID
            CommandList.Add(new Command("hwid", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    HWID playerhwid = sender.GetHWID();
                    WriteChatToPlayer(sender, Command.GetString("hwid", "message").Format(new Dictionary<string, string>()
                    {
                        {"<hwid>", playerhwid.ToString() },
                    }));
                }));

            if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\rules.txt"))
            {
                // RULES
                CommandList.Add(new Command("rules", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayerMultiline(sender, System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\rules.txt"));
                }));
            }

            if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\apply.txt"))
            {
                // RULES
                CommandList.Add(new Command("apply", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayerMultiline(sender, System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\apply.txt"));
                }));
            }
            CommandList.Add(new Command("gl", 1, Command.Behaviour.Normal, (sender, arguments, optarg) =>
            {
                int delay = 750;
                if (arguments[0] == "rules")
                {
                    if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\rules.txt"))
                    {
                        string[] lines = File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\rules.txt");
                        WriteChatToAllMultiline(lines, delay);
                    }
                    else
                    {
                        WriteChatToPlayer(sender, "^1Error: Rules file not found");
                        return;
                    }
                }
                if (arguments[0] == "apply")
                {
                    if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\apply.txt"))
                    {
                        string[] lines = File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\apply.txt");
                        WriteChatToAllMultiline(lines, delay);
                    }
                    else
                    {
                        WriteChatToPlayer(sender, "^1Error: apply file not found");
                        return;
                    }
                }
            }));
            // WARN
            CommandList.Add(new Command("warn", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    int warns = CMD_addwarn(target);

                    target.IPrintLnBold(Command.GetMessage("YouHaveBeenWarned"));

                    if (warns >= ConfigValues.settings_warn_maxwarns)
                    {
                        CMD_resetwarns(target);
                        if (string.IsNullOrEmpty(optarg))
                            CMD_tmpban(target);
                        else
                            CMD_tmpban(target, optarg);
                    }
                    WriteChatToAll(Command.GetString("warn", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                        {"<warncount>", warns.ToString() },
                        {"<maxwarns>", ConfigValues.settings_warn_maxwarns.ToString() },
                    }));
                }));

            // UNWARN
            CommandList.Add(new Command("unwarn", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    int warns = CMD_unwarn(target);
                    WriteChatToAll(Command.GetString("unwarn", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                        {"<warncount>", warns.ToString() },
                        {"<maxwarns>", ConfigValues.settings_warn_maxwarns.ToString() },
                    }));
                }));

            // RESETWARNS
            CommandList.Add(new Command("resetwarns", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    CMD_resetwarns(target);
                    WriteChatToAll(Command.GetString("resetwarns", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                    }));
                }));


            // GETWARNS
            CommandList.Add(new Command("getwarns", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    int warns = CMD_getwarns(target);
                    WriteChatToAll(Command.GetString("getwarns", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<warncount>", warns.ToString() },
                        {"<maxwarns>", ConfigValues.settings_warn_maxwarns.ToString() },
                    }));
                }));

            // ADDIMMUNE
            CommandList.Add(new Command("addimmune", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    target.setImmune(true, database);
                    if (ConfigValues.settings_groups_autosave)
                        database.SaveGroups();
                    WriteChatToAll(Command.GetString("addimmune", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                    }));
                }));

            // UNIMMUNE
            CommandList.Add(new Command("unimmune", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    target.setImmune(false, database);
                    if (ConfigValues.settings_groups_autosave)
                        database.SaveGroups();
                    WriteChatToAll(Command.GetString("unimmune", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                    }));
                }));

            // SETGROUP
            CommandList.Add(new Command("setgroup", 2, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (!target.SetGroup(arguments[1], database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("GroupNotFound"));
                        return;
                    }
                    if (ConfigValues.settings_groups_autosave)
                        database.SaveGroups();
                    WriteChatToAll(Command.GetString("setgroup", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<rankname>", arguments[1].ToLowerInvariant() },
                    }));
                }));

            //CHANGE
            // FIXPLAYERGROUP
            CommandList.Add(new Command("fixplayergroup", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.FixPlayerIdentifiers(database))
                        WriteChatToPlayer(sender, Command.GetString("fixplayergroup", "message"));
                    else
                        WriteChatToPlayer(sender, Command.GetString("fixplayergroup", "notfound"));
                    database.SaveGroups();
                }));

            // SAVEGROUPS
            CommandList.Add(new Command("savegroups", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    database.SaveGroups();
                    WriteChatToAll(Command.GetString("savegroups", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                    }));
                    if (ConfigValues.settings_enable_xlrstats)
                    {
                        xlr_database.Save();
                        WriteChatToAll(Command.GetString("savegroups", "message_xlr").Format(new Dictionary<string, string>()
                        {
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) },
                        }));
                    }
                }));

            // FAST RESTART // RES
            CommandList.Add(new Command("res", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    OnExitLevel();
                    ExecuteCommand("fast_restart");
                }));

            // GETPLAYERINFO
            CommandList.Add(new Command("getplayerinfo", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    WriteChatToPlayer(sender, Command.GetString("getplayerinfo", "message").Format(new Dictionary<string, string>()
                        {
                            {"<target>", target.Name },
                            {"<targetf>", target.GetFormattedName(database) },
                            {"<id>", target.GetEntityNumber().ToString() },
                            {"<guid>", target.GUID.ToString() },
                            {"<ip>", target.IP.Address.ToString() },
                            {"<hwid>", target.GetHWID().ToString() },
                        }));
                }));

            // BALANCE
            CommandList.Add(new Command("balance", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    List<Entity> axis = new List<Entity>();
                    List<Entity> allies = new List<Entity>();
                    foreach (Entity player in Players)
                    {
                        if (player.GetTeam() == "axis")
                            axis.Add(player);
                        else if (player.GetTeam() == "allies")
                            allies.Add(player);
                    }
                    if (Math.Abs(axis.Count - allies.Count) < 2)
                    {
                        WriteChatToPlayer(sender, Command.GetString("balance", "teamsalreadybalanced"));
                        return;
                    }

                    axis = axis.OrderBy(player => player.IsAlive).ToList();
                    allies = allies.OrderBy(player => player.IsAlive).ToList();

                    while (axis.Count > allies.Count && Math.Abs(axis.Count - allies.Count) > 1)
                    {
                        Entity chosenplayer = axis[axis.Count - 1];
                        CMD_changeteam(chosenplayer, "allies");
                        axis.Remove(chosenplayer);
                        allies.Add(chosenplayer);
                    }

                    while (allies.Count > axis.Count && Math.Abs(axis.Count - allies.Count) > 1)
                    {
                        Entity chosenplayer = allies[allies.Count - 1];
                        CMD_changeteam(chosenplayer, "axis");
                        allies.Remove(chosenplayer);
                        axis.Add(chosenplayer);
                    }

                    WriteChatToAll(Command.GetString("balance", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                    }));
                }));

            // AFK
            CommandList.Add(new Command("afk", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    CMD_changeteam(sender, "spectator");
                }));

            // SETAFK
            CommandList.Add(new Command("setafk", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }

                    if (target.IsSpectating())
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("PlayerIsSpectating"));
                        return;
                    }

                    CMD_changeteam(target, "spectator");
                }));

            // SETTEAM
            CommandList.Add(new Command("setteam", 2, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (!Data.TeamNames.Contains(arguments[1]))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("InvalidTeamName"));
                        return;
                    }

                    CMD_changeteam(target, arguments[1]);
                    WriteChatToAll(Command.GetString("setteam", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // CLANVSALL
            CommandList.Add(new Command("clanvsall", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    List<string> identifiers = optarg.Split(' ').ToList();
                    CMD_clanvsall(identifiers);

                    WriteChatToAll(Command.GetString("clanvsall", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<identifiers>", optarg },
                    }));
                }));

            // CLANVSALLSPECTATE
            CommandList.Add(new Command("clanvsallspectate", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    List<string> identifiers = optarg.Split(' ').ToList();
                    CMD_clanvsall(identifiers, true);

                    WriteChatToAll(Command.GetString("clanvsall", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<identifiers>", optarg },
                    }));
                }));

            // CDVAR
            CommandList.Add(new Command("cdvar", 2, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    if (!String.IsNullOrEmpty(optarg))
                    {
                        switch (arguments[0].ToLowerInvariant())
                        {
                            case "int":
                                sender.Call("setclientdvar", arguments[1], int.Parse(optarg));
                                break;
                            case "float":
                                sender.Call("setclientdvar", arguments[1], float.Parse(optarg));
                                break;
                            case "string":
                                sender.Call("setclientdvar", arguments[1], optarg);
                                break;
                            case "direct":
                                sender.SetClientDvar(arguments[1], optarg);
                                break;
                        }
                        WriteChatToPlayer(sender, Command.GetString("cdvar", "message").Format(new Dictionary<string, string>()
                        {
                            {"<type>", arguments[0] },
                            {"<key>", arguments[1] },
                            {"<value>", optarg },
                        }));
                    }
                    else
                    {
                        string dvar = UTILS_GetDefCDvar(arguments[1]);
                        WriteChatToPlayer(sender, Command.GetString("cdvar", "message").Format(new Dictionary<string, string>()
                        {
                            {"<type>", "string" },
                            {"<key>", arguments[1] },
                            {"<value>",String.IsNullOrEmpty(dvar)?"NULL":dvar }
                        }));
                    }
                    return;
                }));

            // SDVAR
            CommandList.Add(new Command("sdvar", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Call("setdvar", arguments[0], optarg);
                    WriteChatToPlayer(sender, Command.GetString("sdvar", "message").Format(new Dictionary<string, string>()
                    {
                        {"<key>", arguments[0] },
                        {"<value>", String.IsNullOrEmpty(optarg)?"NULL":optarg },
                    }));
                    return;
                }));

            // MODE
            CommandList.Add(new Command("mode", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (!System.IO.File.Exists(@"admin\" + arguments[0] + ".dsr") && !System.IO.File.Exists(@"players2\" + arguments[0] + ".dsr"))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("DSRNotFound"));
                        return;
                    }
                    CMD_mode(arguments[0]);
                    WriteChatToAll(Command.GetString("mode", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<dsr>", arguments[0] },
                    }));
                }));

            // GAMETYPE
            CommandList.Add(new Command("gametype", 2, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (!System.IO.File.Exists(@"admin\" + arguments[0] + ".dsr") && !System.IO.File.Exists(@"players2\" + arguments[0] + ".dsr"))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("DSRNotFound"));
                        return;
                    }
                    string newmap = FindSingleMap(arguments[1]);
                    if (string.IsNullOrWhiteSpace(newmap))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOneMapFound"));
                        return;
                    }
                    CMD_mode(arguments[0], newmap);
                    WriteChatToAll(Command.GetString("gametype", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<dsr>", arguments[0] },
                        {"<mapname>", newmap },
                    }));
                }));

            // SERVER
            CommandList.Add(new Command("server", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    ExecuteCommand(optarg);
                    WriteChatToPlayer(sender, Command.GetString("server", "message").Format(new Dictionary<string, string>()
                    {
                        {"<command>", optarg },
                    }));
                }));

            // TMPBANTIME
            CommandList.Add(new Command("tmpbantime", 2, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    int minutes;
                    if (!int.TryParse(arguments[0], out minutes))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("InvalidTimeSpan"));
                        return;
                    }
                    TimeSpan duration = new TimeSpan(0, minutes, 0);
                    if (duration.TotalHours > 24)
                        duration = new TimeSpan(24, 0, 0);
                    Entity target = FindSinglePlayer(arguments[1]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }

                    if (string.IsNullOrEmpty(optarg))
                        CMD_tmpbantime(target, DateTime.Now.Add(duration));
                    else
                        CMD_tmpbantime(target, DateTime.Now.Add(duration), optarg);

                    WriteChatToAll(Command.GetString("tmpbantime", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<reason>", optarg },
                        {"<timespan>", duration.ToString() },
                    }));
                }));

            // PBAN
            CommandList.Add(new Command("pban", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    CMD_pban(target);
                    WriteChatToAll(Command.GetString("pban", "message").Format(new Dictionary<string, string>()
                    {
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                    }));
                }));

            // UNBAN BY ID
            CommandList.Add(new Command("unban-id", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    int bannumber;
                    BanEntry entry;
                    if (!int.TryParse(arguments[0], out bannumber))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("InvalidNumber"));
                        return;
                    }
                    if ((entry = CMD_unban(bannumber)) == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("DefaultError"));
                        return;
                    }
                    WriteChatToAll(Command.GetString("unban-id", "message").Format(
                        new Dictionary<string, string>()
                        {
                            {"<banid>", entry.banid.ToString() },
                            {"<name>",  entry.playername },
                            {"<guid>",  entry.playerinfo.GetGUIDString() },
                            {"<hwid>",  entry.playerinfo.GetHWIDString() },
                            {"<time>",  entry.until.Year == 9999 ? "^6PERMANENT" : entry.until.ToString("yyyy MMM d HH:mm") }
                        }));
                }));

            // UNBAN BY NAME
            CommandList.Add(new Command("unban", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    List<BanEntry> entries = CMD_SearchBanEntries(arguments[0]);
                    if (entries.Count == 0)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NoEntriesFound"));
                        return;
                    }
                    if (entries.Count > 1)
                    {
                        WriteChatToPlayer(sender, Command.GetString("unban", "multiple_entries_found"));
                        return;
                    }
                    if (CMD_unban(entries[0].banid) != null)
                    {
                        WriteChatToAll(Command.GetString("unban", "message").Format(new Dictionary<string, string>()
                            {
                                {"<banid>", entries[0].banid.ToString() },
                                {"<name>",  entries[0].playername },
                                {"<guid>",  entries[0].playerinfo.GetGUIDString() },
                                {"<hwid>",  entries[0].playerinfo.GetHWIDString() },
                                {"<time>",  entries[0].until.Year == 9999 ? "^6PERMANENT" : entries[0].until.ToString("yyyy MMM d HH:mm") }
                            }));
                    }
                    else
                        WriteChatToPlayer(sender, "Unknown error at DGAdmin::cmd_unban");
                }
            ));

            //LASTBANS
            CommandList.Add(new Command("lastbans", 0, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    int count;
                    if (!int.TryParse(optarg, out count))
                    {
                        count = 4;
                    }
                    List<BanEntry> banlist = CMD_GetLastBanEntries(count);
                    List<string> messages = new List<string>();
                    foreach (BanEntry banentry in banlist)
                    {
                        messages.Add(Command.GetString("lastbans", "message").Format(new Dictionary<string, string>()
                        {
                            {"<banid>", banentry.banid.ToString() },
                            {"<name>", banentry.playername },
                            {"<guid>", banentry.playerinfo.GetGUIDString() },
                            {"<ip>", banentry.playerinfo.GetIPString() },
                            {"<hwid>", banentry.playerinfo.GetHWIDString() },
                            {"<time>", banentry.until.Year == 9999 ? "^6PERMANENT" : banentry.until.ToString("yyyy MMM d HH:mm") },
                        }));
                    }
                    WriteChatToPlayer(sender, Command.GetString("lastbans", "firstline").Format(new Dictionary<string, string>()
                    {
                        {"<nr>", count.ToString() },
                    }));
                    if (messages.Count < 1)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NoEntriesFound"));
                        return;
                    }
                    WriteChatToPlayerMultiline(sender, messages.ToArray());
                }));

            //SEARCHBANS
            CommandList.Add(new Command("searchbans", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    PlayerInfo playerinfo = PlayerInfo.Parse(optarg);
                    List<BanEntry> banlist;
                    if (playerinfo.isNull())
                        banlist = CMD_SearchBanEntries(optarg);
                    else
                        banlist = CMD_SearchBanEntries(playerinfo);
                    List<string> messages = new List<string>();
                    foreach (BanEntry banentry in banlist)
                    {
                        messages.Add(Command.GetString("searchbans", "message").Format(new Dictionary<string, string>()
                        {
                            {"<banid>", banentry.banid.ToString() },
                            {"<name>", banentry.playername },
                            {"<guid>", banentry.playerinfo.GetGUIDString() },
                            {"<ip>", banentry.playerinfo.GetIPString() },
                            {"<hwid>", banentry.playerinfo.GetHWIDString() },
                            {"<time>", banentry.until.Year == 9999 ? "^6PERMANENT" : banentry.until.ToString("yyyy MMM d HH:mm") },
                        }));
                    }
                    WriteChatToPlayer(sender, Command.GetString("searchbans", "firstline"));
                    if (messages.Count < 1)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NoEntriesFound"));
                        return;
                    }
                    WriteChatToPlayerMultiline(sender, messages.ToArray());
                }));

            // HELP
            CommandList.Add(new Command("help", 0, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    if (!string.IsNullOrEmpty(optarg))
                    {
                        string actualcommand;
                        if (CommandAliases.TryGetValue(optarg, out actualcommand))
                        {
                            optarg = actualcommand;
                        }
                        if (DefaultCmdLang.ContainsKey(string.Format("command_{0}_usage", optarg)))
                            WriteChatToPlayer(sender, Command.GetString(optarg, "usage"));
                        else
                            WriteChatToPlayer(sender, Command.GetMessage("CommandNotFound"));
                        return;
                    }
                    GroupsDatabase.Group playergroup = sender.GetGroup(database);
                    GroupsDatabase.Group defaultgroup = database.GetGroup("default");
                    List<string> availablecommands = (from cmd in CommandList
                                                      where playergroup.CanDo(cmd.name) || defaultgroup.CanDo(cmd.name)
                                                      orderby cmd.name
                                                      select cmd.name).ToList();
                    WriteChatToPlayer(sender, Command.GetString("help", "firstline"));
                    WriteChatToPlayerMultiline(sender, availablecommands.ToArray().Condense(), 2000);
                }));

            // CLEARTMPBANLIST
            CommandList.Add(new Command("cleartmpbanlist", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    CMDS_ClearTemporaryBanlist();
                }));

            // RAGE
            CommandList.Add(new Command("rage", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (CMDS_IsRekt(sender))
                        return;
                    CMD_kick(sender, Command.GetString("rage", "kickmessage"));

                    string message = Command.GetString("rage", "message");
                    foreach (string name in Command.GetString("rage", "custommessagenames").Split(','))
                    {
                        if (sender.Name.ToLowerInvariant().Contains(name))
                            message = Command.GetString("rage", "message_" + name);
                    }

                    WriteChatToAll(message.Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                    }));
                }));

            // LOADGROUPS
            CommandList.Add(new Command("loadgroups", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    foreach (Entity player in Players)
                    {
                        player.setLogged(false);
                    }
                    database = new GroupsDatabase();
                    WriteChatToAll(Command.GetString("loadgroups", "message"));
                }));

            // MAPS
            CommandList.Add(new Command("maps", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayer(sender, Command.GetString("maps", "firstline"));
                    WriteChatToPlayerCondensed(sender, (from mapname in ConfigValues.AvailableMaps.Keys
                                                        select mapname).ToArray());
                }));

            // TIME
            CommandList.Add(new Command("time", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    WriteChatToPlayer(sender, string.Format(Command.GetString("time", "message"), DateTime.Now));
                }));

            // YELL
            CommandList.Add(new Command("yell", 1, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    if (arguments[0].ToLowerInvariant() == "all")
                    {
                        foreach (Entity player in Players)
                            player.IPrintLnBold(optarg);
                        return;
                    }

                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    target.IPrintLnBold(optarg);
                }));

            // CHANGETEAM
            CommandList.Add(new Command("changeteam", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    switch (target.GetTeam())
                    {
                        case "axis":
                            CMD_changeteam(target, "allies");
                            WriteChatToAll(Command.GetString("setteam", "message").Format(new Dictionary<string, string>()
                            {
                                {"<issuer>", sender.Name },
                                {"<issuerf>", sender.GetFormattedName(database) },
                                {"<target>", target.Name },
                                {"<targetf>", target.GetFormattedName(database) },
                            }));
                            break;
                        case "allies":
                            CMD_changeteam(target, "axis");
                            WriteChatToAll(Command.GetString("setteam", "message").Format(new Dictionary<string, string>()
                            {
                                {"<issuer>", sender.Name },
                                {"<issuerf>", sender.GetFormattedName(database) },
                                {"<target>", target.Name },
                                {"<targetf>", target.GetFormattedName(database) },
                            }));
                            break;
                        case "spectator":
                            WriteChatToPlayer(sender, Command.GetMessage("PlayerIsSpectating"));
                            break;
                    }
                }));

            // WHOIS
            CommandList.Add(new Command("whois", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    WriteChatToPlayer(sender, Command.GetString("whois", "firstline").Format(new Dictionary<string, string>()
                            {
                                {"<target>", target.Name },
                                {"<targetf>", target.GetFormattedName(database) },
                            }));
                    WriteChatToPlayerCondensed(sender, CMD_getallknownnames(target), 500, 50, Command.GetString("whois", "separator"));
                }));

            // END
            CommandList.Add(new Command("end", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    CMD_end();
                    WriteChatToAll(Command.GetString("end", "message").Format(new Dictionary<string, string>()
                            {
                                {"<issuer>", sender.Name },
                                {"<issuerf>", sender.GetFormattedName(database) },
                            }));
                }));

            // SPY
            CommandList.Add(new Command("spy", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    bool enabled = UTILS_ParseBool(arguments[0]);
                    sender.setSpying(enabled);
                    if (enabled)
                    {
                        WriteChatToPlayer(sender, Command.GetString("spy", "message_on"));
                    }
                    else
                    {
                        WriteChatToPlayer(sender, Command.GetString("spy", "message_off"));
                    }
                }));

            // AMSG
            CommandList.Add(new Command("amsg", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    CMD_sendadminmsg(Command.GetString("amsg", "message").Format(new Dictionary<string, string>()
                    {
                        {"<sender>", sender.Name },
                        {"<senderf>", sender.GetFormattedName(database) },
                        {"<message>", optarg },
                    }));
                    if (!sender.HasPermission("receiveadminmsg", database))
                        WriteChatToPlayer(sender, Command.GetString("amsg", "confirmation"));
                }));

            // FREEZE
            CommandList.Add(new Command("freeze", 1, Command.Behaviour.Normal,
                (sender, arguments, optard) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    target.Call("freezecontrols", true);
                    target.SetField("frozenbycommand", 1);
                    WriteChatToAll(Command.GetString("freeze", "message").Format(new Dictionary<string, string>() {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // UNFREEZE
            CommandList.Add(new Command("unfreeze", 1, Command.Behaviour.Normal,
                (sender, arguments, optard) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }

                    if (target.HasField("frozenbycommand") && target.GetField<int>("frozenbycommand") == 1)
                    {
                        target.SetField("frozenbycommand", 0);
                        target.Call("freezecontrols", false);
                    }
                    WriteChatToAll(Command.GetString("unfreeze", "message").Format(new Dictionary<string, string>() {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // MUTE
            CommandList.Add(new Command("mute", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    target.setMuted(true);
                    WriteChatToAll(Command.GetString("mute", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // UNMUTE
            CommandList.Add(new Command("unmute", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    target.setMuted(false);
                    WriteChatToAll(Command.GetString("unmute", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // KILL
            CommandList.Add(new Command("kill", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    AfterDelay(100, () =>
                    {
                        target.Suicide();
                    });
                }));

            // FT // FILMTWEAK
            CommandList.Add(new Command("ft", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (ConfigValues.settings_daytime == "night")
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("blockedByNightMode"));
                        return;
                    }
                    CMD_applyfilmtweak(sender, arguments[0]);
                    WriteChatToPlayer(sender, Command.GetString("ft", "message").Format(new Dictionary<string, string>()
                    {
                        {"<ft>", arguments[0] },
                    }));
                }));

            // NIGHT MODE
            CommandList.Add(new Command("night", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (ConfigValues.settings_daytime == "night")
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("blockedByNightMode"));
                        return;
                    }
                    switch (arguments[0])
                    {
                        case "on":
                            {
                                UTILS_SetClientNightVision(sender);
                                WriteChatToPlayer(sender, "^4NigthMod ^2Activated");
                                break;
                            }
                        case "off":
                            {
                                UTILS_SetCliDefDvars(sender);
                                WriteChatToPlayer(sender, "^4NightMod ^1Deactivated");
                                break;
                            }
                    }
                }));

            // SUNLIGHT COLOR
            CommandList.Add(new Command("sunlight", 3, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    try
                    {
                        Call("setsunlight", Convert.ToSingle(arguments[0]), Convert.ToSingle(arguments[1]), Convert.ToSingle(arguments[2]));
                    }
                    catch
                    {
                        WriteChatToPlayer(sender, Command.GetString("sunlight", "usage"));
                    }
                }));

            //SET ALIAS
            CommandList.Add(new Command("alias", 1, Command.Behaviour.HasOptionalArguments,
            (sender, arguments, optarg) =>
            {
                if (ConfigValues.settings_enable_chat_alias)
                    UTILS_SetChatAlias(sender, arguments[0], optarg);
                else
                    WriteChatToPlayer(sender, Command.GetString("alias", "disabled"));
            }));

            //SET MY ALIAS
            CommandList.Add(new Command("myalias", 0, Command.Behaviour.HasOptionalArguments,
            (sender, arguments, optarg) =>
            {
                if (ConfigValues.settings_enable_chat_alias)
                    UTILS_SetChatAlias(sender, sender.Name, optarg);
                else
                    WriteChatToPlayer(sender, Command.GetString("alias", "disabled"));
            }));

            // SCREAM
            CommandList.Add(new Command("scream", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    CMD_spammessagerainbow(optarg);
                }));

            // KICKHACKER
            CommandList.Add(new Command("kickhacker", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    foreach (Entity entity in GetEntities())
                    {
                        if (entity.Name == optarg)
                            ExecuteCommand("dropclient " + entity.GetEntityNumber().ToString());
                    }
                }));

            // FAKESAY
            CommandList.Add(new Command("fakesay", 1, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    CHAT_WriteChat(target, ChatType.All, optarg);
                }));

            // SILENTBAN
            CommandList.Add(new Command("silentban", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    CMDS_AddToBanList(target, DateTime.MaxValue);
                    target.OnInterval(50, (ent) =>
                    {
                        ent.SetClientDvar("g_scriptMainMenu", "");
                        return true;
                    });
                    WriteChatToPlayer(sender, Command.GetString("silentban", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // REK
            CommandList.Add(new Command("rek", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    if (CMDS_IsRekt(target))
                    {
                        if (CMDS_GetBanTime(target) == DateTime.MinValue)
                            CMDS_AddToBanList(target, DateTime.MaxValue);
                        return;
                    }
                    CMDS_Rek(target);
                    WriteChatToAll(Command.GetString("rek", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // REKTROLL
            CommandList.Add(new Command("rektroll", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    if (CMDS_IsRekt(target))
                    {
                        return;
                    }
                    CMDS_RekEffects(target);
                    WriteChatToAll(Command.GetString("rek", "message").Format(new Dictionary<string, string>()
                    {
                        {"<issuer>", sender.Name },
                        {"<issuerf>", sender.GetFormattedName(database) },
                        {"<target>", target.Name },
                        {"<targetf>", target.GetFormattedName(database) },
                    }));
                }));

            // NOOTNOOT
            CommandList.Add(new Command("nootnoot", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    if (target.HasField("nootnoot") && target.GetField<int>("nootnoot") == 1)
                    {
                        WriteChatToPlayer(sender, Command.GetString("nootnoot", "message_off").Format(new Dictionary<string, string>
                        {
                            {"<target>", target.Name },
                            {"<targetf>", target.GetFormattedName(database) },
                        }));
                        target.SetField("nootnoot", 0);
                    }
                    else
                    {
                        WriteChatToPlayer(sender, Command.GetString("nootnoot", "message_on").Format(new Dictionary<string, string>
                        {
                            {"<target>", target.Name },
                            {"<targetf>", target.GetFormattedName(database) },
                        }));
                        target.SetField("nootnoot", 1);
                    }
                }));

            // BETTERBALANCE
            CommandList.Add(new Command("betterbalance", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    bool state = UTILS_ParseBool(arguments[0]);
                    switch (state)
                    {
                        case true:
                            Call("setdvar", "betterbalance", "1");
                            WriteChatToAll(Command.GetString("betterbalance", "message_on"));
                            break;
                        case false:
                            Call("setdvar", "betterbalance", "0");
                            WriteChatToAll(Command.GetString("betterbalance", "message_off"));
                            break;
                    }
                }));

            // XBAN
            CommandList.Add(new Command("xban", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    if (target.isImmune(database))
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("TargetIsImmune"));
                        return;
                    }
                    CMDS_AddToXBanList(target);
                    CMD_kick(target, optarg);
                    WriteChatToAll(Command.GetString("xban", "message").Format(new Dictionary<string, string>()
                    {
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) },
                            {"<target>", target.Name },
                            {"<targetf>", target.GetFormattedName(database) },
                            {"<reason>", optarg },
                    }));
                }));

            // DBSEARCH
            CommandList.Add(new Command("dbsearch", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    PlayerInfo playerinfo = target.GetInfo();
                    string[] foundplayerinfo = (from player in database.Players
                                                where playerinfo.MatchesOR(player.Key)
                                                select Command.GetString("dbsearch", "message_found").Format(new Dictionary<string, string>()
                                                    {
                                                        {"<playerinfo>", CMDS_CommonIdentifiers(player.Key, playerinfo) + " ^7= ^5" + player.Value },
                                                    })
                                                    ).ToArray();
                    if (foundplayerinfo.Length == 0)
                    {
                        WriteChatToPlayer(sender, Command.GetString("dbsearch", "message_notfound"));
                        return;
                    }
                    WriteChatToPlayer(sender, Command.GetString("dbsearch", "message_firstline").Format(new Dictionary<string, string>
                    {
                        {"<nr>", foundplayerinfo.Length.ToString() },
                    }));
                    WriteChatToPlayerMultiline(sender, foundplayerinfo, 2000);
                }));

            // CLANKICK
            CommandList.Add(new Command("clankick", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    target.SetGroup("default", database);
                    if (ConfigValues.settings_groups_autosave)
                        database.SaveGroups();
                    CMD_kick(target, Command.GetString("clankick", "kickmessage"));
                    WriteChatToAll(Command.GetString("clankick", "message").Format(new Dictionary<string, string>()
                        {
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) },
                            {"<target>", target.Name },
                            {"<targetf>", target.GetFormattedName(database) },
                        }));
                }));
            // DAY TIME
            CommandList.Add(new Command("daytime", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    ConfigValues.settings_daytime = arguments[0];
                    foreach (Entity player in Players)
                        UTILS_SetCliDefDvars(player);
                }));

            //KD
            CommandList.Add(new Command("kd", 3, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    int kills, deaths;
                    if (!(int.TryParse(arguments[1], out kills) && int.TryParse(arguments[2], out deaths)))
                    {
                        WriteChatToPlayer(sender, Command.GetString("kd", "usage"));
                        return;
                    }
                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }
                    target.SetField("kills", kills);
                    target.SetField("Kills", kills);
                    target.SetField("Kill", kills);
                    target.SetField("deaths", deaths);
                    target.SetField("Deaths", deaths);
                    target.SetField("death", deaths);
                    WriteChatToPlayer(sender, Command.GetString("kd", "message").Format(new Dictionary<string, string>()
                        {
                            {"<player>", target.Name },
                            {"<kills>", kills.ToString() },
                            {"<deaths>", deaths.ToString() }
                        }));
                }));

            // REPORT
            CommandList.Add(new Command("report", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                (sender, arguments, optarg) =>
                {
                    using (StreamWriter w = File.AppendText(ConfigValues.ConfigPath + @"Commands\internal\ChatReports.txt"))
                    {
                        w.WriteLine(
                            Command.GetString("lastreports", "message").Format(new Dictionary<string, string>()
                            {
                                {"<sender>", sender.Name },
                                {"<senderf>", sender.GetFormattedName(database) },
                                {"<message>", optarg },
                            }));
                        w.Close();
                    }
                    WriteChatToPlayer(sender, @"^3Niggas reported");
                    CMD_sendadminmsg(Command.GetString("report", "message").Format(new Dictionary<string, string>()
                        {
                            {"<sender>", sender.Name },
                            {"<senderf>", sender.GetFormattedName(database) },
                            {"<message>", optarg },
                        }));
                }));

            //LASTREPORTS
            CommandList.Add(new Command("lastreports", 0, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    int reportcnt;
                    if (!String.IsNullOrEmpty(optarg))
                    {
                        if (!int.TryParse(optarg, out reportcnt))
                        {
                            WriteChatToPlayer(sender, Command.GetString("lastreports", "usage"));
                            return;
                        }
                        if ((reportcnt < 1) || (reportcnt > 8))
                        {
                            WriteChatToPlayer(sender, Command.GetString("lastreports", "usage"));
                            return;
                        }
                    }
                    else
                        reportcnt = 4;
                    string[] reports = File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\internal\ChatReports.txt");
                    reportcnt = Math.Min(reportcnt, reports.Length);
                    for (int i = reports.Length - reportcnt; i < reports.Length; i++)
                    {
                        WriteChatToPlayer(sender, reports[i]);
                    }
                }));

            // SETFX
            CommandList.Add(new Command("setfx", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    bool fx_valid = true;
                    Array.ForEach(arguments[0].Split(','), (s) => {
                        if (!UTILS_ValidateFX(s))
                            fx_valid = false;
                    });
                    if (!fx_valid)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("FX_not_found"));
                        return;
                    }
                    if (!sender.HasField("CMD_SETFX"))
                    {
                        string v = arguments[0];
                        sender.SetField("CMD_SETFX", new Parameter(v));
                        string key = String.IsNullOrEmpty(optarg) ? "+activate" : optarg;
                        if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\setfx.txt"))
                            System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\setfx.txt", new string[] { });
                        sender.Call("notifyonplayercommand", "spawnfx", key);
                        sender.OnNotify("spawnfx", ent =>
                        {
                            using (StreamWriter file = File.AppendText(ConfigValues.ConfigPath + @"Commands\internal\setfx.txt"))
                            {
                                Array.ForEach(sender.GetField<string>("CMD_SETFX").Split(','), (s) =>
                                {
                                    Call("triggerfx", Call<Entity>("spawnFx", Call<int>("loadfx", s), sender.Origin, new Vector3(0, 0, 1), new Vector3(0, 0, 0)));
                                    WriteChatToPlayer(sender, Command.GetString("setfx", "spawned").Format(new Dictionary<string, string>()
                                    {
                                        {"<fx>", s },
                                        {"<origin>", sender.Origin.ToString() },
                                    }));
                                    file.WriteLine("<fx> <x> <y> <z>".Format(new Dictionary<string, string>(){
                                        {"<fx>", s},
                                        {"<x>", sender.Origin.X.ToString()},
                                        {"<y>", sender.Origin.Y.ToString()},
                                        {"<z>", sender.Origin.Z.ToString()}
                                    }));
                                });
                            };
                        });
                        WriteChatToPlayer(sender, Command.GetString("setfx", "enabled").Format(new Dictionary<string, string>()
                        {
                            {"<key>", @"[[{" + key + @"}]]"}
                        }));
                    }
                    else
                    {
                        sender.SetField("CMD_SETFX", arguments[0]);
                        WriteChatToPlayer(sender, Command.GetString("setfx", "changed").Format(new Dictionary<string, string>()
                        {
                            {"<fx>", arguments[0]}
                        }));
                    }
                }));


            // HELL
            CommandList.Add(new Command("hell", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    if (!ConfigValues.HellMode)
                    {
                        if (UTILS_GetDvar("mapname") == "mp_seatown")
                        {
                            ConfigValues.HellMode = true;
                            UTILS_SetHellMod();
                            WriteChatToAll(Command.GetString("hell", "message"));
                        }
                        else
                        {
                            WriteChatToPlayer(sender, Command.GetString("hell", "error2"));
                        }
                    }
                    else
                    {
                        WriteChatToPlayer(sender, Command.GetString("hell", "error1"));
                    }
                }));

            // FIRE
            CommandList.Add(new Command("fire", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    //Entity fx = Call<Entity>("playfxontag", Call<int>("loadfx", "fire/car_fire_mp"), sender, "tag_origin");
                    int addr = Call<int>("loadfx", "misc/flares_cobra");
                    OnInterval(200, () => {
                        Call("playfx", addr, sender.Call<Vector3>("GetEye"));
                        return true;
                    });
                    WriteChatToPlayer(sender, "^1FIREEEEEEEEEEEE");
                }));

            // SUICIDE
            CommandList.Add(new Command("suicide", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    AfterDelay(100, () => {
                        sender.Suicide();
                    });

                }));

            // YES
            CommandList.Add(new Command("yes", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    string command = sender.GetField<string>("CurrentCommand");
                    if (String.IsNullOrEmpty(command))
                    {
                        WriteChatToPlayer(sender, "^3Warning: Command buffer is empty.");
                        return;
                    }
                    ProcessCommand(sender, sender.Name, command);
                }));

            // NO
            CommandList.Add(new Command("no", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    string command = sender.GetField<string>("CurrentCommand");
                    if (String.IsNullOrEmpty(command))
                    {
                        WriteChatToPlayer(sender, "^3Warning: Command buffer is empty.");
                        return;
                    }
                    sender.SetField("CurrentCommand", "");
                    WriteChatToPlayer(sender, "^3Command execution aborted (^1" + command + "^3)");
                }));

            // 3RDPERSON
            CommandList.Add(new Command("3rdperson", 0, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    OnInterval(33, () =>
                    {
                        foreach (Entity player in Players)
                        {
                            player.SetClientDvar("cg_thirdPerson", " 1");
                            player.SetClientDvar("cg_thirdPersonMode", " 1");
                            player.SetClientDvar("cg_thirdPersonSpectator", " 1");
                            player.SetClientDvar("scr_thirdPerson", " 1");
                            player.SetClientDvar("camera_thirdPerson", " 1");
                        }
                        return true;
                    });
                    WriteChatToAll(Command.GetString("3rdperson", "message").Format(new Dictionary<string, string>()
                        {
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) }
                        }));
                }));

            // TELEPORT
            CommandList.Add(new Command("teleport", 2, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    Entity player1 = FindSinglePlayer(arguments[0]);
                    Entity player2 = FindSinglePlayer(arguments[1]);
                    if ((player1 != null) && (player2 != null))
                    {
                        player1.Call("setOrigin", player2.Origin);
                        WriteChatToPlayer(sender, Command.GetString("teleport", "message").Format(new Dictionary<string, string>()
                        {
                            {"<player1>", player1.Name },
                            {"<player2>", player2.Name }
                        }));
                    }
                    else
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                }));

            // FLY <on | off> [bound key]
            // ; exports: CMD_FLY (0 .. 3)
            // ; 0 = disabled; 1 = EventHandleds set; 2 = EventHandlers active; 3 = Effects active
            // ; path: 0 > 2; 2 <> 3; 2 <> 1;
            CommandList.Add(new Command("fly", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    const int DISABLED = 0;
                    const int EVENTHANDLERS_SET = 1;
                    const int EVENTHANDLERS_ACTIVE = 2;
                    const int EFFECTS_ACTIVE = 3;

                    if ((arguments[0] != "on") && (arguments[0] != "off"))
                    {
                        WriteChatToPlayer(sender, Command.GetString("fly", "usage"));
                        return;
                    }
                    if (!sender.HasField("CMD_FLY"))
                        sender.SetField("CMD_FLY", new Parameter(DISABLED));

                    string key = String.IsNullOrEmpty(optarg) ? "activate" : optarg;

                    if (UTILS_GetFieldSafe<int>(sender, "CMD_FLY") == DISABLED)
                    {
                        sender.OnNotify("fly_on", new Action<Entity>((_player) =>
                        {
                            if (UTILS_GetFieldSafe<int>(_player, "CMD_FLY") == EVENTHANDLERS_ACTIVE/* && 
                                _player.GetField<string>("sessionstate") == "playing"*/)
                            {
                                sender.SetField("CMD_FLY", EFFECTS_ACTIVE);
                                _player.Call("allowspectateteam", "freelook", true);
                                _player.SetField("sessionstate", "spectator");
                                _player.Call("setcontents", 0);
                                UTILS_SetClientInShadowFX(_player);
                                int iter = 0;
                                _player.OnInterval(100, __player =>
                                {
                                    if (iter % 10 == 0)
                                        __player.Call("playlocalsound", "ui_mp_nukebomb_timer");
                                    if (iter % 30 == 0)
                                        __player.Call("playlocalsound", "breathing_hurt");
                                    iter += 1;
                                    return UTILS_GetFieldSafe<int>(_player, "CMD_FLY") == EFFECTS_ACTIVE;
                                });
                            }
                        }));
                        sender.OnNotify("fly_off", new Action<Entity>((_player) =>
                        {
                            if (UTILS_GetFieldSafe<int>(_player, "CMD_FLY") == EFFECTS_ACTIVE/* && 
                                _player.GetField<string>("sessionstate") == "spectator"*/)
                            {
                                _player.SetField("CMD_FLY", EVENTHANDLERS_ACTIVE);
                                _player.Call("allowspectateteam", "freelook", false);
                                _player.SetField("sessionstate", "playing");
                                _player.Call("setcontents", 100);
                                _player.Call("ThermalVisionOff");
                                UTILS_SetCliDefDvars(_player);
                            }
                        }));
                    }

                    switch (arguments[0])
                    {
                        case "on":
                            {
                                int CMD_FLY = UTILS_GetFieldSafe<int>(sender, "CMD_FLY");
                                if (CMD_FLY == DISABLED)
                                {
                                    sender.Call("notifyonplayercommand", "fly_on", "+" + key);
                                    sender.Call("notifyonplayercommand", "fly_off", "-" + key);
                                }
                                WriteChatToPlayer(sender, Command.GetString("fly", "enabled").Format(new Dictionary<string, string>()
                                {
                                    {"<key>", (CMD_FLY == EVENTHANDLERS_SET)? "" : @"[[{" + key + @"}]]" }
                                }));
                                sender.SetField("CMD_FLY", EVENTHANDLERS_ACTIVE);
                                break;
                            }
                        case "off":
                            {
                                sender.SetField("CMD_FLY", EVENTHANDLERS_SET);
                                WriteChatToPlayer(sender, Command.GetString("fly", "disabled").Format(new Dictionary<string, string>()
                                {
                                    {"<state>", "enabled" }
                                }));
                                break;
                            }
                    }

                }));

            // JUMP
            CommandList.Add(new Command("jump", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    float height = 0;
                    if (arguments[0].StartsWith("def"))
                        CMD_JUMP(39);
                    else if (float.TryParse(arguments[0], out height))
                        CMD_JUMP(height);
                    WriteChatToAll(Command.GetString("jump", "message").Format(new Dictionary<string, string>()
                        {
                            {"<height>", arguments[0].StartsWith("def")?"default" : height.ToString() },
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) }
                        }));
                }));

            // SPEED
            CommandList.Add(new Command("speed", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    float speed = 0;
                    if (arguments[0].StartsWith("def"))
                        foreach (Entity player in Players)
                            CMD_SPEED(player, 1F);
                    else if (float.TryParse(arguments[0], out speed))
                        foreach (Entity player in Players)
                            CMD_SPEED(player, speed);
                    WriteChatToAll(Command.GetString("speed", "message").Format(new Dictionary<string, string>()
                        {
                            {"<speed>", arguments[0].StartsWith("def")?"default" : speed.ToString() },
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) }
                        }));
                }));

            // GRAVITY
            CommandList.Add(new Command("gravity", 1, Command.Behaviour.Normal,
                (sender, arguments, optarg) =>
                {
                    float gravity = 0;
                    if (arguments[0].StartsWith("def"))
                        CMD_GRAVITY(800);
                    else if (float.TryParse(arguments[0], out gravity))
                        CMD_GRAVITY((int)Math.Round(gravity / 9.8 * 800));
                    WriteChatToAll(Command.GetString("gravity", "message").Format(new Dictionary<string, string>()
                        {
                            {"<g>", arguments[0].StartsWith("def")?"9.8" : gravity.ToString() },
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) }
                        }));
                }));

            // AC130 <all | <player>> [-p]
            CommandList.Add(new Command("ac130", 1, Command.Behaviour.HasOptionalArguments,
                (sender, arguments, optarg) =>
                {
                    if (arguments[0] == "all")
                    {
                        foreach (Entity player in Players)
                            CMD_AC130(player, optarg == "-p");
                        WriteChatToAll(Command.GetString("ac130", "all").Format(new Dictionary<string, string>()
                        {
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) },
                        }));
                        return;
                    }

                    Entity target = FindSinglePlayer(arguments[0]);
                    if (target == null)
                    {
                        WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                        return;
                    }

                    CMD_AC130(target, optarg == "-p");

                    WriteChatToAll(Command.GetString("ac130", "message").Format(new Dictionary<string, string>()
                        {
                            {"<issuer>", sender.Name },
                            {"<issuerf>", sender.GetFormattedName(database) },
                            {"<target>", target.Name },
                            {"<targetf>", target.GetFormattedName(database) },
                        }));
                }));

            // PLAYFXONTAG <fx> [tag = j_head]
            CommandList.Add(new Command("playfxontag", 1, Command.Behaviour.HasOptionalArguments,
            (sender, arguments, optarg) =>
            {
                if (!UTILS_ValidateFX(arguments[0]))
                {
                    WriteChatToPlayer(sender, Command.GetMessage("FX_not_found"));
                    return;
                }
                string tag = String.IsNullOrEmpty(optarg) ? "j_head" : optarg;
                Entity fx = Call<Entity>("playfxontag", new Parameter[] { Call<int>("loadfx", arguments[0]), sender, tag });
                WriteChatToPlayer(sender, Command.GetString("playfxontag", "message").Format(new Dictionary<string, string>()
                {
                    {"<fx>", arguments[0]},
                    {"<tag>", tag}
                }));
            }));

            // SETCLANTAG <player> [tag]
            CommandList.Add(new Command("setclantag", 1, Command.Behaviour.HasOptionalArguments,
            (sender, arguments, optarg) =>
            {
                Entity target = FindSinglePlayer(arguments[0]);
                if (target == null)
                {
                    WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                    return;
                }
                string tag = String.IsNullOrEmpty(optarg) ? "" : optarg;
                target.SetClantag(tag);
            }));

            // ROTATESCREEN <player> <degree>
            CommandList.Add(new Command("rotatescreen", 2, Command.Behaviour.Normal,
            (sender, arguments, optarg) =>
            {
                Entity target = FindSinglePlayer(arguments[0]);
                if (target == null)
                {
                    WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                    return;
                }
                Vector3 angles = target.Call<Vector3>("getplayerangles");
                if (!float.TryParse(arguments[1], out angles.Z))
                {
                    WriteChatToPlayer(sender, Command.GetString("rotatescreen", "usage"));
                    return;
                }

                target.Call("setplayerangles", new Parameter[] { angles });

                WriteChatToPlayer(sender, Command.GetString("rotatescreen", "message").Format(new Dictionary<string, string>()
                {
                    {"<player>", sender.Name},
                    {"<roll>", angles.Z.ToString()}
                }));
            }));

            if (ConfigValues.settings_enable_misccommands)
            {
                // FORCECOMMAND // FC
                CommandList.Add(new Command("fc", 1, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                    (sender, arguments, optarg) =>
                    {
                        Entity target = FindSinglePlayer(arguments[0]);
                        if (target == null)
                        {
                            WriteChatToPlayer(sender, Command.GetMessage("NotOnePlayerFound"));
                            return;
                        }
                        ProcessForceCommand(sender, target, target.Name, "!" + optarg);
                    }));

                // FOREACH
                CommandList.Add(new Command("foreach", 1, Command.Behaviour.HasOptionalArguments | Command.Behaviour.OptionalIsRequired,
                    (sender, arguments, optarg) =>
                    {
                        optarg = "!" + optarg;
                        bool includeself = UTILS_ParseBool(arguments[0]);
                        foreach (Entity player in Players)
                        {
                            int number = player.GetEntityNumber();
                            if (includeself == false && number == sender.GetEntityNumber())
                                continue;
                            ProcessCommand(sender, sender.Name, optarg.Replace("<player>", "#" + player.GetEntityNumber().ToString()));
                        }
                    }));

                // SVPASSWORD
                CommandList.Add(new Command("svpassword", 0, Command.Behaviour.HasOptionalArguments | Command.Behaviour.MustBeConfirmed,
                    (sender, arguments, optarg) =>
                    {
                        string path = @"players2\server.cfg";
                        optarg = String.IsNullOrEmpty(optarg) ? "" : optarg;
                        if (optarg.IndexOf('"') != -1)
                        {
                            WriteChatToPlayer(sender, "^1Error: Password has forbidden characters. Try another.");
                            return;
                        }
                        if (!System.IO.File.Exists(path))
                        {
                            WriteChatToPlayer(sender, "^1Error: ^3" + path + "^1 not found.");
                            return;
                        }

                        WriteChatToAll(@"^3<issuer> ^1executed ^3!svpassword".Format(new Dictionary<string, string>()
                        {
                            {"<issuer>", sender.Name },
                        }));

                        AfterDelay(2000, () =>
                        {
                            WriteChatToAllMultiline(new string[] {
                                "^1Server will be killed in:",
                                "^35",
                                "^34",
                                "^33",
                                "^32",
                                "^31",
                                "^30"
                            }, 1000);
                        });
                        AfterDelay(8000, () =>
                        {
                            string password = "seta g_password \"" + optarg + "\"";
                            List<string> lines = File.ReadAllLines(path).ToList();
                            Regex regex = new Regex(@"seta g_password ""[^""]*""");

                            bool found = false;
                            for (int i = 0; i < lines.Count; i++)
                            {
                                if (regex.Matches(lines[i]).Count == 1)
                                {
                                    found = true;
                                    lines[i] = password;
                                    break;
                                }
                            }
                            if (!found)
                                lines.Add(password);
                            File.WriteAllLines(path, lines.ToArray());
                            foreach (Entity player in Players)
                                CMD_kick(player, "^3Server killed");
                            AfterDelay(1000, () => Environment.Exit(-1));
                        });
                    }));
            }

            #endregion

            WriteLog.Info("Initialized commands.");
        }

        public void InitCommandAliases()
        {
            if (!System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\commandaliases.txt"))
                System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\commandaliases.txt", new string[]{
                    "protect=addimmune",
                    "unprotect=unimmune",
                    "hbi=hidebombicon",
                    "cvsa=clanvsall",
                    "tbt=tmpbantime",
                    "a=amsg"
                });

            foreach (string line in System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\commandaliases.txt"))
            {
                string[] parts = line.Split('=');
                CommandAliases.Add(parts[0], parts[1]);
            }

            WriteLog.Info("Initialized command aliases");
        }

        public void InitCDVars()
        {
            if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Utils\cdvars.txt"))
            {
                foreach (string line in System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Utils\cdvars.txt"))
                {
                    string[] parts = line.Split('=');
                    DefaultCDvars.Add(new Dvar { key = parts[0], value = parts[1] });
                }
            }
            if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Commands\internal\daytime.txt"))
            {
                foreach (string line in System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\internal\daytime.txt"))
                {
                    ConfigValues.settings_daytime = line;
                }
            }
        }

        public void InitChatAlias()
        {
            if (System.IO.File.Exists(ConfigValues.ConfigPath + @"Utils\chatalias.txt"))
            {
                foreach (string line in System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Utils\chatalias.txt"))
                {
                    string[] parts = line.Split('=');
                    for (int i = 2; i < parts.Length; i++)
                        parts[1] += "=" + parts[i];
                    try
                    {
                        ChatAlias.Add(Convert.ToInt64(parts[0]), parts[1]);
                    }
                    catch
                    {
                        WriteLog.Error("Error reading chat alias entry: " + line);
                    }
                }
            }
        }

        #region ACTUAL COMMANDS

        public void CMD_kick(Entity target, string reason = "You have been kicked")
        {
            AfterDelay(100, () =>
            {
                ExecuteCommand("dropclient " + target.GetEntityNumber() + " \"" + reason + "\"");
            });
        }

        public void CMD_tmpban(Entity target, string reason = "You have been tmpbanned")
        {
            AfterDelay(100, () =>
            {
                ExecuteCommand("tempbanclient " + target.GetEntityNumber() + " \"" + reason + "\"");
            });
        }

        public void CMD_ban(Entity target, string reason = "You have been banned")
        {
            CMDS_AddToBanList(target, DateTime.MaxValue);
            CMD_kick(target, reason);
        }

        public void CMD_tmpbantime(Entity target, DateTime until, string reason = "You have been tmpbanned")
        {
            CMDS_AddToBanList(target, until);
            CMD_kick(target, reason);
        }

        public void CMD_say(string message)
        {
            WriteChatToAll(message);
        }

        public void CMD_sayto(Entity target, string message)
        {
            WriteChatToPlayer(target, message);
        }

        public void CMD_sendprivatemessage(Entity target, string sendername, string message)
        {
            AfterDelay(100, () =>
            {
                WriteChatToPlayer(target, Command.GetString("pm", "message").Format(new Dictionary<string, string>()
                    {
                        {"<sender>", sendername },
                        {"<message>", message },
                    }));
            });
        }

        public void CMD_changemap(string devmapname)
        {
            OnExitLevel();
            ChangeMap(devmapname);
        }

        public int CMD_getwarns(Entity player)
        {
            List<string> lines = System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt").ToList();
            string identifiers = player.GetInfo().getIdentifiers();
            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts[0] == identifiers)
                    return int.Parse(parts[1]);
            }
            return 0;
        }

        public int CMD_addwarn(Entity player)
        {
            List<string> lines = System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt").ToList();
            string identifiers = player.GetInfo().getIdentifiers();
            for (int i = 0; i < lines.Count; i++)
            {
                string[] parts = lines[i].Split(':');
                if (parts[0] == identifiers)
                {
                    int warns = (int.Parse(parts[1]) + 1);
                    lines[i] = string.Format("{0}:{1}", parts[0], warns.ToString());
                    System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt", lines);
                    return warns;
                }
            }
            lines.Add(string.Format("{0}:1", player.GetInfo().getIdentifiers()));
            System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt", lines);
            return 1;
        }

        public int CMD_unwarn(Entity player)
        {
            List<string> lines = System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt").ToList();
            string identifiers = player.GetInfo().getIdentifiers();
            for (int i = 0; i < lines.Count; i++)
            {
                string[] parts = lines[i].Split(':');
                if (parts[0] == identifiers)
                {
                    int warns = (int.Parse(parts[1]) - 1);
                    if (warns < 0)
                        warns = 0;
                    lines[i] = parts[0] + warns.ToString();
                    System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt", lines);
                    return warns;
                }
            }
            return 0;

        }

        public void CMD_resetwarns(Entity player)
        {
            List<string> lines = System.IO.File.ReadAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt").ToList();
            string identifiers = player.GetInfo().getIdentifiers();
            for (int i = 0; i < lines.Count; i++)
            {
                string[] parts = lines[i].Split(':');
                if (parts[0] == identifiers)
                {
                    lines.Remove(lines[i]);
                    System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\internal\warns.txt", lines);
                    return;
                }
            }
            return;
        }

        public void CMD_changeteam(Entity player, string team)
        {
            player.SetField("sessionteam", team);
            player.Notify("menuresponse", "team_marinesopfor", team);
        }

        public void CMD_clanvsall(List<string> identifiers, bool changespectators = false)
        {
            foreach (Entity player in Players)
                if (!player.IsSpectating() || changespectators)
                    foreach (string identifier in identifiers)
                    {
                        string tolowidentifier = identifier.ToLowerInvariant();
                        if (player.Name.ToLowerInvariant().Contains(tolowidentifier) || player.GetClantag().ToLowerInvariant().Contains(tolowidentifier))
                        {
                            if (player.GetTeam() == "axis")
                                CMD_changeteam(player, "allies");
                        }
                        else
                            CMD_changeteam(player, "axis");
                    }
        }

        public void CMD_mode(string dsrname, string map = "")
        {
            if (string.IsNullOrWhiteSpace(map))
                map = UTILS_GetDvar("mapname");

            if (!string.IsNullOrWhiteSpace(MapRotation))
            {
                WriteLog.Info("ERROR: Modechange already in progress");
                return;
            }

            map = map.Replace("default:", "");
            using (System.IO.StreamWriter DSPLStream = new System.IO.StreamWriter("players2\\RG.dspl"))
            {
                DSPLStream.WriteLine(map + "," + dsrname + ",1000");
            }
            MapRotation = UTILS_GetDvar("sv_maprotation");
            OnExitLevel();
            ExecuteCommand("sv_maprotation RG");
            CMD_rotate();
            ExecuteCommand("sv_maprotation " + MapRotation);
            MapRotation = "";
        }

        public void CMD_rotate()
        {
            ExecuteCommand("map_rotate");
        }

        public void CMD_pban(Entity player)
        {
            AfterDelay(100, () =>
            {
                ExecuteCommand("ban " + player.GetEntityNumber());
            });
        }

        public BanEntry CMD_unban(int id)
        {
            BanEntry entry = null;
            try
            {
                if (id < BanList.Count && id >= 0)
                {
                    string[] parts = BanList[id].Split(';');
                    string playername = string.Join(";", parts.Skip(2));
                    entry = new BanEntry(id, PlayerInfo.Parse(parts[1]), playername, DateTime.ParseExact(parts[0], "yyyy MMM d HH:mm", Culture));
                    BanList.Remove(BanList[id]);
                }
                CMDS_SaveBanList();
                return entry;
            }
            catch (Exception ex)
            {
                WriteLog.Error("Error while running unban command");
                WriteLog.Error(ex.Message);
                return null;
            }
        }

        public List<BanEntry> CMD_SearchBanEntries(string name)
        {
            List<BanEntry> foundentries = new List<BanEntry>();
            try
            {
                for (int i = 0; i < BanList.Count; i++)
                {
                    string[] parts = BanList[i].Split(';');
                    string playername = string.Join(";", parts.Skip(2));
                    if (playername.ToLowerInvariant().Contains(name.ToLowerInvariant()))
                    {
                        foundentries.Add(new BanEntry(i, PlayerInfo.Parse(parts[1]), playername, DateTime.ParseExact(parts[0], "yyyy MMM d HH:mm", Culture)));
                    }
                }
                return foundentries;
            }
            catch (Exception)
            {
                return new List<BanEntry>();
            }
        }

        public List<BanEntry> CMD_SearchBanEntries(PlayerInfo playerinfo)
        {
            List<BanEntry> foundentries = new List<BanEntry>();
            try
            {
                for (int i = 0; i < BanList.Count; i++)
                {
                    string[] parts = BanList[i].Split(';');
                    string playername = string.Join(";", parts.Skip(2));
                    if (PlayerInfo.Parse(parts[1]).MatchesOR(playerinfo))
                    {
                        foundentries.Add(new BanEntry(i, PlayerInfo.Parse(parts[1]), playername, DateTime.ParseExact(parts[0], "yyyy MMM d HH:mm", Culture)));
                    }
                }
                return foundentries;
            }
            catch (Exception)
            {
                return new List<BanEntry>();
            }
        }

        public List<BanEntry> CMD_GetLastBanEntries(int count)
        {
            List<BanEntry> foundentries = new List<BanEntry>();
            try
            {
                if (count > BanList.Count)
                    count = BanList.Count;
                if (count < 1)
                    count = 1;
                for (int i = BanList.Count - 1; i >= BanList.Count - count; i--)
                {
                    string[] parts = BanList[i].Split(';');
                    string playername = string.Join(";", parts.Skip(2));
                    foundentries.Add(new BanEntry(i, PlayerInfo.Parse(parts[1]), playername, DateTime.ParseExact(parts[0], "yyyy MMM d HH:mm", Culture)));
                }
                return foundentries;
            }
            catch (Exception)
            {
                return new List<BanEntry>();
            }
        }

        public string[] CMD_getallknownnames(Entity player)
        {
            if (System.IO.File.Exists(string.Format(ConfigValues.ConfigPath + @"Utils\playerlogs\{0}.txt", player.GUID)))
                return System.IO.File.ReadAllLines(string.Format(ConfigValues.ConfigPath + @"Utils\playerlogs\{0}.txt", player.GUID));
            return new string[0];
        }

        public void CMD_end()
        {
            foreach (Entity player in Players)
            {
                player.Notify("menuresponse", "menu", "endround");
            }
        }

        public void CMD_sendadminmsg(string message)
        {
            foreach (Entity player in Players)
                if (player.HasPermission("receiveadminmsg", database))
                    WriteChatAdmToPlayer(player, message);
        }

        public void CMD_applyfilmtweak(Entity sender, string ft)
        {
            List<Dvar> dvars = new List<Dvar>();
            switch (ft)
            {
                case "0":
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "0" });
                    dvars.Add(new Dvar { key = "r_colorMap", value = "1" });
                    dvars.Add(new Dvar { key = "r_specularMap", value = "1" });
                    dvars.Add(new Dvar { key = "r_normalMap", value = "1" });
                    break;
                case "1":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "0.65 0.7 0.8" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1.3" });
                    dvars.Add(new Dvar { key = "r_filmtweakbrightness", value = "0.15" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "1.8 1.8 1.8" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "1" });
                    break;
                case "2":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "1.15 1.1 1.3" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1.6" });
                    dvars.Add(new Dvar { key = "r_filmtweakbrightness", value = "0.2" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "1.35 1.3 1.25" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "1" });
                    break;
                case "3":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "0.8 0.8 1.1" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1.3" });
                    dvars.Add(new Dvar { key = "r_filmtweakbrightness", value = "0.48" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "1 1 1.4" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "1" });
                    break;
                case "4":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "1.8 1.8 2" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1.25" });
                    dvars.Add(new Dvar { key = "r_filmtweakbrightness", value = "0.02" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "0.8 0.8 1" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "1" });
                    break;
                case "5":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "1 1 2" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1.5" });
                    dvars.Add(new Dvar { key = "r_filmtweakbrightness", value = "0.07" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "1 1.2 1" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "1" });
                    break;
                case "6":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "1.5 1.5 2" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweakbrightness", value = "0.0.4" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "1" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "1.5 1.5 1" });
                    dvars.Add(new Dvar { key = "r_filmtweakenable", value = "1" });
                    break;
                case "7":
                    dvars.Add(new Dvar { key = "r_specularMap", value = "2" });
                    dvars.Add(new Dvar { key = "r_normalMap", value = "0" });
                    break;
                case "8":
                    dvars.Add(new Dvar { key = "cg_drawFPS", value = "1" });
                    dvars.Add(new Dvar { key = "cg_fovScale", value = "1.5" });
                    break;
                case "9":
                    dvars.Add(new Dvar { key = "r_debugShader", value = "1" });
                    break;
                case "10":
                    dvars.Add(new Dvar { key = "r_colorMap", value = "3" });
                    break;
                case "11":
                    dvars.Add(new Dvar { key = "com_maxfps", value = "0" });
                    dvars.Add(new Dvar { key = "con_maxfps", value = "0" });
                    break;
                case "default":
                    dvars.Add(new Dvar { key = "r_filmtweakdarktint", value = "0.7 0.85 1" });
                    dvars.Add(new Dvar { key = "r_filmtweakcontrast", value = "1.4" });
                    dvars.Add(new Dvar { key = "r_filmtweakdesaturation", value = "0.2" });
                    dvars.Add(new Dvar { key = "r_filmusetweaks", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmtweaklighttint", value = "1.1 1.05 0.85" });
                    dvars.Add(new Dvar { key = "cg_scoreboardpingtext", value = "1" });
                    dvars.Add(new Dvar { key = "waypointIconHeight", value = "13" });
                    dvars.Add(new Dvar { key = "waypointIconWidth", value = "13" });
                    dvars.Add(new Dvar { key = "cl_maxpackets", value = "100" });
                    dvars.Add(new Dvar { key = "r_fog", value = "0" });
                    dvars.Add(new Dvar { key = "fx_drawclouds", value = "0" });
                    dvars.Add(new Dvar { key = "r_distortion", value = "0" });
                    dvars.Add(new Dvar { key = "r_dlightlimit", value = "0" });
                    dvars.Add(new Dvar { key = "cg_brass", value = "0" });
                    dvars.Add(new Dvar { key = "snaps", value = "30" });
                    dvars.Add(new Dvar { key = "com_maxfps", value = "100" });
                    dvars.Add(new Dvar { key = "clientsideeffects", value = "0" });
                    dvars.Add(new Dvar { key = "r_filmTweakBrightness", value = "0.2" });
                    dvars.Add(new Dvar { key = "cg_fovScale", value = "1" });
                    break;
            }
            try
            {
                if (PersonalPlayerDvars.ContainsKey(sender.GUID))
                {
                    if (ft == "0")
                        PersonalPlayerDvars[sender.GUID] = dvars;
                    else
                    {
                        Dictionary<string, string> _dvars = PersonalPlayerDvars[sender.GUID].ToDictionary(x => x.key, x => x.value);
                        foreach (Dvar dvar in dvars)
                            if (_dvars.ContainsKey(dvar.key))
                                _dvars[dvar.key] = dvar.value;
                            else
                                _dvars.Add(dvar.key, dvar.value);
                        PersonalPlayerDvars[sender.GUID].Clear();
                        foreach (KeyValuePair<string, string> dvar in _dvars)
                            PersonalPlayerDvars[sender.GUID].Add(new Dvar { key = dvar.Key, value = dvar.Value });
                    }
                }
                else
                    PersonalPlayerDvars.Add(sender.GUID, dvars);
                UTILS_SetClientDvars(sender, dvars);
                if ((ft == "0") && PersonalPlayerDvars.ContainsKey(sender.GUID))
                    PersonalPlayerDvars.Remove(sender.GUID);
            }
            catch
            {
                WriteLog.Error("Exception at DGAdmin::CMD_applyfilmtweak");
            }

        }

        public void CMD_spammessagerainbow(string message, int times = 8, int delay = 500)
        {
            List<string> messages = new List<string>();
            string[] colors = Data.Colors.Keys.ToArray();
            for (int i = 0; i < times; i++)
            {
                messages.Add(colors[i % Data.Colors.Keys.Count] + message);
            }
            WriteChatToAllMultiline(messages.ToArray(), delay);
        }

        public unsafe void CMD_JUMP(float height)
        {
            *(float*)new IntPtr(7186184) = (float)height;
        }

        public void CMD_SPEED(Entity player, float speed)
        {
            player.Call("setmovespeedscale", new Parameter[] { speed });
        }

        public unsafe void CMD_GRAVITY(int g)
        {
            *(int*)new IntPtr(4679878) = g;
        }

        public void CMD_AC130(Entity player, bool permanent)
        {
            AfterDelay(500, () => {
                player.TakeAllWeapons();
                player.GiveWeapon("ac130_105mm_mp");
                player.GiveWeapon("ac130_40mm_mp");
                player.GiveWeapon("ac130_25mm_mp");
                player.SwitchToWeaponImmediate("ac130_25mm_mp");
            });

            if (permanent)
                player.SetField("CMD_AC130", new Parameter((int)1));
        }

        #endregion

        #region other useful crap

        public void CMDS_OnDisconnect(Entity player)
        {
            player.setSpying(false);
            player.setMuted(false);
            player.SetField("rekt", 0);
            return;
        }

        public void CMDS_OnPlayerSpawned(Entity player)
        {
            if (UTILS_GetFieldSafe<int>(player, "CMD_AC130") == 1)
                CMD_AC130(player, false);
        }

        public void CMDS_AddToBanList(Entity player, DateTime until)
        {
            BanList.Add
                    (
                    string.Format
                        (
                        "{0};{1};{2}",
                        until.ToString("yyyy MMM d HH:mm"),
                        player.GetInfo().getIdentifiers(),
                        player.Name
                        )
                    );
            CMDS_SaveBanList();
        }

        public DateTime CMDS_GetBanTime(Entity player)
        {
            List<string> linescopy = BanList.Clone().ToList();
            foreach (string line in linescopy)
            {
                string[] parts = line.Split(';');
                string playername = string.Join(";", parts.Skip(2));
                if (parts.Length < 3)
                {
                    continue;
                }
                if (player.GetInfo().MatchesOR(PlayerInfo.Parse(parts[1])) || player.Name == playername)
                {
                    DateTime until = DateTime.ParseExact(parts[0], "yyyy MMM d HH:mm", Culture);
                    if (until < DateTime.Now)
                    {
                        BanList.Remove(line);
                    }
                    return until;
                }
            }
            return DateTime.MinValue;
        }

        public void CMDS_Rek(Entity player)
        {
            CMDS_AddToBanList(player, DateTime.MaxValue);
            CMDS_RekEffects(player);
        }

        public void CMDS_RekEffects(Entity player)
        {
            player.SetField("rekt", 1);
            player.OnInterval(50, ent =>
            {
                ent.SetClientDvar("g_scriptMainMenu", "");
                ent.Call("freezecontrols", true);
                try
                {
                    ent.TakeAllWeapons();
                    ent.Call("disableweapons");
                    ent.Call("disableoffhandweapons");
                    ent.Call("disableweaponswitch");
                    /*
                    ent.Call("closemenu");
                    ent.Call("closepopupmenu");
                    ent.Call("closeingamemenu");
                    ent.Call("clearperks");
                    ent.Call("disableweaponpickup");
                    */
                }
                catch (Exception ex)
                {
                    try
                    {
                        HaxLog.WriteInfo("----STARTREPORT----");
                        HaxLog.WriteInfo("Failed Method call while reking");
                        HaxLog.WriteInfo(ex.Message);
                        HaxLog.WriteInfo(ex.StackTrace);
                    }
                    catch (Exception)
                    { }
                    finally
                    {
                        HaxLog.WriteInfo("----ENDREPORT---");
                    }
                }
                ent.IPrintLnBold("^1YOU'RE REKT");
                ent.IPrintLn("^1YOU'RE REKT");
                Utilities.RawSayTo(ent, "^1YOU'RE REKT");
                ent.SetClientDvar("r_colorMap", "3");
                return true;
            });
        }

        public bool CMDS_IsRekt(Entity player)
        {
            return player.HasField("rekt") && player.GetField<int>("rekt") == 1;
        }

        public void CMDS_OnConnect(Entity player)
        {
            DateTime until = CMDS_GetBanTime(player);
            if (until > DateTime.Now)
            {
                player.AfterDelay(1000, ent =>
                {
                    if (until.Year != 9999)
                    {
                        TimeSpan forhowlong = until - DateTime.Now;
                        ExecuteCommand(string.Format("dropclient {0} \"^1You are banned from this server for ^3{1}d {2}m {3}s\"", player.GetEntityNumber(), forhowlong.Days, forhowlong.Hours, forhowlong.Minutes));
                        return;
                    }
                    else
                    {
                        ExecuteCommand(string.Format("dropclient {0} \"^1You are banned from this server ^3permanently.\"", player.GetEntityNumber()));
                        return;
                    }
                });
            }
            if (!player.HasField("CurrentCommand"))
                player.SetField("CurrentCommand", new Parameter((string)""));
        }

        public void CMDS_OnConnecting(Entity player)
        {
            foreach (string xnaddr in XBanList)
            {
                if (player.GetXNADDR().ToString().Contains(xnaddr))
                {
                    ExecuteCommand(string.Format("dropclient {0} \"^1You are banned from this server ^3permanently.\"", player.GetEntityNumber()));
                    return;
                }
            }
        }

        public void CMDS_OnPlayerKilled(Entity player, Entity inflictor, Entity attacker, int damage, string mod, string weapon, Vector3 dir, string hitLoc)
        {
            if (ConfigValues.settings_enable_spree_messages)
            {
                int attacker_killstreak = attacker.GetField<int>("killstreak") + 1;
                int victim_killstreak = player.GetField<int>("killstreak");

                attacker.SetField("killstreak", attacker_killstreak);
                player.SetField("killstreak", (int)0);

                if (mod == "MOD_HEAD_SHOT")
                    WriteChatToAll(Lang_GetString("Spree_Headshot").Format(new Dictionary<string, string>()
                    {
                        {"<attacker>", attacker.Name},
                        {"<victim>", player.Name}
                    }));

                if (attacker_killstreak == 5)
                    WriteChatToAll(Lang_GetString("Spree_Kills_5").Format(new Dictionary<string, string>()
                    {
                        {"<attacker>", attacker.Name}
                    }));

                if (attacker_killstreak == 10)
                    WriteChatToAll(Lang_GetString("Spree_Kills_10").Format(new Dictionary<string, string>()
                    {
                        {"<attacker>", attacker.Name}
                    }));

                switch (weapon)
                {
                    case "moab":
                    case "briefcase_bomb_mp":
                    case "destructible_car":
                    case "barrel_mp":
                    case "destructible_toy":
                        WriteChatToAll(Lang_GetString("Spree_Explosivekill").Format(new Dictionary<string, string>()
                        {
                            {"<victim>", player.Name}
                        }));
                        break;
                    case "trophy_mp":
                        WriteChatToAll(Lang_GetString("Spree_Trophykill").Format(new Dictionary<string, string>()
                        {
                            {"<attacker>", attacker.Name},
                            {"<victim>", player.Name}
                        }));
                        break;
                    case "knife":
                        WriteChatToAll(Lang_GetString("Spree_KnifeKill").Format(new Dictionary<string, string>()
                        {
                            {"<attacker>", attacker.Name},
                            {"<victim>", player.Name}
                        }));
                        break;
                }

                if (victim_killstreak >= 5)
                    WriteChatToAll(Lang_GetString("Spree_Ended").Format(new Dictionary<string, string>()
                    {
                        {"<attacker>", attacker.Name},
                        {"<victim>", player.Name},
                        {"<killstreak>", victim_killstreak.ToString()}
                    }));
            }
        }

        public string CMDS_CommonIdentifiers(PlayerInfo A, PlayerInfo B)
        {
            List<string> identifiers = new List<string>();
            if (B.isNull() || A.isNull())
                return null;
            if (!string.IsNullOrWhiteSpace(A.GetIPString()))
            {
                if (!string.IsNullOrWhiteSpace(B.GetIPString()) && A.GetIPString() == B.GetIPString())
                    identifiers.Add("^2" + A.GetIPString());
                else
                    identifiers.Add("^1" + A.GetIPString());
            }
            if (!string.IsNullOrWhiteSpace(A.GetGUIDString()))
            {
                if (!string.IsNullOrWhiteSpace(B.GetGUIDString()) && A.GetGUIDString() == B.GetGUIDString())
                    identifiers.Add("^2" + A.GetGUIDString());
                else
                    identifiers.Add("^1" + A.GetGUIDString());
            }
            if (!string.IsNullOrWhiteSpace(A.GetHWIDString()))
            {
                if (!string.IsNullOrWhiteSpace(B.GetHWIDString()) && A.GetHWIDString() == B.GetHWIDString())
                    identifiers.Add("^2" + A.GetHWIDString());
                else
                    identifiers.Add("^1" + A.GetHWIDString());
            }
            return string.Join("^7, ", identifiers.ToArray());
        }

        public void CMDS_ClearTemporaryBanlist()
        {
            List<string> linescopy = BanList.Clone().ToList();
            foreach (string line in linescopy)
            {
                string[] parts = line.Split(';');
                if (parts.Length < 3)
                    continue;
                DateTime until = DateTime.ParseExact(parts[0], "yyyy MMM d HH:mm", Culture);
                if (until < DateTime.Now)
                {
                    BanList.Remove(line);
                }
            }
        }

        public void CMDS_SaveBanList()
        {
            System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\bannedplayers.txt", BanList.ToArray());
        }

        public void CMDS_SaveXBanList()
        {
            System.IO.File.WriteAllLines(ConfigValues.ConfigPath + @"Commands\xbans.txt", XBanList.ToArray());
        }

        public void CMDS_AddToXBanList(Entity player)
        {
            XBanList.Add(player.GetXNADDR().ToString());
            CMDS_SaveXBanList();
        }

        public Command FindCommand(string cmdname)
        {
            foreach (Command cmd in CommandList)
                if (cmd.name == cmdname)
                    return cmd;
            return null;
        }

        #endregion
    }

    static partial class Extensions
    {
        public static IList<T> Clone<T>(this IList<T> listToClone) where T : ICloneable
        {
            return listToClone.Select(item => (T)item.Clone()).ToList();
        }

        public static bool isSpying(this Entity entity)
        {
            return System.IO.File.ReadAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\spyingplayers.txt").ToList().Contains(entity.GetInfo().getIdentifiers());
        }

        public static void setSpying(this Entity entity, bool state)
        {
            List<string> spyingfile = System.IO.File.ReadAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\spyingplayers.txt").ToList();
            string identifiers = entity.GetInfo().getIdentifiers();
            bool isalreadyspying = spyingfile.Contains(identifiers);

            if (isalreadyspying && !state)
            {
                spyingfile.Remove(identifiers);
                System.IO.File.WriteAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\spyingplayers.txt", spyingfile.ToArray());
                return;
            }
            if (!isalreadyspying && state)
            {
                spyingfile.Add(identifiers);
                System.IO.File.WriteAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\spyingplayers.txt", spyingfile.ToArray());
                return;
            }
        }

        public static bool isMuted(this Entity entity)
        {
            return System.IO.File.ReadAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\mutedplayers.txt").ToList().Contains(entity.GetInfo().getIdentifiers());
        }

        public static void setMuted(this Entity entity, bool state)
        {
            List<string> mutedfile = System.IO.File.ReadAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\mutedplayers.txt").ToList();
            string identifiers = entity.GetInfo().getIdentifiers();
            bool isalreadymuted = mutedfile.Contains(identifiers);

            if (isalreadymuted && !state)
            {
                mutedfile.Remove(identifiers);
                System.IO.File.WriteAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\mutedplayers.txt", mutedfile.ToArray());
                return;
            }
            if (!isalreadymuted && state)
            {
                mutedfile.Add(identifiers);
                System.IO.File.WriteAllLines(DGAdmin.ConfigValues.ConfigPath + @"Commands\internal\mutedplayers.txt", mutedfile.ToArray());
                return;
            }
        }
    }
}
