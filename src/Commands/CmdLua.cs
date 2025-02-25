﻿using CCLua.LuaObjects.Suppliers;
using MCGalaxy;
using MCGalaxy.Commands;
using MCGalaxy.Network;
using System.Collections.Generic;
using System.Net;
using System.ComponentModel;
using System;
using CCLua.LuaObjects;
using System.IO;
using System.Security.Policy;

namespace CCLua.Commands
{
    public class CmdLua : Command2
    {
        public const long SCRIPT_MAX_SIZE = 2000000;

        public override string name => "Lua";

        public override string type => CommandTypes.Other;

        public override LevelPermission defaultRank => LevelPermission.Guest;

        public override CommandPerm[] ExtraPerms => new CommandPerm[]
        {
            new CommandPerm(LevelPermission.Operator, "upload scripts to any levels")
        };

        public override CommandAlias[] Aliases => new CommandAlias[]
        {
            new CommandAlias("LUP", "upload"),
            new CommandAlias("Call", "call")
        };

        public override void Use(Player p, string message, CommandData data)
        {
            string[] args = message.SplitSpaces();

            if (args.Length > 0)
            {
                if (args[0] == "upload")
                {
                    if (data.Context == CommandContext.MessageBlock)
                    {
                        p.Message("&cYou cannot run /lua upload in message blocks!");
                        return;
                    }

                    if (p.Rank < ExtraPerms[0].Perm && !LevelInfo.IsRealmOwner(p.level, p.name))
                    {
                        p.Message("&cYou can only upload scripts to maps that you own.");
                        return;
                    }

                    if (args.Length < 2)
                    {
                        p.Message("&cYou need to specify the URL of a .lua script for this map.");
                        return;
                    }

                    string url = args[1];
                    HttpUtil.FilterURL(ref url);

                    string fileName = p.level.name.ToLower() + ".lua";
                    string path = Constants.CCLUA_BASE_DIR + Constants.TEMP_DIR + fileName;

                    if (File.Exists(path))
                    {
                        try
                        {
                            using (var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
                            {
                                fs?.Close();
                            }
                        } catch (IOException ex)
                        {
                            p.Message("&cScript file is not accessible! Is there already a download in progress?");
                            return;
                        }
                    }

                    p.Message("Downloading the script file...");
                    WebClient wc = new WebClient();
                    wc.DownloadFileCompleted += new AsyncCompletedEventHandler((sender, e) => OnDownloadComplete(p, sender, fileName, url, e));
                    wc.DownloadProgressChanged += new DownloadProgressChangedEventHandler((sender, e) => OnDownloadProgress(p, sender, fileName, url, e));
                    wc.DownloadFileAsync(new Uri(url), path);
                }
                else if (args[0] == "call")
                {
                    if (!(data.Context == CommandContext.MessageBlock || LevelInfo.IsRealmOwner(p.name, p.level.name) || p.group.Permission >= LevelPermission.Operator))
                    {
                        p.Message("&cYou can only use &b/lua call&c if it is in a message block or you are the map owner.");
                        return;
                    }

                    if (!LevelHandler.HasLuaContext(p.level))
                    {
                        p.Message("&cThis map doesn't have a lua script!");
                        return;
                    }

                    if (args.Length < 2)
                    {
                        p.Message("&cYou need to specify the function name to call.");
                        return;
                    }

                    LuaContext context = LevelHandler.GetContextByLevel(p.level);

                    int? mbX = null, mbY = null, mbZ = null;
                    if (data.Context == CommandContext.MessageBlock)
                    {
                        mbX = data.MBCoords.X;
                        mbY = data.MBCoords.Y;
                        mbZ = data.MBCoords.Z;
                    }

                    LuaPlayerCallFunctionEventSupplier supplier = new LuaPlayerCallFunctionEventSupplier(new PlayerCallFunctionEvent(p, mbX, mbY, mbZ));

                    if (args.Length >= 3)
                    {
                        List<object> parameters = new List<object>();
                        parameters.Add(supplier);

                        for (int i = 2; i < args.Length; i++)
                        {
                            parameters.Add(args[i]);
                        }

                        context.CallByPlayer(args[1], p, parameters.ToArray());
                    }
                    else
                    {
                        context.CallByPlayer(args[1], p, supplier);
                    }
                } else if (args[0] == "reload")
                {
                    if (p.Rank < ExtraPerms[0].Perm && !LevelInfo.IsRealmOwner(p.level, p.name))
                    {
                        p.Message("&cYou may only perform this command on maps that you own.");
                        return;
                    }

                    p.Message($"Reloading lua scripts for map %b{p.level.name}%S...");

                    string fileName = p.level.name.ToLower() + ".lua";
                    string path = Constants.CCLUA_BASE_DIR + Constants.TEMP_DIR + fileName;

                    if (File.Exists(path))
                    {
                        try
                        {
                            using (var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
                            {
                                fs?.Close();
                            }
                        }
                        catch (IOException ex)
                        {
                            p.Message("&cScript file is not accessible! The script file may still be in downloading.");
                            return;
                        }
                    }

                    LuaContext context = LevelHandler.GetContextByLevel(p.level);
                    context?.Stop();

                    LevelHandler.contexts.Remove(p.level.name);

                    if (LevelHandler.TryCreateLuaContext(p.level))
                    {
                        p.Message("Reload completed!");

                        LuaContext newContext = LevelHandler.GetContextByLevel(p.level);

                        newContext.Call("onLevelLoad");

                        foreach (Player player in p.level.players)
                        {
                            newContext.HandlePlayerJoin(p);
                        }
                    } else
                    {
                        p.Message("&cLua script in this map was not found.");
                    }
                } else
                {
                    Help(p, message);
                }
            } else
            {
                Help(p, message);
            }
        }

        public override void Help(Player p)
        {
            Help(p, string.Empty);
        }

        public override void Help(Player p, string message)
        {
            p.Message("&T/Lua upload [url]");
            p.Message("&HUploads a .lua script file to your map.");
            p.Message("&T/Lua reload");
            p.Message("&HReloads and restarts the lua script of the current map.");
            p.Message("&T/Lua call [function] [args]");
            p.Message("&HCalls a lua function with optional arguments.");
        }

        private void OnDownloadComplete(Player p, object sender, string fileName, string url, AsyncCompletedEventArgs e)
        {
            WebClient wc = (WebClient)sender;

            if (e.Error != null)
            {
                p.Message("&cError while downloading the script file! ({0})", e.Error.Message);
            } else
            {
                p.Message("Done! Uploaded %b" + fileName + " %Sfrom URL " + url);
                p.Message("Type %a/lua reload%S to load the script.");
            }

            wc.Dispose();

            string oldPath = Constants.CCLUA_BASE_DIR + Constants.TEMP_DIR + fileName;
            string newPath = Constants.CCLUA_BASE_DIR + Constants.SCRIPT_DIR + fileName;

            if (File.Exists(oldPath))
            {
                if (File.Exists(newPath))
                {
                    File.Delete(newPath);
                }
                File.Move(oldPath, newPath);
            }
        }

        private void OnDownloadProgress(Player p, object sender, string fileName, string url, DownloadProgressChangedEventArgs e)
        {
            WebClient wc = (WebClient)sender;

            if (e.TotalBytesToReceive > SCRIPT_MAX_SIZE)
            {
                p.Message("&cScript file is too large! Aborting download.");
                wc.CancelAsync();
                wc.Dispose();

                string path = Constants.CCLUA_BASE_DIR + Constants.TEMP_DIR + fileName;

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
