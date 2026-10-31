namespace RobloxLauncher.Core;

// Strings of the Trending page. Same format as Loc.Table: English | cs | de | es | fr | pl | pt | ru | tr.
public static partial class Loc
{
    private static readonly string[] Table4 =
    {
        "Trending|Trendy|Im Trend|Tendencias|Tendances|Na topie|Em alta|В тренде|Trendler",
        "Player counts of the games on the Rostr list, collected every hour. Click a game to see its graph.|Počty hráčů her na seznamu Rostr, sbírané každou hodinu. Klikni na hru a uvidíš její graf.|Spielerzahlen der Spiele auf der Rostr-Liste, stündlich erfasst. Klicke auf ein Spiel, um den Graphen zu sehen.|Jugadores de los juegos de la lista de Rostr, registrados cada hora. Haz clic en un juego para ver su gráfica.|Nombre de joueurs des jeux de la liste Rostr, relevé toutes les heures. Clique sur un jeu pour voir son graphique.|Liczba graczy w grach z listy Rostr, zbierana co godzinę. Kliknij grę, aby zobaczyć jej wykres.|Jogadores dos jogos da lista do Rostr, coletados a cada hora. Clique em um jogo para ver o gráfico.|Онлайн игр из списка Rostr, данные собираются каждый час. Нажмите на игру, чтобы увидеть график.|Rostr listesindeki oyunların oyuncu sayıları her saat toplanır. Grafiği görmek için bir oyuna tıkla.",
        "Pick a game on the left to see its player count graph.|Vyber vlevo hru a zobrazí se graf počtu hráčů.|Wähle links ein Spiel, um den Spielerzahl-Graphen zu sehen.|Elige un juego a la izquierda para ver su gráfica de jugadores.|Choisis un jeu à gauche pour voir son graphique de joueurs.|Wybierz grę po lewej, aby zobaczyć wykres liczby graczy.|Escolha um jogo à esquerda para ver o gráfico de jogadores.|Выберите игру слева, чтобы увидеть график онлайна.|Oyuncu sayısı grafiğini görmek için soldan bir oyun seç.",
        "Not enough data yet. Check back in a few hours.|Zatím málo dat. Zkus to znovu za pár hodin.|Noch zu wenig Daten. Schau in ein paar Stunden wieder vorbei.|Aún no hay suficientes datos. Vuelve en unas horas.|Pas encore assez de données. Reviens dans quelques heures.|Za mało danych. Wróć za kilka godzin.|Ainda não há dados suficientes. Volte em algumas horas.|Данных пока мало. Загляните через несколько часов.|Henüz yeterli veri yok. Birkaç saat sonra tekrar bak.",
        "Could not load the list. Check your connection and try Refresh.|Seznam se nepodařilo načíst. Zkontroluj připojení a zkus Obnovit.|Die Liste konnte nicht geladen werden. Prüfe deine Verbindung und versuche Aktualisieren.|No se pudo cargar la lista. Revisa tu conexión e inténtalo con Actualizar.|Impossible de charger la liste. Vérifie ta connexion et essaie Actualiser.|Nie udało się wczytać listy. Sprawdź połączenie i spróbuj Odświeżyć.|Não foi possível carregar a lista. Verifique sua conexão e tente Atualizar.|Не удалось загрузить список. Проверьте соединение и нажмите «Обновить».|Liste yüklenemedi. Bağlantını kontrol et ve Yenile’yi dene.",
        "No games on the list yet.|Na seznamu zatím nejsou žádné hry.|Noch keine Spiele auf der Liste.|Aún no hay juegos en la lista.|Aucun jeu dans la liste pour l’instant.|Na liście nie ma jeszcze gier.|Ainda não há jogos na lista.|В списке пока нет игр.|Listede henüz oyun yok.",
        "Loading...|Načítám...|Wird geladen...|Cargando...|Chargement...|Wczytywanie...|Carregando...|Загрузка...|Yükleniyor...",
        "Refresh|Obnovit|Aktualisieren|Actualizar|Actualiser|Odśwież|Atualizar|Обновить|Yenile",
        "Updated {0}|Aktualizováno {0}|Aktualisiert {0}|Actualizado {0}|Mis à jour {0}|Zaktualizowano {0}|Atualizado {0}|Обновлено {0}|Güncellendi {0}",
        "{0} playing|{0} hraje|{0} spielen|{0} jugando|{0} en jeu|{0} gra|{0} jogando|{0} играют|{0} oynuyor",
        "{0} players|{0} hráčů|{0} Spieler|{0} jugadores|{0} joueurs|{0} graczy|{0} jogadores|{0} игроков|{0} oyuncu",
        "24 hours|24 hodin|24 Stunden|24 horas|24 heures|24 godziny|24 horas|24 часа|24 saat",
        "7 days|7 dní|7 Tage|7 días|7 jours|7 dni|7 dias|7 дней|7 gün",
        "30 days|30 dní|30 Tage|30 días|30 jours|30 dni|30 dias|30 дней|30 gün",
        "NOW|TEĎ|JETZT|AHORA|MAINTENANT|TERAZ|AGORA|СЕЙЧАС|ŞİMDİ",
        "PEAK|MAXIMUM|SPITZE|PICO|PIC|SZCZYT|PICO|ПИК|ZİRVE",
        "LOWEST|MINIMUM|TIEFSTWERT|MÍNIMO|MINIMUM|MINIMUM|MÍNIMO|МИНИМУМ|EN DÜŞÜK",
        "Request a language|Požádat o jazyk|Sprache anfragen|Pedir un idioma|Demander une langue|Poproś o język|Pedir um idioma|Запросить язык|Dil iste",
        "Is your language missing? Open an issue on GitHub with its name and it may get added.|Chybí tvůj jazyk? Otevři na GitHubu issue s jeho názvem a možná ho přidáme.|Fehlt deine Sprache? Eröffne auf GitHub ein Issue mit ihrem Namen, vielleicht wird sie hinzugefügt.|¿Falta tu idioma? Abre un issue en GitHub con su nombre y quizá lo añadamos.|Ta langue manque ? Ouvre une issue sur GitHub avec son nom et elle sera peut-être ajoutée.|Brakuje Twojego języka? Otwórz zgłoszenie na GitHubie z jego nazwą, a może zostanie dodany.|Seu idioma não está na lista? Abra uma issue no GitHub com o nome dele e talvez seja adicionado.|Нет вашего языка? Создайте issue на GitHub с его названием, и он может быть добавлен.|Dilin yok mu? GitHub’da adıyla bir issue aç, eklenebilir.",
        "Request a game|Požádat o hru|Spiel anfragen|Pedir un juego|Demander un jeu|Poproś o grę|Pedir um jogo|Запросить игру|Oyun iste",
    };
}
