﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using PsnClient.POCOs;
using PsnClient.Utils;

namespace CompatBot.ThumbScrapper
{
    internal class PsnScraper
    {
        private static readonly PsnClient.Client Client = new PsnClient.Client();
        public static readonly Regex ContentIdMatcher = new Regex(@"(?<content_id>(?<service_id>(?<service_letters>\w\w)(?<service_number>\d{4}))-(?<product_id>(?<product_letters>\w{4})(?<product_number>\d{5}))_(?<part>\d\d)-(?<label>\w{16}))", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture);
        private static readonly SemaphoreSlim LockObj = new SemaphoreSlim(1, 1);
        private static List<string> PsnStores = new List<string>();
        private static DateTime StoreRefreshTimestamp = DateTime.MinValue;
        private static readonly SemaphoreSlim QueueLimiter = new SemaphoreSlim(32, 32);

        public async Task Run(CancellationToken cancellationToken)
        {
            do
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ScrapeStateProvider.CleanAsync(cancellationToken).ConfigureAwait(false);
                await RefreshStoresAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await DoScrapePassAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    PrintError(e);
                }
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
            } while (!cancellationToken.IsCancellationRequested);
        }

        public static async void CheckContentId(string contentId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(contentId))
                return;

            var match = ContentIdMatcher.Match(contentId);
            if (!match.Success)
                return;

            if (!QueueLimiter.Wait(0))
                return;

            try
            {
                List<string> storesToScrape;
                contentId = match.Groups["content_id"].Value;
                await LockObj.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    storesToScrape = new List<string>(PsnStores);
                }
                finally
                {
                    LockObj.Release();
                }

                foreach (var locale in storesToScrape)
                {
                    var relatedContainer = await Client.ResolveContentAsync(locale, contentId, 1, cancellationToken).ConfigureAwait(false);
                    if (relatedContainer == null)
                        continue;

                    Console.WriteLine($"\tFound {contentId} in {locale} store");
                    await ProcessIncludedGamesAsync(locale, relatedContainer, cancellationToken, false).ConfigureAwait(false);
                    return;
                }
                Console.WriteLine($"\tDidn't find {contentId} in any PSN store");
            }
            finally
            {
                QueueLimiter.Release();
            }
        }

        private static async Task RefreshStoresAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (ScrapeStateProvider.IsFresh(StoreRefreshTimestamp))
                    return;

                var knownLocales = await Client.GetLocales(cancellationToken).ConfigureAwait(false);
                var enabledLocales = knownLocales.EnabledLocales ?? new string[0];
                var result = GetLocalesInPreferredOrder(enabledLocales);
                await LockObj.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (ScrapeStateProvider.IsFresh(StoreRefreshTimestamp))
                        return;

                    PsnStores = result;
                    StoreRefreshTimestamp = DateTime.UtcNow;
                }
                finally
                {
                    LockObj.Release();
                }
            }
            catch (Exception e)
            {
                PrintError(e);
            }
        }

        private static async Task DoScrapePassAsync(CancellationToken cancellationToken)
        {
            List<string> storesToScrape;
            await LockObj.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                storesToScrape = new List<string>(PsnStores);
            }
            finally
            {
                LockObj.Release();
            }

            var percentPerStore = 1.0 / storesToScrape.Count;
            for (var storeIdx = 0; storeIdx < storesToScrape.Count; storeIdx++)
            {
                var locale = storesToScrape[storeIdx];
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (ScrapeStateProvider.IsFresh(locale))
                {
                    //Console.WriteLine($"Cache for {locale} PSN is fresh, skipping");
                    continue;
                }

                Console.WriteLine($"Scraping {locale} PSN for PS3 games...");
                var knownContainers = new HashSet<string>();
                // get containers from the left side navigation panel on the main page
                var containerIds = await Client.GetMainPageNavigationContainerIdsAsync(locale, cancellationToken).ConfigureAwait(false);
                // get all containers from all the menus
                var stores = await Client.GetStoresAsync(locale, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(stores?.Data.BaseUrl))
                    containerIds.Add(Path.GetFileName(stores.Data.BaseUrl));
                foreach (var id in containerIds)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await ScrapeContainerIdsAsync(locale, id, knownContainers, cancellationToken).ConfigureAwait(false);
                }
                Console.WriteLine($"\tFound {knownContainers.Count} containers");

                // now let's scrape for actual games in every container
                var defaultFilters = new Dictionary<string, string>
                {
                    ["platform"] = "ps3",
                    ["game_content_type"] = "games",
                };
                var take = 30;
                var returned = 0;
                var containersToScrape = knownContainers.ToList(); //.Where(c => c.Contains("FULL", StringComparison.InvariantCultureIgnoreCase)).ToList();
                var percentPerContainer = 1.0 / containersToScrape.Count;
                for (var containerIdx = 0; containerIdx < containersToScrape.Count; containerIdx++)
                {
                    var containerId = containersToScrape[containerIdx];
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (ScrapeStateProvider.IsFresh(locale, containerId))
                    {
                        //Console.WriteLine($"\tCache for {locale} container {containerId} is fresh, skipping");
                        continue;
                    }

                    var currentPercent = storeIdx * percentPerStore + containerIdx * percentPerStore * percentPerContainer;
                    Console.WriteLine($"\tScraping {locale} container {containerId} ({currentPercent*100:##0.00}%)...");
                    var total = -1;
                    var start = 0;
                    do
                    {
                        var tries = 0;
                        Container container = null;
                        bool error = false;
                        do
                        {
                            try
                            {
                                container = await Client.GetGameContainerAsync(locale, containerId, start, take, defaultFilters, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                PrintError(e);
                                error = true;
                            }
                            tries++;
                        } while (error && tries < 3 && !cancellationToken.IsCancellationRequested);
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        if (container != null)
                        {
                            // this might've changed between the pages for some stupid reason
                            total = container.Data.Attributes.TotalResults;
                            var pages = (int)Math.Ceiling((double)total / take);
                            if (pages > 1)
                                Console.WriteLine($"\t\tPage {start / take + 1} of {pages}");
                            returned = container.Data?.Relationships?.Children?.Data?.Count(i => i.Type == "game" || i.Type == "legacy-sku") ?? 0;
                            // included contains full data already, so it's wise to get it first
                            await ProcessIncludedGamesAsync(locale, container, cancellationToken).ConfigureAwait(false);

                            // returned items are just ids that need to be resolved
                            if (returned > 0)
                            {
                                foreach (var item in container.Data.Relationships.Children.Data)
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                        return;

                                    if (item.Type == "game")
                                    {
                                        if (!NeedLookup(item.Id))
                                            continue;
                                    }
                                    else if (item.Type != "legacy-sku")
                                        continue;

                                    //need depth=1 in case it's a crossplay title, so ps3 id will be in entitlements instead
                                    container = await Client.ResolveContentAsync(locale, item.Id, 1, cancellationToken).ConfigureAwait(false);
                                    if (container == null)
                                        PrintError(new InvalidOperationException("No container for " + item.Id));
                                    else
                                        await ProcessIncludedGamesAsync(locale, container, cancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                        start += take;
                    } while ((returned > 0 || (total > -1 && start * take <= total)) && !cancellationToken.IsCancellationRequested);
                    await ScrapeStateProvider.SetLastRunTimestampAsync(locale, containerId).ConfigureAwait(false);
                    Console.WriteLine($"\tFinished scraping {locale} container {containerId}, processed {start - take + returned} items");
                }
                await ScrapeStateProvider.SetLastRunTimestampAsync(locale).ConfigureAwait(false);
            }
            Console.WriteLine("Finished scraping all the PSN stores");
        }

        private static List<string> GetLocalesInPreferredOrder(string[] locales)
        {
            /*
             * what we want here: only one language per country
             * prefer en, then ja language for the region if it has it
             * then order by language, so we get as much English titles as possible
             * then Japanese
             * then the rest
             * withing one language prefer US, then GB, then JP to cover the largest ones first
             */
            var en = new List<string>();
            var ja = new List<string>();
            foreach (var l in locales)
            {
                if (l.StartsWith("en"))
                    en.Add(l);
                else if (l.StartsWith("ja"))
                    ja.Add(l);
            }
            var orderedLocales = new[] {"en-US", "en-GB"}
                .Concat(en)
                .Concat(new[] {"ja-JP"})
                .Concat(ja)
                .Concat(locales);
            var countries = new HashSet<string>();
            var result = new List<string>(locales.Length);
            foreach (var locale in orderedLocales)
                if (countries.Add(locale.AsLocaleData().country))
                    result.Add(locale);
            Console.WriteLine($"Selected stores ({result.Count}): " + string.Join(' ', result));
            return result;
        }

        private static bool NeedLookup(string contentId)
        {
            using (var db = new ThumbnailDb())
                if (db.Thumbnail.FirstOrDefault(t => t.ContentId == contentId) is Thumbnail thumbnail)
                    if (!string.IsNullOrEmpty(thumbnail.Url))
                        if (ScrapeStateProvider.IsFresh(new DateTime(thumbnail.Timestamp, DateTimeKind.Utc)))
                            return false;
            return true;
        }

        private static async Task ProcessIncludedGamesAsync(string locale, Container container, CancellationToken cancellationToken, bool resolveCrossplay = true)
        {
            if (container.Included?.Length > 0)
                foreach (var item in container.Included)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    switch (item.Type)
                    {
                        case "game":
                            if (string.IsNullOrEmpty(item.Id))
                                continue;

                            await AddOrUpdateThumbnailAsync(item.Id, item.Attributes?.Name, item.Attributes?.ThumbnailUrlBase, cancellationToken).ConfigureAwait(false);
                            break;

                        case "legacy-sku":
                            if (!resolveCrossplay)
                                continue;

                            var relatedSkus = (item.Attributes?.Eligibilities ?? Enumerable.Empty<GameSkuRelation>())
                                .Concat(item.Attributes?.Entitlements ?? Enumerable.Empty<GameSkuRelation>())
                                .Select(sku => sku.Id)
                                .Distinct()
                                .Where(id => ProductCodeLookup.ProductCode.IsMatch(id) && NeedLookup(id))
                                .ToList();
                            foreach (var relatedSku in relatedSkus)
                            {
                                var relatedContainer = await Client.ResolveContentAsync(locale, relatedSku, 1, cancellationToken).ConfigureAwait(false);
                                if (relatedContainer != null)
                                    await ProcessIncludedGamesAsync(locale, relatedContainer, cancellationToken, false).ConfigureAwait(false);
                            }
                            break;
                    }
                }
        }

        private static async Task AddOrUpdateThumbnailAsync(string contentId, string name, string url, CancellationToken cancellationToken)
        {
            var match = ContentIdMatcher.Match(contentId);
            if (!match.Success)
                return;

            var productCode = match.Groups["product_id"].Value;
            if (!ProductCodeLookup.ProductCode.IsMatch(productCode))
                return;

            name = string.IsNullOrEmpty(name) ? null : name;
            using (var db = new ThumbnailDb())
            {
                var savedItem = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productCode);
                if (savedItem == null)
                {
                    var newItem = new Thumbnail
                    {
                        ProductCode = productCode,
                        ContentId = contentId,
                        Name = name,
                        Url = url,
                        Timestamp = DateTime.UtcNow.Ticks,
                    };
                    db.Thumbnail.Add(newItem);
                }
                else if (!string.IsNullOrEmpty(url))
                {
                    if (!ScrapeStateProvider.IsFresh(savedItem.Timestamp))
                    {
                        if (savedItem.Url != url)
                        {
                            savedItem.Url = url;
                            savedItem.EmbeddableUrl = null;
                        }
                        if (name != null && savedItem.Name != name)
                            savedItem.Name = name;
                    }
                    savedItem.ContentId = contentId;
                    savedItem.Timestamp = DateTime.UtcNow.Ticks;
                }
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task ScrapeContainerIdsAsync(string locale, string containerId, HashSet<string> knownContainerIds, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (string.IsNullOrEmpty(containerId))
                return;

            if (!knownContainerIds.Add(containerId))
                return;

            var navigation = await Client.GetStoreNavigationAsync(locale, containerId, cancellationToken).ConfigureAwait(false);
            if (navigation?.Data?.Attributes?.Navigation is StoreNavigationNavigation[] navs)
            {
                foreach (var nav in navs)
                {
                    await ScrapeContainerIdsAsync(locale, nav.Id, knownContainerIds, cancellationToken).ConfigureAwait(false);
                    if (nav.Submenu is StoreNavigationSubmenu[] submenus)
                        foreach (var submenu in submenus)
                            if (submenu.Items is StoreNavigationSubmenuItem[] items)
                                foreach (var item in items)
                                    if (!item.IsSeparator && !string.IsNullOrEmpty(item.TargetContainerId))
                                    {
                                        if (cancellationToken.IsCancellationRequested)
                                            return;

                                        await ScrapeContainerIdsAsync(locale, item.TargetContainerId, knownContainerIds, cancellationToken).ConfigureAwait(false);
                                    }
                }
            }
            if (navigation?.Data?.Relationships?.Children?.Data is RelationshipsChildrenItem[] childItems)
                foreach (var item in childItems.Where(i => i.Type == "container"))
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await ScrapeContainerIdsAsync(locale, item.Id, knownContainerIds, cancellationToken).ConfigureAwait(false);
                }
        }

        private static void PrintError(Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error scraping thumbnails: " + e);
            Console.ResetColor();
        }
    }
}