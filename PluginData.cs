using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace BGSnowballEngine
{
    public class PluginData : IPlugin
    {
        public string Name => "Battlegrounds Snowball Engine";
        public string Description => "Эвристический анализатор для Полей Сражений.";
        public string ButtonText => "Настройки";
        public string Author => "AI & User";
        public Version Version => new Version(1, 0, 0);
        
        public MenuItem MenuItem => null;

        private EngineCore _engine;
        private OverlayUI _overlay;

        public void OnLoad()
        {
            _engine = new EngineCore();
            _engine.Initialize();

            _overlay = new OverlayUI();
            
            GameEvents.OnTurnStart.Add(player => AnalyzeAndDraw());
        }

        public void OnUnload()
        {
            _overlay?.ClearOverlay();
        }

        public void OnButtonPress() { }

        public void OnUpdate()
        {
            AnalyzeAndDraw();
        }

        private void AnalyzeAndDraw()
        {
            try
            {
                if (Core.Game == null || Core.Game.Player == null) return;

                if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                {
                    _overlay?.ClearHighlights();
                    return;
                }

                var playerEntities = Core.Game.Player.Board?.Where(e => e != null && e.Card != null).ToList();
                var tavernEntities = Core.Game.Opponent?.Board?.Where(e => e != null && e.Card != null).ToList();

                if (playerEntities != null && _engine != null && _overlay != null)
                {
                    var boardCards = playerEntities.Select(e => e.Card).ToList();

                    // Обновляем правую плашку предсказанной сборки
                    var buildSummary = _engine.AnalyzeBuild(boardCards);
                    _overlay.UpdateBuildStatus(buildSummary);

                    // Оцениваем таверну Боба и подсвечиваем контуры
                    if (tavernEntities != null && tavernEntities.Count > 0)
                    {
                        var scoredItems = new List<ScoredTavernEntity>();
                        int totalCount = tavernEntities.Count;

                        for (int i = 0; i < totalCount; i++)
                        {
                            var entity = tavernEntities[i];
                            var singleList = new List<Card> { entity.Card };
                            
                            var scoreDict = _engine.EvaluateTavern(singleList, boardCards);
                            double score = scoreDict.ContainsKey(entity.Card) ? scoreDict[entity.Card] : 0.0;

                            scoredItems.Add(new ScoredTavernEntity
                            {
                                Entity = entity,
                                SlotIndex = i,
                                TotalSlots = totalCount,
                                Score = score
                            });
                        }

                        _overlay.UpdateTavernHighlights(scoredItems);
                    }
                    else
                    {
                        _overlay.ClearHighlights();
                    }
                }
            }
            catch { }
        }
    }
}
