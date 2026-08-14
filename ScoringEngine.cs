using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using HearthstoneDeckTracker.API;
using HearthstoneDeckTracker.Enums;
using HearthstoneDeckTracker.Hearthstone;

namespace BGSnowballEngine
{
    public class SynergyMatrixData
    {
        public List<CardConfig> Cards { get; set; }
    }

    public class CardConfig
    {
        public string Id { get; set; }
        public double BaseWeight { get; set; }
        
        public List<string> ProvidedTags { get; set; }
        public List<string> RequiredTags { get; set; }
        public Dictionary<string, double> SynergyMultipliers { get; set; }

        public CardConfig()
        {
            ProvidedTags = new List<string>();
            RequiredTags = new List<string>();
            SynergyMultipliers = new Dictionary<string, double>();
        }
    }

    public class EngineCore
    {
        private SynergyMatrixData _matrix;
        private string _configPath = @"Plugins\BGSnowballEngine\Patch36.2_Meta.json";

        public void Initialize()
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                _matrix = JsonConvert.DeserializeObject<SynergyMatrixData>(json);
            }
        }

        public Dictionary<Card, double> EvaluateTavern(List<Card> tavernCards, List<Card> playerBoard)
        {
            var scoredCards = new Dictionary<Card, double>();
            if (_matrix == null || _matrix.Cards == null) return scoredCards;
            
            var activeTags = new HashSet<string>();
            foreach (var boardCard in playerBoard)
            {
                var config = _matrix.Cards.FirstOrDefault(c => c.Id == boardCard.Id);
                if (config != null)
                {
                    foreach (var tag in config.ProvidedTags)
                        activeTags.Add(tag);
                }
            }

            foreach (var tavernCard in tavernCards)
            {
                var config = _matrix.Cards.FirstOrDefault(c => c.Id == tavernCard.Id);
                if (config == null) continue;

                double finalScore = config.BaseWeight;

                foreach (var reqTag in config.RequiredTags)
                {
                    if (activeTags.Contains(reqTag) && config.SynergyMultipliers.ContainsKey(reqTag))
                    {
                        finalScore *= config.SynergyMultipliers[reqTag];
                    }
                }

                scoredCards.Add(tavernCard, finalScore);
            }

            return scoredCards;
        }
    }
}
