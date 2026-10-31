#!/usr/bin/env python3
"""
Collects the current player count of every game listed in trending/places.json
and appends it to trending/data/<placeId>.json. Also writes trending/index.json,
the list Rostr shows (sorted by players right now).

Run by .github/workflows/trending.yml every hour. Needs nothing but Python 3.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
PLACES_FILE = os.path.join(HERE, "places.json")
DATA_DIR = os.path.join(HERE, "data")
INDEX_FILE = os.path.join(HERE, "index.json")

MAX_GAMES = 100          # only the first 100 place IDs in places.json are checked
KEEP_DAYS = 30           # older points are dropped so the files stay small
UA = {"User-Agent": "Rostr-Trending/1.0"}


def get_json(url, tries=4):
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers=UA)
            with urllib.request.urlopen(req, timeout=20) as res:
                return json.loads(res.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            if e.code == 429 or e.code >= 500:
                time.sleep(2 ** attempt + 1)
                continue
            raise
        except (urllib.error.URLError, TimeoutError):
            time.sleep(2 ** attempt + 1)
    raise RuntimeError("Giving up on " + url)


def load_json(path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def save_json(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, separators=(",", ":"), ensure_ascii=False)
    os.replace(tmp, path)


def read_places():
    raw = load_json(PLACES_FILE, None)
    if not isinstance(raw, list):
        sys.exit("trending/places.json must be a list of place IDs, e.g. [920587237, 4924922222]")
    ids = []
    for item in raw:
        try:
            pid = int(item)
        except (TypeError, ValueError):
            print("Skipping bad entry:", item)
            continue
        if pid > 0 and pid not in ids:
            ids.append(pid)
    return ids[:MAX_GAMES]


def main():
    os.makedirs(DATA_DIR, exist_ok=True)
    now = int(time.time())
    cutoff = now - KEEP_DAYS * 86400

    places = read_places()
    if not places:
        sys.exit("places.json is empty")

    # Existing history, and the universe id of each place (saved in its file so it is only looked up once).
    files = {pid: load_json(os.path.join(DATA_DIR, f"{pid}.json"), {}) for pid in places}

    universe = {}
    for pid in places:
        uid = files[pid].get("universeId")
        if not uid:
            try:
                uid = get_json(f"https://apis.roblox.com/universes/v1/places/{pid}/universe").get("universeId")
            except Exception as e:
                print(f"Place {pid}: cannot find its universe ({e})")
                continue
        if uid:
            universe[pid] = int(uid)

    if not universe:
        sys.exit("Roblox did not answer for any game, nothing saved")

    # Player counts: up to 100 games in one request.
    info = {}
    uids = sorted(set(universe.values()))
    for i in range(0, len(uids), 100):
        chunk = uids[i:i + 100]
        data = get_json("https://games.roblox.com/v1/games?universeIds=" + ",".join(map(str, chunk)))
        for g in data.get("data", []):
            info[int(g["id"])] = g

    games = []
    for pid in places:
        uid = universe.get(pid)
        g = info.get(uid) if uid else None
        if g is None:
            print(f"Place {pid}: no data this time, skipped")
            continue

        entry = files[pid]
        points = [p for p in entry.get("points", []) if p[0] >= cutoff]
        playing = int(g.get("playing", 0))
        points.append([now, playing])

        entry = {
            "placeId": pid,
            "universeId": uid,
            "name": g.get("name", f"Game {pid}"),
            "points": points,
        }
        save_json(os.path.join(DATA_DIR, f"{pid}.json"), entry)

        day = [p[1] for p in points if p[0] >= now - 86400]
        games.append({
            "placeId": pid,
            "universeId": uid,
            "name": entry["name"],
            "playing": playing,
            "peak24h": max(day) if day else playing,
        })

    games.sort(key=lambda x: x["playing"], reverse=True)
    save_json(INDEX_FILE, {"updated": now, "games": games})
    print(f"Saved {len(games)} of {len(places)} games")


if __name__ == "__main__":
    main()
