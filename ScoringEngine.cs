using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace BGSnowballEngine
{
    public class SynergyMatrixData
    {
        public List<CardConfig> Cards { get; set; } = new List<CardConfig>();
    }

    public class CardConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double BaseWeight { get; set; } = 1.0;
        public List<string> ProvidedTags { get; set; } = new List<string>();
        public List<string> RequiredTags { get; set; } = new List<string>();
        public Dictionary<string, double> SynergyMultipliers { get; set; } = new Dictionary<string, double>();
    }

    public class ArchetypeSummary
    {
        public string Name { get; set; } = "Поиск направления";
        public string Subtitle { get; set; } = "Ранняя игра / Темп";
        public int SynergyPower { get; set; } = 0;
    }

    // === Схема v2 (Patch36.2_Meta_v2_draft.json): каталог сборок сезона 14 ===
    public class MetaCatalogV2
    {
        public List<BuildDef> Builds { get; set; } = new List<BuildDef>();
        public List<SpellDef> Spells { get; set; } = new List<SpellDef>();
        public List<string> Tier7Minions { get; set; } = new List<string>();
    }

    public class BuildDef
    {
        public string Id { get; set; }
        public string NameRu { get; set; }
        public string NameEn { get; set; }
        public string Tier { get; set; }
        public string Race { get; set; }
        public string Anchor { get; set; }
        public string Playstyle { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> CoreCards { get; set; } = new List<string>();
        public List<string> SupportCards { get; set; } = new List<string>();
        public List<string> KeySpells { get; set; } = new List<string>();
        public int Priority { get; set; }
    }

    public class SpellDef
    {
        public string Name { get; set; }
        public int? Tier { get; set; }
        public int? Cost { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string Note { get; set; }
    }

    public class BuildMatch
    {
        public BuildDef Build { get; set; }
        public int CoreHits { get; set; }
        public int SupportHits { get; set; }
        public double Score { get; set; }
    }

    // === Состояние игры для советника (фаза 2) ===
    public class GameStateSnapshot
    {
        public int TavernTier { get; set; }
        public int Gold { get; set; }
        public int Health { get; set; }
        public int BoardSize { get; set; }
    }

    public class ActionAdvice
    {
        public string Action { get; set; } = "Ждать";
        public string Reason { get; set; } = "";
        public int Priority { get; set; } = 1;
    }

    public class EngineCore
    {
        private SynergyMatrixData _matrix = new SynergyMatrixData();
        private MetaCatalogV2 _catalog = new MetaCatalogV2();

        public void Initialize()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // Схема v2 (каталог сборок) — основной источник для советника
                string v2Path = Path.Combine(pluginDir, "Patch36.2_Meta_v2_draft.json");
                if (File.Exists(v2Path))
                {
                    string json = File.ReadAllText(v2Path);
                    _catalog = JsonConvert.DeserializeObject<MetaCatalogV2>(json) ?? new MetaCatalogV2();
                }

                // Схема v1 (веса конкретных карт) — дополнительные веса/синергии
                string configPath = Path.Combine(pluginDir, "Patch36.2_Meta.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    _matrix = JsonConvert.DeserializeObject<SynergyMatrixData>(json) ?? new SynergyMatrixData();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        public string NormalizeRace(string rawRace)
        {
            if (string.IsNullOrEmpty(rawRace)) return "Neutral";
            string lower = rawRace.ToLower();

            if (lower.Contains("звер") || lower.Contains("beast")) return "Beast";
            if (lower.Contains("свин") || lower.Contains("quilboar") || lower.Contains("boar")) return "Quilboar";
            if (lower.Contains("нежит") || lower.Contains("undead")) return "Undead";
            if (lower.Contains("мех") || lower.Contains("mech")) return "Mech";
            if (lower.Contains("элем") || lower.Contains("elemental")) return "Elemental";
            if (lower.Contains("демон") || lower.Contains("demon")) return "Demon";
            if (lower.Contains("пират") || lower.Contains("pirate")) return "Pirate";
            if (lower.Contains("дракон") || lower.Contains("dragon")) return "Dragon";
            if (lower.Contains("наг") || lower.Contains("naga")) return "Naga";
            if (lower.Contains("мурлок") || lower.Contains("murloc")) return "Murloc";
            if (lower.Contains("все") || lower.Contains("all")) return "All";

            return rawRace;
        }

        public ArchetypeSummary AnalyzeBuild(IEnumerable<Card> playerBoard)
        {
            var summary = new ArchetypeSummary();
            if (playerBoard == null || !playerBoard.Any()) return summary;

            // 1) Сначала каталог сборок v2: определяем направление по совпадениям ядра/опоры
            var match = FindBestBuildMatch(playerBoard);
            if (match != null && match.Build != null && match.CoreHits + match.SupportHits > 0)
            {
                summary.Name = !string.IsNullOrEmpty(match.Build.NameRu)
                    ? match.Build.NameRu
                    : (!string.IsNullOrEmpty(match.Build.NameEn) ? match.Build.NameEn : "Сборка");

                int coreTotal = match.Build.CoreCards?.Count ?? 0;
                string tierInfo = string.IsNullOrEmpty(match.Build.Tier) ? "" : match.Build.Tier + "-тир";

                if (coreTotal > 0)
                {
                    summary.Subtitle = $"Ядро {match.CoreHits}/{coreTotal} · опора {match.SupportHits} · {tierInfo}".TrimEnd(' ', '·');
                    summary.SynergyPower = Math.Min(100, (int)((match.CoreHits / (double)coreTotal) * 85) + Math.Min(15, match.SupportHits * 3));
                }
                else
                {
                    summary.Subtitle = $"Опора {match.SupportHits} · {tierInfo}".TrimEnd(' ', '·');
                    summary.SynergyPower = Math.Min(100, 40 + match.SupportHits * 8);
                }
                return summary;
            }

            // 2) Фолбэк: эвристика по расам/тегам (ранняя игра, направление ещё не сложилось)
            var raceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var card in playerBoard)
            {
                if (card == null) continue;

                string normRace = NormalizeRace(card.Race);
                if (normRace != "Neutral" && normRace != "Invalid")
                {
                    raceCounts[normRace] = raceCounts.ContainsKey(normRace) ? raceCounts[normRace] + 1 : 1;
                    activeTags.Add(normRace);
                }

                var config = FindConfig(card);
                if (config?.ProvidedTags != null)
                {
                    foreach (var tag in config.ProvidedTags)
                        activeTags.Add(tag);
                }
            }

            if (raceCounts.Count == 0)
            {
                summary.Name = "Темп / Статы";
                summary.Subtitle = "Сборка без ярко выраженного типа";
                summary.SynergyPower = 10;
                return summary;
            }

            var topRace = raceCounts.OrderByDescending(x => x.Value).FirstOrDefault();
            int totalMinions = playerBoard.Count();

            if (topRace.Value >= 2)
            {
                summary.Name = TranslateRaceToRu(topRace.Key);
                summary.SynergyPower = Math.Min(100, (int)((topRace.Value / (double)totalMinions) * 70) + (activeTags.Count * 6));

                if (activeTags.Contains("Token_Beasts") || activeTags.Contains("Deathrattle_Trigger_x2"))
                    summary.Subtitle = "Спам токенов / Хрипы";
                else if (activeTags.Contains("Gem_Scaling") || activeTags.Contains("BloodGem_Engine"))
                    summary.Subtitle = "Кровавые самоцветы";
                else if (activeTags.Contains("Magnetic_Echo") || activeTags.Contains("DivineShield_Attacker"))
                    summary.Subtitle = "Магнетизм / Щиты";
                else if (activeTags.Contains("APM_Cycle"))
                    summary.Subtitle = "Прокрутка таверны (APM)";
                else if (activeTags.Contains("Self_Damage"))
                    summary.Subtitle = "Урон по герою";
                else if (activeTags.Contains("Handbuff_Murloc"))
                    summary.Subtitle = "Раскачка руки / Яды";
                else
                    summary.Subtitle = $"Сборка через {summary.Name}";
            }
            else if (raceCounts.Count >= 3)
            {
                summary.Name = "Солянка (Menagerie)";
                summary.Subtitle = "Мульти-трибы / Разные расы";
                summary.SynergyPower = Math.Min(100, raceCounts.Count * 22);
            }
            else
            {
                summary.Name = "Ранняя игра";
                summary.Subtitle = "Покупка сильных существ";
                summary.SynergyPower = 20;
            }

            return summary;
        }

        public Dictionary<Card, double> EvaluateTavern(IEnumerable<Card> tavernCards, IEnumerable<Card> playerBoard)
        {
            var scoredCards = new Dictionary<Card, double>();
            if (tavernCards == null || playerBoard == null) return scoredCards;

            var activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var boardCard in playerBoard)
            {
                if (boardCard == null) continue;
                string r = NormalizeRace(boardCard.Race);
                if (r != "Neutral") activeTags.Add(r);

                var config = FindConfig(boardCard);
                if (config?.ProvidedTags != null)
                {
                    foreach (var tag in config.ProvidedTags)
                        activeTags.Add(tag);
                }
            }

            // Текущее направление по каталогу сборок v2
            var match = FindBestBuildMatch(playerBoard);
            BuildDef direction = match?.Build;
            string directionRace = direction != null ? NormalizeRace(direction.Race) : "Neutral";

            foreach (var tavernCard in tavernCards)
            {
                if (tavernCard == null) continue;
                double score = 1.0;
                string tRace = NormalizeRace(tavernCard.Race);

                // Совместимость с v1: веса и синергии конкретных карт
                var config = FindConfig(tavernCard);
                if (config != null)
                {
                    score = config.BaseWeight;
                    if (config.RequiredTags != null && config.SynergyMultipliers != null)
                    {
                        foreach (var reqTag in config.RequiredTags)
                        {
                            if (activeTags.Contains(reqTag) && config.SynergyMultipliers.ContainsKey(reqTag))
                            {
                                score *= config.SynergyMultipliers[reqTag];
                            }
                        }
                    }
                }
                else if (tRace != "Neutral" && (activeTags.Contains(tRace) || tRace == "All"))
                {
                    score = 3.5;
                }

                // v2: подгонка карты под текущее направление сборки
                if (direction != null)
                {
                    var names = CardNames(tavernCard);
                    if (names.Any(n => NameEquals(n, direction.Anchor)))
                        score = Math.Max(score, 8.0);   // якорь сборки
                    else if (names.Any(n => direction.CoreCards != null && direction.CoreCards.Any(c => NameEquals(c, n))))
                        score = Math.Max(score, 7.0);   // ядро сборки
                    else if (names.Any(n => direction.SupportCards != null && direction.SupportCards.Any(c => NameEquals(c, n))))
                        score = Math.Max(score, 4.5);   // опора сборки
                    else if (directionRace != "Neutral" && (tRace == directionRace || tRace == "All"))
                        score = Math.Max(score, 3.0);   // та же раса, что и сборка
                }
                else if (_catalog.Builds != null)
                {
                    // Направления ещё нет: подсказываем якоря известных сборок
                    var names = CardNames(tavernCard);
                    if (_catalog.Builds.Any(b => names.Any(n => NameEquals(n, b.Anchor))))
                        score = Math.Max(score, 2.5);
                }

                scoredCards[tavernCard] = score;
            }

            return scoredCards;
        }

        // === Советник действий (фаза 2) ===
        public ActionAdvice Advise(GameStateSnapshot state, IEnumerable<Card> board, double bestTavernScore, bool tavernHasTriplet)
        {
            var advice = new ActionAdvice { Action = "Ждать", Reason = "Конец хода", Priority = 1 };
            if (state == null) return advice;

            var match = FindBestBuildMatch(board);
            bool hasDirection = match != null;

            int upgradeCost = state.TavernTier + 4; // формула апгрейда (тир+4) — TODO: сверить для 36.2
            bool canAffordUpgrade = state.Gold >= upgradeCost;
            bool lowHealth = state.Health > 0 && state.Health <= 15;
            bool boardFull = state.BoardSize >= 7;

            // 1) Триплет в таверне — покупаем в первую очередь
            if (tavernHasTriplet && state.Gold >= 3 && !boardFull)
            {
                advice.Action = "Купить триплет";
                advice.Reason = "Триплет в таверне: золотая карта + два бафа";
                advice.Priority = 5;
                return advice;
            }

            // 2) Карта ядра/опоры текущей сборки (или сильный старт направления)
            if (bestTavernScore >= 4.5 && state.Gold >= 3 && !boardFull)
            {
                string buildName = match?.Build?.NameRu;
                advice.Action = "Купить карту";
                advice.Reason = hasDirection
                    ? $"Карта под сборку «{buildName}» (скор {bestTavernScore:0.#})"
                    : $"Сильная карта для старта (скор {bestTavernScore:0.#})";
                advice.Priority = 4;
                return advice;
            }

            // 3) Темп-покупка при низком здоровье
            if (lowHealth && bestTavernScore >= 3.0 && state.Gold >= 3 && !boardFull)
            {
                advice.Action = "Купить темп";
                advice.Reason = $"Здоровье {state.Health} — усиливаем стол, а не гоним тир";
                advice.Priority = 3;
                return advice;
            }

            // 4) Апгрейд таверны
            if (canAffordUpgrade)
            {
                advice.Action = "Апгрейд таверны";
                advice.Reason = $"Тир {state.TavernTier} → {state.TavernTier + 1} за {upgradeCost} золота";
                advice.Priority = 4;
                return advice;
            }

            // 5) Реролл в поисках ядра сборки
            if (state.Gold >= 3 && hasDirection)
            {
                advice.Action = "Реролл";
                advice.Reason = "В таверне нет карт под сборку — ищем ядро";
                advice.Priority = 2;
                return advice;
            }

            // 6) Ждать (конец хода)
            advice.Action = "Ждать";
            advice.Reason = state.Gold < 3
                ? "Мало золота для действий"
                : "Таверна не предлагает ничего важного";
            advice.Priority = 1;
            return advice;
        }

        private BuildMatch FindBestBuildMatch(IEnumerable<Card> boardCards)
        {
            if (_catalog.Builds == null || boardCards == null) return null;
            var board = boardCards.Where(c => c != null).ToList();
            if (board.Count == 0) return null;

            BuildMatch best = null;
            foreach (var build in _catalog.Builds)
            {
                if (build == null) continue;

                int coreHits = 0;
                int supportHits = 0;
                foreach (var card in board)
                {
                    var names = CardNames(card);
                    if (names.Any(n => build.CoreCards != null && build.CoreCards.Any(c => NameEquals(c, n))))
                        coreHits++;
                    else if (names.Any(n => build.SupportCards != null && build.SupportCards.Any(c => NameEquals(c, n))))
                        supportHits++;
                }

                if (coreHits + supportHits == 0) continue;

                double score = coreHits * 3.0 + supportHits * 1.0;
                if (best == null || score > best.Score ||
                    (Math.Abs(score - best.Score) < 0.001 && build.Priority < best.Build.Priority))
                {
                    best = new BuildMatch
                    {
                        Build = build,
                        CoreHits = coreHits,
                        SupportHits = supportHits,
                        Score = score
                    };
                }
            }

            return best;
        }

        private IEnumerable<string> CardNames(Card card)
        {
            var names = new List<string>();
            if (card == null) return names;

            string en = GetEnglishName(card);
            if (!string.IsNullOrEmpty(en)) names.Add(en);
            if (!string.IsNullOrEmpty(card.Name)) names.Add(card.Name);

            return names;
        }

        private string GetEnglishName(Card card)
        {
            try
            {
                if (!string.IsNullOrEmpty(card.Id) && HearthDb.Cards.All.TryGetValue(card.Id, out var dbCard))
                    return dbCard.Name;
            }
            catch
            {
                // HearthDb может быть недоступен в отдельных сборках — молча пропускаем
            }
            return null;
        }

        private static bool NameEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private CardConfig FindConfig(Card card)
        {
            if (_matrix.Cards == null || card == null) return null;

            string enName = null;
            if (!string.IsNullOrEmpty(card.Id) && HearthDb.Cards.All.TryGetValue(card.Id, out var dbCard))
            {
                enName = dbCard.Name;
            }

            return _matrix.Cards.FirstOrDefault(c => 
                (!string.IsNullOrEmpty(c.Id) && string.Equals(c.Id, card.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Name) && (
                    string.Equals(c.Name, card.Name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(enName) && string.Equals(c.Name, enName, StringComparison.OrdinalIgnoreCase))
                )));
        }

        private string TranslateRaceToRu(string race)
        {
            switch (race.ToLower())
            {
                case "beast": return "Звери";
                case "quilboar": return "Свинобразы";
                case "undead": return "Нежить";
                case "mech": return "Механизмы";
                case "elemental": return "Элементали";
                case "demon": return "Демоны";
                case "pirate": return "Пираты";
                case "dragon": return "Драконы";
                case "naga": return "Наги";
                case "murloc": return "Мурлоки";
                default: return race;
            }
        }
    }
}
