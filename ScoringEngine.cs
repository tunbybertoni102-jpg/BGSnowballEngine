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

    // === Состояние игры для советника ===
    public class GameStateSnapshot
    {
        public int TavernTier { get; set; }
        public int Gold { get; set; }
        public int Health { get; set; }
        public int BoardSize { get; set; }
        public int Turn { get; set; }
    }

    public class ActionAdvice
    {
        public string Action { get; set; } = "Ждать";
        public string Reason { get; set; } = "";
        public int Priority { get; set; } = 1;
    }

    /// <summary>Оценённое предложение таверны: карта + скор + роль в сборке.</summary>
    public class TavernOffer
    {
        public Card Card { get; set; }
        public double Score { get; set; }
        public bool IsTriplet { get; set; }
        public string Role { get; set; } = "Темп"; // Якорь / Ядро / Опора / Раса / Триплет / Темп
    }

    /// <summary>Полный контекст для советника: состояние, стол, предложения таверны.</summary>
    public class AdviceContext
    {
        public GameStateSnapshot State { get; set; }
        public List<Card> Board { get; set; } = new List<Card>();
        public List<TavernOffer> Tavern { get; set; } = new List<TavernOffer>();
    }

    public class EngineCore
    {
        private SynergyMatrixData _matrix = new SynergyMatrixData();
        private MetaCatalogV2 _catalog = new MetaCatalogV2();

        // === Экономика таверны 36.2 (проверено: wiki.gg/Battlegrounds + патчноуты 34.2/36.2) ===
        // Базовая стоимость апгрейда на целевой тир: 1→2=5, 2→3=7, 3→4=8, 4→5=11, 5→6=11.
        // 4→5 и 5→6 подорожали с 9/10 до 11 в патче 34.2; в 36.2 изменений нет.
        // Тир 7 за золото НЕ доступен (только особые эффекты: триплеты, герои, тринкеты).
        // TODO: каждый раунд на текущем тире снижает стоимость на 1 — в расчёте не учтено
        // (совет консервативный: если по базе хватает — по факту тем более хватит).
        private static readonly int[] UpgradeBaseCost = { 0, 0, 5, 7, 8, 11, 11 };

        // Рекомендуемый ход апгрейда на целевой тир (стандартная кривая с учётом удорожания 34.2;
        // ранняя игра: T2 к ходу 3, T3 к ходу 5; мид: T4 к ходу 7; поздняя: T5 к 9-10, T6 к 11-12).
        private static readonly int[] RecommendedUpgradeTurn = { 99, 3, 5, 7, 9, 11, 13 };

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

        /// <summary>
        /// Оценка предложений таверны: скор + роль в сборке + флаг триплета.
        /// Скор: якорь 8.0, ядро 7.0, опора 4.5, та же раса 3.0, без направления — якоря сборок 2.5,
        /// триплет (2 копии на столе) — +10. Пороги совпадают со шкалой старой подсветки.
        /// </summary>
        public List<TavernOffer> EvaluateTavernOffers(IEnumerable<Card> tavernCards, IEnumerable<Card> playerBoard)
        {
            var offers = new List<TavernOffer>();
            if (tavernCards == null || playerBoard == null) return offers;

            var board = playerBoard.Where(c => c != null).ToList();

            var activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var boardCard in board)
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
            var match = FindBestBuildMatch(board);
            BuildDef direction = match?.Build;
            string directionRace = direction != null ? NormalizeRace(direction.Race) : "Neutral";

            foreach (var tavernCard in tavernCards)
            {
                if (tavernCard == null) continue;

                double score = 1.0;
                string role = "Темп";
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
                    role = "Раса";
                }

                // v2: подгонка карты под текущее направление сборки
                if (direction != null)
                {
                    var names = CardNames(tavernCard);
                    if (names.Any(n => NameEquals(n, direction.Anchor)))
                    {
                        score = Math.Max(score, 8.0);   // якорь сборки
                        role = "Якорь";
                    }
                    else if (names.Any(n => direction.CoreCards != null && direction.CoreCards.Any(c => NameEquals(c, n))))
                    {
                        score = Math.Max(score, 7.0);   // ядро сборки
                        role = "Ядро";
                    }
                    else if (names.Any(n => direction.SupportCards != null && direction.SupportCards.Any(c => NameEquals(c, n))))
                    {
                        score = Math.Max(score, 4.5);   // опора сборки
                        role = "Опора";
                    }
                    else if (directionRace != "Neutral" && (tRace == directionRace || tRace == "All"))
                    {
                        score = Math.Max(score, 3.0);   // та же раса, что и сборка
                        role = "Раса";
                    }
                }
                else if (_catalog.Builds != null)
                {
                    // Направления ещё нет: подсказываем якоря известных сборок
                    var names = CardNames(tavernCard);
                    if (_catalog.Builds.Any(b => names.Any(n => NameEquals(n, b.Anchor))))
                    {
                        score = Math.Max(score, 2.5);
                        role = "Якорь?";
                    }
                }

                // Триплет: 2 копии на столе + эта карта = золотая
                bool isTriplet = board.Count(c => c.Id == tavernCard.Id) == 2;
                if (isTriplet)
                {
                    score += 10.0;
                    role = "Триплет";
                }

                offers.Add(new TavernOffer
                {
                    Card = tavernCard,
                    Score = score,
                    IsTriplet = isTriplet,
                    Role = role
                });
            }

            return offers.OrderByDescending(o => o.Score).ToList();
        }

        /// <summary>Следующая цель сборки: первая недостающая карта ядра (или сообщение о полном ядре).</summary>
        public string GetNextGoal(IEnumerable<Card> board)
        {
            var match = FindBestBuildMatch(board);
            if (match?.Build == null) return "";

            var boardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in board)
            {
                if (card == null) continue;
                foreach (var n in CardNames(card))
                    boardNames.Add(n);
            }

            if (match.Build.CoreCards != null)
            {
                foreach (var core in match.Build.CoreCards)
                {
                    if (!boardNames.Any(n => NameEquals(n, core)))
                    {
                        return $"{core} (ядро «{match.Build.NameRu}»)";
                    }
                }
            }
            return "Ядро собрано — усиливай опору";
        }

        // === Советник действий (фаза 4: покупка vs апгрейд по ситуации) ===
        // Правила собраны из исследования тактик (гайды 10K+ MMR, Jeef, Magnechu, кривые левелинга)
        // и сверены с экономикой 36.2 (стоимости апгрейдов, кривая золота). Ключевые:
        // R1  — апгрейды по стандартной кривой; R5 — плохой шоп → левел;
        // R9  — шоп «слишком хорош» (≥2 карт под сборку) → купить, апгрейд отложить;
        // R10/R14 — низкое здоровье → темп вместо апгрейда (salvation mode);
        // R15 — при HP ≤ 20 досрочный апгрейд не советуем;
        // R19 — в ранней игре не рероллить; R21 — копя на апгрейд, не тратить на реролл;
        // R25 — триплет → золотой откроет миньона тира выше (приоритет выше всего).
        public ActionAdvice Advise(AdviceContext ctx)
        {
            var advice = new ActionAdvice { Action = "Ждать", Reason = "Конец хода", Priority = 1 };
            if (ctx?.State == null) return advice;

            var state = ctx.State;
            int tier = Math.Max(0, state.TavernTier);
            int gold = state.Gold;
            int hp = state.Health;
            int turn = Math.Max(0, state.Turn);
            bool boardFull = state.BoardSize >= 7;
            bool canBuy = gold >= 3 && !boardFull;

            var offers = ctx.Tavern ?? new List<TavernOffer>();
            var triplet = offers.FirstOrDefault(o => o.IsTriplet);
            var core = offers.FirstOrDefault(o => !o.IsTriplet && o.Score >= 7.0);
            var support = offers.FirstOrDefault(o => !o.IsTriplet && o.Score >= 4.5);
            var race = offers.FirstOrDefault(o => !o.IsTriplet && o.Score >= 3.0);
            var best = offers.OrderByDescending(o => o.Score).FirstOrDefault();

            bool lowHp = hp > 0 && hp <= 15;          // salvation mode (R14)
            bool dangerHp = hp > 0 && hp <= 20;       // осторожный режим (R15): без досрочных апгрейдов

            int upgradeTarget = tier + 1;
            bool canUpgrade = upgradeTarget >= 2 && upgradeTarget <= 6 && gold >= UpgradeBaseCost[upgradeTarget];
            bool upgradeDue = upgradeTarget >= 2 && upgradeTarget <= 6 && turn >= RecommendedUpgradeTurn[upgradeTarget];

            var match = FindBestBuildMatch(ctx.Board);
            bool hasDirection = match != null;
            string buildName = match?.Build?.NameRu;
            bool earlyGame = turn > 0 && turn <= 5;

            // 1) Триплет в таверне — доступ к миньону тира выше (R25, R28).
            // Приоритет выше всего, даже при полной доске: продажа слабого миньона + покупка
            // (продажа даёт 1 золото, покупка стоит 3 → хватает при gold >= 2).
            // Реролл/апгрейд при триплете в таверне НИКОГДА не советуем — они уничтожают триплет.
            if (triplet != null)
            {
                if (canBuy)
                {
                    advice.Action = "Купить триплет";
                    advice.Reason = $"«{triplet.Card?.Name}» — золотой откроет миньона тира выше";
                    advice.Priority = 5;
                    return advice;
                }
                if (gold >= 2)
                {
                    advice.Action = "Продать и купить триплет";
                    advice.Reason = $"Доска полна: продай слабого миньона и возьми «{triplet.Card?.Name}» — золотой откроет миньона тира выше";
                    advice.Priority = 5;
                    return advice;
                }
                advice.Action = "Ждать";
                advice.Reason = "Триплет в таверне — не рероллим, копим 2 золота";
                advice.Priority = 3;
                return advice;
            }

            // 2) Ядро/якорь текущей сборки — карта решает исход (R9)
            if (core != null && canBuy)
            {
                advice.Action = "Купить карту";
                advice.Reason = $"«{core.Card?.Name}» — {core.Role.ToLower()} сборки «{buildName ?? "текущей"}»";
                advice.Priority = 5;
                return advice;
            }

            // 3) Режим выживания: низкое здоровье → темп, апгрейды отложить (R10, R14)
            if (lowHp)
            {
                if (race != null && canBuy)
                {
                    advice.Action = "Купить темп";
                    advice.Reason = $"«{race.Card?.Name}» — здоровье {hp}: стабилизируем стол";
                    advice.Priority = 4;
                    return advice;
                }
                if (canUpgrade && upgradeDue)
                {
                    advice.Action = "Апгрейд таверны";
                    advice.Reason = $"Здоровье {hp}: тир {tier} → {upgradeTarget} за {UpgradeBaseCost[upgradeTarget]}";
                    advice.Priority = 3;
                    return advice;
                }
                if (gold >= 3 && turn >= 6 && !boardFull)
                {
                    advice.Action = "Реролл";
                    advice.Reason = "Ищем спасение для стола";
                    advice.Priority = 2;
                    return advice;
                }
                advice.Action = "Ждать";
                advice.Reason = "Нет карт — копим золото";
                advice.Priority = 1;
                return advice;
            }

            // 4) Апгрейд по расписанию (R1, R12): T4 к ходу 7, T5 к 9-10, T6 к 11-12
            if (canUpgrade && upgradeDue)
            {
                // Шоп «слишком хорош» (≥2 карт под сборку) — купить, апгрейд отложить (R9)
                var goodOffers = offers.Where(o => !o.IsTriplet && o.Score >= 4.5).Take(2).ToList();
                if (goodOffers.Count >= 2 && !boardFull && gold >= 6)
                {
                    advice.Action = "Купить карту";
                    advice.Reason = $"{goodOffers.Count} карты под сборку — шоп лучше апгрейда (ход {turn})";
                    advice.Priority = 4;
                    return advice;
                }
                advice.Action = "Апгрейд таверны";
                advice.Reason = $"Пора: тир {tier} → {upgradeTarget} за {UpgradeBaseCost[upgradeTarget]} (ход {turn})";
                advice.Priority = 4;
                return advice;
            }

            // 5) Апгрейд доступен, но рано по расписанию
            if (canUpgrade)
            {
                // Шоп плохой → левел раньше срока (3-on-3 / Powerlevel, R5);
                // при осторожном HP (<=20) досрочный апгрейд не советуем — рискуем (R15)
                if (!dangerHp && (best == null || best.Score < 3.0))
                {
                    advice.Action = "Апгрейд таверны";
                    advice.Reason = "Шоп слабый — золото лучше вложить в тир";
                    advice.Priority = 3;
                    return advice;
                }
                if (support != null && canBuy)
                {
                    advice.Action = "Купить карту";
                    advice.Reason = $"«{support.Card?.Name}» — опора сборки, апгрейд ещё рано";
                    advice.Priority = 3;
                    return advice;
                }
                if (race != null && canBuy)
                {
                    advice.Action = "Купить карту";
                    advice.Reason = $"«{race.Card?.Name}» — раса сборки «{buildName ?? "текущей"}»";
                    advice.Priority = 2;
                    return advice;
                }
                if (boardFull && gold >= 2)
                {
                    var target = support ?? race;
                    if (target != null)
                    {
                        advice.Action = "Продать и купить";
                        advice.Reason = $"Продай слабого миньона и возьми «{target.Card?.Name}» ({target.Role.ToLower()})";
                        advice.Priority = 3;
                        return advice;
                    }
                }
                advice.Action = "Ждать";
                advice.Reason = "Копим на апгрейд — не тратим на рероллы";
                advice.Priority = 2;
                return advice;
            }

            // 6) На апгрейд не хватает — покупаем лучшее из предложенного
            if (support != null && canBuy)
            {
                advice.Action = "Купить карту";
                advice.Reason = $"«{support.Card?.Name}» — {support.Role.ToLower()} (скор {support.Score:0.#})";
                advice.Priority = 3;
                return advice;
            }
            if (race != null && canBuy)
            {
                advice.Action = "Купить карту";
                advice.Reason = $"«{race.Card?.Name}» — под сборку (скор {race.Score:0.#})";
                advice.Priority = 2;
                return advice;
            }
            // Полная доска, но есть полезная карта — продажа + покупка
            if (boardFull && gold >= 2)
            {
                var target = support ?? race;
                if (target != null)
                {
                    advice.Action = "Продать и купить";
                    advice.Reason = $"Продай слабого миньона и возьми «{target.Card?.Name}» ({target.Role.ToLower()})";
                    advice.Priority = 3;
                    return advice;
                }
            }
            // Реролл только в мид/лейте и при свободном слоте (R19; реролл при полной доске бессмыслен)
            if (gold >= 3 && turn >= 6 && !boardFull)
            {
                advice.Action = "Реролл";
                advice.Reason = hasDirection ? "Ищем карты сборки" : "Ищем направление";
                advice.Priority = 2;
                return advice;
            }
            if (gold < 3)
            {
                advice.Action = "Ждать";
                advice.Reason = "Мало золота для действий";
                advice.Priority = 1;
                return advice;
            }
            advice.Action = "Ждать";
            advice.Reason = earlyGame ? "Ранняя игра: не рероллим, копим на апгрейд" : "Таверна не предлагает ничего важного";
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
