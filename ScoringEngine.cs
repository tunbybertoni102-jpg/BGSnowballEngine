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
        public double BaseWeight { get; set; }
        public List<string> ProvidedTags { get; set; } = new List<string>();
        public List<string> RequiredTags { get; set; } = new List<string>();
        public Dictionary<string, double> SynergyMultipliers { get; set; } = new Dictionary<string, double>();
    }

    public class EngineCore
    {
        private SynergyMatrixData _matrix;

        public void Initialize()
        {
            try
            {
                // Находим точную папку, где лежит наша DLL плагина
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(pluginDir, "Patch36.2_Meta.json");

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    _matrix = JsonConvert.DeserializeObject<SynergyMatrixData>(json);
                }
            }
            catch (Exception)
            {
                // Игнорируем сбои чтения
            }
        }

        public Dictionary<Card, double> EvaluateTavern(IEnumerable<Card> tavernCards, IEnumerable<Card> playerBoard)
        {
            var scoredCards = new Dictionary<Card, double>();
            if (_matrix == null || _matrix.Cards == null || tavernCards == null || playerBoard == null) 
                return scoredCards;
            
            var activeTags = new HashSet<string>();
            foreach (var boardCard in playerBoard)
            {
                if (boardCard == null) continue;
                var config = _matrix.Cards.FirstOrDefault(c => c.Id == boardCard.Id);
                if (config != null && config.ProvidedTags != null)
                {
                    foreach (var tag in config.ProvidedTags)
                        activeTags.Add(tag);
                }
            }

            foreach (var tavernCard in tavernCards)
            {
                if (tavernCard == null) continue;
                var config = _matrix.Cards.FirstOrDefault(c => c.Id == tavernCard.Id);
                if (config == null) continue;

                double finalScore = config.BaseWeight;

                if (config.RequiredTags != null && config.SynergyMultipliers != null)
                {
                    foreach (var reqTag in config.RequiredTags)
                    {
                        if (activeTags.Contains(reqTag) && config.SynergyMultipliers.ContainsKey(reqTag))
                        {
                            finalScore *= config.SynergyMultipliers[reqTag];
                        }
                    }
                }

                scoredCards[tavernCard] = finalScore;
            }

            return scoredCards;
        }
    }
}
