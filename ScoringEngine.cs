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

    public class EngineCore
    {
        private SynergyMatrixData _matrix = new SynergyMatrixData();

        public void Initialize()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(pluginDir, "Patch36.2_Meta.json");

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    _matrix = JsonConvert.DeserializeObject<SynergyMatrixData>(json) ?? new SynergyMatrixData();
                }
            }
            catch { }
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

            foreach (var tavernCard in tavernCards)
            {
                if (tavernCard == null) continue;
                double score = 1.0;
                string tRace = NormalizeRace(tavernCard.Race);

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

                scoredCards[tavernCard] = score;
            }

            return scoredCards;
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
