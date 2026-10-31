using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobloxLauncher.Core;
using RobloxLauncher.Settings;

namespace RobloxLauncher;

public partial class MainWindow : Window
{
    // ---------- Game link ----------

    private void HandleAnyLink(string uri)
    {
        if (ShareLink.IsShareLink(uri)) HandleShareLink(uri);
        else HandleLink(uri);
    }

    // rostr://join?place=...&job=...: a friend shared a server. Asks first, because any web page can
    // open a rostr:// link and nothing should start Roblox without the person agreeing.
    private async void HandleShareLink(string uri)
    {
        var share = ShareLink.TryParse(uri);
        if (share == null)
        {
            BringToFront();
            SetStatus("That is not a valid Rostr server link.", Bad);
            return;
        }

        try
        {
            BringToFront();
            OpenPlace(share.PlaceId);

            if (ActiveAccount() == null)
            {
                SetStatus("Pick an account in the sidebar, then open the server link again.", Bad);
                return;
            }

            var (name, _) = await KnownGameAsync(share.PlaceId);
            var answer = MessageBox.Show(this,
                $"Join this server in {name}?\n\nServer: {share.JobId}",
                "Rostr", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            if (!_clientReady)
            {
                SetStatus("Preparing Roblox, then joining the shared server...", Busy);
                for (var i = 0; i < 240 && !_clientReady; i++) await Task.Delay(500);
            }
            await LaunchAsync(share.PlaceId, share.JobId);
        }
        catch (Exception ex)
        {
            Log.Error("Server link failed", ex);
            SetStatus("Server link: " + ex.Message, Bad);
        }
    }

    private void CopyLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id || _placeId == 0) return;
        try
        {
            Clipboard.SetText(ShareLink.Build(_placeId, id));
            SetStatus("Server link copied. A friend with Rostr can open it to join this server.", Ok);
        }
        catch (Exception ex)
        {
            SetStatus("Could not copy the link: " + ex.Message, Bad);
        }
    }

    private void HandleLink(string uri)
    {
        _directWebLaunchPending = false;
        try
        {
            _link = GameLauncher.Parse(uri);
            _linkUsed = false;
            // Skip choosing a server: a Play link from roblox.com launches right away, Rostr stays closed.
            _directWebLaunchPending = GameLauncher.IsDirectJoin(_link) || S.AutoBestServer;
            if (_directWebLaunchPending)
            {
                _autoLaunchPending = false;
                TryAutoLaunch();
                return;
            }

            OpenGame(_link.PlaceId);
            TryAutoLaunch();
        }
        catch (Exception ex)
        {
            SetStatus("Link error: " + ex.Message, Bad);
        }
    }

    // Opens a game from inside the app (no website link). Launching needs an account.
    private void OpenPlace(long placeId)
    {
        _link = null;
        _linkUsed = false;
        OpenGame(placeId);
    }

    private void OpenGame(long placeId)
    {
        _placeId = placeId;

        ShowPage(0);
        EmptyState.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Visible;

        _gameName = "";
        _gameIcon = null;
        GameName.Text = Loc.T("Loading game...");
        GameMeta.Text = "";
        GamePlaying.Text = "";
        GameIcon.Background = Dim;
        BannerBorder.Background = Brushes.Transparent;
        ServerList.ItemsSource = null;
        ServerCount.Text = "";
        _servers = new();
        _shown = new();
        _nextCursor = null;
        _serverReq++;
        _regionTried.Clear();
        _regionAutoDone = false;   // allow one automatic region scan for this game
        _detectCts?.Cancel();      // stop a scan that is still running for the previous game
        _hasBaseline = false;
        UpdateLoadMore();

        UpdateFavButton();
        UpdatePlayButtons();
        _ = LoadGameAsync(placeId);
    }

    private async Task LoadGameAsync(long placeId)
    {
        var id = ++_loadId;
        try
        {
            var game = await RobloxApi.GetGameAsync(placeId);
            if (id != _loadId) return;

            _gameName = game.Name;
            _gameIcon = game.IconUrl;
            _ = RefreshDiscordAsync();   // idle status follows the game page you are browsing
            GameName.Text = game.Name;
            GameMeta.Text = Loc.T("by " + game.Creator);
            GamePlaying.Text = Loc.T($"{game.Playing:N0} playing") + "  |  " + Loc.T($"{game.Visits:N0} visits");
            if (S.Playtime.TryGetValue(placeId, out var mine) && mine.Seconds >= 60)
                GamePlaying.Text += $"  |  You played {FormatPlaytime(mine.Seconds)}";

            var iconImg = await ImageService.LoadAsync(game.IconUrl);
            var bannerImg = await ImageService.LoadAsync(game.BannerUrl);
            if (id != _loadId) return;

            if (iconImg != null)
            {
                GameIcon.Background = new ImageBrush(iconImg)
                {
                    Stretch = Stretch.UniformToFill
                };
            }
            if (bannerImg != null)
            {
                BannerBorder.Background = new ImageBrush(bannerImg)
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.3
                };
            }
        }
        catch (Exception ex)
        {
            if (id != _loadId) return;
            GameName.Text = Loc.T($"Game {placeId}");
            SetStatus("Could not load game info: " + ex.Message, Bad);
        }

        if (id != _loadId) return;
        await RefreshServersAsync();
    }


    // ---------- Favorites ----------

    private void UpdateFavButton()
    {
        var fav = _placeId != 0 && S.Favorites.Any(f => f.PlaceId == _placeId);
        FavIcon.Text = fav ? "\uE735" : "\uE734";
        FavIcon.Foreground = fav ? Gold : Brushes.White;
    }

    private void FavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_placeId == 0) return;
        var existing = S.Favorites.FirstOrDefault(f => f.PlaceId == _placeId);
        if (existing != null)
        {
            S.Favorites.Remove(existing);
        }
        else
        {
            S.Favorites.Insert(0, new GameEntry
            {
                PlaceId = _placeId,
                Name = string.IsNullOrEmpty(_gameName) ? $"Game {_placeId}" : _gameName,
                IconUrl = _gameIcon,
                LastPlayed = DateTime.Now
            });
        }
        S.Save();
        UpdateFavButton();
    }


    // ---------- Servers ----------

    // detectNew is true only for the Refresh button: servers that were not in the previous list of the
    // same sort order, and are still nearly empty, are very likely newly created.
    private async Task RefreshServersAsync(bool detectNew = false)
    {
        if (_placeId == 0) return;
        var req = ++_serverReq;
        ServerCount.Text = Loc.T("Loading servers...");
        try
        {
            var asc = SortAscending;
            var page = await RobloxApi.GetServersAsync(_placeId, asc);
            if (req != _serverReq) return;
            _baselineAsc = asc;
            _hasBaseline = true;
            _servers = page.Servers;
            _nextCursor = page.NextCursor;
            UpdateLoadMore();
            ApplyFilter();
            _ = DetectRegionsAsync(true);
        }
        catch (Exception ex)
        {
            if (req != _serverReq) return;
            ServerCount.Text = Loc.T("Could not load servers");
            Log.Error("Server list failed", ex);
            SetStatus("Server list failed: " + ex.Message, Bad);
        }
    }

    private void UpdateLoadMore() =>
        LoadMoreBtn.Visibility = _nextCursor != null ? Visibility.Visible : Visibility.Collapsed;

    private void LoadMoreBtn_Click(object sender, RoutedEventArgs e) => _ = LoadMoreServersAsync();

    private async Task LoadMoreServersAsync()
    {
        if (_nextCursor == null || _loadingMore || _placeId == 0) return;
        _loadingMore = true;
        var req = _serverReq;
        var place = _placeId;
        LoadMoreBtn.IsEnabled = false;
        try
        {
            var page = await RobloxApi.GetServersAsync(place, SortAscending, _nextCursor);
            if (req != _serverReq || place != _placeId) return;

            // Servers shift between pages while you browse, so skip ones we already have.
            var have = new HashSet<string>(_servers.Select(x => x.Id));
            _servers = _servers.Concat(page.Servers.Where(x => have.Add(x.Id))).ToList();
            _nextCursor = page.NextCursor;
            ApplyFilter();
            _ = DetectRegionsAsync(true);
        }
        catch (Exception ex)
        {
            Log.Error("Loading more servers failed", ex);
            SetStatus("Could not load more servers: " + ex.Message, Bad);
        }
        finally
        {
            _loadingMore = false;
            LoadMoreBtn.IsEnabled = true;
            UpdateLoadMore();
        }
    }

    private static Brush PingBrushFor(int ping) =>
        ping <= 0 ? PingNone : ping <= PingGoodMax ? Ok : ping <= PingMidMax ? Busy : Bad;

    private void ApplyFilter()
    {
        IEnumerable<ServerInfo> q = _servers;

        if (int.TryParse(MinPlayersBox.Text, out var minP))
            q = q.Where(s => s.Playing >= minP);
        if (int.TryParse(MaxPingBox.Text, out var maxP) && maxP > 0)
            q = q.Where(s => s.Ping > 0 && s.Ping <= maxP);

        var rf = RegionFilterBox.Text.Trim();
        if (rf.Length > 0)
            q = q.Where(s => S.Regions.TryGetValue(s.Id, out var r) &&
                             r.Contains(rf, StringComparison.OrdinalIgnoreCase));

        q = _sort switch
        {
            "fewest" => q.OrderBy(s => s.Playing),
            "ping" => q.OrderBy(s => s.Ping == 0 ? int.MaxValue : s.Ping),
            _ => q.OrderByDescending(s => s.Playing)
        };

        _shown = q.ToList();
        ServerList.ItemsSource = _shown.Select(s =>
        {
            return new ServerRow
            {
                Id = s.Id,
                PlayersText = $"{s.Playing} / {s.MaxPlayers}",
                PingText = s.Ping > 0 ? $"{s.Ping} ms" : "-",
                FpsText = s.Fps > 0 ? $"{s.Fps:0} fps" : "-",
                RegionText = S.Regions.TryGetValue(s.Id, out var reg) ? reg : "?",
                PingBrush = PingBrushFor(s.Ping)
            };
        }).ToList();
        ServerList.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Loc.Apply(ServerList, true));
        ServerCount.Text = Loc.T($"{_shown.Count} servers") + (_nextCursor != null ? Loc.T(" (more available)") : "");
    }

    private void UpdateSortButtons()
    {
        SortMostBtn.Background = _sort == "most" ? Accent : Dim;
        SortFewestBtn.Background = _sort == "fewest" ? Accent : Dim;
        SortPingBtn.Background = _sort == "ping" ? Accent : Dim;
    }

    private void SortMost_Click(object sender, RoutedEventArgs e) => SetSort("most");
    private void SortFewest_Click(object sender, RoutedEventArgs e) => SetSort("fewest");
    private void SortPing_Click(object sender, RoutedEventArgs e) => SetSort("ping");

    private void SetSort(string sort)
    {
        _sort = sort;
        UpdateSortButtons();
        _ = RefreshServersAsync();
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => _ = RefreshServersAsync(true);

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyFilter();
    }

    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);


    // ---------- Launching ----------

    private string? LastJobForCurrent() =>
        _placeId == 0 ? null : S.Recent.FirstOrDefault(r => r.PlaceId == _placeId)?.LastJobId;

    private void UpdatePlayButtons()
    {
        var acc = ActiveAccount();
        var canLaunch = acc != null || (_link != null && !_linkUsed);
        var can = _placeId != 0 && _clientReady && canLaunch;

        PlayBtn.IsEnabled = can;
        HopBtn.IsEnabled = can;
        RejoinLastBtn.IsEnabled = can && !string.IsNullOrEmpty(LastJobForCurrent());
        PlayBtn.Content = Loc.T(!_clientReady ? "Preparing..." : (acc == null && _linkUsed ? "Launched" : "Play"));
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        // Play joins a random server from the list. A server picked on roblox.com (a link that was not
        // used yet) is never overridden, and without an account Roblox chooses the server itself.
        if (_placeId != 0 && ActiveAccount() != null && (_link == null || _linkUsed))
        {
            _ = JoinRandomAsync();
            return;
        }
        LaunchGame(null);
    }

    // Joins straight away. If the list is not loaded, it asks Roblox directly.
    private async Task JoinRandomAsync()
    {
        if (_shown.Count > 0)
        {
            var pick = S.AutoBestServer ? PickBestPing(_shown) : _shown[Random.Shared.Next(_shown.Count)];
            LaunchGame(pick.Id);
            return;
        }

        if (_servers.Count > 0)
        {
            SetStatus("No servers match your filters.", Bad);
            return;
        }

        if (_placeId != 0) await QuickRandomAsync(_placeId);
    }

    // Takes the servers with the lowest ping (a ping of 0 means Roblox has no value) and picks one at
    // random, so many people pressing Play at once are not all sent to the very same server.
    private static ServerInfo PickBestPing(IReadOnlyList<ServerInfo> list)
    {
        var withPing = list.Where(s => s.Ping > 0).OrderBy(s => s.Ping).ToList();
        if (withPing.Count == 0) return list[Random.Shared.Next(list.Count)];
        var best = withPing[0].Ping;
        var limit = Math.Max(best + 15, (int)(best * 1.25));
        var pool = withPing.Where(s => s.Ping <= limit).Take(8).ToList();
        return pool[Random.Shared.Next(pool.Count)];
    }

    private void JoinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id) LaunchGame(id);
    }

    // ---------- Server hop ----------

    private bool _hopBusy;

    private async void HopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_placeId != 0) await ServerHopAsync(_placeId, null);
    }

    private void MarkVisited(long placeId, string? job)
    {
        if (placeId <= 0 || string.IsNullOrEmpty(job)) return;
        if (!S.VisitedServers.TryGetValue(placeId, out var list))
            S.VisitedServers[placeId] = list = new List<string>();
        if (list.Contains(job)) return;

        list.Add(job);
        if (list.Count > 500) list.RemoveRange(0, list.Count - 500);
        if (S.VisitedServers.Count > 100) S.VisitedServers.Remove(S.VisitedServers.Keys.First());
        S.Save();
    }

    // Joins a random open server you have not been in yet. "from" is the client you are hopping away from.
    private async Task ServerHopAsync(long placeId, Session? from)
    {
        if (_hopBusy || _launching) return;
        if (ActiveAccount() == null && (_link == null || _linkUsed || _link.PlaceId != placeId))
        {
            SetStatus("Pick an account in the sidebar first.", Bad);
            return;
        }

        _hopBusy = true;
        try
        {
            SetStatus("Finding a server you have not been in...", Busy);

            var visited = new HashSet<string>(
                S.VisitedServers.TryGetValue(placeId, out var seen) ? seen : new List<string>());
            foreach (var running in _sessions)
                if (running.PlaceId == placeId && !string.IsNullOrEmpty(running.Job)) visited.Add(running.Job);
            if (!string.IsNullOrEmpty(from?.Job)) visited.Add(from!.Job!);

            ServerInfo? pick = null;
            string? cursor = null;
            for (var page = 0; page < 6 && pick == null; page++)
            {
                var res = await RobloxApi.GetServersAsync(placeId, false, cursor);
                var fresh = res.Servers
                    .Where(x => !visited.Contains(x.Id) && (x.MaxPlayers <= 0 || x.Playing < x.MaxPlayers))
                    .ToList();
                if (fresh.Count > 0)
                    pick = S.AutoBestServer ? PickBestPing(fresh) : fresh[Random.Shared.Next(fresh.Count)];
                if (res.NextCursor == null) break;
                cursor = res.NextCursor;
            }

            if (pick == null)
            {
                SetStatus("You have already been in every open server I could find. Try again later.", Bad);
                return;
            }

            MarkVisited(placeId, pick.Id);

            var ok = placeId == _placeId
                ? await LaunchCoreAsync(_placeId, _gameName, _gameIcon, pick.Id, _link)
                : await LaunchAsync(placeId, pick.Id);

            if (ok && _sessions.Count > 0) _sessions[^1].HopPrompt = true;
        }
        catch (Exception ex)
        {
            Log.Error("Server hop failed", ex);
            SetStatus("Could not find a server: " + ex.Message, Bad);
        }
        finally
        {
            _hopBusy = false;
        }
    }

    // Closes one client (and its session) and waits until it is really gone.
    private async Task CloseClientAsync(Session o)
    {
        var pid = o.Pid;
        if (pid > 0)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(true);
                await Task.Run(() => p.WaitForExit(5000));
            }
            catch { }   // already closed
        }
        if (_sessions.Contains(o)) EndSession(o);
    }

    // After a hop lands in a server: stay here, or hop to another new one.
    private async Task AskStayOrHopAsync(Session s, long placeId)
    {
        await Task.Delay(8000);   // let the game start loading first
        if (!_sessions.Contains(s)) return;

        var dialog = new HopPromptWindow(this);
        dialog.ShowDialog();
        if (dialog.HopAgain && _sessions.Contains(s)) await ServerHopAsync(placeId, s);
    }

    private void RejoinLastBtn_Click(object sender, RoutedEventArgs e)
    {
        var job = LastJobForCurrent();
        if (!string.IsNullOrEmpty(job)) LaunchGame(job);
    }

    private static long ParsePlaceId(string? text)
    {
        var m = Regex.Match(text ?? "", @"(\d{4,})");
        return m.Success && long.TryParse(m.Groups[1].Value, out var id) ? id : 0;
    }

    // A place id or a roblox.com link opens directly; anything else is searched by name.
    private static bool LooksLikeId(string text) =>
        Regex.IsMatch(text, @"^\d{4,}$") || text.Contains("roblox.com", StringComparison.OrdinalIgnoreCase);

    private void OpenPlaceBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = PlaceBox.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("Type a game name, a place ID or a roblox.com/games link.", Bad);
            return;
        }

        if (!LooksLikeId(text))
        {
            _searchDebounce.Stop();
            _ = SearchGamesAsync(text);
            return;
        }

        var id = ParsePlaceId(text);
        if (id == 0)
        {
            SetStatus("Could not find a place ID in that link.", Bad);
            return;
        }
        OpenPlace(id);
    }

    private void PlaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenPlaceBtn_Click(sender, e);
    }

    private void PlaceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = PlaceBox.Text.Trim();
        var isId = LooksLikeId(text);
        PlaceHint.Visibility = PlaceBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaceRandomBtn.Visibility = isId ? Visibility.Visible : Visibility.Collapsed;
        PlaceOpenBtn.Content = Loc.T(isId ? "Open" : "Search");

        _searchDebounce.Stop();
        if (text.Length == 0) ClearSearch();
        else if (!isId && text.Length >= 2) _searchDebounce.Start();
    }

    private void ClearSearch()
    {
        _searchReq++;
        SearchResults.ItemsSource = null;
        SearchScroll.Visibility = Visibility.Collapsed;
        SearchStatus.Visibility = Visibility.Collapsed;
        SearchHelp.Visibility = Visibility.Visible;
        EmptyHero.Visibility = Visibility.Visible;
    }

    private async Task SearchGamesAsync(string query)
    {
        var req = ++_searchReq;
        SearchStatus.Text = Loc.T("Searching...");
        SearchStatus.Visibility = Visibility.Visible;
        try
        {
            var results = await RobloxApi.SearchGamesAsync(query);
            if (req != _searchReq) return;

            var rows = results.Select(r => new SearchRow
            {
                PlaceId = r.PlaceId,
                Name = r.Name,
                Sub = Loc.T($"{r.Playing:N0} playing") + (r.LikePercent is int p ? "  |  " + Loc.T($"{p}% liked") : ""),
                IconBrush = Dim
            }).ToList();

            SearchResults.ItemsSource = rows;
            TranslateList(SearchResults);
            SearchScroll.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchHelp.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyHero.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            SearchStatus.Text = rows.Count == 0
                ? $"No games found for \"{query}\"."
                : $"{rows.Count} game(s) found. Open lets you pick a server.";

            if (rows.Count > 0) _ = LoadSearchIconsAsync(rows, results, req);
        }
        catch (Exception ex)
        {
            if (req != _searchReq) return;
            Log.Error("Game search failed", ex);
            SearchStatus.Text = Loc.T("Search failed: " + ex.Message);
        }
    }

    private async Task LoadSearchIconsAsync(List<SearchRow> rows, List<SearchResult> results, int req)
    {
        try
        {
            var urls = await RobloxApi.GetIconUrlsAsync(results.Select(r => r.UniverseId));
            for (var i = 0; i < rows.Count; i++)
            {
                if (req != _searchReq) return;
                if (!urls.TryGetValue(results[i].UniverseId, out var url)) continue;
                var img = await ImageService.LoadAsync(url);
                if (img != null && req == _searchReq)
                    rows[i].IconBrush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Search pictures: " + ex.Message);
        }
    }

    private void SearchOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        OpenPlace(Convert.ToInt64(b.Tag));
        if (ActiveAccount() == null)
            SetStatus("Pick an account in the sidebar to join from here.", Busy);
    }

    private void SearchRandom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        _ = QuickRandomAsync(Convert.ToInt64(b.Tag));
    }

    // Back from a game's server list to the search page.
    private void BackToSearch_Click(object sender, RoutedEventArgs e)
    {
        GamePanel.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        _ = RefreshDiscordAsync();
    }

    private async void OpenPlaceRandomBtn_Click(object sender, RoutedEventArgs e)
    {
        var id = ParsePlaceId(PlaceBox.Text);
        if (id == 0)
        {
            SetStatus("Enter a place ID or a roblox.com/games link.", Bad);
            return;
        }
        await QuickRandomAsync(id);
    }

    private async Task QuickRandomAsync(long placeId)
    {
        if (ActiveAccount() == null && (_link == null || _linkUsed || _link.PlaceId != placeId))
        {
            SetStatus("Pick an account in the sidebar first.", Bad);
            return;
        }

        SetStatus(S.AutoBestServer ? "Finding a server with good ping..." : "Finding a random server...", Busy);
        try
        {
            var list = (await RobloxApi.GetServersAsync(placeId, false)).Servers;
            if (list.Count == 0)
            {
                SetStatus("No open servers found for this game.", Bad);
                return;
            }

            var pick = S.AutoBestServer ? PickBestPing(list) : list[Random.Shared.Next(list.Count)];
            if (placeId == _placeId) LaunchGame(pick.Id);
            else await LaunchAsync(placeId, pick.Id);
        }
        catch (Exception ex)
        {
            SetStatus("Could not find a server: " + ex.Message, Bad);
        }
    }

    private static string? ExtractJob(string placeUrl)
    {
        var m = Regex.Match(placeUrl, @"[?&]gameId=([0-9a-fA-F\-]{36})");
        if (!m.Success)
            m = Regex.Match(placeUrl, @"[?&]gameInstanceId=([0-9a-fA-F\-]{36})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private void RecordRecent(long placeId, string name, string? icon, string? jobId)
    {
        var entry = S.Recent.FirstOrDefault(r => r.PlaceId == placeId);
        if (entry != null) S.Recent.Remove(entry);
        entry ??= new GameEntry { PlaceId = placeId };

        entry.Name = string.IsNullOrEmpty(name) ? $"Game {placeId}" : name;
        entry.IconUrl = icon ?? entry.IconUrl;
        entry.LastPlayed = DateTime.Now;
        entry.LastJobId = jobId;
        entry.LastRegion = null;

        S.Recent.Insert(0, entry);
        if (S.Recent.Count > 12) S.Recent.RemoveRange(12, S.Recent.Count - 12);
        S.Save();
    }

    // Launch the game shown on the Play page.
    private async void LaunchGame(string? jobId)
    {
        if (_placeId == 0)
        {
            SetStatus("No game selected. Open a game first.", Bad);
            return;
        }
        await LaunchCoreAsync(_placeId, _gameName, _gameIcon, jobId, _link);
    }

    private async Task LaunchDirectWebLinkAsync(LaunchLink link)
    {
        var known = link.PlaceId > 0
            ? (GameEntry?)S.Recent.FirstOrDefault(r => r.PlaceId == link.PlaceId)
              ?? S.Favorites.FirstOrDefault(f => f.PlaceId == link.PlaceId)
            : null;
        var name = known?.Name ?? (link.PlaceId > 0 ? $"Game {link.PlaceId}" : "Friend's game");
        var icon = known?.IconUrl;
        var ok = await LaunchCoreAsync(link.PlaceId, name, icon, null, link,
            ticket => link with { Ticket = ticket });

        // Rostr stayed hidden for this launch. If it failed, open the window so the reason is visible.
        if (!ok && !_launching && !IsVisible) BringToFront();
    }

    // Launch any game by place id (library, place box) with the active account.
    private async Task<bool> LaunchAsync(long placeId, string? jobId)
    {
        var known = (GameEntry?)S.Recent.FirstOrDefault(r => r.PlaceId == placeId)
                    ?? S.Favorites.FirstOrDefault(f => f.PlaceId == placeId);
        var name = known?.Name ?? "";
        var icon = known?.IconUrl;

        if (string.IsNullOrEmpty(name))
        {
            try
            {
                var g = await RobloxApi.GetGameAsync(placeId);
                name = g.Name;
                icon = g.IconUrl;
            }
            catch { }
        }

        return await LaunchCoreAsync(placeId, name, icon, jobId, null);
    }

    private async Task<bool> LaunchCoreAsync(
        long placeId, string name, string? icon, string? jobId, LaunchLink? webLink,
        Func<string, LaunchLink>? buildLink = null)
    {
        if (_launching) return false;
        if (!_clientReady || _versionDir == null)
        {
            SetStatus("Still preparing Roblox. Try again in a moment.", Busy);
            return false;
        }

        var acc = ActiveAccount();
        if (acc == null && (webLink == null || _linkUsed))
        {
            SetStatus("Pick an account in the sidebar, or press Play on roblox.com first.", Bad);
            return false;
        }

        _launching = true;
        try
        {
            // Multi Roblox: every client is tagged with the account that started it. Launching again with
            // the same account replaces that account's old client, so Play, Rejoin and Server hop never
            // pile up clients. Clients of other accounts are left alone.
            if (S.MultiRoblox && acc != null)
            {
                var old = _sessions.Where(x => x.AccountName == acc.Name).ToList();
                if (old.Count > 0)
                {
                    SetStatus("Closing the old client of this account...", Busy);
                    foreach (var o in old) await CloseClientAsync(o);
                }

                // Also close every client tagged with this account that the launcher no longer tracks
                // (re-spawned PID, leftover from before a restart). This is what stops the pile-up.
                await ClientTags.KillAccountAsync(acc.Name, TimeSpan.FromSeconds(8));
            }

            // A second client only starts if the first one's singleton event was closed.
            if (MultiInstance.RunningPids().Count > 0)
            {
                if (!S.MultiRoblox)
                {
                    // Without Multi Roblox only one client can run, so close the old one (even if it is
                    // only sitting in the tray) and start a fresh session.
                    SetStatus("Closing the Roblox that is still running...", Busy);
                    if (!await MultiInstance.KillAllAsync(TimeSpan.FromSeconds(8)))
                    {
                        SetStatus("Could not close the running Roblox. Close it in Task Manager and try again.", Bad);
                        return false;
                    }
                }

                if (S.MultiRoblox) SetStatus("Getting the running client ready...", Busy);
                if (!await _multi.WaitUntilClearAsync(TimeSpan.FromSeconds(12)))
                {
                    SetStatus("Could not prepare the running client for a second one. Try again, or restart Roblox.", Bad);
                    return false;
                }
            }

            LaunchLink link;
            if (acc != null)
            {
                var cookie = AccountStore.GetCookie(acc);
                if (cookie == null)
                {
                    SetStatus($"Could not read the saved login for {acc.Name}. Remove the account and add it again.", Bad);
                    return false;
                }

                SetStatus($"Logging in as {acc.Name}...", Busy);
                var ticket = await RobloxAuth.GetTicketAsync(cookie, placeId);
                link = buildLink != null ? buildLink(ticket) : RobloxAuth.BuildLink(ticket, placeId);
            }
            else
            {
                link = webLink!;
            }

            GlobalSettings.TrySetFramerateCap(S.FpsCap, out _);
            FastFlagManager.Shared.ApplyTo(_versionDir);

            var launchedAt = DateTime.UtcNow;
            var before = MultiInstance.RunningPids();
            var proc = GameLauncher.Launch(link, _versionDir, jobId);
            if (acc != null)
            {
                if (proc != null) ClientTags.Register(proc.Id, acc.Name);
                _ = ClientTags.TagNewAsync(acc.Name, before, launchedAt);
            }
            if (acc == null) _linkUsed = true;

            var job = jobId ?? ExtractJob(link.PlaceUrl);
            if (placeId > 0) RecordRecent(placeId, name, icon, job);

            StartSession(new Session
            {
                Pid = proc?.Id ?? 0,
                AccountName = acc?.Name ?? "",
                PlaceId = placeId,
                Job = job
            }, launchedAt);

            UpdatePlayButtons();
            UpdateFavButton();
            SetStatus(jobId == null ? "Starting Roblox..." : "Starting Roblox on the chosen server...", Ok);

            if (S.MinimizeOnLaunch) WindowState = WindowState.Minimized;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Launch failed", ex);
            SetStatus("Launch failed: " + ex.Message, Bad);
            return false;
        }
        finally
        {
            _launching = false;
        }
    }


    // ---------- Region detection for the server list ----------

    // Asks Roblox where each listed server is, without joining. Needs an account.
    // Runs by itself after the list loads (auto = true) and from the button as a manual retry / Stop.
    private async void DetectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_detectCts != null)
        {
            _detectCts.Cancel();
            return;
        }
        await DetectRegionsAsync(false);
    }

    // Updates one row in place so the list does not jump back to the top while you scroll.
    private void SetRowRegion(string id, string region)
    {
        if (RegionFilterBox.Text.Trim().Length > 0)
        {
            ApplyFilter();
            return;
        }
        if (ServerList.ItemsSource is IEnumerable<ServerRow> rows)
        {
            var row = rows.FirstOrDefault(r => r.Id == id);
            if (row != null) row.RegionText = region;
        }
    }

    private async Task DetectRegionsAsync(bool auto)
    {
        if (_detectCts != null) return;
        if (auto && DateTime.Now < _detectPausedUntil) return;

        var acc = ActiveAccount();
        if (acc == null)
        {
            if (!auto) SetStatus("Detecting regions needs an account selected in the sidebar.", Bad);
            return;
        }

        var cookie = AccountStore.GetCookie(acc);
        if (cookie == null || _placeId == 0) return;
        if (auto && _regionAutoDone) return;   // already scanned once for this game

        var todo = _shown
            .Where(x => !S.Regions.ContainsKey(x.Id) && (!auto || !_regionTried.Contains(x.Id)))
            .Take(auto ? 20 : 12)
            .ToList();
        if (todo.Count == 0)
        {
            if (!auto) SetStatus("The shown servers already have a region.", Ok);
            return;
        }

        if (auto) _regionAutoDone = true;
        var cts = _detectCts = new CancellationTokenSource();
        DetectBtn.Content = Loc.T("Stop");
        var placeId = _placeId;
        int found = 0;

        try
        {
            for (int i = 0; i < todo.Count && !cts.IsCancellationRequested && placeId == _placeId; i++)
            {
                if (!auto) SetStatus($"Detecting regions... {i + 1}/{todo.Count}", Busy);

                _regionTried.Add(todo[i].Id);
                var probe = await RobloxAuth.ProbeServerAddressAsync(cookie, placeId, todo[i].Id);
                if (probe.RateLimited)
                {
                    _detectPausedUntil = DateTime.Now.AddMinutes(1);
                    _regionTried.Remove(todo[i].Id);
                    if (!auto) SetStatus("Roblox asked to slow down. Try again in a minute.", Bad);
                    break;
                }

                if (probe.Ip != null)
                {
                    var region = await GeoLocator.LookupAsync(probe.Ip);
                    if (region != null && placeId == _placeId)
                    {
                        StoreRegion(todo[i].Id, region);
                        found++;
                        SetRowRegion(todo[i].Id, region);
                    }
                }

                await Task.Delay(1200, cts.Token);
            }

            S.Save();
            if (!auto)
                SetStatus(found > 0
                    ? $"Detected {found} region(s)."
                    : "Roblox did not reveal those servers' locations. Regions still show after you join.",
                    found > 0 ? Ok : Busy);
        }
        catch (OperationCanceledException)
        {
            S.Save();
            if (!auto) SetStatus("Stopped.", Busy);
        }
        catch (Exception ex)
        {
            Log.Warn("Region detection failed: " + ex.Message);
            if (!auto) SetStatus("Region detection failed: " + ex.Message, Bad);
        }
        finally
        {
            _detectCts = null;
            DetectBtn.Content = Loc.T("Detect regions");
        }
    }


    // ---------- Private servers ----------

    private async Task<(string Name, string? Icon)> KnownGameAsync(long placeId)
    {
        var known = (GameEntry?)S.Recent.FirstOrDefault(r => r.PlaceId == placeId)
                    ?? S.Favorites.FirstOrDefault(f => f.PlaceId == placeId);
        if (known != null && !string.IsNullOrEmpty(known.Name)) return (known.Name, known.IconUrl);

        try
        {
            var g = await RobloxApi.GetGameAsync(placeId);
            return (g.Name, g.IconUrl);
        }
        catch
        {
            return ($"Game {placeId}", null);
        }
    }

    private async void PrivateJoin_Click(object sender, RoutedEventArgs e)
    {
        if (_privateBusy) return;

        var acc = ActiveAccount();
        if (acc == null)
        {
            SetStatus("Pick an account in the sidebar first. Private servers need one.", Bad);
            return;
        }
        var cookie = AccountStore.GetCookie(acc);
        if (cookie == null)
        {
            SetStatus($"Could not read the saved login for {acc.Name}. Remove the account and add it again.", Bad);
            return;
        }
        if (string.IsNullOrWhiteSpace(PrivateBox.Text))
        {
            SetStatus("Paste a private server link first.", Bad);
            return;
        }

        _privateBusy = true;
        PrivateJoinBtn.IsEnabled = false;
        try
        {
            SetStatus("Checking the private server link...", Busy);
            var r = await RobloxAuth.ResolvePrivateLinkAsync(cookie, PrivateBox.Text);
            if (r == null)
            {
                SetStatus("That does not look like a private server link. Use a roblox.com/games/...?privateServerLinkCode=... " +
                          "link or a roblox.com/share?code=...&type=Server link.", Bad);
                return;
            }

            var access = await RobloxAuth.GetPrivateAccessCodeAsync(cookie, r.PlaceId, r.LinkCode);
            var (name, icon) = await KnownGameAsync(r.PlaceId);
            await LaunchCoreAsync(r.PlaceId, name, icon, null, null,
                ticket => RobloxAuth.BuildPrivateLink(ticket, r.PlaceId, access, r.LinkCode));
        }
        catch (Exception ex)
        {
            Log.Error("Private server join failed", ex);
            SetStatus("Private server: " + ex.Message, Bad);
        }
        finally
        {
            _privateBusy = false;
            PrivateJoinBtn.IsEnabled = true;
        }
    }
}
