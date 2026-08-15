using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
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

                // Проверка наличия матча
                if (!Core.Game.IsBattlegroundsMatch && Core.Game.Opponent?.Name != "Бармен Боб" && Core.Game.Opponent?.Name != "Bartender Bob")
                {
                    _overlay?.ClearHighlights();
                    return;
                }

                var playerEntities = Core.Game.Player.Board?.Where(e => e != null && e.Card != null && e.IsMinion).ToList();
                var boardCards = playerEntities != null ? playerEntities.Select(e => e.Card).ToList() : new List<Card>();

                // 1. Всегда обновляем плашку сборки
                if (_engine != null && _overlay != null)
                {
                    var buildSummary = _engine.AnalyzeBuild(boardCards);
                    _overlay.UpdateBuildStatus(buildSummary);
                }

                // 2. Получаем существ в таверне и сортируем строго слева направо
                var tavernEntities = Core.Game.Opponent?.Board?
                    .Where(e => e != null && e.Card != null && e.IsMinion && e.IsInPlay)
                    .OrderBy(e => e.GetTag(GameTag.ZONE_POSITION))
                    .ToList();

                if (tavernEntities != null && tavernEntities.Count > 0 && _engine != null && _overlay != null)
                {
                    var scoredSlots = new List<ScoredSlot>();
                    int totalCount = tavernEntities.Count;

                    for (int i = 0; i < totalCount; i++)
                    {
                        var entity = tavernEntities[i];
                        var singleList = new List<Card> { entity.Card };
                        
                        var scoreDict = _engine.EvaluateTavern(singleList, boardCards);
                        double score = scoreDict.ContainsKey(entity.Card) ? scoreDict[entity.Card] : 1.0;

                        // Золотой триплет или точное совпадение
                        if (boardCards.Count(c => c.Id == entity.Card.Id) == 2)
                        {
                            score += 10.0;
                        }

                        scoredSlots.Add(new ScoredSlot
                        {
                            SlotIndex = i,
                            TotalSlots = totalCount,
                            Score = score
                        });
                    }

                    _overlay.UpdateTavernHighlights(scoredSlots);
                }
                else
                {
                    _overlay?.ClearHighlights();
                }
            }
            catch { }
        }
    }
}
