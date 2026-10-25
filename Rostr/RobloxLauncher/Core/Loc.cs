using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace RobloxLauncher.Core;

/// <summary>
/// Languages. The XAML keeps its English text; Apply() swaps every plain text it finds for the chosen language and
/// remembers the English original, so you can switch back and forth at any time. Anything missing from the table
/// stays English. To add a language: add its code + name below and a column to Table.
/// </summary>
public static partial class Loc
{
    public static readonly string[] Codes = { "en", "cs", "de", "es", "fr", "pl", "pt", "ru", "tr" };
    public static readonly string[] Names = { "English", "Čeština", "Deutsch", "Español", "Français", "Polski", "Português", "Русский", "Türkçe" };

    // English | cs | de | es | fr | pl | pt | ru | tr
    private static readonly string[] Table =
    {
        "Play|Hrát|Spielen|Jugar|Jouer|Graj|Jogar|Играть|Oyna",
        "Library|Knihovna|Bibliothek|Biblioteca|Bibliothèque|Biblioteka|Biblioteca|Библиотека|Kütüphane",
        "Friends|Přátelé|Freunde|Amigos|Amis|Znajomi|Amigos|Друзья|Arkadaşlar",
        "Settings|Nastavení|Einstellungen|Ajustes|Paramètres|Ustawienia|Configurações|Настройки|Ayarlar",
        "Search|Hledat|Suchen|Buscar|Rechercher|Szukaj|Buscar|Поиск|Ara",
        "Servers|Servery|Server|Servidores|Serveurs|Serwery|Servidores|Серверы|Sunucular",
        "Random|Náhodný|Zufällig|Aleatorio|Aléatoire|Losowy|Aleatório|Случайный|Rastgele",
        "Rejoin|Znovu připojit|Erneut beitreten|Reentrar|Rejoindre|Dołącz ponownie|Reentrar|Зайти снова|Yeniden katıl",
        "Open|Otevřít|Öffnen|Abrir|Ouvrir|Otwórz|Abrir|Открыть|Aç",
        "Open web|Otevřít web|Website öffnen|Abrir web|Ouvrir le site|Otwórz stronę|Abrir site|Открыть сайт|Siteyi aç",
        "Join|Připojit|Beitreten|Unirse|Rejoindre|Dołącz|Entrar|Войти|Katıl",
        "Refresh|Obnovit|Aktualisieren|Actualizar|Actualiser|Odśwież|Atualizar|Обновить|Yenile",
        "Load more|Načíst další|Mehr laden|Cargar más|Charger plus|Wczytaj więcej|Carregar mais|Загрузить ещё|Daha fazla yükle",
        "Random server|Náhodný server|Zufälliger Server|Servidor aleatorio|Serveur aléatoire|Losowy serwer|Servidor aleatório|Случайный сервер|Rastgele sunucu",
        "Rejoin last server|Poslední server|Letzter Server|Último servidor|Dernier serveur|Ostatni serwer|Último servidor|Последний сервер|Son sunucu",
        "Detect regions|Zjistit regiony|Regionen erkennen|Detectar regiones|Détecter les régions|Wykryj regiony|Detectar regiões|Определить регионы|Bölgeleri algıla",
        "Most players|Nejvíce hráčů|Meiste Spieler|Más jugadores|Plus de joueurs|Najwięcej graczy|Mais jogadores|Больше игроков|En çok oyuncu",
        "Fewest players|Nejméně hráčů|Wenigste Spieler|Menos jugadores|Moins de joueurs|Najmniej graczy|Menos jogadores|Меньше игроков|En az oyuncu",
        "Best ping|Nejlepší ping|Bester Ping|Mejor ping|Meilleur ping|Najlepszy ping|Melhor ping|Лучший пинг|En iyi ping",
        "Min players|Min. hráčů|Min. Spieler|Mín. jugadores|Joueurs min.|Min. graczy|Mín. jogadores|Мин. игроков|Min. oyuncu",
        "Max ping|Max. ping|Max. Ping|Ping máx.|Ping max.|Maks. ping|Ping máx.|Макс. пинг|Maks. ping",
        "Region|Region|Region|Región|Région|Region|Região|Регион|Bölge",
        "PLAYERS|HRÁČI|SPIELER|JUGADORES|JOUEURS|GRACZE|JOGADORES|ИГРОКИ|OYUNCULAR",
        "REGION|REGION|REGION|REGIÓN|RÉGION|REGION|REGIÃO|РЕГИОН|BÖLGE",
        "About|O aplikaci|Über|Acerca de|À propos|O programie|Sobre|О программе|Hakkında",
        "Performance|Výkon|Leistung|Rendimiento|Performances|Wydajność|Desempenho|Производительность|Performans",
        "Overlay|Overlay|Overlay|Overlay|Overlay|Overlay|Overlay|Оверлей|Overlay",
        "General|Obecné|Allgemein|General|Général|Ogólne|Geral|Общие|Genel",
        "Updates|Aktualizace|Updates|Actualizaciones|Mises à jour|Aktualizacje|Atualizações|Обновления|Güncellemeler",
        "Launcher|Launcher|Launcher|Launcher|Lanceur|Launcher|Launcher|Лаунчер|Başlatıcı",
        "FPS cap|Limit FPS|FPS-Limit|Límite de FPS|Limite FPS|Limit FPS|Limite de FPS|Лимит FPS|FPS sınırı",
        "Save FPS cap|Uložit limit FPS|FPS-Limit speichern|Guardar límite de FPS|Enregistrer la limite|Zapisz limit FPS|Salvar limite de FPS|Сохранить лимит FPS|FPS sınırını kaydet",
        "Check for updates|Zkontrolovat aktualizace|Nach Updates suchen|Buscar actualizaciones|Rechercher des mises à jour|Sprawdź aktualizacje|Verificar atualizações|Проверить обновления|Güncellemeleri denetle",
        "Open log file|Otevřít log|Logdatei öffnen|Abrir registro|Ouvrir le journal|Otwórz log|Abrir log|Открыть лог|Günlüğü aç",
        "Register|Zaregistrovat|Registrieren|Registrar|Enregistrer|Zarejestruj|Registrar|Зарегистрировать|Kaydet",
        "Unregister|Odregistrovat|Abmelden|Anular registro|Désenregistrer|Wyrejestruj|Cancelar registro|Отменить|Kaydı sil",
        "Reset|Obnovit|Zurücksetzen|Restablecer|Réinitialiser|Resetuj|Redefinir|Сбросить|Sıfırla",
        "Appearance|Vzhled|Aussehen|Apariencia|Apparence|Wygląd|Aparência|Оформление|Görünüm",
        "Language|Jazyk|Sprache|Idioma|Langue|Język|Idioma|Язык|Dil",
        "Theme|Motiv|Design|Tema|Thème|Motyw|Tema|Тема|Tema",
        "Community|Komunita|Community|Comunidad|Communauté|Społeczność|Comunidade|Сообщество|Topluluk",
        "Join our Discord|Připoj se na Discord|Discord beitreten|Únete al Discord|Rejoindre Discord|Dołącz do Discorda|Entrar no Discord|Зайти в Discord|Discord’a katıl",
    };

    private sealed class Entry { public string Orig = ""; public string Applied = ""; }
    private static readonly ConditionalWeakTable<DependencyObject, Entry> Seen = new();
    private static readonly ConditionalWeakTable<DependencyObject, Entry> SeenTip = new();
    private static readonly ConditionalWeakTable<DependencyObject, Entry> SeenTitle = new();
    private static Dictionary<string, string> _map = new();
    // Entries with {0}, {1}...: matched against text that already has its values filled in.
    private static List<(Regex Pattern, string Translated)> _templates = new();

    public static string Current { get; private set; } = "en";

    /// <summary>"" means follow the Windows display language.</summary>
    public static string Resolve(string saved)
    {
        if (!string.IsNullOrEmpty(saved) && Codes.Contains(saved)) return saved;
        var sys = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (sys == "sk") sys = "cs";
        return Codes.Contains(sys) ? sys : "en";
    }

    public static void Set(string code)
    {
        Current = Codes.Contains(code) ? code : "en";
        int col = Array.IndexOf(Codes, Current);
        _map = new();
        _templates = new();
        if (col <= 0) return;
        foreach (var row in Table.Concat(Table2))
        {
            var p = row.Split('|');
            if (p.Length <= col) continue;
            _map[p[0]] = p[col];
            if (p[0].Contains("{0}"))
            {
                // "Launch failed: {0}" becomes ^Launch failed: (.*)$
                var rx = "^" + Regex.Replace(Regex.Escape(p[0]), @"\\\{(\d)\}", "(?<g$1>.*?)") + "$";
                _templates.Add((new Regex(rx, RegexOptions.Singleline), p[col]));
            }
        }
    }

    /// <summary>Whole-text match only (what the XAML walk uses, because game names and user text pass through it).</summary>
    public static string TExact(string english) => _map.TryGetValue(english, out var v) ? v : english;

    /// <summary>Translates one English text. Text with its values already filled in ("Launch failed: boom") works too.</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english)) return english;
        if (_map.TryGetValue(english, out var v)) return v;
        foreach (var (pattern, translated) in _templates)
        {
            var m = pattern.Match(english);
            if (!m.Success) continue;
            var result = translated;
            foreach (Group g in m.Groups)
            {
                if (!g.Name.StartsWith('g')) continue;
                // The filled-in part can itself be a known text ("Saved: 60 FPS. {note}").
                result = result.Replace("{" + g.Name.Substring(1) + "}", TExact(g.Value));
            }
            return result;
        }
        return english;
    }

    /// <summary>Translates all plain text under root. visual = also walk templates (lists), not only the XAML tree.</summary>
    public static void Apply(DependencyObject root, bool visual = false)
    {
        Walk(root, visual);
    }

    private static void Walk(DependencyObject d, bool visual)
    {
        if (d is Window win && !string.IsNullOrEmpty(win.Title))
            win.Title = Tr(win, win.Title, SeenTitle);
        if (d is FrameworkElement fe && fe.ToolTip is string tip)
            fe.ToolTip = Tr(fe, tip, SeenTip);

        switch (d)
        {
            case TextBlock tb when tb.Inlines.Count <= 1 && !BindingOperations.IsDataBound(tb, TextBlock.TextProperty):
                tb.Text = Tr(tb, tb.Text);
                break;
            case ContentControl cc when cc.Content is string s && !BindingOperations.IsDataBound(cc, ContentControl.ContentProperty):
                cc.Content = Tr(cc, s);
                break;
        }

        if (visual && d is System.Windows.Media.Visual)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i), true);
        }
        else
        {
            foreach (var child in LogicalTreeHelper.GetChildren(d))
                if (child is DependencyObject dc) Walk(dc, visual);
        }
    }

    private static string Tr(DependencyObject d, string current) => Tr(d, current, Seen);

    private static string Tr(DependencyObject d, string current, ConditionalWeakTable<DependencyObject, Entry> table)
    {
        if (string.IsNullOrEmpty(current)) return current;
        var e = table.GetOrCreateValue(d);
        // The code set a new text since last time: that text is the new English original.
        if (e.Orig == "" || (current != e.Applied && current != e.Orig)) e.Orig = current;
        e.Applied = TExact(e.Orig);   // exact matches only: a game called "Play Together" must not turn into a template
        return e.Applied;
    }
}
