using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CSharpDiscordWebhook.NET.Discord;
using HarmonyLib;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.ModDb;
using Vintagestory.Server;

namespace DiscordUpdateReporter;

public class ModVersionChecker : ModSystem
{
    internal class ModVersionConfig
    {
        private static ModVersionConfig? _instance;

        public static ModVersionConfig Instance =>
            _instance ??= ServerProgram.server?.api.LoadModConfig<ModVersionConfig>(Filename) 
                          ?? new ModVersionConfig();

        public static void SaveInstance() =>
            ServerProgram.server?.api.StoreModConfig(Instance, Filename);

        public string WebhookUrl { get; set; } = "";
        // public bool AutoDownload { get; set; } = false;
        // public bool AutoInstall { get; set; } = false;
        // public bool StopAfterInstall { get; set; } = true;
        // public bool RemoveOldInstalls { get; set; } = true;
        // public string[] LockedVersions { get; set; } = Array.Empty<string>(); 

        [JsonIgnore] private DiscordWebhook? _webhook;

        [JsonIgnore]
        internal DiscordWebhook? Webhook =>
            _webhook ??= string.IsNullOrWhiteSpace(WebhookUrl) ? null : new DiscordWebhook { Uri = new Uri(WebhookUrl) };
    }
    
    private const string Filename = "DiscordUpdateReporter.cfg.json";

    private CancellationTokenSource DisposalToken { get; } = new();
    private HttpClient MessageClient { get; } = new();
    private ServerConfig ServerConfig => 
        (ServerConfig)AccessTools.Field(typeof(ServerMain), "Config").GetValue(ServerProgram.server)!;

    public override void StartPre(ICoreAPI api)
    {
        ModVersionConfig.SaveInstance();
        
        MessageClient.DefaultRequestHeaders.Add("Accept", "application/json");

        api.ChatCommands.Create("check-mods")
            .WithDescription("Runs the Mod Version Discord Reporter")
            .RequiresPrivilege(Privilege.gamemode)
            .HandleWith((args) =>
            {
                _ = CheckModsForUpdates(api);
                return TextCommandResult.Success("Ran the update checker!");
            });

#if DEBUG
        Task.Run(async () => await CheckModsForUpdates(api)).Wait();
#endif
    }
    
    async Task CheckForModUpdate(List<(Mod modLoaderMod, ExtendedModDbEntry modEntry)> needsUpdates, List<string> notInDb, Mod? modLoaderMod)
    {
        
        var modLoaderIdAndVer = modLoaderMod!.Info.ModID + '@' + modLoaderMod.Info.Version;

        var response = await MessageClient.PostAsync($"{ServerConfig.ModDbUrl.TrimEnd('/')}/api/mod/{modLoaderMod.Info.ModID}", null);
        if (!response.IsSuccessStatusCode)
        {
            notInDb.Add(modLoaderIdAndVer);
            return;
        }

        var responseString = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response to request for modid '{modLoaderMod.Info.ModID}': {response.StatusCode}");
        

        ExtendedModDbEntryResponse? modEntry = null;
        try
        {
            modEntry = JsonConvert.DeserializeObject<ExtendedModDbEntryResponse>(responseString);
        } catch { /* Ignored */ }
                
        if (modEntry?.Mod == null)
        {
            notInDb.Add(modLoaderIdAndVer);
            return;
        }

        if (modLoaderMod.Info.Version != modEntry.Mod!.Releases!.First().ModVersion)
        {
            needsUpdates.Add((modLoaderMod, modEntry.Mod));
            return;
        }
    }

    async Task CheckModsForUpdates(ICoreAPI Api)
    {
#if DEBUG
        try
        {
#endif
            if (Api is not ServerCoreAPI serverCoreAPI) return;

            Api.Logger.Debug("Beginning search for mods that might need updates!");

            var needsUpdates = new List<(Mod modLoaderMod, ExtendedModDbEntry modEntry)>();
            var notInDb = new List<string>();

            var webhook = ModVersionConfig.Instance.Webhook;
            if (webhook == null) return;

            var timeStart = DateTimeOffset.Now;

            List<Task> tasks = new();
            var maxTasksAtOnce = 5;
            var mods = Api.ModLoader.Mods.ToList();

            for (var index = 0; index < mods.Count; index++)
            {
                var modLoaderMod = mods[index];
                tasks.Add(CheckForModUpdate(needsUpdates, notInDb, modLoaderMod));
                if (tasks.Count >= maxTasksAtOnce || index == mods.Count - 1)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }

            var duration = DateTimeOffset.Now - timeStart;
            var durationString = $"Checked {Api.ModLoader.Mods.Count() - 3 /*Offset for game, survival, & creative*/} mod(s) in {duration.ToReadableString()}.";


            var updatedMods = Api.ModLoader.Mods.Where(mod => !notInDb.Contains($"{mod.Info.ModID}@{mod.Info.Version}") &&
                                                              !needsUpdates.Any(kv =>
                                                                  kv.modLoaderMod.Info.ModID == mod.Info.ModID &&
                                                                  kv.modLoaderMod.Info.Version == mod.Info.Version)).ToList();
            var added = 0;
            var updatedModsStr = "`";
            foreach (var upToDateMod in updatedMods)
            {
                updatedModsStr += $"{upToDateMod.Info.ModID}@{upToDateMod.Info.Version}\n";
                added++;
                if (added < 3 || updatedMods.Count <= 3) continue;
                updatedModsStr += $"and {updatedMods.Count - 3} more...";
                break;
            }

            updatedModsStr += "`";
            notInDb.Remove(notInDb.First(f => f.StartsWith("game")));
            notInDb.Remove(notInDb.First(f => f.StartsWith("survival")));
            notInDb.Remove(notInDb.First(f => f.StartsWith("creative")));

            var firstEmbed = new DiscordEmbed()
            {
                Color = new DiscordColor(Color.DeepSkyBlue),
                Title = ":blue_square: Mod Update Report",
                Timestamp = new DiscordTimestamp(DateTime.UtcNow),
                Footer = new EmbedFooter
                {
                    Text = durationString
                },
                Fields =
                {
                    new EmbedField
                    {
                        Name = $"Up-to-date Mods ({Api.ModLoader.Mods.Count() - notInDb.Count - needsUpdates.Count - 3 /*Offset for game, survival, & creative*/})",
                        Value = updatedModsStr
                    },
                    new EmbedField
                    {
                        Name = $"Off-DB Mods ({notInDb.Count})",
                        Value = $"`{string.Join("\n", notInDb)}`"
                    },
                    new EmbedField
                    {
                        Name = "Version Legend",
                        Value =
                            ":small_blue_diamond: Latest Version\n:black_small_square: Intermediary Version\n:small_red_triangle_down: Installed Version"
                    },
                    new EmbedField
                    {
                        Name = $":arrow_down: Mods w/ Updates ({needsUpdates.Count}) :arrow_down:",
                        Value = ""
                    }
                }
            };

            var discordEmbeds = new List<DiscordEmbed>();
            await webhook.SendAsync(new DiscordMessage { Embeds = { firstEmbed } });
            discordEmbeds.Clear();

            for (var index = 0; index < needsUpdates.Count; index++)
            {
                var (modLoaderMod, modDbEntryResponse) = needsUpdates[index];
                var oldVer = modDbEntryResponse.Releases!.FirstOrDefault(release => release.ModVersion == modLoaderMod.Info.Version);
                var latestVer = modDbEntryResponse.Releases!.OrderByDescending(release => release.Created).First();
                var modName = modDbEntryResponse.Name;
                var modLink = modDbEntryResponse.UrlAlias != null
                    ? $"https://mods.vintagestory.at/{modDbEntryResponse.UrlAlias}"
                    : $"https://mods.vintagestory.at/show/mod/{modDbEntryResponse.AssetId}";

                discordEmbeds.Add(new DiscordEmbed
                {
                    Color = new DiscordColor(Color.DeepSkyBlue),
                    Title = $"{modName}",
                    Url = new Uri(modLink),
                    Description = modDbEntryResponse.Releases!
                        .OrderByDescending(release => release.Created)
                        .TakeWhile(release => release.CorrectedCreatedDate >= (oldVer?.CorrectedCreatedDate ?? release.CorrectedCreatedDate))
                        .Aggregate("", (desc, release) =>
                            desc + $"\t{(release.ModVersion == modLoaderMod.Info.Version ?
                                ":small_red_triangle_down:" :
                                (release.ModVersion == latestVer.ModVersion ?
                                    ":small_blue_diamond:" :
                                    ":black_small_square:"))}\t [{release.ModVersion}](https://mods.vintagestory.at/download?fileid={release.FileId}) " +
                            $"<t:{release.CorrectedCreatedDate.ToUnixTimeSeconds()}:R> " +
                            $"`{(release.Tags?.Length > 1 ? release.Tags[^1] + " ... " : "")}{(release.Tags?.Length > 0 ? release.Tags[0] : "")}`\n"),
                    Footer = new EmbedFooter
                    {
                        Text = oldVer == null
                            ? ""
                            : $"Out of date by {(latestVer.CorrectedCreatedDate - oldVer.CorrectedCreatedDate)
                                .ToReadableString(minutes: false, seconds: false)}!"
                    },
                    Thumbnail = modDbEntryResponse.LogoFile == null ? null : new EmbedMedia { Url = new Uri(modDbEntryResponse.LogoFile) }
                });

                if (discordEmbeds.Count >= 9 || index == needsUpdates.Count - 1)
                {
                    var message = new DiscordMessage();
                    message.Embeds.AddRange(discordEmbeds);
                    await webhook.SendAsync(message);
                    discordEmbeds.Clear();
                }
            }
#if DEBUG
        } catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
#endif
    }

    public override void Dispose()
    {
        DisposalToken.Cancel();
    }
}