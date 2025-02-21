using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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

public partial class ModVersionChecker : ModSystem
{
    internal class ModVersionConfig
    {
        private static ModVersionConfig? _instance;

        public static ModVersionConfig Instance =>
            _instance ??= ServerProgram.server?.api.LoadModConfig<ModVersionConfig>(Filename) 
                          ?? new ModVersionConfig();

        public static void SaveInstance() =>
            ServerProgram.server?.api.StoreModConfig(Instance, Filename);

        public string CheckWebhookUrl { get; set; } = "";
        public string ListWebhookUrl { get; set; } = "";
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
            .WithDescription("Runs the Mod Version Discord Reporter using the given, or last given, webhook url")
            .RequiresPrivilege(Privilege.gamemode)
            .WithArgs(new StringArgParser("webhookUrl", false))
            .HandleWith((args) =>
            {
                var webhookUrl = args.Parsers[0].IsMissing ? "" : (args.Parsers[0].GetValue() as string)!;
                var webhook = string.IsNullOrWhiteSpace(webhookUrl) ? null : new DiscordWebhook()
                {
                    Uri = new Uri(!args.Parsers[0].IsMissing ? webhookUrl! : ModVersionConfig.Instance.CheckWebhookUrl)
                };
                if (!string.IsNullOrWhiteSpace(webhookUrl))
                {
                    ModVersionConfig.Instance.CheckWebhookUrl = webhookUrl;
                    ModVersionConfig.SaveInstance();
                }
                
                if (webhook == null) 
                    return TextCommandResult.Error("No webhook provided, error when creating, or no mod version config webhook found!");
                _ = CheckModsForUpdates(api, webhook);
                return TextCommandResult.Success("Ran the update checker!");
            });

        api.ChatCommands.Create("list-mods")
            .WithDescription("Lists the mods using the given, or last given, webhook url.")
            .RequiresPrivilege(Privilege.gamemode)
            .WithArgs(new StringArgParser("webhookUrl", false))
            .HandleWith((args) =>
            {
                var webhookUrl = args.Parsers[0].IsMissing ? "" : (args.Parsers[0].GetValue() as string)!;
                
                var webhook = string.IsNullOrWhiteSpace(webhookUrl) ? null : new DiscordWebhook()
                {
                    Uri = new Uri(!args.Parsers[0].IsMissing ? webhookUrl : ModVersionConfig.Instance.ListWebhookUrl)
                };
                if (!string.IsNullOrWhiteSpace(webhookUrl))
                {
                    ModVersionConfig.Instance.ListWebhookUrl = webhookUrl;
                    ModVersionConfig.SaveInstance();
                }
                
                if (webhook == null) 
                    return TextCommandResult.Error("No webhook provided, error when creating, or no mod version config webhook found!");
                _ = LogModsTo(webhook);
                return TextCommandResult.Success("Listed mods to webhook!");
            });

#if DEBUG
        if (string.IsNullOrWhiteSpace(ModVersionConfig.Instance.CheckWebhookUrl)) return;
        Task.Run(async () => await CheckModsForUpdates(api, new DiscordWebhook()
        {
            Uri = new Uri(ModVersionConfig.Instance.CheckWebhookUrl)
        })).Wait();
#endif
    }

    async Task LogModsTo(DiscordWebhook webhook)
    {
        var embeds = new List<DiscordEmbed>();

        foreach (var (modLoaderMod, modEntry) in await GetAllModsFromDb())
        {
            var modLink = modEntry.UrlAlias != null
                ? $"https://mods.vintagestory.at/{modEntry.UrlAlias}"
                : $"https://mods.vintagestory.at/show/mod/{modEntry.AssetId}";
            embeds.Add(new DiscordEmbed
            {
                Color = new DiscordColor(Color.DeepSkyBlue),
                Title = modEntry.Name,
                Url = new Uri(modLink),
                Description = modLoaderMod.Info.Description,
                Author = new EmbedAuthor { Name = modEntry.Author },
                Thumbnail = !string.IsNullOrWhiteSpace(modEntry.LogoFile) ? new EmbedMedia { Url = new Uri(modEntry.LogoFile) } : null,
                Footer = new EmbedFooter
                {
                    Text = $"Downloads: {modEntry.Downloads} | Follows: {modEntry.Follows}"
                }
            });
            if (!string.IsNullOrWhiteSpace(modEntry.HomepageUrl))
                embeds.Last().Fields.Add(new EmbedField {Inline = true, Name = " ", Value = $"[Homepage]({modEntry.HomepageUrl})"});
            if (!string.IsNullOrWhiteSpace(modEntry.SourcecodeUrl))
                embeds.Last().Fields.Add(new EmbedField {Inline = true, Name = " ", Value = $"[Source]({modEntry.SourcecodeUrl})"});
            if (!string.IsNullOrWhiteSpace(modEntry.IssueTrackerUrl))
                embeds.Last().Fields.Add(new EmbedField {Inline = true, Name = " ", Value = $"[Issues]({modEntry.IssueTrackerUrl})"});
            if (!string.IsNullOrWhiteSpace(modEntry.WikiUrl))
                embeds.Last().Fields.Add(new EmbedField {Inline = true, Name = " ", Value = $"[Wiki]({modEntry.WikiUrl})"});
            embeds.Last().Fields.Add(new EmbedField {Inline = false, Name = " ", Value = $"Last Modified <t:{modEntry.CorrectedLastModifiedDate.ToUnixTimeSeconds()}:f>"});
            var versionFileId = modEntry.Releases?
                .FirstOrDefault(rel => rel.ModVersion == modLoaderMod.Info.Version)?.FileId;
            var versionStr = $"v{modLoaderMod.Info.Version}";
            embeds.Last().Fields.Add(new EmbedField {Inline = false, Name = " ", 
                Value = $"Installed Version " + (versionFileId != null ? $"[{versionStr}](https://mods.vintagestory.at/download?fileid={versionFileId})" : versionStr)});

            await TrySend();
        }
        await TrySend(false);

        return;

        async Task TrySend(bool checkCount = true)
        {
            try
            {
                if (checkCount && embeds.Count < 10) return;
                var msg = new DiscordMessage();
                msg.Embeds.AddRange(embeds);
                await webhook.SendAsync(msg);
                embeds.Clear();
            }
            catch (Exception ex)
            {
                embeds.Clear();
                Console.WriteLine(ex.Message);
            }
        }
    }

    private List<(Mod, ExtendedModDbEntry)> cachedModDbReturns = new();
    async Task<List<(Mod, ExtendedModDbEntry)>> GetAllModsFromDb()
    {
        if (cachedModDbReturns.Count != 0) return cachedModDbReturns;
        
        List<Task> tasks = new();
        const int maxTasksAtOnce = 4;
        var mods = ServerProgram.server.api.ModLoader.Mods.ToList();

        for (var index = 0; index < mods.Count; index++)
        {
            var modLoaderMod = mods[index];
            if (modLoaderMod.Info.ModID is "game" or "survival" or "creative") continue;

            tasks.Add(Task.Run(async () => await getModFromAPI(modLoaderMod)));
            if (tasks.Count >= maxTasksAtOnce || index == mods.Count - 1)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }
        return cachedModDbReturns;

        async Task getModFromAPI(Mod modLoaderMod)
        {
            var response = await MessageClient.PostAsync($"{ServerConfig.ModDbUrl.TrimEnd('/')}/api/mod/{modLoaderMod.Info.ModID}", null);
            if (!response.IsSuccessStatusCode) return;

            var responseString = await response.Content.ReadAsStringAsync();
            ServerProgram.server.api.Logger.Debug($"Response to request for modid '{modLoaderMod.Info.ModID}': {response.StatusCode}");

            ExtendedModDbEntryResponse? modEntry = null;
            try
            {
                modEntry = JsonConvert.DeserializeObject<ExtendedModDbEntryResponse>(responseString);
            }
            catch (Exception ex)
            {
                /* Ignored */
                ServerProgram.server.api.Logger.Debug(ex.ToString());
            }

            if (modEntry?.Mod == null) return;

            cachedModDbReturns.Add((modLoaderMod, modEntry.Mod));
        }
    }
    
    async Task CheckModsForUpdates(ICoreAPI Api, DiscordWebhook webhook)
    {
#if DEBUG
        try
        {
#endif
            if (Api is not ServerCoreAPI serverCoreAPI) return;

            Api.Logger.Debug("Beginning search for mods that might need updates!");

            var needsUpdates = new List<(Mod modLoaderMod, ExtendedModDbEntry modEntry)>();
            var updatedMods = new List<Mod>();

            var timeStart = DateTimeOffset.Now;

            var allModsInApi = await GetAllModsFromDb();
            var notInDb = Api.ModLoader.Mods
                .Select(mod => $"{mod.Info.ModID}@{mod.Info.Version}").ToList();
            
            foreach (var (mod, dbEntry) in allModsInApi)
            {
                notInDb.Remove($"{mod.Info.ModID}@{mod.Info.Version}");
                if (mod.Info.Version == dbEntry.Releases!
                        .OrderByDescending(rel => rel.CorrectedCreatedDate).First().ModVersion)
                {
                    updatedMods.Add(mod);
                    continue;
                }
                needsUpdates.Add((mod, dbEntry));
            }

            

            var duration = DateTimeOffset.Now - timeStart;
            var durationString = $"Checked {Api.ModLoader.Mods.Count() - 3 /*Offset for game, survival, & creative*/} mod(s) in {duration.ToReadableString()}.";

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
                                release.ModVersion == latestVer.ModVersion ?
                                    ":small_blue_diamond:" :
                                    ":black_small_square:")}\t [{release.ModVersion}](https://mods.vintagestory.at/download?fileid={release.FileId}) " +
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

    [GeneratedRegex(@"(\[!\[\]\([\w\d]*\)\])(?:\([\w\d]*\))")]
    private static partial Regex MyRegex();
}