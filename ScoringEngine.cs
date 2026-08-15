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
            catch (Exception) { }
        }

        public Dictionary<Card, double> EvaluateTavern(IEnumerable<Card> tavernCards, IEnumerable<Card> playerBoard)
        {
            var scoredCards = new Dictionary<Card, double>();
            if (tavernCards == null || playerBoard == null) return scoredCards;

            var activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Сбор тегов с нашего стола
            foreach (var boardCard in playerBoard)
            {
                if (boardCard == null) continue;

                // Автоматический сбор тега расы существа
                if (!string.IsNullOrEmpty(boardCard.Race))
                    activeTags.Add(boardCard.Race);

                // Поиск кастомных тегов из JSON-базы
                var config = FindConfig(boardCard);
                if (config != null && config.ProvidedTags != null)
                {
                    foreach (var tag in config.ProvidedTags)
                        activeTags.Add(tag);
                }
            }

            // 2. Оценка карт в таверне Боба
            foreach (var tavernCard in tavernCards)
            {
                if (tavernCard == null) continue;

                double score = 1.0;
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
                else
                {
                    // Базовый вес для карт совпавшего типа (трибы)
                    if (!string.IsNullOrEmpty(tavernCard.Race) && activeTags.Contains(tavernCard.Race))
                    {
                        score = 2.5;
                    }
                }

                scoredCards[tavernCard] = score;
            }

            return scoredCards;
        }

        private CardConfig FindConfig(Card card)
        {
            if (_matrix.Cards == null || card == null) return null;
            return _matrix.Cards.FirstOrDefault(c => 
                (!string.IsNullOrEmpty(c.Id) && string.Equals(c.Id, card.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Name) && (string.Equals(c.Name, card.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, card.EnglishText, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
