using UnityEngine;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Oxide.Core;

namespace Oxide.Plugins
{

    [Info("Sign Artist", "Bombardir", "0.2.0", ResourceId = 992)]
    class SignArtist : RustPlugin
    {
        static GameObject WebObject;
        static UnityWeb UWeb;
        static MethodInfo getFileData = typeof(FileStorage).GetMethod("StorageGet", (BindingFlags.Instance | BindingFlags.NonPublic));
        static Dictionary<BasePlayer, float> CoolDowns = new Dictionary<BasePlayer, float>();

        #region Unity WWW

        struct QueueItem
        {
            public string url;
            public Signage sign;
            public BasePlayer sender;
            public QueueItem(string ur, BasePlayer se, Signage si)
            {
                url = ur;
                sender = se;
                sign = si;
            }
        }

        class UnityWeb : MonoBehaviour
        {
            internal static bool ConsoleLog = true;
            internal static string ConsoleLogMsg = "[Sign Artist] Player[{steam} {name}] loaded {id} image from {url}!";
            internal static int MaxActiveLoads = 3;
            static List<QueueItem> QueueList = new List<QueueItem>();
            static byte ActiveLoads = 0;

            public void Add(string url, BasePlayer player, Signage s)
            {
                QueueList.Add(new QueueItem(url, player, s));
                if (ActiveLoads < MaxActiveLoads)
                    Next();
            }

            void Next()
            {
                ActiveLoads++;
                QueueItem qi = QueueList[0];
                QueueList.RemoveAt(0);
                WWW www = new WWW(qi.url);
                StartCoroutine(WaitForRequest(www, qi)); 
            }

            IEnumerator WaitForRequest(WWW www, QueueItem info)
            {
                yield return www;
                BasePlayer player = info.sender;
                if (www.error == null)
                {
                    if (www.size <= MaxSize)
                    {
                        Signage sign = info.sign;
                        if (sign.textureID > 0U)
                            FileStorage.server.Remove(sign.textureID, FileStorage.Type.png);
                        sign.textureID = FileStorage.server.Store(www.bytes, FileStorage.Type.png);
                        sign.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                        player.ChatMessage(Loaded);
                        if (ConsoleLog)
                            ServerConsole.PrintColoured(System.ConsoleColor.DarkYellow, ConsoleLogMsg
                                .Replace("{steam}", player.userID.ToString())
                                .Replace("{name}", player.displayName)
                                .Replace("{id}", sign.textureID.ToString())
                                .Replace("{url}", info.url));
                    }
                    else
                    {
                        player.ChatMessage(SizeError);
                        CoolDowns.Remove(player);
                    }
                }
                else
                {
                    player.ChatMessage(Error.Replace("{error}", www.error));
                    CoolDowns.Remove(player);
                }
                ActiveLoads--;
                if (QueueList.Count > 0)
                    Next();
            }
        }

        #endregion 

        #region Chat Commands

        [ChatCommand("sil")]
        void sil(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0)
            {
                float cd;
                if (CoolDowns.TryGetValue(player, out cd) && cd > Time.realtimeSinceStartup && !HasPerm(player, "sil_cd"))
                {
                    player.ChatMessage(CooldownMsg.Replace( "{time}",  ToReadableString(cd - Time.realtimeSinceStartup) ));
                    return;
                }
                
                RaycastHit hit;
                if (Physics.Raycast(player.eyes.Ray(), out hit, MaxDist))
                {
                    Signage sign = hit.transform.GetComponentInParent<Signage>();
                    if (sign != null)
                    {
                        if (player.CanBuild() || HasPerm(player, "sil_owner"))
                        {
                            if (args.Length > 1 && args[0] == "s")
                            {
                                if (HasPerm(player, "sil_storage"))
                                {
                                    uint result;
                                    if (UInt32.TryParse(args[1], out result))
                                    {
                                        if (FileStorage.server.Exists(result, FileStorage.Type.png))
                                        {
                                            if (sign.textureID > 0U)
                                                FileStorage.server.Remove(sign.textureID, FileStorage.Type.png);
                                            sign.textureID = FileStorage.server.Store((byte[])getFileData.Invoke(FileStorage.server, new object[] { result, FileStorage.Type.png }), FileStorage.Type.png);
                                            sign.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                                            player.ChatMessage(Loaded);
                                            CoolDowns[player] = Time.realtimeSinceStartup + StorageCooldown;
                                        }
                                        else
                                            player.ChatMessage(NotExists);
                                    }
                                    else
                                        player.ChatMessage(Syntax);
                                }
                                else
                                    player.ChatMessage(NoPerm);
                            }
                            else if (HasPerm(player, "sil_url"))
                            {
                                UWeb.Add(args[0], player, sign);
                                player.ChatMessage(AddedToQueue);
                                CoolDowns[player] = Time.realtimeSinceStartup + UrlCooldown;
                            }
                            else
                                player.ChatMessage(NoPerm);
                        }
                        else
                            player.ChatMessage(NotYourSign);
                        return;
                    }
                }
                player.ChatMessage(NoSignFound);
            }
            else
                player.ChatMessage(Syntax);
        }

        #endregion

        #region Config | Init | Unload

        static float MaxDist = 2f;
        static float StorageCooldown = 30f;
        static float UrlCooldown = 30f;
        static uint MaxSize = 2048U;
        static string NoPerm = "You don't have permission to use this command!";
        static string Syntax = "Syntax: /sil <URL> | /sil s <number>";
        static string NoSignFound = "You need to look/get closer to a sign!"; 
        static string NotYourSign = "You can't change this sign! (protected by tool cupboard)";
        static string CooldownMsg = "You have recently used this command! You need to wait: {time}";
        static string AddedToQueue = "Your picture was added to load queue!";
        static string Loaded = "Image was loaded to Sign!";
        static string Error = "Image loading fail! Error: {error}";
        static string NotExists = "File with this name not exists in storage folder!";
        static string SizeError = "This file is too large. Max size: {size}KB";

        void LoadDefaultConfig() { }

        void OnServerInitialized()
        {
            permission.RegisterPermission("sil_url", this);
            permission.RegisterPermission("sil_storage", this);
            permission.RegisterPermission("sil_owner", this);
            permission.RegisterPermission("sil_cd", this);

            CheckCfg<bool>("Log url console", ref UnityWeb.ConsoleLog);
            CheckCfg<string>("Log format", ref UnityWeb.ConsoleLogMsg);
            CheckCfg<int>("Max active uploads", ref UnityWeb.MaxActiveLoads);
            CheckCfg<float>("Max sign detection distance", ref MaxDist);
            CheckCfg<uint>("Max file size(KB)", ref MaxSize);
            CheckCfg<float>("Command cooldown after storage", ref StorageCooldown);
            CheckCfg<float>("Command cooldown after url", ref UrlCooldown);
            CheckCfg<string>("Command cooldown msg", ref CooldownMsg);
            CheckCfg<string>("NoPermission", ref NoPerm);
            CheckCfg<string>("Syntax", ref Syntax);
            CheckCfg<string>("No sign", ref NoSignFound);
            CheckCfg<string>("Not your sign", ref NotYourSign);
            CheckCfg<string>("Added to queue", ref AddedToQueue);
            CheckCfg<string>("Loaded", ref Loaded);
            CheckCfg<string>("Not Exists", ref NotExists);
            CheckCfg<string>("Error", ref Error);
            SaveConfig();

            SizeError = SizeError.Replace("{size}", MaxSize.ToString());

            MaxSize *= 1024;

            WebObject = new GameObject("WebObject");
            UWeb = WebObject.AddComponent<UnityWeb>();
        }

        void Unload()
        {
            GameObject.Destroy(WebObject);
        }

        #endregion

        #region Util methods

        void CheckCfg<T>(string Key, ref T var)
        {
            if (Config[Key] == null)
                Config[Key] = var;
            else
                try { var = (T)Convert.ChangeType(Config[Key], typeof(T)); }
                catch { Config[Key] = var; }
        }

        bool HasPerm(BasePlayer p, string pe) => permission.UserHasPermission(p.userID.ToString(), pe);

        static string ToReadableString(float seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(seconds).Duration();
            string formatted = string.Format("{0}{1}{2}{3}",
                span.Days > 0 ? string.Format("{0:0} day{1}, ", span.Days, span.Days == 1 ? String.Empty : "s") : string.Empty,
                span.Hours > 0 ? string.Format("{0:0} hour{1}, ", span.Hours, span.Hours == 1 ? String.Empty : "s") : string.Empty,
                span.Minutes > 0 ? string.Format("{0:0} minute{1}, ", span.Minutes, span.Minutes == 1 ? String.Empty : "s") : string.Empty,
                span.Seconds > 0 ? string.Format("{0:0} second{1}", span.Seconds, span.Seconds == 1 ? String.Empty : "s") : string.Empty);

            if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

            if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

            return formatted;
        }

        #endregion
    }
}